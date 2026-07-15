using System;
using System.Linq;
using System.Windows;
using PixelAssetGenerator.Core.AiImage;
using PixelAssetGenerator.Services.AiImage;

namespace PixelAssetGenerator;

public partial class MainWindow
{
    private LocalAiImageGenerationService? _localAiImageService;
    private string _aiImageModelStatus = "正在检查内置模型…";
    private string _aiImageTranslatorStatus = "中文翻译：正在检查内置模型…";
    private string _aiImageNodeStatus = "点击生成后开始本地推理";
    private string _aiImageBackendStatus = "后端：正在检测";
    private double _aiImageProgress;
    private bool _aiImageCanGenerate;
    private bool _aiImageCanCancelGeneration;

    public string AiImageModelStatus { get => _aiImageModelStatus; private set => SetField(ref _aiImageModelStatus, value); }
    public string AiImageTranslatorStatus { get => _aiImageTranslatorStatus; private set => SetField(ref _aiImageTranslatorStatus, value); }
    public string AiImageNodeStatus { get => _aiImageNodeStatus; private set => SetField(ref _aiImageNodeStatus, value); }
    public string AiImageBackendStatus { get => _aiImageBackendStatus; private set => SetField(ref _aiImageBackendStatus, value); }
    public double AiImageProgress { get => _aiImageProgress; private set => SetField(ref _aiImageProgress, value); }
    public bool AiImageCanGenerate { get => _aiImageCanGenerate; private set => SetField(ref _aiImageCanGenerate, value); }
    public bool AiImageCanCancelGeneration { get => _aiImageCanCancelGeneration; private set => SetField(ref _aiImageCanCancelGeneration, value); }
    public string AiImageModelDirectories => _localAiImageService == null
        ? "正在初始化内置模型目录…"
        : $"图像模型：{_localAiImageService.ModelDirectory}{Environment.NewLine}翻译模型：{_localAiImageService.TranslationModelDirectory}";
    public string AiImageOutputSizeText
    {
        get
        {
            var size = GetSelectedTileSize();
            return $"{size} × {size}（跟随全局图块大小）";
        }
    }
    public string AiImagePresetSummary
    {
        get
        {
            if (SelectedNode?.RegistryKey != "AiImageGen")
                return "内置风格和视角限制会在生成时自动加入";

            string Display(string name, string fallback)
                => SelectedNode.Parameters.FirstOrDefault(parameter => parameter.Name == name)?.SelectedDisplayChoice
                   ?? fallback;
            return $"内置限制：{Display("visualStyle", "经典 32×32 RPG")} · {Display("viewAngle", "自动匹配")}";
        }
    }
    public string AiImageReferenceStatus
    {
        get
        {
            if (SelectedNode?.RegistryKey != "AiImageGen")
                return "连接参考图后可选择保留构图、高保真复刻、自由重绘或仅参考配色。";
            var connected = NodeConnections.Any(connection => !connection.IsPreview
                && ReferenceEquals(connection.EndNode, SelectedNode)
                && connection.EndPortIndex == 0);
            if (!connected)
                return "当前未连接参考图，将使用文生图；可把任意图像节点连接到“参考图像”端口。";
            var mode = SelectedNode.Parameters.FirstOrDefault(parameter => parameter.Name == "referenceMode")
                ?.SelectedDisplayChoice ?? "保留构图";
            return $"参考图已连接：生成时会实际执行图生图（{mode}）；蒙版可限定重绘区域。";
        }
    }

    private void InitializeLocalAiImageGeneration()
    {
        _localAiImageService = LocalAiImageGenerationService.Instance;
        AiImageGenerationRuntime.Current = _localAiImageService;
        _localAiImageService.StateChanged += LocalAiImageService_StateChanged;
        OnPropertyChanged(nameof(AiImageModelDirectories));
        OnPropertyChanged(nameof(AiImageOutputSizeText));
        UpdateAiImagePanelFromSelection();
    }

    private void LocalAiImageService_StateChanged(object? sender, AiImageNodeStateChangedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (SelectedNode?.Id == e.NodeId)
                ApplyAiImageStatus(e.Status);
            if (e.Status.IsTerminal)
                RequestPreviewRefresh(false);
        });
    }

    private void UpdateAiImagePanelFromSelection()
    {
        if (_localAiImageService == null) return;
        OnPropertyChanged(nameof(AiImagePresetSummary));
        OnPropertyChanged(nameof(AiImageReferenceStatus));
        var status = SelectedNode?.RegistryKey.Equals("AiImageGen", StringComparison.OrdinalIgnoreCase) == true
            ? _localAiImageService.GetStatus(SelectedNode.Id)
            : new AiImageGenerationStatus(AiImageGenerationPhase.Idle, 0,
                "点击生成后开始本地推理", _localAiImageService.BackendName, 0, _localAiImageService.IsModelInstalled);
        ApplyAiImageStatus(status);
    }

    private void ApplyAiImageStatus(AiImageGenerationStatus status)
    {
        AiImageModelStatus = status.ModelInstalled
            ? "图像模型：已随软件内置并通过固定版本校验"
            : "图像模型：缺失或不完整，请重新安装完整离线版本";
        AiImageTranslatorStatus = _localAiImageService?.IsTranslationModelInstalled == true
            ? "中文翻译：OPUS-MT INT8 ONNX 已内置（CPU 推理）"
            : "中文翻译：模型缺失，当前使用 RPG 像素素材词典兜底";
        AiImageBackendStatus = "推理后端：" + status.Backend;
        AiImageNodeStatus = status.Message;
        AiImageProgress = Math.Clamp(status.Progress, 0, 1);
        AiImageCanGenerate = status.ModelInstalled && !status.IsBusy;
        AiImageCanCancelGeneration = status.IsBusy;
    }

    private void AiImageGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (_localAiImageService == null || SelectedNode?.RegistryKey != "AiImageGen") return;
        if (!_localAiImageService.IsModelInstalled)
        {
            AiImageNodeStatus = "内置像素生成模型缺失，请重新安装完整离线版本";
            UpdateAiImagePanelFromSelection();
            return;
        }

        if (_localAiImageService.GetStatus(SelectedNode.Id).IsBusy)
        {
            AiImageNodeStatus = "当前任务尚未结束，请等待或先取消";
            return;
        }

        var prompt = SelectedNode.Parameters.FirstOrDefault(parameter => parameter.Name == "prompt");
        if (string.IsNullOrWhiteSpace(prompt?.TextValue))
        {
            AiImageNodeStatus = "请先输入要生成的素材提示词";
            return;
        }

        var requestVersion = SelectedNode.Parameters.FirstOrDefault(parameter => parameter.Name == "requestVersion");
        if (requestVersion == null)
        {
            AiImageNodeStatus = "节点版本过旧，请删除后重新添加 AI 像素素材生成节点";
            return;
        }

        requestVersion.IntValue = requestVersion.IntValue == int.MaxValue ? 1 : requestVersion.IntValue + 1;
        AiImageNodeStatus = $"正在提交 {AiImageOutputSizeText} 的本地生成请求…";
        RequestPreviewRefresh(false);
    }

    private void AiImageCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_localAiImageService == null || SelectedNode?.RegistryKey != "AiImageGen") return;
        _localAiImageService.Cancel(SelectedNode.Id);
    }

    private void MarkAiImageParametersDirty(NodeViewModel node, NodeParameterViewModel parameter, string? propertyName)
    {
        if (node.RegistryKey != "AiImageGen" || parameter.Name == "requestVersion") return;

        if (parameter.Name is "style" or "visualStyle" or "viewAngle" or "referenceMode"
            && propertyName == nameof(NodeParameterViewModel.SelectedChoice))
        {
            OnPropertyChanged(nameof(AiImagePresetSummary));
            OnPropertyChanged(nameof(AiImageReferenceStatus));
        }

        if (parameter.Name == "style" && propertyName == nameof(NodeParameterViewModel.SelectedChoice))
        {
            var prompt = node.Parameters.FirstOrDefault(candidate => candidate.Name == "prompt");
            if (prompt != null)
            {
                prompt.TextValue = AiImagePromptTemplates.Get(parameter.SelectedChoice);
                AiImageNodeStatus = $"已切换为“{parameter.SelectedDisplayChoice}”提示词模板，可继续编辑后生成";
            }
        }

        if (parameter.Name is "visualStyle" or "viewAngle"
            && propertyName == nameof(NodeParameterViewModel.SelectedChoice))
            AiImageNodeStatus = $"已应用“{parameter.SelectedDisplayChoice}”固定限制，生成时会自动拼入提示词";

        if (_localAiImageService?.GetStatus(node.Id).Phase == AiImageGenerationPhase.Completed)
            AiImageNodeStatus = "参数已修改；当前仍显示上次结果，点击生成以更新";
    }
}
