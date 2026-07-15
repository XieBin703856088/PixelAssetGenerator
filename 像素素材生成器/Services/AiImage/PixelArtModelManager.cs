using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PixelAssetGenerator.Services.AiImage;

public sealed record PixelArtModelDescriptor(
    string Id,
    string DisplayName,
    string FileName,
    Uri DownloadUri,
    long FileSize,
    string Sha256,
    string License,
    Uri ModelPage,
    Uri LicenseUri);

public sealed record ModelInstallProgress(string Phase, double Progress, long BytesReceived, long TotalBytes, string Message);

/// <summary>
/// Resolves and validates the pixel model shipped with the application. Model
/// acquisition is a packaging concern: the runtime never downloads large files.
/// </summary>
public sealed class PixelArtModelManager
{
    public static PixelArtModelDescriptor DefaultModel { get; } = new(
        "stable-diffusion-v1-5-pixel-lora",
        "Stable Diffusion 1.5 · Q8 semantic base",
        "stable-diffusion-v1-5-pruned-emaonly-Q8_0.gguf",
        new Uri("https://huggingface.co/second-state/stable-diffusion-v1-5-GGUF/resolve/main/stable-diffusion-v1-5-pruned-emaonly-Q8_0.gguf?download=true"),
        1_763_578_176,
        "d0555243938c62faeefb4ac93f6c7a053ad373a4290c5256bce229aeb193bf94",
        "CreativeML Open RAIL-M",
        new Uri("https://huggingface.co/second-state/stable-diffusion-v1-5-GGUF"),
        new Uri("https://huggingface.co/spaces/CompVis/stable-diffusion-license"));

    public static PixelArtModelDescriptor PixelLora { get; } = new(
        "varo-pixel-art-lora",
        "Varo Pixel Art LoRA",
        "Varo_pixel_Art.safetensors",
        new Uri("https://huggingface.co/VaroDZAKY/Varo_pixel_Art/resolve/main/adapter_model.safetensors?download=true"),
        12_795_512,
        "3c50888c1b0e9a6905052abdc5ca997a2ad7f5936c91582f4ebb96c18a537350",
        "Apache-2.0",
        new Uri("https://huggingface.co/VaroDZAKY/Varo_pixel_Art"),
        new Uri("https://www.apache.org/licenses/LICENSE-2.0"));

    public static string BundledModelRoot { get; } = Path.Combine(AppContext.BaseDirectory, "Models", "Bundled");

    public string ModelDirectory { get; } = Path.Combine(BundledModelRoot, DefaultModel.Id);
    public string ModelPath => Path.Combine(ModelDirectory, DefaultModel.FileName);
    public string PixelLoraPath => Path.Combine(ModelDirectory, PixelLora.FileName);

    public bool IsInstalled
    {
        get
        {
            try
            {
                return IsAssetInstalled(ModelPath, DefaultModel)
                    && IsAssetInstalled(PixelLoraPath, PixelLora);
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<string> EnsureInstalledAsync(
        IProgress<ModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInstalled)
        {
            throw new FileNotFoundException(
                "随软件分发的像素生成模型缺失或校验标记不正确，请重新安装完整离线版本。",
                ModelPath);
        }

        var totalSize = DefaultModel.FileSize + PixelLora.FileSize;
        progress?.Report(new ModelInstallProgress(
            "ready",
            1,
            totalSize,
            totalSize,
            "内置语义底模与像素风格模型已就绪"));
        return Task.FromResult(ModelPath);
    }

    private static bool IsAssetInstalled(string path, PixelArtModelDescriptor descriptor)
    {
        var stampPath = path + ".sha256";
        return File.Exists(path)
            && new FileInfo(path).Length == descriptor.FileSize
            && File.Exists(stampPath)
            && string.Equals(
                File.ReadAllText(stampPath).Trim(),
                descriptor.Sha256,
                StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatBytes(long bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        const double mib = 1024d * 1024d;
        return bytes >= gib
            ? (bytes / gib).ToString("0.00", CultureInfo.InvariantCulture) + " GiB"
            : (bytes / mib).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
    }
}
