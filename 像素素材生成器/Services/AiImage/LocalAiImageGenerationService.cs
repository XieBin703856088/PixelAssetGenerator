using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HPPH;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.AiImage;
using PixelAssetGenerator.Core.Nodes;
using StableDiffusion.NET;

namespace PixelAssetGenerator.Services.AiImage;

/// <summary>
/// Process-wide local diffusion runtime. It coalesces repeated requests per node,
/// serializes GPU work, owns one lazily-loaded model and keeps completed node outputs.
/// </summary>
public sealed class LocalAiImageGenerationService : IAiImageGenerationRuntime
{
    private sealed class PendingRequest : IDisposable
    {
        public required AiImageGenerationRequest Request { get; init; }
        public PixelBuffer? Reference { get; init; }
        public PixelBuffer? Mask { get; init; }

        public void Dispose()
        {
            Reference?.Dispose();
            Mask?.Dispose();
        }
    }

    private sealed class NodeState
    {
        public object Gate { get; } = new();
        public int RequestedRevision;
        public int CancelledThroughRevision;
        public bool WorkerRunning;
        public PendingRequest? Pending;
        public PixelBuffer? Output;
        public AiImageGenerationStatus Status = AiImageGenerationStatus.NotInitialized;
    }

    private readonly ConcurrentDictionary<int, NodeState> _nodes = new();
    private readonly PixelArtModelManager _modelManager = new();
    private readonly LocalChinesePromptTranslator _promptTranslator = new();
    private readonly SemaphoreSlim _modelGate = new(1, 1);
    private readonly SemaphoreSlim _generationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private DiffusionModel? _model;
    private int _activeNativeNodeId;
    private bool _disposed;

    public static LocalAiImageGenerationService Instance { get; } = new();

    public event EventHandler<AiImageNodeStateChangedEventArgs>? StateChanged;

    public string ModelDirectory => _modelManager.ModelDirectory;
    public string TranslationModelDirectory => _promptTranslator.ModelDirectory;
    public bool IsModelInstalled => _modelManager.IsInstalled;
    public bool IsTranslationModelInstalled => _promptTranslator.IsAvailable;
    public string BackendName { get; }

    private LocalAiImageGenerationService()
    {
        BackendName = ConfigureBackends();
        StableDiffusionCpp.InitializeEvents();
        StableDiffusionCpp.Progress += (_, args) =>
        {
            var nodeId = Volatile.Read(ref _activeNativeNodeId);
            if (nodeId <= 0) return;
            var progress = Math.Clamp(args.Progress, 0f, 1f);
            SetStatus(nodeId, current => current with
            {
                Phase = AiImageGenerationPhase.Generating,
                Progress = 0.52 + progress * 0.38,
                Message = $"本地生成中：{args.Step}/{args.Steps} 步",
                Backend = BackendName
            });
        };
    }

    public AiImageGenerationStatus GetStatus(int nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var state))
            return new AiImageGenerationStatus(AiImageGenerationPhase.Idle, 0,
                _modelManager.IsInstalled ? "内置模型已就绪，点击生成" : "内置模型缺失，请安装完整离线版本",
                BackendName, 0, _modelManager.IsInstalled);
        lock (state.Gate)
            return state.Status with { ModelInstalled = _modelManager.IsInstalled, Backend = BackendName };
    }

    public PixelBuffer? GetOutputClone(int nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var state)) return null;
        lock (state.Gate) return state.Output?.Clone();
    }

    public bool TryRequest(int nodeId, AiImageGenerationRequest request, PixelBuffer? referenceImage, PixelBuffer? mask)
    {
        if (_disposed || nodeId <= 0 || request.Revision <= 0 || !_modelManager.IsInstalled) return false;
        var state = _nodes.GetOrAdd(nodeId, _ => new NodeState());
        var startWorker = false;
        lock (state.Gate)
        {
            if (request.Revision == state.RequestedRevision) return false;

            state.RequestedRevision = request.Revision;
            state.Pending?.Dispose();
            state.Pending = new PendingRequest
            {
                Request = request,
                Reference = referenceImage?.Clone(),
                Mask = mask?.Clone()
            };
            state.Status = new AiImageGenerationStatus(
                AiImageGenerationPhase.Queued, 0,
                state.WorkerRunning
                    ? "已更新请求，等待当前生成结束"
                    : referenceImage != null
                        ? $"已接收参考图，将按“{ReferenceModeDisplay(request.ReferenceMode)}”执行图生图"
                        : "已加入本地文生图队列",
                BackendName, request.Revision, _modelManager.IsInstalled);
            if (!state.WorkerRunning)
            {
                state.WorkerRunning = true;
                startWorker = true;
            }
        }

        RaiseStateChanged(nodeId, GetStatus(nodeId));
        if (startWorker)
            _ = Task.Run(() => RunNodeWorkerAsync(nodeId, state, _shutdown.Token));
        return true;
    }

    public async Task EnsureModelAsync(int nodeId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        try
        {
            await _modelManager.EnsureInstalledAsync(cancellationToken: linked.Token).ConfigureAwait(false);
            SetStatus(nodeId, current => current with
            {
                Phase = AiImageGenerationPhase.Idle,
                Progress = 1,
                Message = "内置像素生成模型已就绪",
                Backend = BackendName,
                ModelInstalled = true
            });
        }
        catch (OperationCanceledException)
        {
            SetStatus(nodeId, current => current with
            {
                Phase = AiImageGenerationPhase.Idle,
                Progress = 0,
                Message = "模型检查已取消",
                Backend = BackendName,
                ModelInstalled = _modelManager.IsInstalled
            });
            throw;
        }
        catch
        {
            SetStatus(nodeId, current => current with
            {
                Phase = AiImageGenerationPhase.Failed,
                Progress = 0,
                Message = "内置模型缺失或不完整，请重新安装完整离线版本",
                Backend = BackendName,
                ModelInstalled = _modelManager.IsInstalled
            });
            throw;
        }
    }

    public void Cancel(int nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var state)) return;
        lock (state.Gate)
        {
            state.CancelledThroughRevision = Math.Max(state.CancelledThroughRevision, state.RequestedRevision);
            state.Pending?.Dispose();
            state.Pending = null;
            state.Status = state.Status with
            {
                Phase = AiImageGenerationPhase.Cancelled,
                Message = state.WorkerRunning ? "已取消等待任务；正在执行的本地推理将在本轮后停止" : "任务已取消",
                Progress = 0
            };
        }
        RaiseStateChanged(nodeId, GetStatus(nodeId));
    }

    private async Task RunNodeWorkerAsync(int nodeId, NodeState state, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PendingRequest? job;
            lock (state.Gate)
            {
                job = state.Pending;
                state.Pending = null;
                if (job == null)
                {
                    state.WorkerRunning = false;
                    return;
                }
            }

            PixelBuffer? generated = null;
            try
            {
                generated = await GenerateAsync(nodeId, job, cancellationToken).ConfigureAwait(false);
                var accepted = false;
                lock (state.Gate)
                {
                    accepted = job.Request.Revision == state.RequestedRevision
                        && job.Request.Revision > state.CancelledThroughRevision;
                    if (accepted)
                    {
                        state.Output?.Dispose();
                        state.Output = generated;
                        generated = null;
                        state.Status = new AiImageGenerationStatus(
                            AiImageGenerationPhase.Completed, 1,
                            job.Reference != null
                                ? $"参考图生成完成 · {ReferenceModeDisplay(job.Request.ReferenceMode)} · {job.Request.OutputSize}×{job.Request.OutputSize} · {BackendName}"
                                : $"生成完成 · {job.Request.OutputSize}×{job.Request.OutputSize} · {BackendName}",
                            BackendName, job.Request.Revision, true);
                    }
                }

                if (accepted) RaiseStateChanged(nodeId, GetStatus(nodeId));
            }
            catch (OperationCanceledException)
            {
                SetStatus(nodeId, current => current with
                {
                    Phase = AiImageGenerationPhase.Cancelled,
                    Progress = 0,
                    Message = "本地生成已取消"
                });
            }
            catch (Exception ex)
            {
                SetStatus(nodeId, current => current with
                {
                    Phase = AiImageGenerationPhase.Failed,
                    Progress = 0,
                    Message = "生成失败：" + FriendlyError(ex),
                    Backend = BackendName,
                    ModelInstalled = _modelManager.IsInstalled
                });
            }
            finally
            {
                generated?.Dispose();
                job.Dispose();
            }
        }

        lock (state.Gate) state.WorkerRunning = false;
    }

    private async Task<PixelBuffer> GenerateAsync(int nodeId, PendingRequest job, CancellationToken cancellationToken)
    {
        if (!_modelManager.IsInstalled)
            throw new InvalidOperationException("随软件分发的像素生成模型缺失，请重新安装完整离线版本。");

        SetStatus(nodeId, current => current with
        {
            Phase = AiImageGenerationPhase.LoadingModel,
            Progress = 0.04,
            Message = _promptTranslator.IsAvailable
                ? "正在使用内置翻译模型解析中文提示词"
                : "正在使用 RPG 像素素材词典解析提示词",
            Backend = BackendName,
            ModelInstalled = true
        });
        var translatedPrompt = await _promptTranslator
            .TranslateAsync(job.Request.Prompt, cancellationToken)
            .ConfigureAwait(false);
        var model = await EnsureModelLoadedAsync(nodeId, cancellationToken).ConfigureAwait(false);

        await _generationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetStatus(nodeId, current => current with
            {
                Phase = AiImageGenerationPhase.Generating,
                Progress = 0.52,
                Message = job.Reference != null
                    ? $"正在使用 {BackendName} 执行参考图生成 · {ReferenceModeDisplay(job.Request.ReferenceMode)}"
                    : $"正在使用 {BackendName} 生成 512×512 潜空间图像",
                Backend = BackendName,
                ModelInstalled = true
            });

            const int diffusionSize = 512;
            var prompt = PixelArtPromptComposer.Compose(
                translatedPrompt,
                job.Request.Style,
                job.Request.VisualStyle,
                job.Request.ViewAngle,
                job.Request.OutputSize);
            var negative = PixelArtPromptComposer.ComposeNegative(
                job.Request.NegativePrompt,
                translatedPrompt,
                job.Request.Style,
                job.Request.ViewAngle);
            using var compositionGuide = job.Reference == null
                ? AiCompositionGuideFactory.TryCreate(translatedPrompt, job.Request.ViewAngle, job.Request.OutputSize)
                : null;
            var referenceSource = job.Reference ?? compositionGuide;
            ImageGenerationParameter parameters;

            if (referenceSource != null)
            {
                if (compositionGuide != null)
                {
                    SetStatus(nodeId, current => current with
                    {
                        Message = $"已应用 {job.Request.ViewAngle} 建筑构图骨架，正在生成材质与风格",
                        Backend = BackendName
                    });
                }
                if (job.Reference != null)
                    prompt = ComposeReferencePrompt(prompt, job.Request.ReferenceMode);
                var reference = HpphPixelBufferAdapter.ToImage(referenceSource, diffusionSize, diffusionSize);
                var nativeStrength = compositionGuide != null
                    ? 0.30f
                    : ToNativeDenoiseStrength(job.Request.ReferenceMode, job.Request.ReferenceStrength);
                parameters = ImageGenerationParameter.ImageToImage(prompt, reference)
                    .WithSd1Defaults()
                    .WithStrength(nativeStrength);

                if (job.Mask != null)
                {
                    var mask = HpphPixelBufferAdapter.ToImage(job.Mask, diffusionSize, diffusionSize, monochrome: true);
                    parameters.WithMaskImage(mask);
                }
            }
            else
            {
                parameters = ImageGenerationParameter.TextToImage(prompt).WithSd1Defaults();
            }

            parameters.WithNegativePrompt(negative)
                .WithSteps(job.Request.Steps)
                .WithCfg(job.Request.Guidance)
                .WithSeed(job.Request.Seed)
                .WithSize(diffusionSize, diffusionSize)
                .WithSampler(Sampler.Euler_A);

            // Keep object semantics in the general SD 1.5 base and apply pixel
            // styling as a controllable adapter. A moderate multiplier avoids
            // the style adapter overpowering simple subjects such as rocks.
            parameters.Loras.Add(new Lora
            {
                Path = _modelManager.PixelLoraPath,
                Multiplier = job.Request.VisualStyle == "detailed64" ? 0.68f : 0.76f
            });

            Volatile.Write(ref _activeNativeNodeId, nodeId);
            Image<ColorRGB>? generatedImage;
            try
            {
                generatedImage = model.GenerateImage(parameters);
            }
            finally
            {
                Volatile.Write(ref _activeNativeNodeId, 0);
            }

            if (generatedImage == null)
                throw new InvalidOperationException("推理后端未返回图像。请检查显存和模型文件。");

            SetStatus(nodeId, current => current with
            {
                Phase = AiImageGenerationPhase.PixelProcessing,
                Progress = 0.92,
                Message = "正在执行网格对齐、背景清理与调色板量化"
            });

            using var fullSize = HpphPixelBufferAdapter.ToPixelBuffer(generatedImage);
#if DEBUG
            var debugRawPath = Environment.GetEnvironmentVariable("PIXEL_ASSET_DEBUG_RAW_PATH");
            if (!string.IsNullOrWhiteSpace(debugRawPath))
                SaveDebugPng(fullSize, debugRawPath);
#endif
            var options = new PixelArtPostProcessOptions(
                job.Request.OutputSize,
                job.Request.PaletteSize,
                job.Request.Style,
                job.Request.VisualStyle,
                job.Request.BackgroundMode,
                job.Request.Dithering,
                job.Request.AddOutline,
                job.Request.Seed);
            var processed = PixelArtPostProcessor.Process(fullSize, options, job.Mask);
            if (job.Reference == null || job.Request.ReferenceMode != "palette")
                return processed;

            try
            {
                var paletteTransfer = new SmartPaletteTransferNode();
                return paletteTransfer.Process([processed, job.Reference],
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["paletteSize"] = job.Request.PaletteSize,
                        ["strength"] = job.Request.ReferenceStrength,
                        ["preserveLuminance"] = 0.72f,
                        ["finalCleanup"] = true
                    },
                    new PixelGraphContext
                    {
                        TileSize = job.Request.OutputSize,
                        Seed = job.Request.Seed
                    });
            }
            finally
            {
                processed.Dispose();
            }
        }
        finally
        {
            _generationGate.Release();
        }
    }

    private static float ToNativeDenoiseStrength(string mode, float fidelity)
    {
        fidelity = Math.Clamp(fidelity, 0.1f, 0.95f);
        // StableDiffusion.NET Strength is denoising amount: lower values preserve
        // more of the reference. The public UI intentionally exposes the inverse,
        // more understandable "reference fidelity".
        return mode switch
        {
            "strict" => Math.Clamp(0.62f - fidelity * 0.48f, 0.12f, 0.55f),
            "repaint" => Math.Clamp(0.98f - fidelity * 0.42f, 0.55f, 0.92f),
            "palette" => Math.Clamp(0.94f - fidelity * 0.22f, 0.68f, 0.88f),
            _ => Math.Clamp(0.88f - fidelity * 0.62f, 0.25f, 0.78f)
        };
    }

    private static string ComposeReferencePrompt(string prompt, string mode)
        => mode switch
        {
            "strict" => prompt + ", preserve the exact subject silhouette, proportions, pose and camera composition from the reference image",
            "repaint" => prompt + ", use the reference only as a loose visual concept while following the requested subject",
            "palette" => prompt + ", use the color harmony and material mood from the reference, allow a new composition",
            _ => prompt + ", preserve the main silhouette, spatial layout and camera composition from the reference image"
        };

    private static string ReferenceModeDisplay(string mode)
        => mode switch
        {
            "strict" => "高保真复刻",
            "repaint" => "自由重绘",
            "palette" => "参考配色",
            _ => "保留构图"
        };

    private async Task<DiffusionModel> EnsureModelLoadedAsync(int nodeId, CancellationToken cancellationToken)
    {
        if (_model != null) return _model;
        await _modelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_model != null) return _model;
            SetStatus(nodeId, current => current with
            {
                Phase = AiImageGenerationPhase.LoadingModel,
                Progress = 0.50,
                Message = $"首次加载像素模型到 {BackendName}，请稍候",
                Backend = BackendName,
                ModelInstalled = true
            });

            var modelParameters = DiffusionModelParameter.Create()
                .WithModelPath(_modelManager.ModelPath)
                .WithMultithreading(Math.Max(1, Environment.ProcessorCount - 1))
                .WithVaeTiling();
            if (!BackendName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                modelParameters.WithFlashAttention();

            _model = await Task.Run(() => new DiffusionModel(modelParameters), cancellationToken).ConfigureAwait(false);
            return _model;
        }
        finally
        {
            _modelGate.Release();
        }
    }

    private void SetStatus(int nodeId, Func<AiImageGenerationStatus, AiImageGenerationStatus> update)
    {
        if (nodeId <= 0) return;
        var state = _nodes.GetOrAdd(nodeId, _ => new NodeState());
        AiImageGenerationStatus status;
        lock (state.Gate)
        {
            state.Status = update(state.Status);
            status = state.Status;
        }
        RaiseStateChanged(nodeId, status);
    }

    private void RaiseStateChanged(int nodeId, AiImageGenerationStatus status)
        => StateChanged?.Invoke(this, new AiImageNodeStateChangedEventArgs(nodeId, status));

    private static string ConfigureBackends()
    {
        Backends.CpuBackend.IsEnabled = true;
        Backends.CpuBackend.Priority = 0;
        Backends.CudaBackend.IsEnabled = Backends.CudaBackend.IsAvailable;
        Backends.CudaBackend.Priority = 10;
        Backends.RocmBackend.IsEnabled = false;
        Backends.SyclBackend.IsEnabled = false;

        var hasVulkanLoader = false;
        try
        {
            if (NativeLibrary.TryLoad("vulkan-1.dll", out var handle))
            {
                hasVulkanLoader = true;
                NativeLibrary.Free(handle);
            }
        }
        catch { }

        Backends.VulkanBackend.IsEnabled = hasVulkanLoader;
        Backends.VulkanBackend.Priority = 5;

        var selected = Backends.ActiveBackends.OrderByDescending(backend => backend.Priority).FirstOrDefault();
        return selected switch
        {
            CudaBackend => "CUDA 12（NVIDIA）",
            VulkanBackend => "Vulkan（AMD / Intel / NVIDIA）",
            _ => "CPU"
        };
    }

    private static string FriendlyError(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        if (message.Contains("initialize diffusion-model", StringComparison.OrdinalIgnoreCase))
            return "模型加载失败，可能是显存不足或当前显卡后端不可用。可关闭其他占用显存的软件后重试。";
        if (message.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
            return "显存或内存不足。请关闭占用显存的软件后重试。";
        return message;
    }

#if DEBUG
    private static void SaveDebugPng(PixelBuffer source, string path)
    {
        var stride = source.Width * 4;
        var pixels = new byte[stride * source.Height];
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var pixel = source.GetPixel(x, y);
            var offset = y * stride + x * 4;
            pixels[offset] = ToByte(pixel.B);
            pixels[offset + 1] = ToByte(pixel.G);
            pixels[offset + 2] = ToByte(pixel.R);
            pixels[offset + 3] = ToByte(pixel.A);
        }

        var bitmap = BitmapSource.Create(
            source.Width, source.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static byte ToByte(float value)
        => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
#endif

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        foreach (var state in _nodes.Values)
        {
            lock (state.Gate)
            {
                state.Pending?.Dispose();
                state.Pending = null;
                state.Output?.Dispose();
                state.Output = null;
            }
        }

        // Native generation is not cancellable. Dispose only when no call is active;
        // otherwise process shutdown safely reclaims the native context.
        if (_generationGate.CurrentCount == 1)
            _model?.Dispose();
        _promptTranslator.Dispose();
        _shutdown.Dispose();
    }
}
