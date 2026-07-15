using System;
using System.Threading;
using System.Threading.Tasks;

namespace PixelAssetGenerator.Core.AiImage;

public enum AiImageGenerationPhase
{
    Idle,
    Queued,
    Downloading,
    Verifying,
    LoadingModel,
    Generating,
    PixelProcessing,
    Completed,
    Failed,
    Cancelled
}

public sealed record AiImageGenerationRequest(
    int Revision,
    string Prompt,
    string NegativePrompt,
    string Style,
    string VisualStyle,
    string ViewAngle,
    int OutputSize,
    int PaletteSize,
    int Steps,
    float Guidance,
    string ReferenceMode,
    float ReferenceStrength,
    string BackgroundMode,
    string Dithering,
    bool AddOutline,
    int Seed);

public sealed record AiImageGenerationStatus(
    AiImageGenerationPhase Phase,
    double Progress,
    string Message,
    string Backend,
    int Revision,
    bool ModelInstalled)
{
    public bool IsBusy => Phase is AiImageGenerationPhase.Queued
        or AiImageGenerationPhase.Downloading
        or AiImageGenerationPhase.Verifying
        or AiImageGenerationPhase.LoadingModel
        or AiImageGenerationPhase.Generating
        or AiImageGenerationPhase.PixelProcessing;

    public bool IsTerminal => Phase is AiImageGenerationPhase.Completed
        or AiImageGenerationPhase.Failed
        or AiImageGenerationPhase.Cancelled;

    public static AiImageGenerationStatus NotInitialized { get; } = new(
        AiImageGenerationPhase.Idle, 0, "本地生成服务尚未初始化", "未初始化", 0, false);
}

public sealed class AiImageNodeStateChangedEventArgs(int nodeId, AiImageGenerationStatus status) : EventArgs
{
    public int NodeId { get; } = nodeId;
    public AiImageGenerationStatus Status { get; } = status;
}

/// <summary>
/// Dependency-inversion boundary between the synchronous graph evaluator and the
/// asynchronous local diffusion runtime. Implementations must copy input buffers
/// before returning from <see cref="TryRequest"/>.
/// </summary>
public interface IAiImageGenerationRuntime : IDisposable
{
    event EventHandler<AiImageNodeStateChangedEventArgs>? StateChanged;

    AiImageGenerationStatus GetStatus(int nodeId);
    PixelBuffer? GetOutputClone(int nodeId);
    bool TryRequest(int nodeId, AiImageGenerationRequest request, PixelBuffer? referenceImage, PixelBuffer? mask);
    Task EnsureModelAsync(int nodeId, CancellationToken cancellationToken = default);
    void Cancel(int nodeId);
}

/// <summary>Application-owned runtime bridge used by compiled graph nodes.</summary>
public static class AiImageGenerationRuntime
{
    public static IAiImageGenerationRuntime? Current { get; set; }
}
