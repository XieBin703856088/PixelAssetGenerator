using PixelAssetGenerator.Services.Clients;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PixelAssetGenerator.Services;

public sealed record AiChatRequest(
    AiSettings Settings,
    string SystemPrompt,
    IReadOnlyList<AiMessage> Messages,
    IReadOnlyList<JsonElement>? ToolDefinitions
);

public sealed record AiMessage(
    string Role,
    string? Content,
    IReadOnlyList<AiToolCallData>? ToolCalls,
    IReadOnlyList<AiFileAttachment>? Attachments = null,
    string? ReasoningContent = null
);

public sealed record AiFileAttachment(string FileName, string MediaType, byte[] Data)
{
    public string ToBase64() => Convert.ToBase64String(Data);
    public string ToDataUri() => $"data:{MediaType};base64,{ToBase64()}";
}

public sealed record AiToolCallData(string Id, string FunctionName, string Arguments);

public abstract record AiStreamEvent;
public sealed record AiStatus(string Message) : AiStreamEvent;
public sealed record AiTextDelta(string Delta) : AiStreamEvent;
public sealed record AiReasoningDelta(string Delta) : AiStreamEvent;
public sealed record AiToolCallBatch(IReadOnlyList<AiToolCallData> Calls) : AiStreamEvent;
public sealed record AiError(string Message) : AiStreamEvent;
public sealed record AiDone(bool IsTruncated = false) : AiStreamEvent;

/// <summary>
/// Dispatches AI streaming requests to the appropriate IStreamingChatClient
/// based on provider name. Acts as a thin routing layer.
/// Each client receives its own HttpClient to avoid header mutation conflicts.
/// </summary>
public sealed class AiService : IDisposable
{
    private readonly List<HttpClient> _clients = new();

    public async IAsyncEnumerable<AiStreamEvent> ChatStreamAsync(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        IStreamingChatClient client = CreateClient(request.Settings.Provider);

        bool errored = false;
        string? errorMessage = null;

        var enumerator = client.ChatStreamAsync(request, ct).GetAsyncEnumerator(ct);

        while (true)
        {
            AiStreamEvent? current = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                current = enumerator.Current;
            }
            catch (OperationCanceledException)
            {
                errorMessage = "Request cancelled";
                errored = true;
                break;
            }
            catch (HttpRequestException ex)
            {
                errorMessage = $"Network error: {ex.Message}";
                errored = true;
                break;
            }
            catch (JsonException ex)
            {
                errorMessage = $"JSON parse error in stream: {ex.Message}";
                errored = true;
                break;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error: {ex.Message}";
                errored = true;
                break;
            }

            if (current is AiError) errored = true;
            yield return current;
        }

        await enumerator.DisposeAsync();

        if (errorMessage != null)
            yield return new AiError(errorMessage);
        else if (!errored)
            yield return new AiDone();
    }

    // Reuse HttpClient per provider type to avoid socket exhaustion.
    // Separate instances prevent header mutation conflicts between providers
    // (e.g. x-api-key vs Authorization: Bearer).
    private HttpClient? _anthropicHttpClient;
    private HttpClient? _openaiHttpClient;

    private IStreamingChatClient CreateClient(string provider)
    {
        if (provider.Contains("Anthropic", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("Claude", StringComparison.OrdinalIgnoreCase))
        {
            _anthropicHttpClient ??= new HttpClient();
            return new AnthropicChatClient(_anthropicHttpClient);
        }

        // Default: OpenAI-compatible
        _openaiHttpClient ??= new HttpClient();
        return new OpenAiChatClient(_openaiHttpClient);
    }

    public void Dispose()
    {
        _anthropicHttpClient?.Dispose();
        _openaiHttpClient?.Dispose();
    }
}
