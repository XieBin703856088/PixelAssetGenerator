using System.Collections.Generic;

namespace PixelAssetGenerator.Core;

/// <summary>
/// Context propagated through the node graph during evaluation.
/// Carries tile-wide information: size, seed, and other shared state.
/// </summary>
public sealed record PixelGraphContext
{
    /// <summary>Random seed for deterministic procedural generation.</summary>
    public int Seed { get; init; } = 42;

    /// <summary>Output tile width/height in pixels.</summary>
    public int TileSize { get; init; } = 32;

    /// <summary>
    /// Override tile size for multi-tile output (e.g., building facades spanning 2x2 tiles).
    /// When set, nodes should produce output at this size instead of TileSize.
    /// </summary>
    public int? OutputSize { get; init; }

    /// <summary>
    /// Semantic parameter overrides injected by SemanticControlNode.
    /// Key: parameter name pattern, Value: override value (float 0-1 range).
    /// The evaluator checks this dictionary before reading node parameters.
    /// </summary>
    public IReadOnlyDictionary<string, float>? SemanticOverrides { get; init; }

    /// <summary>
    /// Normalized time [0, 1) for animation playback.
    /// Driven by AnimationPlaybackService when playback is active.
    /// Nodes like FrameSequence, Rain, Snow, etc. can use this instead of
    /// or in addition to their per-node "frame" / "time" parameter.
    /// When <c>null</c>, no animation is active and nodes should use their
    /// static parameter values.
    /// </summary>
    public float? AnimationTime { get; init; }

    /// <summary>
    /// Current frame index for discrete frame sequences (e.g. sprite sheets).
    /// When <c>null</c>, no animation is active.
    /// </summary>
    public int? AnimationFrame { get; init; }

    /// <summary>
    /// Total number of frames in the current animation sequence.
    /// Used by nodes to map AnimationTime to discrete frame indices.
    /// </summary>
    public int AnimationFrameCount { get; init; } = 1;

    /// <summary>
    /// Frame delta time in seconds for animation/physics simulation.
    /// Driven by AnimationPlaybackService.
    /// </summary>
    public float DeltaTime { get; init; } = 1f / 60f;

    /// <summary>
    /// Global accumulated time in seconds.
    /// Useful for continuous animations that should not reset on parameter changes.
    /// </summary>
    public float GlobalTime { get; init; }

    /// <summary>
    /// Returns normalized UV coordinate for a given pixel position.
    /// U and V are in [0,1) range.
    /// </summary>
    public (float U, float V) GetUV(int x, int y)
    {
        var size = TileSize > 0 ? (float)TileSize : 1f;
        return (x / size, y / size);
    }

    /// <summary>
    /// Creates a derived context with a modified seed (for per-node seed variation).
    /// </summary>
    public PixelGraphContext WithSeedOffset(int offset) => this with { Seed = Seed + offset };

    /// <summary>
    /// Returns the effective output size: OutputSize if set, otherwise TileSize.
    /// </summary>
    public int GetEffectiveSize() => OutputSize ?? TileSize;

    /// <summary>
    /// Creates a context for a specific animation frame.
    /// </summary>
    public PixelGraphContext WithAnimationFrame(float normalizedTime, int frameIndex, float deltaTime = 1f / 60f) =>
        this with { AnimationTime = normalizedTime, AnimationFrame = frameIndex, DeltaTime = deltaTime, GlobalTime = GlobalTime + deltaTime };
}
