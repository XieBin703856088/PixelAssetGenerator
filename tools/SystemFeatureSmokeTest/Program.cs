using PixelAssetGenerator;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.Animation.Nodes;
using PixelAssetGenerator.Core.AiImage;
using PixelAssetGenerator.Core.Nodes;
using PixelAssetGenerator.Core.Particles;
using PixelAssetGenerator.Core.Particles.Nodes;
using PixelAssetGenerator.Core.Physics.Nodes;
using PixelAssetGenerator.Services;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static Dictionary<string, object> P(params (string Key, object Value)[] values)
    => values.ToDictionary(pair => pair.Key, pair => pair.Value);

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static double MaskCoverage(PixelBuffer mask, float threshold = 0.25f)
{
    var active = 0;
    for (var y = 0; y < mask.Height; y++)
    for (var x = 0; x < mask.Width; x++)
        if (mask.GetPixel(x, y).R >= threshold) active++;
    return active / (double)(mask.Width * mask.Height);
}

static double ImageDifference(PixelBuffer a, PixelBuffer b)
{
    var width = Math.Min(a.Width, b.Width);
    var height = Math.Min(a.Height, b.Height);
    double difference = 0;
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var pa = a.GetPixel(x, y);
        var pb = b.GetPixel(x, y);
        difference += Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B) + Math.Abs(pa.A - pb.A);
    }
    return difference / Math.Max(1, width * height * 4);
}

static double AlphaCoverage(PixelBuffer image, float threshold = 0.05f)
{
    var active = 0;
    for (var y = 0; y < image.Height; y++)
    for (var x = 0; x < image.Width; x++)
        if (image.GetPixel(x, y).A >= threshold) active++;
    return active / (double)(image.Width * image.Height);
}

static void SavePixelArtPng(PixelBuffer buffer, string path, int scale = 8)
{
    using var enlarged = PixelBufferPool.Borrow(buffer.Width * scale, buffer.Height * scale);
    for (var y = 0; y < enlarged.Height; y++)
    for (var x = 0; x < enlarged.Width; x++)
    {
        var pixel = buffer.GetPixel(x / scale, y / scale);
        enlarged.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
    }
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(enlarged.ToBitmapSource()));
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    encoder.Save(stream);
}

var context = new PixelGraphContext
{
    TileSize = 32,
    AnimationTime = 0.75f,
    AnimationFrame = 6,
    AnimationFrameCount = 8,
    DeltaTime = 1f / 12f,
    GlobalTime = 0.5f,
    Seed = 42
};

Require(GraphNodeRegistry.Create("SmartMaterialWeathering") is SmartMaterialWeatheringNode,
    "智能材质风化节点未注册到节点库");
Require(GraphNodeRegistry.Create("SmartCrackDamage") is SmartCrackDamageNode,
    "智能裂纹破损节点未注册到节点库");
Require(GraphNodeRegistry.Create("SpriteEffectAnimator") is SpriteEffectAnimatorNode,
    "精灵特效动画节点未注册到节点库");
Require(GraphNodeRegistry.Create("AnimationWorkflowOutput") is AnimationWorkflowOutputNode,
    "动画工作流输出元节点未注册到节点库");
Require(GraphNodeRegistry.Create("SpriteMotionMeta") is SpriteMotionMetaNode,
    "精灵动作元节点未注册到节点库");
Require(GraphNodeRegistry.Create("ParticleEffectMeta") is ParticleEffectMetaNode,
    "完整粒子元节点未注册到节点库");
Require(GraphNodeRegistry.Create("MaterialEffectStackMeta") is MaterialEffectStackMetaNode,
    "材质效果栈元节点未注册到节点库");
Require(GraphNodeRegistry.Create("TileVariationMeta") is TileVariationMetaNode,
    "图块变体元节点未注册到节点库");
Require(GraphNodeRegistry.Create("PhysicsSprite") is PhysicsSpriteNode,
    "精灵物理动画节点未注册到节点库");
Require(GraphNodeRegistry.Create("PhysicsSimulate") is PhysicsSimulateNode,
    "粒子刚体模拟节点未注册到节点库");
Require(GraphNodeRegistry.Create("PhysicsField") is PhysicsFieldNode,
    "物理力场节点未注册到节点库");
Require(GraphNodeRegistry.Create("PhysicsSoftBody") is PhysicsSoftBodyNode,
    "软体网格节点未注册到节点库");
Require(GraphNodeRegistry.Create("ColorExtraction") is PaletteExtractionNode,
    "ColorExtraction did not resolve to the parameter-aware compiled node");
Require(GraphNodeRegistry.Create("Cache") is ImageCacheNode,
    "Cache did not resolve to the stateful compiled node");
Require(GraphNodeRegistry.Create("Constant") is ConstantValueNode,
    "Constant did not resolve to the real multi-output node");
Require(GraphNodeRegistry.Create("ImageAnalysis") is ImageAnalysisNode,
    "ImageAnalysis did not resolve to the real multi-output node");
Require(GraphNodeRegistry.Create("Output") is GraphOutputNode,
    "Output did not resolve to the parameter-aware compiled node");
Require(GraphNodeRegistry.Create("Preview") is GraphPreviewNode,
    "Preview did not resolve to the parameter-aware compiled node");
Require(GraphNodeRegistry.Create("Text") is PixelTextNode,
    "Text did not resolve to the spacing-aware compiled node");

var captureRuntime = new CaptureAiRuntime();
AiImageGenerationRuntime.Current = captureRuntime;
try
{
    var aiNode = new AiImageGenNode { NodeInstanceId = 7_701 };
    using var aiReference = PixelBuffer.CreateSolid(19, 23, 0.16f, 0.72f, 0.34f, 1f);
    using var aiPlaceholder = aiNode.Process([aiReference, null], P(
        ("prompt", "参考图测试"), ("requestVersion", 1),
        ("referenceMode", "strict"), ("referenceStrength", 0.82f)), context);
    Require(captureRuntime.ReferenceWidth == 19 && captureRuntime.ReferenceHeight == 23,
        "AI 参考图没有从节点输入端口传入生成运行时");
    Require(captureRuntime.Request?.ReferenceMode == "strict"
            && Math.Abs(captureRuntime.Request.ReferenceStrength - 0.82f) < 0.001f,
        "AI 参考图模式或保真度没有进入生成请求");
}
finally
{
    AiImageGenerationRuntime.Current = null;
    captureRuntime.Dispose();
}

var workflowASource = new NodeViewModel("动作 A", 0, 0) { TypeName = "SpriteMotionMeta" };
var workflowAOutput = new NodeViewModel("输出 A", 200, 0) { TypeName = "AnimationWorkflowOutput" };
var workflowBRender = new NodeViewModel("粒子 B", 0, 200) { TypeName = "ParticleRender" };
var workflowBOutput = new NodeViewModel("输出 B", 200, 200) { TypeName = "AnimationWorkflowOutput" };
var workflowNodes = new List<NodeViewModel> { workflowBRender, workflowBOutput, workflowASource, workflowAOutput };
var workflowConnections = new List<NodeConnectionViewModel>
{
    new() { StartNode = workflowASource, EndNode = workflowAOutput },
    new() { StartNode = workflowBRender, EndNode = workflowBOutput }
};
Require(ReferenceEquals(WorkflowPreviewResolver.Resolve(workflowNodes, workflowConnections, workflowASource), workflowAOutput),
    "多工作流预览跳转到了其他动画工作流");

var timeline = new AnimationTimelineNode();
var timelineOutputs = timeline.ProcessMulti([], P(("pulseEvery", 2), ("pulseWidth", 1)), context);
Require(timelineOutputs.Length == 5, "动画时间轴输出数量错误");
Require(timelineOutputs[0].GetPixel(0, 0).R is >= 0f and <= 1f, "动画时间超出范围");
foreach (var output in timelineOutputs) output.Dispose();

var audioReactive = new AudioReactiveNode();
var audioLow = audioReactive.ProcessMulti([], P(("channel", "low"), ("smoothing", 0.15f)), context);
var audioHigh = audioReactive.ProcessMulti([], P(("channel", "high"), ("smoothing", 0.15f)), context);
try
{
    Require(Math.Abs(audioLow[0].GetPixel(0, 0).R - audioLow[1].GetPixel(0, 0).R) < 0.00001f,
        "AudioReactive channel=low did not drive the primary amplitude output");
    Require(Math.Abs(audioHigh[0].GetPixel(0, 0).R - audioHigh[2].GetPixel(0, 0).R) < 0.00001f,
        "AudioReactive channel=high did not drive the primary amplitude output");
}
finally
{
    foreach (var output in audioLow) output.Dispose();
    foreach (var output in audioHigh) output.Dispose();
}

var motion = new MotionPresetNode();
var motionOutputs = motion.ProcessMulti([], P(("preset", "hop"), ("strength", 0.1f)), context);
Require(motionOutputs.Length == 4, "动作预设输出数量错误");
foreach (var output in motionOutputs) output.Dispose();

using (var sheet = PixelBuffer.CreateSolid(4, 2, 1f, 0f, 0f, 1f))
{
    for (var y = 0; y < 2; y++)
    for (var x = 2; x < 4; x++)
        sheet.SetPixel(x, y, 0f, 1f, 0f, 1f);
    var animator = new SpriteAnimatorNode();
    using var frame = animator.Process([sheet, null], P(("columns", 2), ("rows", 1),
        ("frameCount", 2), ("speed", 1f), ("playback", "loop"), ("fitCanvas", true)), context);
    Require(frame.GetPixel(16, 16).G > 0.9f, "精灵表没有提取预期帧");
}

using (var sprite = PixelBuffer.CreateSolid(8, 8, 0f, 0f, 0f, 0f))
{
    for (var y = 2; y <= 5; y++)
    for (var x = 2; x <= 4; x++)
        sprite.SetPixel(x, y, 0.9f, 0.2f, 0.1f, 1f);
    var framer = new SmartSpriteFramerNode();
    using var framed = framer.Process([sprite], P(("detectMode", "alpha"), ("alignment", "ground"),
        ("padding", 2), ("occupancy", 0.88f)), context);
    Require(framed.Width == 32 && framed.Height == 32, "智能取景输出尺寸错误");
    var foundOpaquePixel = false;
    var framedData = framed.AsReadOnlySpan();
    for (var i = 3; i < framedData.Length; i += 4)
        if (framedData[i] > 0.9f) { foundOpaquePixel = true; break; }
    Require(foundOpaquePixel, "智能取景丢失了主体");
    using var smoothFramed = framer.Process([sprite], P(("detectMode", "alpha"), ("alignment", "ground"),
        ("padding", 2), ("occupancy", 0.83f), ("pixelSnap", false)), context);
    using var snappedFramed = framer.Process([sprite], P(("detectMode", "alpha"), ("alignment", "ground"),
        ("padding", 2), ("occupancy", 0.83f), ("pixelSnap", true)), context);
    Require(ImageDifference(smoothFramed, snappedFramed) > 0.0001,
        "SmartSpriteFramer pixelSnap parameter did not change sampling");
}

using (var colorTest = PixelBufferPool.Borrow(32, 32))
{
    for (var y = 0; y < colorTest.Height; y++)
    for (var x = 0; x < colorTest.Width; x++)
    {
        var red = x / 31f;
        var green = y / 31f;
        var blue = ((x * 7 + y * 13) % 32) / 31f;
        colorTest.SetPixel(x, y, red, green, blue, 1f);
    }
    var quantize = new ColorQuantizeNode();
    using var rgbQuantized = quantize.Process([colorTest], P(("numColors", 6), ("ditherMode", "none"),
        ("colorSpace", "RGB")), context);
    using var hsvQuantized = quantize.Process([colorTest], P(("numColors", 6), ("ditherMode", "none"),
        ("colorSpace", "HSVWeighted")), context);
    Require(ImageDifference(rgbQuantized, hsvQuantized) > 0.0001,
        "ColorQuantize colorSpace parameter did not change palette matching");
}

using (var noisy = PixelBuffer.CreateSolid(32, 32, 0f, 0f, 0f, 0f))
{
    for (var y = 5; y < 27; y++)
    for (var x = 5; x < 27; x++)
    {
        var value = GraphNodeBase.HashToUnit(x, y, 7);
        noisy.SetPixel(x, y, value, 0.25f + value * 0.35f, 0.15f, 1f);
    }
    var polish = new SmartPixelPolishNode();
    using var polished = polish.Process([noisy], P(("targetStyle", "crisp32"), ("palette", "8"),
        ("cleanup", 0.7f)), context);
    Require(polished.Width == 32 && polished.Height == 32, "智能像素整理输出尺寸错误");

    using var reference = PixelBuffer.CreateSolid(32, 32, 0.1f, 0.2f, 0.75f, 1f);
    for (var y = 0; y < 16; y++)
    for (var x = 0; x < 32; x++)
        reference.SetPixel(x, y, 0.75f, 0.25f, 0.65f, 1f);
    var transfer = new SmartPaletteTransferNode();
    using var transferred = transfer.Process([polished, reference], P(("paletteSize", 6),
        ("strength", 0.9f), ("preserveLuminance", 0.6f), ("finalCleanup", true)), context);
    Require(transferred.Width == 32 && transferred.Height == 32, "智能参考配色输出尺寸错误");
}

using (var utilitySource = PixelBufferPool.Borrow(32, 32))
{
    for (var y = 0; y < utilitySource.Height; y++)
    for (var x = 0; x < utilitySource.Width; x++)
    {
        var band = (x / 4 + y / 8) % 5;
        utilitySource.SetPixel(x, y, band / 4f, y / 31f, 1f - x / 31f,
            (x + y) % 7 == 0 ? 0.45f : 1f);
    }

    var paletteNode = new PaletteExtractionNode();
    var uniformPalette = paletteNode.ProcessMulti([utilitySource], P(("colorCount", 5),
        ("sampleMode", "uniform"), ("sortMode", "luminance"), ("seed", 17)), context);
    var clusteredPalette = paletteNode.ProcessMulti([utilitySource], P(("colorCount", 5),
        ("sampleMode", "kmeans"), ("sortMode", "hue"), ("seed", 17)), context);
    try
    {
        Require(uniformPalette.Length == 2 && clusteredPalette.Length == 2,
            "ColorExtraction did not return quantized image and palette outputs");
        Require(ImageDifference(uniformPalette[0], clusteredPalette[0]) > 0.0001,
            "ColorExtraction sampleMode/sortMode parameters did not affect its result");
    }
    finally
    {
        foreach (var output in uniformPalette) output.Dispose();
        foreach (var output in clusteredPalette) output.Dispose();
    }

    var analysis = new ImageAnalysisNode();
    var basicAnalysis = analysis.ProcessMulti([utilitySource], P(("analysisMode", "basic")), context);
    var colorAnalysis = analysis.ProcessMulti([utilitySource], P(("analysisMode", "color")), context);
    try
    {
        Require(basicAnalysis.Length == 7 && colorAnalysis.Length == 7,
            "ImageAnalysis did not return all seven declared outputs");
        Require(basicAnalysis[0].GetPixel(0, 0).R > 0f && basicAnalysis[4].GetPixel(0, 0).R == 0f,
            "ImageAnalysis basic mode did not gate color metrics");
        Require(colorAnalysis[0].GetPixel(0, 0).R == 0f && colorAnalysis[5].GetPixel(0, 0).R > 0f,
            "ImageAnalysis color mode did not gate basic metrics");
    }
    finally
    {
        foreach (var output in basicAnalysis) output.Dispose();
        foreach (var output in colorAnalysis) output.Dispose();
    }

    var preview = new GraphPreviewNode();
    using var redPreview = preview.Process([utilitySource], P(("displayMode", "R"),
        ("tilePreview", 1), ("background", "transparent"), ("scale", 1f)), context);
    using var greenTiledPreview = preview.Process([utilitySource], P(("displayMode", "G"),
        ("tilePreview", 3), ("background", "white"), ("scale", 1.35f)), context);
    Require(ImageDifference(redPreview, greenTiledPreview) > 0.01,
        "Preview display/tile/background/scale parameters did not affect the preview");

    var outputNode = new GraphOutputNode();
    using var resizedOutput = outputNode.Process([utilitySource], P(("outputSize", "16"),
        ("outputScale", 2f), ("outputFormat", "PNG"), ("premultipliedAlpha", false),
        ("background", "white")), context);
    Require(resizedOutput.Width == 32 && resizedOutput.Height == 32,
        "Output size and scale parameters were ignored");
    Require(resizedOutput.GetPixel(0, 0).A > 0.99f,
        "Output background parameter did not flatten alpha");
}

var constant = new ConstantValueNode();
var constantOutputs = constant.ProcessMulti([], P(("outputType", "color"),
    ("floatValue", 0.1f), ("colorValue", Color.FromRgb(255, 0, 0))), context);
try
{
    Require(constantOutputs.Length == 2 && constantOutputs[1].GetPixel(0, 0).R > 0.99f,
        "Constant did not provide its declared float and color outputs");
    Require(constantOutputs[0].GetPixel(0, 0).R > 0.2f,
        "Constant outputType did not synchronize the scalar representation");
}
finally
{
    foreach (var output in constantOutputs) output.Dispose();
}

var cache = new ImageCacheNode();
using (var cachedRedInput = PixelBuffer.CreateSolid(4, 4, 1f, 0f, 0f, 1f))
using (var cachedGreenInput = PixelBuffer.CreateSolid(4, 4, 0f, 1f, 0f, 1f))
using (var cachedRed = cache.Process([cachedRedInput], P(("cacheKey", "cache1"), ("expireFrames", 3)),
           context with { AnimationFrame = 0 }))
using (var stillCachedRed = cache.Process([cachedGreenInput], P(("cacheKey", "cache1"), ("expireFrames", 3)),
           context with { AnimationFrame = 1 }))
using (var refreshedGreen = cache.Process([cachedGreenInput], P(("cacheKey", "cache1"), ("expireFrames", 3)),
           context with { AnimationFrame = 3 }))
{
    Require(cachedRed.GetPixel(0, 0).R > 0.99f && stillCachedRed.GetPixel(0, 0).R > 0.99f,
        "Cache key/expiry did not retain the cached frame");
    Require(refreshedGreen.GetPixel(0, 0).G > 0.99f,
        "Cache expireFrames did not refresh the cached image");
}
(cache.PersistentState as IDisposable)?.Dispose();
cache.PersistentState = null;

var brush = new BrushStampNode();
Require(brush.Parameters.All(parameter => parameter.Name != "spacing"),
    "Brush still exposes a path-spacing parameter despite having no path input");
var textNode = new PixelTextNode();
var tightText = textNode.ProcessMulti([], P(("text", "II"), ("fontSize", 14), ("spacing", 0),
    ("showBg", false)), context);
var spacedText = textNode.ProcessMulti([], P(("text", "II"), ("fontSize", 14), ("spacing", 8),
    ("showBg", false)), context);
try
{
    Require(tightText.Length == 2 && ImageDifference(tightText[0], spacedText[0]) > 0.0001,
        "Text spacing parameter did not change rendered text or provide a mask output");
}
finally
{
    foreach (var output in tightText) output.Dispose();
    foreach (var output in spacedText) output.Dispose();
}

var visualOutputDirectory = Path.Combine(Path.GetTempPath(), "PixelAssetGenerator-Smoke");
Directory.CreateDirectory(visualOutputDirectory);

var wallNode = new PixelWallNode();
using (var wall = wallNode.Process([], P(
           ("wallType", "stone"),
           ("mainColor", Color.FromRgb(142, 73, 55)),
           ("mortarColor", Color.FromRgb(75, 62, 57)),
           ("width", 1f), ("height", 1f), ("seed", 42)), context))
{
    var crackNode = new SmartCrackDamageNode();
    var crackOutputs = crackNode.ProcessMulti([wall], P(
        ("material", "brick"), ("damage", 0.64f), ("crackWidth", 0.38f),
        ("chips", 0.48f), ("depth", 0.72f), ("networkScale", 6),
        ("breakThrough", false), ("seed", 87)), context);
    using var cracked = crackOutputs[0];
    using var crackMask = crackOutputs[1];
    var crackCoverage = MaskCoverage(crackMask);
    Require(crackCoverage is > 0.025 and < 0.58, $"裂纹覆盖率异常: {crackCoverage:P1}");
    SavePixelArtPng(cracked, Path.Combine(visualOutputDirectory, "cracked-brick.png"));

    var weatheringNode = new SmartMaterialWeatheringNode();
    var corrosionOutputs = weatheringNode.ProcessMulti([cracked, null], P(
        ("effect", "corrosion"), ("amount", 0.56f), ("clusterScale", 0.42f),
        ("edgeAffinity", 0.78f), ("colorStrength", 0.86f),
        ("preserveShading", 0.72f), ("palette", "natural"),
        ("pixelClusters", true), ("seamless", true), ("seed", 42)), context);
    using var corrosion = corrosionOutputs[0];
    using var corrosionMask = corrosionOutputs[1];
    var corrosionCoverage = MaskCoverage(corrosionMask);
    var corrosionScore = AestheticEvaluator.Evaluate(corrosion);
    Require(corrosionCoverage is > 0.06 and < 0.82, $"腐蚀覆盖率异常: {corrosionCoverage:P1}");
    Require(corrosionScore.PixelPurity >= 0.95, "腐蚀效果产生了过多半透明杂点");
    Require(corrosionScore.Overall >= 0.40, $"腐蚀效果美术启发式评分过低: {corrosionScore.Overall:F2}");
    SavePixelArtPng(corrosion, Path.Combine(visualOutputDirectory, "corroded-brick.png"));
}

var flagstoneNode = new PixelFlagstoneNode();
using (var flagstone = flagstoneNode.Process([], P(("seed", 54)), context))
{
    var weatheringNode = new SmartMaterialWeatheringNode();
    var mossOutputs = weatheringNode.ProcessMulti([flagstone, null], P(
        ("effect", "moss"), ("amount", 0.62f), ("clusterScale", 0.48f),
        ("edgeAffinity", 0.82f), ("directionBias", 0.28f),
        ("colorStrength", 0.88f), ("preserveShading", 0.76f),
        ("palette", "natural"), ("pixelClusters", true), ("seamless", true), ("seed", 54)), context);
    using var moss = mossOutputs[0];
    using var mossMask = mossOutputs[1];
    var mossCoverage = MaskCoverage(mossMask);
    var mossScore = AestheticEvaluator.Evaluate(moss);
    Require(mossCoverage is > 0.08 and < 0.86, $"苔藓覆盖率异常: {mossCoverage:P1}");
    Require(mossScore.PixelPurity >= 0.95, "苔藓效果产生了过多半透明杂点");
    Require(mossScore.Overall >= 0.40, $"苔藓效果美术启发式评分过低: {mossScore.Overall:F2}");
    SavePixelArtPng(moss, Path.Combine(visualOutputDirectory, "mossy-flagstone.png"));

    var materialMeta = new MaterialEffectStackMetaNode();
    var materialOutputs = materialMeta.ProcessMulti([flagstone], P(
        ("preset", "mossyRuins"), ("effectAmount", 0.58f), ("damageAmount", 0.34f),
        ("edgeAffinity", 0.78f), ("seamless", true), ("seed", 92)), context);
    using var ruinedMaterial = materialOutputs[0];
    using var ruinedMask = materialOutputs[1];
    var ruinedCoverage = MaskCoverage(ruinedMask);
    var ruinedScore = AestheticEvaluator.Evaluate(ruinedMaterial);
    Require(ruinedCoverage is > 0.05 and < 0.92, $"元节点材质遮罩覆盖率异常: {ruinedCoverage:P1}");
    Require(ruinedScore.PixelPurity >= 0.95, "元节点材质效果产生了过多半透明杂点");
    Require(ruinedScore.Overall >= 0.40, $"元节点材质美术评分过低: {ruinedScore.Overall:F2}");
    SavePixelArtPng(ruinedMaterial, Path.Combine(visualOutputDirectory, "meta-mossy-ruins.png"));

    var variationMeta = new TileVariationMetaNode();
    var variants = variationMeta.ProcessMulti([flagstone], P(
        ("style", "natural"), ("variation", 0.22f),
        ("allowMirror", true), ("wrapOffset", true), ("seed", 42)), context);
    try
    {
        Require(variants.Length == 4, "图块变体元节点没有输出四个变体");
        Require(ImageDifference(variants[0], variants[1]) > 0.01, "图块变体 A/B 过于相似");
        Require(ImageDifference(variants[0], variants[2]) > 0.005, "图块变体 A/C 过于相似");
        for (var i = 0; i < variants.Length; i++)
            SavePixelArtPng(variants[i], Path.Combine(visualOutputDirectory, $"meta-tile-variant-{i + 1}.png"));
    }
    finally
    {
        foreach (var variant in variants) variant.Dispose();
    }
}

var slimeNode = new PixelSlimeNode();
using (var slime = slimeNode.Process([], P(("seed", 42)), context))
{
    var spriteMotionMeta = new SpriteMotionMetaNode();
    var motionParameters = P(("motion", "hop"), ("effect", "pulseGlow"),
        ("motionStrength", 0.16f), ("effectStrength", 0.55f), ("speed", 1f),
        ("phase", 0f), ("pixelSnap", true), ("seed", 42));
    using var motionFrameA = spriteMotionMeta.Process([slime, null], motionParameters,
        context with { AnimationTime = 0.25f, GlobalTime = 0.25f });
    using var motionFrameB = spriteMotionMeta.Process([slime, null], motionParameters,
        context with { AnimationTime = 0.58f, GlobalTime = 0.58f });
    Require(ImageDifference(motionFrameA, motionFrameB) > 0.018, "精灵动作元节点前后帧没有明显变化");
    SavePixelArtPng(motionFrameA, Path.Combine(visualOutputDirectory, "meta-motion-frame-a.png"));
    SavePixelArtPng(motionFrameB, Path.Combine(visualOutputDirectory, "meta-motion-frame-b.png"));
}

using (var physicsSpriteSource = PixelBuffer.CreateSolid(32, 32, 0f, 0f, 0f, 0f))
{
    for (var y = 10; y < 25; y++)
    for (var x = 10; x < 22; x++)
        physicsSpriteSource.SetPixel(x, y, 0.22f, 0.82f, 0.46f, 1f);

    var physicsSprite = new PhysicsSpriteNode();
    var physicsParameters = P(("preset", "jelly"), ("strength", 0.82f),
        ("speed", 1f), ("restitution", 0.72f), ("pixelSnap", true));
    var early = physicsSprite.ProcessMulti([physicsSpriteSource, null], physicsParameters,
        context with { AnimationTime = 0.10f, GlobalTime = 0.10f });
    var late = physicsSprite.ProcessMulti([physicsSpriteSource, null], physicsParameters,
        context with { AnimationTime = 0.75f, GlobalTime = 0.75f });
    try
    {
        Require(ImageDifference(early[0], late[0]) > 0.018,
            "精灵物理动画在不同帧没有产生可见形变");
        Require(early.Length == 3 && early[2].Width == 1,
            "精灵物理动画没有输出图像、接触遮罩和运动进度");
        SavePixelArtPng(early[0], Path.Combine(visualOutputDirectory, "physics-jelly-frame-a.png"));
        SavePixelArtPng(late[0], Path.Combine(visualOutputDirectory, "physics-jelly-frame-b.png"));
    }
    finally
    {
        foreach (var output in early) output.Dispose();
        foreach (var output in late) output.Dispose();
    }
}

var softBody = new PhysicsSoftBodyNode();
using (var softEarly = softBody.Process([], P(("shape", "rectangle"), ("pinTop", true),
           ("wind", 0.18f), ("gravity", 0.2f), ("gridX", 6), ("gridY", 6)),
           context with { DeltaTime = 1f / 15f }))
{
    PixelBuffer? softLate = null;
    try
    {
        for (var frame = 0; frame < 12; frame++)
        {
            softLate?.Dispose();
            softLate = softBody.Process([], P(("shape", "rectangle"), ("pinTop", true),
                ("wind", 0.18f), ("gravity", 0.2f), ("gridX", 6), ("gridY", 6)),
                context with { DeltaTime = 1f / 15f, AnimationFrame = frame });
        }
        Require(softLate != null && AlphaCoverage(softLate) > 0.01,
            "软体网格没有绘制可见的质点与弹簧");
        Require(ImageDifference(softEarly, softLate!) > 0.002,
            "软体网格在风力和重力下没有发生形变");
        SavePixelArtPng(softLate!, Path.Combine(visualOutputDirectory, "physics-soft-body.png"));
    }
    finally
    {
        softLate?.Dispose();
    }
}
var originalSoftBodyState = softBody.GetOrCreateState(P(("shape", "rectangle"), ("gridX", 5),
    ("gridY", 5), ("size", 0.3f), ("stiffness", 0.5f)), context);
var rebuiltSoftBodyState = softBody.GetOrCreateState(P(("shape", "circle"), ("gridX", 7),
    ("gridY", 6), ("size", 0.42f), ("stiffness", 0.8f), ("pinTop", true)), context);
Require(!ReferenceEquals(originalSoftBodyState, rebuiltSoftBodyState)
        && rebuiltSoftBodyState.Points.Length == 42
        && rebuiltSoftBodyState.Shape == "circle"
        && rebuiltSoftBodyState.PinTop,
    "PhysicsSoftBody structural parameters were ignored after state creation");

var particleMeta = new ParticleEffectMetaNode();
PixelBuffer? metaParticleFrame = null;
try
{
    var particleParameters = P(("effect", "magic"), ("intensity", 0.9f), ("scale", 0.9f),
        ("positionX", 0.5f), ("positionY", 0.58f), ("wind", 0.04f),
        ("seamless", true), ("prewarm", true), ("seed", 42));
    for (var frameIndex = 0; frameIndex < 10; frameIndex++)
    {
        metaParticleFrame?.Dispose();
        metaParticleFrame = particleMeta.Process([null], particleParameters,
            context with { AnimationFrame = frameIndex, AnimationTime = frameIndex / 10f,
                GlobalTime = frameIndex / 15f, DeltaTime = 1f / 15f });
    }
    Require(metaParticleFrame != null && AlphaCoverage(metaParticleFrame) > 0.008,
        "完整粒子元节点没有生成可见粒子");
    var metaState = particleMeta.PersistentState as ParticleEffectMetaNode.MetaParticleState;
    Require(metaState != null, "完整粒子元节点没有保留模拟状态");
    var verifiedMetaState = metaState!;
    var metaBaseSpeed = verifiedMetaState.Emitter.SpeedMax;
    var metaBaseLife = verifiedMetaState.Emitter.LifeMax;
    metaParticleFrame!.Dispose();
    metaParticleFrame = particleMeta.Process([null], P(("effect", "magic"), ("intensity", 0.9f), ("scale", 0.9f),
        ("speedMultiplier", 2f), ("lifespanMultiplier", 2f), ("gravityMultiplier", 1f),
        ("positionX", 0.5f), ("positionY", 0.58f), ("wind", 0.04f),
        ("seamless", true), ("prewarm", true), ("seed", 42)),
        context with { AnimationFrame = 10, GlobalTime = 10f / 15f, DeltaTime = 1f / 120f });
    Require(verifiedMetaState.Emitter.SpeedMax > metaBaseSpeed * 1.9f,
        "完整粒子元节点的速度倍率没有进入发射器");
    Require(verifiedMetaState.Emitter.LifeMax > metaBaseLife * 1.9f,
        "完整粒子元节点的寿命倍率没有进入发射器");
    SavePixelArtPng(metaParticleFrame!, Path.Combine(visualOutputDirectory, "meta-particle-magic.png"));
}
finally
{
    metaParticleFrame?.Dispose();
    (particleMeta.PersistentState as IDisposable)?.Dispose();
    particleMeta.PersistentState = null;
}

var iconNode = new PixelIconNode();
using (var icon = iconNode.Process([], P(("iconType", "star"), ("size", 0.7f)), context))
{
    var spriteEffect = new SpriteEffectAnimatorNode();
    using var earlyFrame = spriteEffect.Process([icon, null], P(
        ("effect", "dissolve"), ("speed", 1f), ("strength", 1f),
        ("effectColor", Color.FromRgb(120, 225, 255)), ("pixelStep", 1),
        ("pingPong", true), ("seed", 42)), context with { AnimationTime = 0.12f });
    using var lateFrame = spriteEffect.Process([icon, null], P(
        ("effect", "dissolve"), ("speed", 1f), ("strength", 1f),
        ("effectColor", Color.FromRgb(120, 225, 255)), ("pixelStep", 1),
        ("pingPong", true), ("seed", 42)), context with { AnimationTime = 0.56f });
    Require(ImageDifference(earlyFrame, lateFrame) > 0.025, "精灵溶解动画前后帧没有明显变化");
    SavePixelArtPng(earlyFrame, Path.Combine(visualOutputDirectory, "dissolve-frame-a.png"));
    SavePixelArtPng(lateFrame, Path.Combine(visualOutputDirectory, "dissolve-frame-b.png"));
}

var emitterNode = new ParticleEmitterNode();
var emitterParameters = P(("preset", "fire"), ("presetIntensity", 1f), ("presetScale", 1f),
    ("maxParticles", 128), ("prewarm", false));
var emitterState = emitterNode.GetOrCreateState(emitterParameters, context);
Require(emitterState.Buffer.ActiveCount > 0, "火焰预设没有产生初始粒子");
var baseEmitterSpeed = emitterState.Emitter.SpeedMax;
var baseEmitterLife = emitterState.Emitter.LifeMax;
var baseParticleSpeed = MathF.Sqrt(emitterState.Buffer.AsSpan()[0].VX * emitterState.Buffer.AsSpan()[0].VX +
    emitterState.Buffer.AsSpan()[0].VY * emitterState.Buffer.AsSpan()[0].VY);
var baseParticleLife = emitterState.Buffer.AsSpan()[0].MaxLife;
var tunedEmitterParameters = P(("preset", "fire"), ("presetIntensity", 1f), ("presetScale", 1f),
    ("speed", 0.6f), ("lifespan", 4f), ("maxParticles", 128), ("prewarm", false));
emitterNode.SimulateFrame(tunedEmitterParameters, context with { DeltaTime = 0f });
var tunedParticleSpeed = MathF.Sqrt(emitterState.Buffer.AsSpan()[0].VX * emitterState.Buffer.AsSpan()[0].VX +
    emitterState.Buffer.AsSpan()[0].VY * emitterState.Buffer.AsSpan()[0].VY);
Require(emitterState.Emitter.SpeedMax > baseEmitterSpeed * 1.9f,
    "粒子预设覆盖了用户设置的速度");
Require(emitterState.Emitter.LifeMax > baseEmitterLife * 1.9f,
    "粒子预设覆盖了用户设置的寿命");
Require(tunedParticleSpeed > baseParticleSpeed * 1.9f,
    "速度修改没有实时作用于已经存在的粒子");
Require(emitterState.Buffer.AsSpan()[0].MaxLife > baseParticleLife * 1.9f,
    "寿命修改没有实时作用于已经存在的粒子");
emitterNode.SimulateFrame(emitterParameters, context with { DeltaTime = 0f });
var behaviorNode = new ParticleBehaviorNode();
behaviorNode.ApplyBehavior(emitterState.Buffer, P(("behavior", "flicker"), ("strength", 0.4f),
    ("frequency", 4f), ("randomPhase", true), ("seed", 42)), context);

var trailNode = new ParticleTrailNode();
for (var frameIndex = 0; frameIndex < 4; frameIndex++)
{
    emitterState.Simulator.Update(1f / 15f, emitterState.Buffer);
    trailNode.SimulateFrame(P(("trailLength", 0.38f), ("segments", 4),
        ("fadeAlpha", 0.68f), ("sizeScale", 0.5f)),
        context with { DeltaTime = 1f / 15f, AnimationFrame = frameIndex }, emitterState.Buffer);
}
var ghostCount = 0;
var particleSpan = emitterState.Buffer.AsSpan();
for (var i = 0; i < emitterState.Buffer.ActiveCount; i++)
    if (particleSpan[i].Active && particleSpan[i].IsTrailGhost) ghostCount++;
Require(ghostCount > 0, "粒子拖尾没有生成残影粒子");
Require(ghostCount < emitterState.Buffer.Capacity / 2, "粒子拖尾残影数量失控");

var renderNode = new ParticleRenderNode();
using (renderNode.Process([null, null, null], P(("texture", "auto"), ("blendMode", "auto")), context)) { }
renderNode.ApplyParameters(P(("texture", "auto"), ("blendMode", "auto")), "fire");
Require(renderNode.PersistentState is ParticleRenderer { TextureType: ParticleTextureType.Flame },
    "渲染器没有根据火焰预设选择火焰纹理");

var rigidPhysics = new PhysicsSimulateNode();
using (rigidPhysics.Process([null, null], P(), context)) { }
var constraint = new PhysicsConstraintNode();
var constraintParameters = P(("constraintType", "rope"), ("bodyIndexA", 0),
    ("bodyIndexB", 1), ("maxLength", 0.85f));
var firstParticleBefore = emitterState.Buffer.AsSpan()[0].Y;
var firstParticleVelocityBefore = emitterState.Buffer.AsSpan()[0].VX;
rigidPhysics.SimulateFrame(P(("gravityX", 0.65f), ("gravityY", 0.85f), ("restitution", 0.72f),
        ("friction", 0.18f), ("groundY", 0.90f), ("substeps", 2)),
    context with { DeltaTime = 1f / 15f }, emitterState.Buffer,
    new[] { (constraint, (IReadOnlyDictionary<string, object>)constraintParameters) });
Require(rigidPhysics.PersistentState is PhysicsSimulateNode.PhysicsState { World.Constraints.Count: > 0 },
    "物理约束没有安装到刚体世界");
Require(Math.Abs(emitterState.Buffer.AsSpan()[0].Y - firstParticleBefore) > 0.00001f,
    "粒子刚体模拟没有更新粒子位置");
Require(Math.Abs(emitterState.Buffer.AsSpan()[0].VX - firstParticleVelocityBefore) > 0.00001f,
    "PhysicsSimulate gravityX parameter did not affect existing bodies");
var resizedEmitterState = emitterNode.GetOrCreateState(P(("preset", "fire"), ("maxParticles", 256),
    ("prewarm", false)), context);
Require(resizedEmitterState.Buffer.Capacity == 256,
    "运行中修改最大粒子数没有重建粒子容量");
resizedEmitterState.Buffer.Dispose();
emitterNode.PersistentState = null;

var isolatedRuntime = new ParticleEvaluationService();
var isolatedEmitterA = new ParticleEmitterNode();
var isolatedEmitterB = new ParticleEmitterNode();
isolatedEmitterA.GetOrCreateState(P(("preset", "fire"), ("maxParticles", 32), ("prewarm", false)), context);
isolatedEmitterB.GetOrCreateState(P(("preset", "smoke"), ("maxParticles", 32), ("prewarm", false)), context);
isolatedRuntime.SaveState([new GraphNodeInstance(81_001, isolatedEmitterA),
    new GraphNodeInstance(81_002, isolatedEmitterB)]);
Require(isolatedRuntime.ClearState(81_001),
    "Targeted simulation reset did not find the edited node state");
var restoredEmitterA = new ParticleEmitterNode();
var restoredEmitterB = new ParticleEmitterNode();
isolatedRuntime.RestoreState([new GraphNodeInstance(81_001, restoredEmitterA),
    new GraphNodeInstance(81_002, restoredEmitterB)]);
Require(restoredEmitterA.PersistentState == null && restoredEmitterB.PersistentState != null,
    "Editing one particle workflow restarted another workflow's state");
isolatedRuntime.ClearState();
isolatedEmitterA.PersistentState = null;
isolatedEmitterB.PersistentState = null;
restoredEmitterB.PersistentState = null;

var projectPath = Path.Combine(Path.GetTempPath(), $"pixel-graph-roundtrip-{Guid.NewGuid():N}.pxtile");
try
{
    var project = new ProjectFileService.ProjectData { TileSize = 32 };
    var persistedNode = new ProjectFileService.NodeData
    {
        Title = "自定义烟雾",
        TypeName = "ParticleEmitter",
        Kind = NodeLibraryItemKind.Compute,
        X = 125,
        Y = 240
    };
    persistedNode.Parameters.Add(new ProjectFileService.NodeParameterData
    {
        Name = "prompt",
        Kind = NodeParameterKind.Text,
        TextValue = "中文提示词"
    });
    persistedNode.Parameters.Add(new ProjectFileService.NodeParameterData
    {
        Name = "startColor",
        Kind = NodeParameterKind.Color,
        ColorArgb = 0xFF3366CCu
    });
    project.Nodes.Add(persistedNode);

    ProjectFileService.WriteProjectFile(projectPath, project);
    var restored = ProjectFileService.ReadProjectFile(projectPath);
    Require(restored?.Nodes.Count == 1, "project round-trip lost nodes");
    Require(restored!.Nodes[0].TypeName == "ParticleEmitter", "project round-trip lost stable node type");
    Require(restored.Nodes[0].Parameters[0].TextValue == "中文提示词", "project round-trip lost text parameter");
    Require(restored.Nodes[0].Parameters[1].ColorArgb == 0xFF3366CCu, "project round-trip lost color parameter");
}
finally
{
    if (File.Exists(projectPath)) File.Delete(projectPath);
}

Console.WriteLine("System feature smoke test passed: AI reference routing, meta nodes, workflow routing, animation, particles, physics, smart nodes and persistence are operational.");
Console.WriteLine($"Visual smoke outputs: {visualOutputDirectory}");

sealed class CaptureAiRuntime : IAiImageGenerationRuntime
{
    public AiImageGenerationRequest? Request { get; private set; }
    public int ReferenceWidth { get; private set; }
    public int ReferenceHeight { get; private set; }
    public event EventHandler<AiImageNodeStateChangedEventArgs>? StateChanged
    {
        add { }
        remove { }
    }
    public AiImageGenerationStatus GetStatus(int nodeId)
        => new(AiImageGenerationPhase.Idle, 0, "ready", "smoke", 0, true);
    public PixelBuffer? GetOutputClone(int nodeId) => null;
    public bool TryRequest(int nodeId, AiImageGenerationRequest request,
        PixelBuffer? referenceImage, PixelBuffer? mask)
    {
        Request = request;
        ReferenceWidth = referenceImage?.Width ?? 0;
        ReferenceHeight = referenceImage?.Height ?? 0;
        return true;
    }
    public Task EnsureModelAsync(int nodeId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public void Cancel(int nodeId) { }
    public void Dispose() { }
}
