using System.Collections.Generic;

namespace PixelAssetGenerator.Core;

/// <summary>
/// Port definition for a graph node.
/// </summary>
public sealed record GraphNodePort(string Name, GraphPortType Type);

/// <summary>
/// Core interface for all graph nodes. Each node is a pure function:
/// inputs (PixelBuffer[]) + context → output (PixelBuffer).
/// </summary>
public interface IGraphNode
{
    /// <summary>Unique identifier for this node type (e.g. "纯色", "噪波").</summary>
    string TypeName { get; }

    /// <summary>Which category this node belongs to (PascalCase key: Source, Nature, Material, Structure, Adjust, Effect, Utility, Custom).</summary>
    string Category { get; }

    /// <summary>Input port definitions.</summary>
    IReadOnlyList<GraphNodePort> InputPorts { get; }

    /// <summary>Output port definitions.</summary>
    IReadOnlyList<GraphNodePort> OutputPorts { get; }

    /// <summary>Parameter definitions exposed to the UI.</summary>
    IReadOnlyList<NodeParameterDefinition> Parameters { get; }

    /// <summary>
    /// Processes input buffers and produces an output buffer.
    /// </summary>
    /// <param name="inputs">Input pixel buffers matching InputPorts order. Null entries mean unconnected.</param>
    /// <param name="parameters">Runtime parameter values keyed by parameter name.</param>
    /// <param name="context">Shared graph context (seed, tile size, etc.).</param>
    PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context);
}

/// <summary>
/// Optional interface for nodes that produce more than one output port.
/// The returned array must have exactly one entry per output port;
/// null entries are treated as an empty buffer by the evaluator.
/// Nodes that implement this interface should still provide <see cref="IGraphNode.Process"/>
/// as a fallback (e.g. returning port 0).
/// </summary>
public interface IMultiOutputNode
{
    PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context);
}

/// <summary>
/// Optional GPU-accelerated variant of <see cref="IMultiOutputNode"/>.
/// Nodes that implement both interfaces can process all output ports on the GPU.
/// The returned array must have exactly one entry per output port, matching ProcessMulti().
/// </summary>
public interface IGpuAcceleratedMultiOutputNode
{
    PixelBuffer[] ProcessGpuMulti(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context);
}

/// <summary>
/// Marker interface for nodes that accept only one connected input at a time.
/// When a node implements this interface, connecting a second input port is rejected
/// in the UI if any other input port is already occupied.
/// </summary>
public interface IExclusiveInputNode { }

/// <summary>
/// Optional interface for nodes that maintain state across evaluation frames.
/// Particle systems and physics simulations use this to preserve particle/body
/// state between frames.
/// </summary>
public interface IPersistentStateNode : IGraphNode
{
    /// <summary>
    /// Key identifying the persistent state for caching purposes.
    /// Should be unique per node instance (e.g., "ParticleEmitter_3").
    /// </summary>
    string PersistentStateKey { get; }

    /// <summary>
    /// Gets or sets the persistent state object. Set by the evaluator before
    /// Process() is called if cached state from the previous frame exists.
    /// Nodes should cast this to their expected state type.
    /// </summary>
    object? PersistentState { get; set; }
}

/// <summary>
/// Port data type for particle buffer connections between particle nodes.
/// Added to GraphPortType for the particle system.
/// </summary>
public enum GraphPortType
{
    /// <summary>RGBA pixel buffer.</summary>
    Image,

    /// <summary>Single-channel grayscale mask.</summary>
    Mask,

    /// <summary>Single float value.</summary>
    Float,

    /// <summary>RGBA color value.</summary>
    Color,

    /// <summary>Accepts any port type (Image, Mask, etc.) — used for utility nodes like Preview.</summary>
    Any,

    /// <summary>Particle buffer for particle system connections.</summary>
    Particle
}
