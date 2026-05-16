using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Handles serialization/deserialization of .pxtile project files.
/// All data types for project persistence are defined here to keep
/// them decoupled from MainWindow's UI logic.
/// </summary>
public static class ProjectFileService
{
    private const string ProjectMagic = "PXTL";

    // ─── Data types ──────────────────────────────────────────────────────

    public sealed class ProjectData
    {
        public int TileSize { get; set; } = 32;
        public List<NodeData> Nodes { get; set; } = new();
        public List<ConnectionData> Connections { get; set; } = new();
    }

    public sealed class NodeData
    {
        public string Title { get; set; } = string.Empty;
        /// <summary>Language-independent registry key (TypeName). Empty on old saves — fall back to Title.</summary>
        public string TypeName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public NodeLibraryItemKind Kind { get; set; }
        public TileType? TileType { get; set; }
        public TileProperties Properties { get; set; } = new TileProperties();
        public List<NodeParameterData> Parameters { get; set; } = new();
    }

    public sealed class NodeParameterData
    {
        public string Name { get; set; } = string.Empty;
        public NodeParameterKind Kind { get; set; }
        public double NumberValue { get; set; }
        public int IntValue { get; set; }
        public bool BoolValue { get; set; }
        public string? SelectedChoice { get; set; }
        public string? TextValue { get; set; }
        public List<Point> PointListData { get; set; } = new();
    }

    public sealed class ConnectionData
    {
        public int StartNodeIndex { get; set; }
        public int StartPortIndex { get; set; }
        public int EndNodeIndex { get; set; }
        public int EndPortIndex { get; set; }
    }

    // ─── Serialization ───────────────────────────────────────────────────

    public static void WriteProjectFile(string path, ProjectData data)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, false);

        writer.Write(ProjectMagic.ToCharArray());
        writer.Write((ushort)6); // project version
        writer.Write(data.TileSize);

        writer.Write(data.Nodes.Count);
        foreach (var n in data.Nodes)
        {
            WriteString(writer, n.Title);
            WriteString(writer, n.TypeName); // v6: canonical TypeName
            writer.Write((byte)n.Kind);
            writer.Write(n.TileType.HasValue);
            writer.Write(n.TileType.HasValue ? (byte)n.TileType!.Value : (byte)0);
            writer.Write(n.X);
            writer.Write(n.Y);

            // TileProperties
            writer.Write(n.Properties.Seed);
            writer.Write((float)n.Properties.Scale);
            writer.Write(n.Properties.Octaves);
            writer.Write((float)n.Properties.Persistence);
            writer.Write((float)n.Properties.Lacunarity);
            writer.Write((float)n.Properties.DetailDensity);
            writer.Write((float)n.Properties.EdgeStrength);
            writer.Write((float)n.Properties.MacroScale);
            writer.Write((float)n.Properties.MacroStrength);
            writer.Write((float)n.Properties.MicroScale);
            writer.Write((float)n.Properties.MicroStrength);
            writer.Write((float)n.Properties.AccentDensity);
            writer.Write((float)n.Properties.AccentSize);
            writer.Write((float)n.Properties.ColorVariation);
            writer.Write((float)n.Properties.Opacity);
            writer.Write((byte)n.Properties.BlendMode);
            writer.Write(n.Properties.MaskEnabled);
            writer.Write(n.Properties.MaskInvert);
            writer.Write((byte)n.Properties.MaskElement);

            // Parameters
            writer.Write(n.Parameters.Count);
            foreach (var p in n.Parameters)
            {
                WriteString(writer, p.Name ?? string.Empty);
                writer.Write((byte)p.Kind);
                writer.Write(p.NumberValue);
                writer.Write(p.IntValue);
                writer.Write(p.BoolValue);
                WriteString(writer, p.SelectedChoice ?? string.Empty);
                WriteString(writer, p.TextValue ?? string.Empty);
                writer.Write(p.PointListData.Count);
                foreach (var pt in p.PointListData)
                {
                    writer.Write(pt.X);
                    writer.Write(pt.Y);
                }
            }
        }

        // Connections
        writer.Write(data.Connections.Count);
        foreach (var c in data.Connections)
        {
            writer.Write(c.StartNodeIndex);
            writer.Write(c.StartPortIndex);
            writer.Write(c.EndNodeIndex);
            writer.Write(c.EndPortIndex);
        }
    }

    public static ProjectData? ReadProjectFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, false);

        var magic = new string(reader.ReadChars(ProjectMagic.Length));
        if (!string.Equals(magic, ProjectMagic, StringComparison.Ordinal))
            return null;

        var version = reader.ReadUInt16();

        // Legacy format (version < 4)
        if (version < 4)
        {
            return ReadLegacyFormat(reader);
        }

        // Version >= 4
        var proj = new ProjectData();
        proj.TileSize = reader.ReadInt32();

        var nodeCount = reader.ReadInt32();
        for (var i = 0; i < nodeCount; i++)
        {
            var title = ReadString(reader);
            var typeName = version >= 6 ? ReadString(reader) : string.Empty;
            var kind = (NodeLibraryItemKind)reader.ReadByte();
            var hasTile = reader.ReadBoolean();
            TileType? tileType = null;
            if (hasTile)
                tileType = (TileType)reader.ReadByte();
            var x = reader.ReadDouble();
            var y = reader.ReadDouble();

            var props = new TileProperties
            {
                Seed = reader.ReadInt32(),
                Scale = reader.ReadSingle(),
                Octaves = reader.ReadInt32(),
                Persistence = reader.ReadSingle(),
                Lacunarity = reader.ReadSingle(),
                DetailDensity = reader.ReadSingle(),
                EdgeStrength = reader.ReadSingle(),
                MacroScale = reader.ReadSingle(),
                MacroStrength = reader.ReadSingle(),
                MicroScale = reader.ReadSingle(),
                MicroStrength = reader.ReadSingle(),
                AccentDensity = reader.ReadSingle(),
                AccentSize = reader.ReadSingle(),
                ColorVariation = reader.ReadSingle(),
                Opacity = reader.ReadSingle(),
                BlendMode = (LayerBlendMode)reader.ReadByte(),
                MaskEnabled = reader.ReadBoolean(),
                MaskInvert = reader.ReadBoolean(),
                MaskElement = (MaskElement)reader.ReadByte()
            };

            var paramCount = reader.ReadInt32();
            var parameters = new List<NodeParameterData>();
            for (var p = 0; p < paramCount; p++)
            {
                var pname = ReadString(reader);
                var pkind = (NodeParameterKind)reader.ReadByte();
                var numberValue = reader.ReadDouble();
                var intValue = reader.ReadInt32();
                var boolValue = reader.ReadBoolean();
                var choice = ReadString(reader);
                string? textValue = null;
                if (version >= 5)
                {
                    textValue = ReadString(reader);
                }
                var pointListData = new List<Point>();
                if (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    try
                    {
                        var ptCount = reader.ReadInt32();
                        for (var pi = 0; pi < ptCount; pi++)
                        {
                            var px = reader.ReadDouble();
                            var py = reader.ReadDouble();
                            pointListData.Add(new Point(px, py));
                        }
                    }
                    catch { /* ignore point list read errors */ }
                }

                parameters.Add(new NodeParameterData
                {
                    Name = pname,
                    Kind = pkind,
                    NumberValue = numberValue,
                    IntValue = intValue,
                    BoolValue = boolValue,
                    SelectedChoice = choice,
                    TextValue = textValue,
                    PointListData = pointListData
                });
            }

            proj.Nodes.Add(new NodeData
            {
                Title = title,
                TypeName = typeName,
                X = x,
                Y = y,
                Kind = kind,
                TileType = tileType,
                Properties = props,
                Parameters = parameters
            });
        }

        // Connections
        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            try
            {
                var connCount = reader.ReadInt32();
                for (var i = 0; i < connCount; i++)
                {
                    proj.Connections.Add(new ConnectionData
                    {
                        StartNodeIndex = reader.ReadInt32(),
                        StartPortIndex = reader.ReadInt32(),
                        EndNodeIndex = reader.ReadInt32(),
                        EndPortIndex = reader.ReadInt32()
                    });
                }
            }
            catch { /* ignore connection read errors for backward compat */ }
        }

        return proj;
    }

    private static ProjectData? ReadLegacyFormat(BinaryReader reader)
    {
        try
        {
            var data = new ProjectData();
            data.TileSize = reader.ReadInt32();
            var layerCount = reader.ReadInt32();
            for (var i = 0; i < layerCount; i++)
            {
                var name = ReadString(reader);
                var isEnabled = reader.ReadBoolean();
                var tileType = (TileType)reader.ReadByte();
                var blendMode = (LayerBlendMode)reader.ReadByte();
                var seed = reader.ReadInt32();
                var opacity = reader.ReadSingle();
                var scale = reader.ReadSingle();
                var octaves = reader.ReadInt32();
                var persistence = reader.ReadSingle();
                var lacunarity = reader.ReadSingle();
                var detailDensity = reader.ReadSingle();
                var edgeStrength = reader.ReadSingle();

                var props = new TileProperties
                {
                    Seed = seed,
                    Opacity = opacity,
                    Scale = scale,
                    Octaves = octaves,
                    Persistence = persistence,
                    Lacunarity = lacunarity,
                    DetailDensity = detailDensity,
                    EdgeStrength = edgeStrength
                };

                data.Nodes.Add(new NodeData
                {
                    Title = name,
                    X = 80 + data.Nodes.Count * 40,
                    Y = 80 + data.Nodes.Count * 40,
                    Kind = NodeLibraryItemKind.Tile,
                    TileType = tileType,
                    Properties = props
                });
            }
            return data;
        }
        catch
        {
            return null;
        }
    }

    // ─── Binary I/O helpers ───────────────────────────────────────────

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0)
            throw new InvalidDataException("Invalid string length.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }
}
