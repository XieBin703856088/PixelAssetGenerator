using System.Collections.Generic;
using System.Threading;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Abstract streaming chat client interface for multi-provider support.
/// </summary>
public interface IStreamingChatClient
{
    IAsyncEnumerable<AiStreamEvent> ChatStreamAsync(AiChatRequest request, CancellationToken ct);
}
