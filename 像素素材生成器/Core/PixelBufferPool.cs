using System.Collections.Concurrent;

namespace PixelAssetGenerator.Core;

/// <summary>
/// Simple object pool for PixelBuffer recycling to reduce GC pressure.
/// Thread-safe. Buffers are keyed by (width, height) for reuse at matching sizes.
/// </summary>
public static class PixelBufferPool
{
    private static readonly ConcurrentDictionary<(int, int), ConcurrentBag<PixelBuffer>> Pool = new();
    private static readonly int MaxPoolSizePerBucket = 16;

    /// <summary>
    /// Borrows a buffer from the pool, or creates a new one if none available.
    /// The returned buffer may contain stale data — callers must overwrite all pixels.
    /// </summary>
    public static PixelBuffer Borrow(int width, int height)
    {
        if (Pool.TryGetValue((width, height), out var bag) && bag.TryTake(out var buffer))
        {
            return buffer;
        }
        return new PixelBuffer(width, height);
    }

    /// <summary>
    /// Returns a buffer to the pool for reuse. Do not use the buffer after returning it.
    /// </summary>
    public static void Return(PixelBuffer buffer)
    {
        if (buffer == null) return;
        var key = (buffer.Width, buffer.Height);
        var bag = Pool.GetOrAdd(key, _ => new ConcurrentBag<PixelBuffer>());
        if (bag.Count < MaxPoolSizePerBucket)
        {
            bag.Add(buffer);
        }
    }

    /// <summary>
    /// Clears the pool and allows buffers to be reclaimed by GC.
    /// </summary>
    public static void Clear()
    {
        Pool.Clear();
    }
}
