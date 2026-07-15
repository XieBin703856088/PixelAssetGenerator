using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.AiImage;
using PixelAssetGenerator.Services.AiImage;

Console.OutputEncoding = Encoding.UTF8;
var outputPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(Path.GetTempPath(), "rock-rpg-32-preview.png");
var backgroundMode = args.Length > 1 ? args[1] : "auto";
var prompt = args.Length > 2 ? args[2] : "生成一块石块";
var referenceTest = args.Contains("--reference-test", StringComparer.OrdinalIgnoreCase);

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
var service = LocalAiImageGenerationService.Instance;
Console.WriteLine($"Backend: {service.BackendName}");
Console.WriteLine($"Image model: {service.IsModelInstalled}");
Console.WriteLine($"Translation model: {service.IsTranslationModelInstalled}");

var assembly = typeof(LocalAiImageGenerationService).Assembly;
var translatorType = assembly.GetType("PixelAssetGenerator.Services.AiImage.LocalChinesePromptTranslator", true)!;
using (var translator = (IDisposable)Activator.CreateInstance(translatorType, nonPublic: true)!)
{
    var translateMethod = translatorType.GetMethod("TranslateAsync", BindingFlags.Instance | BindingFlags.Public)!;
    var translationTask = (Task<string>)translateMethod.Invoke(translator, new object[] { prompt, timeout.Token })!;
    var translated = await translationTask;
    var composerType = assembly.GetType("PixelAssetGenerator.Services.AiImage.PixelArtPromptComposer", true)!;
    var composeMethod = composerType.GetMethod("Compose", BindingFlags.Static | BindingFlags.Public)!;
    var effectivePrompt = (string)composeMethod.Invoke(null,
        new object[] { translated, "sprite", "classic32", "auto", 32 })!;
    Console.WriteLine($"Translated: {translated}");
    Console.WriteLine($"Effective prompt: {effectivePrompt}");
}

if (args.Contains("--prompt-only", StringComparer.OrdinalIgnoreCase))
    return;

if (!service.IsModelInstalled)
    throw new FileNotFoundException("Bundled image model is not available beside the smoke-test executable.");

const int nodeId = 9_001;
var request = new AiImageGenerationRequest(
    Revision: 1,
    Prompt: prompt,
    NegativePrompt: string.Empty,
    Style: "sprite",
    VisualStyle: "classic32",
    ViewAngle: "auto",
    OutputSize: 32,
    PaletteSize: 16,
    Steps: 22,
    Guidance: 7.5f,
    ReferenceMode: referenceTest ? "strict" : "structure",
    ReferenceStrength: referenceTest ? 0.86f : 0.72f,
    BackgroundMode: backgroundMode,
    Dithering: "none",
    AddOutline: backgroundMode != "opaque",
    Seed: 42);

using var reference = referenceTest ? CreateReferenceSprite() : null;
if (reference != null)
{
    var referencePath = Path.Combine(Path.GetDirectoryName(outputPath)!,
        Path.GetFileNameWithoutExtension(outputPath) + "-reference.png");
    SaveNearestPreview(reference, referencePath, 16);
    Console.WriteLine($"Reference: {referencePath} (strict fidelity 86%)");
}

if (!service.TryRequest(nodeId, request, reference, null))
    throw new InvalidOperationException("The local generation request was rejected.");

while (true)
{
    timeout.Token.ThrowIfCancellationRequested();
    var status = service.GetStatus(nodeId);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} {status.Phase,-16} {status.Progress,6:P0} {status.Message}");
    if (status.Phase == AiImageGenerationPhase.Completed)
        break;
    if (status.Phase is AiImageGenerationPhase.Failed or AiImageGenerationPhase.Cancelled)
        throw new InvalidOperationException(status.Message);
    await Task.Delay(1_000, timeout.Token);
}

using var output = service.GetOutputClone(nodeId)
    ?? throw new InvalidOperationException("Generation completed without an output buffer.");
SaveNearestPreview(output, outputPath, 16);
Console.WriteLine($"Saved: {outputPath}");
if (reference != null)
    Console.WriteLine($"Reference response difference: {ImageDifference(reference, output):P1}");

static PixelBuffer CreateReferenceSprite()
{
    var image = PixelBuffer.CreateSolid(32, 32, 0f, 0f, 0f, 0f);
    for (var y = 9; y <= 25; y++)
    for (var x = 6; x <= 25; x++)
    {
        var center = 15.5f;
        var nx = Math.Abs(x - center) / 10f;
        var ny = Math.Abs(y - 18f) / 9f;
        if (nx + ny * 0.62f > 1.05f) continue;
        var edge = nx + ny * 0.62f > 0.86f;
        var highlight = x < 13 && y < 17;
        image.SetPixel(x, y,
            edge ? 0.08f : highlight ? 0.36f : 0.16f,
            edge ? 0.16f : highlight ? 0.72f : 0.44f,
            edge ? 0.25f : highlight ? 0.88f : 0.70f, 1f);
    }
    return image;
}

static double ImageDifference(PixelBuffer a, PixelBuffer b)
{
    var width = Math.Min(a.Width, b.Width);
    var height = Math.Min(a.Height, b.Height);
    double sum = 0;
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var pa = a.GetPixel(x, y);
        var pb = b.GetPixel(x, y);
        sum += Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G)
             + Math.Abs(pa.B - pb.B) + Math.Abs(pa.A - pb.A);
    }
    return sum / Math.Max(1, width * height * 4);
}

static void SaveNearestPreview(PixelBuffer source, string path, int scale)
{
    var width = source.Width * scale;
    var height = source.Height * scale;
    var stride = width * 4;
    var pixels = new byte[stride * height];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = source.GetPixel(x / scale, y / scale);
            var offset = y * stride + x * 4;
            pixels[offset] = ToByte(pixel.B);
            pixels[offset + 1] = ToByte(pixel.G);
            pixels[offset + 2] = ToByte(pixel.R);
            pixels[offset + 3] = ToByte(pixel.A);
        }
    }

    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var stream = File.Create(path);
    encoder.Save(stream);
}

static byte ToByte(float value)
    => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
