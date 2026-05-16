using System.Collections.Generic;
using System.Linq;
using PixelAssetGenerator.Models;
using PixelAssetGenerator.Services;

namespace PixelAssetGenerator.Core.Nodes.Sources;

/// <summary>
/// Wraps <see cref="DynamicNodeService"/> as an <see cref="INodeSource"/>.
/// Dynamic nodes can be deleted but are transient by nature (stored in the DynamicNodeService registry).
/// When the DynamicNodeService persists to .node.json, they become File nodes instead.
/// </summary>
public sealed class DynamicNodeSource : INodeSource
{
    private readonly DynamicNodeService _service;

    public string SourceName => "dynamic";
    public NodeResourceSourceKind SourceKind => NodeResourceSourceKind.Dynamic;
    public bool CanDelete => true;

    public DynamicNodeSource(DynamicNodeService service)
    {
        _service = service;
    }

    public IReadOnlyList<NodeResourceMetadata> GetAvailableTypes()
    {
        return _service.GetAllDefinitions().Select(d => new NodeResourceMetadata
        {
            TypeName = d.Name,
            DisplayName = d.Name,
            Description = d.Description ?? "AI-generated script node",
            Category = "动态节点",
            SourceKind = NodeResourceSourceKind.Dynamic,
        }).ToList();
    }

    public IGraphNode? CreateNode(string typeName)
    {
        var def = _service.GetDefinition(typeName);
        if (def == null) return null;
        return new DynamicScriptNode(def);
    }

    public NodeResource? LoadResource(string typeName)
    {
        var def = _service.GetDefinition(typeName);
        if (def == null) return null;
        return new NodeResource
        {
            Identity = new NodeResourceIdentity
            {
                TypeName = def.Name,
                DisplayName = new NodeLocText { { "zh-Hans", def.Name } },
                Description = new NodeLocText { { "zh-Hans", def.Description ?? "" } },
                Category = "动态节点"
            }
        };
    }

    public void Delete(string typeName)
    {
        _service.RemoveDefinition(typeName);
    }
}

/// <summary>
/// Runtime-executable dynamic script node.
/// Compiles and runs C# code embedded in the ScriptNodeDefinition.
/// </summary>
public sealed class DynamicScriptNode : GraphNodeBase
{
    private readonly ScriptNodeDefinition _def;
    private readonly List<GraphNodePort> _inputs;
    private readonly List<GraphNodePort> _outputs;
    private readonly List<NodeParameterDefinition> _params;

    public override string TypeName => _def.Name;
    public override string Category => "Utility";
    public override IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => _params;

    public DynamicScriptNode(ScriptNodeDefinition def)
    {
        _def = def;

        // Build ports and parameters from the definition
        _inputs = new List<GraphNodePort>
        {
            new GraphNodePort("Input", GraphPortType.Image)
        };
        _outputs = new List<GraphNodePort>
        {
            new GraphNodePort("Output", GraphPortType.Image)
        };
        _params = new List<NodeParameterDefinition>();
        if (def.Parameters != null)
        {
            foreach (var p in def.Parameters)
            {
                var kind = p.Type switch
                {
                    "integer" => NodeParameterKind.Integer,
                    "boolean" => NodeParameterKind.Boolean,
                    "color" => NodeParameterKind.Color,
                    _ => NodeParameterKind.Number
                };
                _params.Add(kind switch
                {
                    NodeParameterKind.Integer => NodeParameterDefinition.Integer(p.Name, 0, 0, 100, 1),
                    NodeParameterKind.Boolean => NodeParameterDefinition.Boolean(p.Name, false),
                    NodeParameterKind.Color => NodeParameterDefinition.Color(p.Name, System.Windows.Media.Colors.White),
                    _ => NodeParameterDefinition.Number(p.Name, 0.5, 0, 1, 0.01)
                });
            }
        }
    }

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Pass-through fallback; full Roslyn compilation can be added later
        if (inputs[0] != null)
            return inputs[0]!.Clone();
        var size = context.GetEffectiveSize();
        return PixelBuffer.CreateSolid(size, size, 128f/255f, 80f/255f, 200f/255f);
    }
}
