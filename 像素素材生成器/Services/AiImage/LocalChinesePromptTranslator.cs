using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tokenizers.DotNet;

namespace PixelAssetGenerator.Services.AiImage;

/// <summary>
/// Small CPU-only Chinese-to-English translator used before the SD 1.x text
/// encoder. The ONNX model is bundled with the application and is loaded lazily.
/// </summary>
internal sealed class LocalChinesePromptTranslator : IDisposable
{
    private const int DecoderStartTokenId = 65_000;
    private const int EndTokenId = 0;
    private const int LayerCount = 6;
    private const int AttentionHeads = 8;
    private const int AttentionHeadSize = 64;
    private const int MaximumSourceTokens = 256;
    private const int MaximumOutputTokens = 96;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;
    private Tokenizer? _tokenizer;
    private bool _disposed;

    public string ModelDirectory { get; } = Path.Combine(
        PixelArtModelManager.BundledModelRoot,
        "opus-mt-zh-en-int8");

    private string EncoderPath => Path.Combine(ModelDirectory, "encoder_model_int8.onnx");
    private string DecoderPath => Path.Combine(ModelDirectory, "decoder_model_merged_int8.onnx");
    private string TokenizerPath => Path.Combine(ModelDirectory, "tokenizer.json");

    public bool IsAvailable =>
        HasExpectedSize(EncoderPath, 52_726_552) &&
        HasExpectedSize(DecoderPath, 60_013_017) &&
        File.Exists(TokenizerPath);

    public async Task<string> TranslateAsync(string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt) || !ContainsCjk(prompt))
            return prompt;

        var fallback = PixelArtPromptComposer.TranslateChineseGamePrompt(prompt);
        if (!IsAvailable || _disposed)
            return fallback;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureLoaded();
            var translated = TranslateCore(prompt, cancellationToken);
            return MergeDomainSemantics(translated, fallback);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A domain-specific dictionary still gives the image model useful
            // English semantics if ONNX initialization or translation fails.
            return fallback;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureLoaded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_encoder != null && _decoder != null && _tokenizer != null)
            return;

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2)
        };
        _encoder = new InferenceSession(EncoderPath, options);
        _decoder = new InferenceSession(DecoderPath, options);
        _tokenizer = new Tokenizer(TokenizerPath);
    }

    private string TranslateCore(string prompt, CancellationToken cancellationToken)
    {
        var encodedIds = _tokenizer!.Encode(prompt);
        if (encodedIds.Length == 0)
            return prompt;

        var sourceIds = encodedIds.Length <= MaximumSourceTokens
            ? encodedIds.Select(id => (long)id).ToArray()
            : encodedIds.Take(MaximumSourceTokens - 1).Select(id => (long)id).Append((long)EndTokenId).ToArray();
        var attentionValues = Enumerable.Repeat(1L, sourceIds.Length).ToArray();
        var inputIds = new DenseTensor<long>(sourceIds, new[] { 1, sourceIds.Length });
        var attention = new DenseTensor<long>(attentionValues, new[] { 1, sourceIds.Length });

        using var encoderResults = _encoder!.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attention)
        });
        var encoded = encoderResults.First(value => value.Name == "last_hidden_state").AsTensor<float>();
        var hidden = new DenseTensor<float>(encoded.ToArray(), encoded.Dimensions.ToArray());
        var generated = new List<long> { DecoderStartTokenId };

        for (var step = 0; step < MaximumOutputTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decoderIds = new DenseTensor<long>(generated.ToArray(), new[] { 1, generated.Count });
            var decoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attention),
                NamedOnnxValue.CreateFromTensor("input_ids", decoderIds),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden),
                NamedOnnxValue.CreateFromTensor(
                    "use_cache_branch",
                    new DenseTensor<bool>(new[] { false }, new[] { 1 }))
            };

            for (var layer = 0; layer < LayerCount; layer++)
            {
                AddEmptyPastState(decoderInputs, layer, "decoder.key");
                AddEmptyPastState(decoderInputs, layer, "decoder.value");
                AddEmptyPastState(decoderInputs, layer, "encoder.key");
                AddEmptyPastState(decoderInputs, layer, "encoder.value");
            }

            using var decoderResults = _decoder!.Run(decoderInputs);
            var logits = decoderResults.First(value => value.Name == "logits").AsTensor<float>();
            var vocabularySize = logits.Dimensions[^1];
            var lastTokenOffset = (generated.Count - 1) * vocabularySize;
            var bestId = FindBestToken(logits, lastTokenOffset, vocabularySize);
            if (bestId == EndTokenId)
                break;
            generated.Add(bestId);
        }

        var translated = _tokenizer.Decode(generated.Skip(1).Select(id => (uint)id).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(translated) ? prompt : translated;
    }

    private static void AddEmptyPastState(List<NamedOnnxValue> inputs, int layer, string stateName)
    {
        inputs.Add(NamedOnnxValue.CreateFromTensor(
            $"past_key_values.{layer}.{stateName}",
            new DenseTensor<float>(new[] { 1, AttentionHeads, 0, AttentionHeadSize })));
    }

    private static int FindBestToken(Tensor<float> logits, int offset, int vocabularySize)
    {
        var bestId = EndTokenId;
        var bestScore = float.NegativeInfinity;
        for (var token = 0; token < vocabularySize; token++)
        {
            var score = logits.GetValue(offset + token);
            if (score <= bestScore)
                continue;
            bestScore = score;
            bestId = token;
        }
        return bestId;
    }

    private static bool ContainsCjk(string value)
        => value.Any(character => character is >= '\u3400' and <= '\u9fff');

    private static string MergeDomainSemantics(string translated, string domainFallback)
    {
        if (string.IsNullOrWhiteSpace(domainFallback)
            || string.Equals(domainFallback, "fantasy RPG game asset", StringComparison.OrdinalIgnoreCase))
            return translated;
        if (translated.Contains(domainFallback, StringComparison.OrdinalIgnoreCase))
            return translated;
        return $"{translated}, {domainFallback}";
    }

    private static bool HasExpectedSize(string path, long size)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length == size;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _tokenizer?.Dispose();
        _encoder?.Dispose();
        _decoder?.Dispose();
        _gate.Dispose();
    }
}
