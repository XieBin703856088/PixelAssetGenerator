using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelAssetGenerator.Models;

/// <summary>
/// Serialization-friendly model for a .node.json resource file.
/// formatVersion 1 = legacy flat-string fields.
/// formatVersion 2 = multi-lingual displayName/description + processorType + ai metadata.
/// </summary>
public sealed class NodeResource
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 2;

    /// <summary>
    /// C# processor class TypeName (e.g. "SolidColor"). Null/empty = pure script node.
    /// </summary>
    [JsonPropertyName("processorType")]
    public string? ProcessorType { get; set; }

    [JsonPropertyName("identity")]
    public NodeResourceIdentity Identity { get; set; } = new();

    [JsonPropertyName("ports")]
    public NodeResourcePorts Ports { get; set; } = new();

    [JsonPropertyName("parameters")]
    public List<NodeResourceParameter> Parameters { get; set; } = new();

    [JsonPropertyName("script")]
    public NodeResourceScript? Script { get; set; }

    /// <summary>AI-facing metadata — optional, only present when the node has AI-relevant capabilities.</summary>
    [JsonPropertyName("ai")]
    public NodeResourceAi? Ai { get; set; }

    /// <summary>Converts a legacy V1 resource to V2 by normalizing flat text fields into locale dictionaries.</summary>
    public static NodeResource UpgradeFromV1(NodeResource v1)
    {
        if (v1.FormatVersion >= 2) return v1;

        return new NodeResource
        {
            FormatVersion = 2,
            ProcessorType = null,
            Identity = new NodeResourceIdentity
            {
                TypeName = v1.Identity.TypeName ?? "",
                DisplayName = new NodeLocText { { "zh-Hans", v1.Identity.DisplayName.Get("zh-Hans") } },
                Category = v1.Identity.Category ?? "Tool",
                Subcategory = v1.Identity.Subcategory ?? "",
                Description = new NodeLocText { { "zh-Hans", v1.Identity.Description.Get("zh-Hans") } },
                Tags = null,
                Author = "",
                Version = "1.0"
            },
            Ports = v1.Ports,
            Parameters = v1.Parameters,
            Script = v1.Script,
            Ai = null
        };
    }
}

public sealed class NodeResourceIdentity
{
    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    /// <summary>Multi-locale display name. Key = locale code (e.g. "zh-Hans", "en").</summary>
    [JsonPropertyName("displayName")]
    public NodeLocText DisplayName { get; set; } = new();

    /// <summary>Category key (e.g. "Source", "Nature", "Material", "Structure", "Adjust", "Effect", "Utility", "Custom").</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "Tool";

    [JsonPropertyName("subcategory")]
    public string Subcategory { get; set; } = "";

    /// <summary>Multi-locale description.</summary>
    [JsonPropertyName("description")]
    public NodeLocText Description { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
}

/// <summary>Multi-locale text dictionary. Supports both string and dictionary in JSON via custom converter.</summary>
[JsonConverter(typeof(NodeLocTextConverter))]
public sealed class NodeLocText : Dictionary<string, string>
{
    public NodeLocText() : base() { }
    public NodeLocText(IDictionary<string, string> dictionary) : base(dictionary) { }

    /// <summary>Gets the best-matching text for a locale, falling back to any available entry,
    /// then to the global LocalizationService via <paramref name="globalKey"/> (if provided).</summary>
    public string Get(string locale, string? globalKey = null)
    {
        if (string.IsNullOrEmpty(locale)) goto fallbackAny;
        if (TryGetValue(locale, out var val) && !string.IsNullOrEmpty(val)) return val;

        // Try parent culture fallback: "fr-CA" → "fr", "zh-CN" → "zh-Hans"
        var dashIdx = locale.IndexOf('-');
        if (dashIdx > 0)
        {
            var parent = locale.Substring(0, dashIdx);
            if (parent == "zh") parent = "zh-Hans";
            if (TryGetValue(parent, out val) && !string.IsNullOrEmpty(val)) return val;
        }

        if (TryGetValue("zh-Hans", out val) && !string.IsNullOrEmpty(val)) return val;
        if (TryGetValue("en", out val) && !string.IsNullOrEmpty(val)) return val;

    fallbackAny:
        foreach (var kv in this) return kv.Value;

        // Last resort: try the global LocalizationService using a convention key
        if (!string.IsNullOrEmpty(globalKey))
        {
            try
            {
                var globalVal = Services.Localization.LocalizationService.Instance.GetString(globalKey);
                if (!string.IsNullOrEmpty(globalVal) && globalVal != globalKey)
                    return globalVal;
            }
            catch { }
        }

        return "";
    }
}

/// <summary>JSON converter that handles both string (legacy) and object (multi-locale) formats.</summary>
public sealed class NodeLocTextConverter : JsonConverter<NodeLocText>
{
    public override NodeLocText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Legacy format: flat string → wrap as zh-Hans
            var text = reader.GetString() ?? "";
            return new NodeLocText { { "zh-Hans", text } };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options);
            return dict != null ? new NodeLocText(dict) : new NodeLocText();
        }

        return new NodeLocText();
    }

    public override void Write(Utf8JsonWriter writer, NodeLocText value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, (IDictionary<string, string>)value, options);
    }
}

public sealed class NodeResourcePorts
{
    [JsonPropertyName("inputs")]
    public List<NodeResourcePortDef> Inputs { get; set; } = new();

    [JsonPropertyName("outputs")]
    public List<NodeResourcePortDef> Outputs { get; set; } = new() { NodeResourcePortDef.FromString("Output", "Image") };
}

public sealed class NodeResourcePortDef
{
    /// <summary>Port name — can be a plain string (legacy) or a locale dictionary.</summary>
    [JsonPropertyName("name")]
    public NodeLocText Name { get; set; } = new();

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Image";

    /// <summary>Returns the port display name in the given locale.</summary>
    public string GetName(string locale) => Name.Get(locale);

    /// <summary>Helper to create a port def from a plain string (backward compat).</summary>
    public static NodeResourcePortDef FromString(string name, string type = "Image")
        => new() { Name = new NodeLocText { { "zh-Hans", name }, { "en", name } }, Type = type };
}

public sealed class NodeResourceParameter
{
    /// <summary>Parameter name — can be a plain string (legacy) or a locale dictionary.</summary>
    [JsonPropertyName("name")]
    public NodeLocText Name { get; set; } = new();

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Number";

    [JsonPropertyName("default")]
    public System.Text.Json.JsonElement? Default { get; set; }

    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }

    [JsonPropertyName("step")]
    public double? Step { get; set; }

    [JsonPropertyName("choices")]
    [JsonConverter(typeof(NodeResourceChoiceListConverter))]
    public List<NodeResourceChoice>? Choices { get; set; }

    /// <summary>Multi-locale parameter description.</summary>
    [JsonPropertyName("description")]
    public NodeLocText Description { get; set; } = new();

    /// <summary>Returns the parameter display name in the given locale.</summary>
    public string GetName(string locale) => Name.Get(locale);

    /// <summary>Returns the parameter description in the given locale.</summary>
    public string GetDescription(string locale) => Description.Get(locale);
}

/// <summary>A selectable choice for a parameter. Supports plain string or localized {value, label} object.</summary>
public sealed class NodeResourceChoice
{
    /// <summary>The internal value used in script logic (e.g. "gaussian").</summary>
    public string Value { get; set; } = "";

    /// <summary>Localized display label. Falls back to Value if not set.</summary>
    public NodeLocText? Label { get; set; }

    /// <summary>Returns the display label for the given locale, falling back to Value.</summary>
    public string GetLabel(string locale)
    {
        if (Label == null) return Value;
        var txt = Label.Get(locale);
        return string.IsNullOrWhiteSpace(txt) ? Value : txt;
    }
}

/// <summary>Deserializes choices as either plain strings or {value, label} objects.</summary>
internal sealed class NodeResourceChoiceListConverter : JsonConverter<List<NodeResourceChoice>?>
{
    public override List<NodeResourceChoice>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected array for choices");

        var list = new List<NodeResourceChoice>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                list.Add(new NodeResourceChoice { Value = reader.GetString() ?? "" });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                var choice = new NodeResourceChoice();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var propName = reader.GetString();
                    reader.Read();
                    if (propName == "value")
                        choice.Value = reader.GetString() ?? "";
                    else if (propName == "label")
                        choice.Label = JsonSerializer.Deserialize<NodeLocText>(ref reader, options);
                    else
                        reader.Skip();
                }
                list.Add(choice);
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<NodeResourceChoice>? value, JsonSerializerOptions options)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray();
        foreach (var c in value)
        {
            if (c.Label == null)
                writer.WriteStringValue(c.Value);
            else
            {
                writer.WriteStartObject();
                writer.WriteString("value", c.Value);
                writer.WritePropertyName("label");
                JsonSerializer.Serialize(writer, c.Label, options);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }
}

public sealed class NodeResourceScript
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "csharp";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";
}

public sealed class NodeResourceAi
{
    /// <summary>Capability tags for AI classification (e.g. "generate-noise", "color-adjust").</summary>
    [JsonPropertyName("capabilities")]
    public List<string>? Capabilities { get; set; }

    /// <summary>Natural language triggers for AI to find this node (multi-locale).</summary>
    [JsonPropertyName("triggers")]
    public NodeLocText? Triggers { get; set; }

    /// <summary>Typical input node type hints for AI graph building.</summary>
    [JsonPropertyName("suggestedInputs")]
    public List<string>? SuggestedInputs { get; set; }

    /// <summary>Example usage description for AI context (multi-locale).</summary>
    [JsonPropertyName("exampleUsage")]
    public NodeLocText? ExampleUsage { get; set; }
}

/// <summary>
/// Lightweight metadata about a resource node (no script body).
/// Used for catalog display and AI context.
/// </summary>
public sealed class NodeResourceMetadata
{
    public string TypeName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Description { get; set; } = "";
    public NodeResourceSourceKind SourceKind { get; set; }
    public int InputPortCount { get; set; }
    public int OutputPortCount { get; set; }
    public int ParameterCount { get; set; }
}

public enum NodeResourceSourceKind
{
    BuiltIn,
    File,
    Dynamic
}
