using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Utilities;

namespace PixelAssetGenerator.Services.ToolProviders;

/// <summary>
/// IToolProvider for graph mutation operations: modify_nodes, modify_connections,
/// set_parameter, and canvas state query (graph_summary, node_library, node_detail).
/// </summary>
public sealed class GraphToolProvider : IToolProvider
{
    private readonly NodeGraphController _controller;
    private readonly ObservableCollection<NodeViewModel> _nodes;
    private readonly ObservableCollection<NodeConnectionViewModel> _connections;
    private readonly Func<NodeLibraryItem, double, double, NodeViewModel> _createNodeFromLibrary;
    private readonly Dispatcher _dispatcher;
    private readonly List<NodeLibraryItem> _library;

    /// <summary>
    /// Current tile size in pixels, used for AI context in graph_summary.
    /// </summary>
    public int CurrentTileSize { get; set; } = 32;

    public string ProviderName => "graph";

    public GraphToolProvider(
        NodeGraphController controller,
        ObservableCollection<NodeViewModel> nodes,
        ObservableCollection<NodeConnectionViewModel> connections,
        Func<NodeLibraryItem, double, double, NodeViewModel> createNodeFromLibrary,
        Dispatcher dispatcher,
        List<NodeLibraryItem> library)
    {
        _controller = controller;
        _nodes = nodes;
        _connections = connections;
        _createNodeFromLibrary = createNodeFromLibrary;
        _dispatcher = dispatcher;
        _library = library;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return new ToolDefinition(
            "modify_nodes",
            "Create, delete, move, or select nodes. action=create requires typeName/x/y; action=delete requires nodeIds; action=move requires nodeId/x/y; action=select requires nodeId.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"action":{"type":"string","enum":["create","delete","move","select"],"description":"Action type"},"typeName":{"type":"string","description":"Node type name. Use query_info query_type=node_library to get all available node types with descriptions and capabilities."},"nodeIds":{"type":"array","items":{"type":"integer"},"description":"Node ID array (for deletion)"},"nodeId":{"type":"integer","description":"Node ID (for move/select)"},"x":{"type":"number","description":"X coordinate on the canvas"},"y":{"type":"number","description":"Y coordinate on the canvas"}},"required":["action"]}
            """)
        );

        yield return new ToolDefinition(
            "modify_connections",
            "Connect or disconnect ports between nodes. action=connect connects from source node output port to target node input port; action=disconnect disconnects the specified connection.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"action":{"type":"string","enum":["connect","disconnect"],"description":"Action type"},"startNodeId":{"type":"integer","description":"Source node ID"},"startPortIndex":{"type":"integer","description":"Source node output port index (usually 0)"},"endNodeId":{"type":"integer","description":"Target node ID"},"endPortIndex":{"type":"integer","description":"Target node input port index (usually 0)"}},"required":["action","startNodeId","endNodeId"]}
            """)
        );

        yield return new ToolDefinition(
            "set_parameter",
            "Set a node's parameter value, auto-detects parameter type (supports number, integer, boolean, color, choice, text, etc.)",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"nodeId":{"type":"integer","description":"Node ID"},"paramName":{"type":"string","description":"Parameter name. Use query_info query_type=graph_summary to see each node's parameter names and kinds."},"value":{"description":"Parameter value. For Color: {\"r\":200,\"g\":100,\"b\":50} (RGB 0-255). For number: 0.5. For int: 42. For boolean: true/false. For choice: \"option_name\""}},"required":["nodeId","paramName","value"]}
            """)
        );

        yield return new ToolDefinition(
            "query_info",
            "Query graph state or node library. query_type=graph_summary gets full canvas state; query_type=node_library lists all node types; query_type=node_detail requires param parameter for node type details.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"query_type":{"type":"string","enum":["graph_summary","node_library","node_detail"],"description":"Query type"},"param":{"type":"string","description":"Additional parameter. For query_type=node_detail it's the node type name."}},"required":["query_type"]}
            """)
        );
    }

    public Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "modify_nodes" => DispatchAsync(() => ModifyNodes(arguments)),
            "modify_connections" => DispatchAsync(() => ModifyConnections(arguments)),
            "set_parameter" => DispatchAsync(() => SetParameter(arguments)),
            "query_info" => Task.FromResult(QueryInfo(arguments)),
            _ => Task.FromResult(new ToolResult(
                $"{{\"success\":false,\"error\":\"Unknown graph tool: {toolName}\"}}", true)
            { IsUnhandled = true })
        };
    }

    private async Task<ToolResult> DispatchAsync(Func<ToolResult> action)
    {
        if (_dispatcher.HasShutdownFinished || _dispatcher.HasShutdownStarted)
            return action();

        try
        {
            var op = _dispatcher.InvokeAsync(action);
            return await op.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GraphToolProvider] Dispatcher error: {ex.Message}");
            try { return action(); }
            catch (Exception fallbackEx)
            {
                return new ToolResult(
                    "{\"success\":false,\"error\":\"" + AiHelpers.Escape(fallbackEx.Message) + "\"}", true);
            }
        }
    }

    // ── modify_nodes ──

    private ToolResult ModifyNodes(JsonElement args)
    {
        var action = args.TryGetProperty("action", out var a) ? a.GetString() : "";
        return action switch
        {
            "create" => CreateNode(args),
            "delete" => DeleteNodes(args),
            "move" => MoveNode(args),
            "select" => SelectNode(args),
            _ => new ToolResult(
                $"{{\"success\":false,\"error\":\"Unknown modify_nodes action: {action}\"}}", true)
        };
    }

    private ToolResult CreateNode(JsonElement args)
    {
        var typeName = args.TryGetProperty("typeName", out var tn) ? tn.GetString() : "";
        var x = args.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 100;
        var y = args.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : 100;

        if (string.IsNullOrEmpty(typeName))
            return new ToolResult("{\"success\":false,\"error\":\"Missing typeName parameter\"}", true);

        var libraryItem = _library.FirstOrDefault(i =>
            string.Equals(i.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

        if (libraryItem == null)
            return new ToolResult(
                $"{{\"success\":false,\"error\":\"Node type not found: {AiHelpers.Escape(typeName)}\"}}", true);

        try
        {
            var node = _createNodeFromLibrary(libraryItem, x, y);
            _nodes.Add(node);
            return new ToolResult(
                $"{{\"success\":true,\"nodeId\":{node.Id},\"typeName\":\"{AiHelpers.Escape(node.Title)}\"}}");
        }
        catch (Exception ex)
        {
            return new ToolResult(
                $"{{\"success\":false,\"error\":\"{AiHelpers.Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult DeleteNodes(JsonElement args)
    {
        if (!args.TryGetProperty("nodeIds", out var idsEl))
            return new ToolResult("{\"success\":false,\"error\":\"Missing nodeIds parameter\"}", true);

        var ids = new HashSet<int>();
        foreach (var idEl in idsEl.EnumerateArray())
            ids.Add(idEl.GetInt32());

        var toRemove = _nodes.Where(n => ids.Contains(n.Id)).ToList();
        _controller.DeleteNodes(toRemove);
        return new ToolResult($"{{\"success\":true,\"deletedCount\":{toRemove.Count}}}");
    }

    private ToolResult MoveNode(JsonElement args)
    {
        var nodeId = args.TryGetProperty("nodeId", out var n) ? n.GetInt32() : -1;
        var x = args.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 0;
        var y = args.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : 0;

        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null)
            return new ToolResult($"{{\"success\":false,\"error\":\"Node not found: {nodeId}\"}}", true);

        node.X = x;
        node.Y = y;
        return new ToolResult("{\"success\":true}");
    }

    private ToolResult SelectNode(JsonElement args)
    {
        var nodeId = args.TryGetProperty("nodeId", out var n) ? n.GetInt32() : -1;

        foreach (var node in _nodes)
            node.IsSelected = node.Id == nodeId;

        return new ToolResult("{\"success\":true}");
    }

    // ── modify_connections ──

    private ToolResult ModifyConnections(JsonElement args)
    {
        var action = args.TryGetProperty("action", out var a) ? a.GetString() : "";
        return action switch
        {
            "connect" => ConnectNodes(args),
            "disconnect" => DisconnectNodes(args),
            _ => new ToolResult(
                $"{{\"success\":false,\"error\":\"Unknown modify_connections action: {action}\"}}", true)
        };
    }

    private ToolResult ConnectNodes(JsonElement args)
    {
        var startNodeId = args.TryGetProperty("startNodeId", out var sn) ? sn.GetInt32() : -1;
        var startPortIndex = args.TryGetProperty("startPortIndex", out var sp) ? sp.GetInt32() : 0;
        var endNodeId = args.TryGetProperty("endNodeId", out var en) ? en.GetInt32() : -1;
        var endPortIndex = args.TryGetProperty("endPortIndex", out var ep) ? ep.GetInt32() : 0;

        var startNode = _nodes.FirstOrDefault(n => n.Id == startNodeId);
        var endNode = _nodes.FirstOrDefault(n => n.Id == endNodeId);

        if (startNode == null)
            return new ToolResult($"{{\"success\":false,\"error\":\"Source node not found: {startNodeId}\"}}", true);
        if (endNode == null)
            return new ToolResult($"{{\"success\":false,\"error\":\"Target node not found: {endNodeId}\"}}", true);

        if (startPortIndex >= startNode.OutputPorts.Count)
            return new ToolResult(
                $"{{\"success\":false,\"error\":\"Source node output port index out of range: {startPortIndex}\"}}", true);
        if (endPortIndex >= endNode.InputPorts.Count)
            return new ToolResult(
                $"{{\"success\":false,\"error\":\"Target node input port index out of range: {endPortIndex}\"}}", true);

        NodeGraphController.RemoveConflictingConnections(_connections, endNode, endPortIndex);

        // ── 幂等性检查：是否已经存在完全相同的连接 ──
        foreach (var existing in _connections)
        {
            if (existing.StartNode == startNode &&
                existing.StartPortIndex == startPortIndex &&
                existing.EndNode == endNode &&
                existing.EndPortIndex == endPortIndex)
            {
                return new ToolResult(
                    "{\"success\":true,\"skipped\":true,\"message\":\"Connection already exists, skipped. \"}" +
                    "Tip: If you see this message, the connection is already established. Move on to the next step.");
            }
        }

        var conn = new NodeConnectionViewModel
        {
            StartNode = startNode,
            StartPortIndex = startPortIndex,
            EndNode = endNode,
            EndPortIndex = endPortIndex,
            IsPreview = false
        };
        _connections.Add(conn);

        return new ToolResult("{\"success\":true}");
    }

    private ToolResult DisconnectNodes(JsonElement args)
    {
        var startNodeId = args.TryGetProperty("startNodeId", out var sn) ? sn.GetInt32() : -1;
        var startPortIndex = args.TryGetProperty("startPortIndex", out var sp) ? sp.GetInt32() : 0;
        var endNodeId = args.TryGetProperty("endNodeId", out var en) ? en.GetInt32() : -1;
        var endPortIndex = args.TryGetProperty("endPortIndex", out var ep) ? ep.GetInt32() : 0;

        var conn = _connections.FirstOrDefault(c =>
            c.StartNode?.Id == startNodeId && c.StartPortIndex == startPortIndex &&
            c.EndNode?.Id == endNodeId && c.EndPortIndex == endPortIndex);

        if (conn != null)
        {
            _connections.Remove(conn);
            return new ToolResult("{\"success\":true}");
        }
        return new ToolResult("{\"success\":false,\"error\":\"No matching connection found.\"}", true);
    }

    // ── set_parameter ──

    private ToolResult SetParameter(JsonElement args)
    {
        var nodeId = args.TryGetProperty("nodeId", out var n) ? n.GetInt32() : -1;
        var paramName = args.TryGetProperty("paramName", out var p) ? p.GetString() : "";
        var value = args.TryGetProperty("value", out var v) ? v : default;

        if (string.IsNullOrEmpty(paramName))
            return new ToolResult("{\"success\":false,\"error\":\"Missing paramName parameter\"}", true);

        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null)
            return new ToolResult($"{{\"success\":false,\"error\":\"Node not found: {nodeId}\"}}", true);

        var param = node.Parameters.FirstOrDefault(p => p.Name == paramName)
            ?? node.Parameters.FirstOrDefault(p => p.Name.Contains(paramName));

        if (param == null)
            return new ToolResult(
                $"{{\"success\":false,\"error\":\"Parameter not found: '{AiHelpers.Escape(paramName)}'\"}}", true);

        try
        {
            // ── 幂等性检查：如果参数值相同，跳过 ──
            if (IsParameterAlreadySet(param, value))
            {
                return new ToolResult(
                    "{\"success\":true,\"skipped\":true,\"message\":\"Parameter already set to this value. Move on.\"}");
            }

            ApplyParameterValue(param, value);
            return new ToolResult("{\"success\":true}");
        }
        catch (Exception ex)
        {
            return new ToolResult(
                $"{{\"success\":false,\"error\":\"{AiHelpers.Escape(ex.Message)}\"}}", true);
        }
    }

    /// <summary>将 JsonElement 的值转为字符串用于比较。</summary>
    private static string ValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => value.GetRawText()
    };

    /// <summary>检查参数是否已经设为目标值。</summary>
    private static bool IsParameterAlreadySet(NodeParameterViewModel param, JsonElement value)
    {
        var newStr = ValueToString(value);
        if (string.IsNullOrEmpty(newStr)) return false;

        string currentStr = param.Kind switch
        {
            NodeParameterKind.Number => param.NumberValue.ToString("G"),
            NodeParameterKind.Integer or NodeParameterKind.Seed => param.IntValue.ToString(),
            NodeParameterKind.Boolean => param.BoolValue ? "true" : "false",
            NodeParameterKind.Choice => param.SelectedChoice ?? "",
            NodeParameterKind.Text => param.TextValue ?? "",
            _ => ""
        };
        return string.Equals(currentStr, newStr, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyParameterValue(NodeParameterViewModel param, JsonElement value)
    {
        switch (param.Kind)
        {
            case NodeParameterKind.Number:
            case NodeParameterKind.Seed:
                if (value.ValueKind == JsonValueKind.Number)
                    param.NumberValue = (float)value.GetDouble();
                break;
            case NodeParameterKind.Integer:
                if (value.ValueKind == JsonValueKind.Number)
                    param.IntValue = value.GetInt32();
                break;
            case NodeParameterKind.Boolean:
                if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                    param.BoolValue = value.GetBoolean();
                break;
            case NodeParameterKind.Choice:
                if (value.ValueKind == JsonValueKind.String)
                    param.SelectedChoice = value.GetString() ?? "";
                break;
            case NodeParameterKind.Color:
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var r = value.TryGetProperty("r", out var rEl) || value.TryGetProperty("red", out rEl) ? (byte)rEl.GetInt32() : (byte)255;
                    var g = value.TryGetProperty("g", out var gEl) || value.TryGetProperty("green", out gEl) ? (byte)gEl.GetInt32() : (byte)255;
                    var b = value.TryGetProperty("b", out var bEl) || value.TryGetProperty("blue", out bEl) ? (byte)bEl.GetInt32() : (byte)255;
                    param.ColorValue = Color.FromRgb(r, g, b);
                }
                break;
            case NodeParameterKind.Text:
                if (value.ValueKind == JsonValueKind.String)
                    param.TextValue = value.GetString();
                break;
        }
    }

    // ── query_info ──

    private ToolResult QueryInfo(JsonElement args)
    {
        var queryType = args.TryGetProperty("query_type", out var qt) ? qt.GetString() : "graph_summary";
        return queryType switch
        {
            "graph_summary" => new ToolResult(GetGraphState()),
            "node_library" => new ToolResult(GetNodeLibrary()),
            "node_detail" => new ToolResult(GetNodeDetail(args)),
            _ => new ToolResult(
                $"{{\"success\":false,\"error\":\"Unknown query_type: {queryType}\"}}", true)
        };
    }

    // ── Graph state serialization ──

    private string GetGraphState()
    {
        var sb = new StringBuilder();
        sb.Append("{\"tileSize\":").Append(CurrentTileSize).Append(",\"nodes\":[");
        bool first = true;
        foreach (var node in _nodes)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"id\":").Append(node.Id);
            sb.Append(",\"title\":\"").Append(AiHelpers.Escape(node.Title)).Append('"');
            sb.Append(",\"x\":").Append(node.X);
            sb.Append(",\"y\":").Append(node.Y);
            sb.Append(",\"inputPorts\":[");
            bool firstPort = true;
            foreach (var port in node.InputPorts)
            {
                if (!firstPort) sb.Append(',');
                firstPort = false;
                sb.Append("{\"name\":\"").Append(AiHelpers.Escape(port.Name))
                  .Append("\",\"type\":\"").Append(port.Type).Append("\"}");
            }
            sb.Append("],\"outputPorts\":[");
            firstPort = true;
            foreach (var port in node.OutputPorts)
            {
                if (!firstPort) sb.Append(',');
                firstPort = false;
                sb.Append("{\"name\":\"").Append(AiHelpers.Escape(port.Name))
                  .Append("\",\"type\":\"").Append(port.Type).Append("\"}");
            }
            sb.Append("],\"parameters\":[");
            bool firstParam = true;
            foreach (var param in node.Parameters)
            {
                if (!firstParam) sb.Append(',');
                firstParam = false;
                sb.Append("{\"name\":\"").Append(AiHelpers.Escape(param.Name))
                  .Append("\",\"kind\":\"").Append(param.Kind).Append("\"");
                sb.Append(",\"value\":").Append(GetParamValueJson(param));
                sb.Append('}');
            }
            sb.Append("]}");
        }
        sb.Append("],\"connections\":[");
        first = true;
        foreach (var conn in _connections.Where(c => !c.IsPreview && c.StartNode != null && c.EndNode != null))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"startNodeId\":").Append(conn.StartNode!.Id);
            sb.Append(",\"startPortIndex\":").Append(conn.StartPortIndex);
            sb.Append(",\"endNodeId\":").Append(conn.EndNode!.Id);
            sb.Append(",\"endPortIndex\":").Append(conn.EndPortIndex);
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private string GetNodeLibrary()
    {
        var sb = new StringBuilder();
        sb.Append('[');
        bool first = true;
        foreach (var item in _library)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"typeName\":\"").Append(AiHelpers.Escape(item.TypeName)).Append('"');
            sb.Append(",\"name\":\"").Append(AiHelpers.Escape(item.Name)).Append('"');
            sb.Append(",\"category\":\"").Append(AiHelpers.Escape(item.Category)).Append('"');
            sb.Append(",\"subcategory\":\"").Append(AiHelpers.Escape(item.Subcategory ?? "")).Append('"');
            if (!string.IsNullOrEmpty(item.Description))
                sb.Append(",\"description\":\"").Append(AiHelpers.Escape(item.Description)).Append('"');
            if (item.AiMetadata != null)
            {
                if (item.AiMetadata.Capabilities is { Count: > 0 })
                {
                    sb.Append(",\"capabilities\":[");
                    for (int ci = 0; ci < item.AiMetadata.Capabilities.Count; ci++)
                    {
                        if (ci > 0) sb.Append(',');
                        sb.Append('"').Append(AiHelpers.Escape(item.AiMetadata.Capabilities[ci])).Append('"');
                    }
                    sb.Append(']');
                }
                if (!string.IsNullOrEmpty(item.AiMetadata.Triggers))
                    sb.Append(",\"triggers\":\"").Append(AiHelpers.Escape(item.AiMetadata.Triggers)).Append('"');
                if (item.AiMetadata.SuggestedInputs is { Count: > 0 })
                {
                    sb.Append(",\"suggestedInputs\":[");
                    for (int si = 0; si < item.AiMetadata.SuggestedInputs.Count; si++)
                    {
                        if (si > 0) sb.Append(',');
                        sb.Append('"').Append(AiHelpers.Escape(item.AiMetadata.SuggestedInputs[si])).Append('"');
                    }
                    sb.Append(']');
                }
                if (!string.IsNullOrEmpty(item.AiMetadata.ExampleUsage))
                    sb.Append(",\"exampleUsage\":\"").Append(AiHelpers.Escape(item.AiMetadata.ExampleUsage)).Append('"');
            }
            var portTypes = GetPortTypes(item.TypeName);

            sb.Append(",\"inputPorts\":[");
            bool firstPort = true;
            for (int pi = 0; pi < item.InputPorts.Count; pi++)
            {
                if (!firstPort) sb.Append(',');
                firstPort = false;
                var pName = item.InputPorts[pi];
                var pType = portTypes.InputPorts.TryGetValue(pi, out var pt) ? pt : "Image";
                sb.Append("{\"name\":\"").Append(AiHelpers.Escape(pName))
                  .Append("\",\"type\":\"").Append(pType).Append("\"}");
            }
            sb.Append("],\"outputPorts\":[");
            firstPort = true;
            for (int pi = 0; pi < item.OutputPorts.Count; pi++)
            {
                if (!firstPort) sb.Append(',');
                firstPort = false;
                var pName = item.OutputPorts[pi];
                var pType = portTypes.OutputPorts.TryGetValue(pi, out var pt) ? pt : "Image";
                sb.Append("{\"name\":\"").Append(AiHelpers.Escape(pName))
                  .Append("\",\"type\":\"").Append(pType).Append("\"}");
            }
            sb.Append("],\"parameters\":[");
            bool firstParam = true;
            foreach (var param in item.Parameters)
            {
                if (!firstParam) sb.Append(',');
                firstParam = false;
                sb.Append("{\"name\":\"").Append(AiHelpers.Escape(param.Name)).Append('"');
                sb.Append(",\"kind\":\"").Append(param.Kind).Append('"');
                sb.Append(",\"default\":").Append(GetParamDefValueJson(param));
                if (param.Kind == NodeParameterKind.Choice && param.Choices is { Count: > 0 })
                {
                    sb.Append(",\"choices\":[");
                    for (int i = 0; i < param.Choices.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append(AiHelpers.Escape(param.Choices[i])).Append('"');
                    }
                    sb.Append(']');
                }
                if (param.Kind == NodeParameterKind.Number || param.Kind == NodeParameterKind.Integer)
                {
                    sb.Append(",\"min\":").Append(param.Min);
                    sb.Append(",\"max\":").Append(param.Max);
                }
                sb.Append('}');
            }
            sb.Append("]}");
        }
        sb.Append(']');
        return sb.ToString();
    }

    private string GetNodeDetail(JsonElement args)
    {
        var typeName = args.TryGetProperty("param", out var p) ? p.GetString() : "";
        if (string.IsNullOrEmpty(typeName))
            return "{\"error\":\"Missing param parameter\"}";

        var item = _library.FirstOrDefault(i => i.Name == typeName)
            ?? _library.FirstOrDefault(i => i.Name.Contains(typeName));

        if (item == null)
            return $"{{\"error\":\"Node type not found: {AiHelpers.Escape(typeName)}\"}}";

        var sb = new StringBuilder();
        sb.Append("{\"name\":\"").Append(AiHelpers.Escape(item.Name)).Append('"');
        sb.Append(",\"category\":\"").Append(AiHelpers.Escape(item.Category)).Append('"');
        sb.Append(",\"subcategory\":\"").Append(AiHelpers.Escape(item.Subcategory ?? "")).Append('"');
        if (!string.IsNullOrEmpty(item.Description))
            sb.Append(",\"description\":\"").Append(AiHelpers.Escape(item.Description)).Append('"');
        if (item.AiMetadata?.Capabilities is { Count: > 0 })
        {
            sb.Append(",\"capabilities\":[");
            for (int ci = 0; ci < item.AiMetadata.Capabilities.Count; ci++)
            {
                if (ci > 0) sb.Append(',');
                sb.Append('"').Append(AiHelpers.Escape(item.AiMetadata.Capabilities[ci])).Append('"');
            }
            sb.Append(']');
        }
        if (item.AiMetadata?.SuggestedInputs is { Count: > 0 })
        {
            sb.Append(",\"suggestedInputs\":[");
            for (int si = 0; si < item.AiMetadata.SuggestedInputs.Count; si++)
            {
                if (si > 0) sb.Append(',');
                sb.Append('"').Append(AiHelpers.Escape(item.AiMetadata.SuggestedInputs[si])).Append('"');
            }
            sb.Append(']');
        }
        var portTypes = GetPortTypes(item.TypeName);

        sb.Append(",\"inputPorts\":[");
        for (int pi = 0; pi < item.InputPorts.Count; pi++)
        {
            if (pi > 0) sb.Append(',');
            var pName = item.InputPorts[pi];
            var pType = portTypes.InputPorts.TryGetValue(pi, out var pt) ? pt : "Image";
            sb.Append("{\"name\":\"").Append(AiHelpers.Escape(pName))
              .Append("\",\"type\":\"").Append(pType).Append("\"}");
        }
        sb.Append("],\"outputPorts\":[");
        for (int pi = 0; pi < item.OutputPorts.Count; pi++)
        {
            if (pi > 0) sb.Append(',');
            var pName = item.OutputPorts[pi];
            var pType = portTypes.OutputPorts.TryGetValue(pi, out var pt) ? pt : "Image";
            sb.Append("{\"name\":\"").Append(AiHelpers.Escape(pName))
              .Append("\",\"type\":\"").Append(pType).Append("\"}");
        }
        sb.Append("],\"parameters\":[");
        foreach (var param in item.Parameters)
        {
            sb.Append("{\"name\":\"").Append(AiHelpers.Escape(param.Name)).Append('"');
            sb.Append(",\"kind\":\"").Append(param.Kind).Append('"');
            sb.Append(",\"default\":").Append(GetParamDefValueJson(param));
            if (param.Kind == NodeParameterKind.Choice && param.Choices is { Count: > 0 })
            {
                sb.Append(",\"choices\":[");
                for (int i = 0; i < param.Choices.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(AiHelpers.Escape(param.Choices[i])).Append('"');
                }
                sb.Append(']');
            }
            if (param.Kind == NodeParameterKind.Number || param.Kind == NodeParameterKind.Integer)
            {
                sb.Append(",\"min\":").Append(param.Min);
                sb.Append(",\"max\":").Append(param.Max);
            }
            sb.Append("},");
        }
        if (item.Parameters.Count > 0) sb.Length--;
        sb.Append("]}");
        return sb.ToString();
    }

    // ── JSON helpers ──

    private static string GetParamValueJson(NodeParameterViewModel param)
    {
        return param.Kind switch
        {
            NodeParameterKind.Number or NodeParameterKind.Seed => param.NumberValue.ToString("0.##"),
            NodeParameterKind.Integer => param.IntValue.ToString(),
            NodeParameterKind.Boolean => param.BoolValue ? "true" : "false",
            NodeParameterKind.Choice => $"\"{AiHelpers.Escape(param.SelectedChoice ?? "")}\"",
            NodeParameterKind.Color => $"{{\"r\":{param.ColorValue.R},\"g\":{param.ColorValue.G},\"b\":{param.ColorValue.B}}}",
            NodeParameterKind.Text => $"\"{AiHelpers.Escape(param.TextValue ?? "")}\"",
            _ => "\"\""
        };
    }

    private static string GetParamDefValueJson(NodeParameterDefinition param)
    {
        return param.Kind switch
        {
            NodeParameterKind.Number => param.DefaultNumber.ToString("0.##"),
            NodeParameterKind.Seed => ((int)param.DefaultNumber).ToString(),
            NodeParameterKind.Integer => param.DefaultInt.ToString(),
            NodeParameterKind.Boolean => param.DefaultBool == true ? "true" : "false",
            NodeParameterKind.Choice => $"\"{AiHelpers.Escape(param.DefaultChoice ?? "")}\"",
            NodeParameterKind.Color => $"{{\"r\":{param.DefaultColor.R},\"g\":{param.DefaultColor.G},\"b\":{param.DefaultColor.B}}}",
            NodeParameterKind.Text => $"\"{AiHelpers.Escape(param.DefaultText ?? "")}\"",
            _ => "\"\""
        };
    }

    /// <summary>
    /// Looks up the actual port types for a node by checking GraphNodeRegistry.
    /// </summary>
    private static (Dictionary<int, string> InputPorts, Dictionary<int, string> OutputPorts) GetPortTypes(string nodeName)
    {
        var inputTypes = new Dictionary<int, string>();
        var outputTypes = new Dictionary<int, string>();

        try
        {
            var proto = GraphNodeRegistry.Create(nodeName);
            if (proto != null)
            {
                for (int i = 0; i < proto.InputPorts.Count; i++)
                    inputTypes[i] = proto.InputPorts[i].Type.ToString();
                for (int i = 0; i < proto.OutputPorts.Count; i++)
                    outputTypes[i] = proto.OutputPorts[i].Type.ToString();
            }
        }
        catch { /* fallback: leave dictionaries empty, caller uses "Image" default */ }

        return (inputTypes, outputTypes);
    }
}
