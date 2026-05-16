using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Provides image export functionality: encoder selection, bitmap export,
/// nine-slice tile splitting, and alpha removal.
/// </summary>
public static class ExportService
{
    public enum ExportFormat
    {
        Png,
        Jpeg,
        Bmp,
        Tiff
    }

    public static ExportFormat ParseExportFormat(string? key)
    {
        return key switch
        {
            "Jpeg" => ExportFormat.Jpeg,
            "Bmp" => ExportFormat.Bmp,
            "Tiff" => ExportFormat.Tiff,
            _ => ExportFormat.Png
        };
    }

    public static string GetDefaultExtension(ExportFormat format)
    {
        return format switch
        {
            ExportFormat.Jpeg => ".jpg",
            ExportFormat.Bmp => ".bmp",
            ExportFormat.Tiff => ".tif",
            _ => ".png"
        };
    }

    public static string GetFilterForFormat(ExportFormat format)
    {
        return format switch
        {
            ExportFormat.Jpeg => "JPEG Images (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            ExportFormat.Bmp => "BMP Images (*.bmp)|*.bmp",
            ExportFormat.Tiff => "TIFF Images (*.tif;*.tiff)|*.tif;*.tiff",
            _ => "PNG Images (*.png)|*.png"
        };
    }

    public static bool SupportsAlpha(ExportFormat format)
    {
        return format is ExportFormat.Png or ExportFormat.Tiff;
    }

    public static BitmapEncoder CreateEncoder(ExportFormat format)
    {
        return format switch
        {
            ExportFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = 95 },
            ExportFormat.Bmp => new BmpBitmapEncoder(),
            ExportFormat.Tiff => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
    }

    /// <summary>
    /// Exports a BitmapSource to a file at the given path using the specified format.
    /// </summary>
    public static void ExportBitmap(string path, BitmapSource source, ExportFormat format, bool saveAlpha)
    {
        var encoder = CreateEncoder(format);
        var exportSource = saveAlpha ? source : RemoveAlpha(source);
        encoder.Frames.Add(BitmapFrame.Create(exportSource));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
    }

    /// <summary>
    /// Exports a 3x3 nine-slice composite to individual tile files.
    /// </summary>
    public static void ExportNineSliceTiles(string basePath, BitmapSource composite, int tileSize, ExportFormat format, bool saveAlpha)
    {
        var directory = Path.GetDirectoryName(basePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);

        foreach (var tile in GetNineSliceTiles(composite, tileSize))
        {
            var targetPath = Path.Combine(directory, $"{baseName}_{tile.Suffix}{extension}");
            ExportBitmap(targetPath, tile.Bitmap, format, saveAlpha);
        }
    }

    /// <summary>
    /// Enumerates the nine tiles from a 3x3 nine-slice composite bitmap.
    /// </summary>
    public static IEnumerable<(string Suffix, BitmapSource Bitmap)> GetNineSliceTiles(BitmapSource composite, int tileSize)
    {
        var entries = new (string Suffix, int Col, int Row)[]
        {
            ("Top-Left", 0, 0),
            ("Top", 1, 0),
            ("Top-Right", 2, 0),
            ("Left", 0, 1),
            ("Center", 1, 1),
            ("Right", 2, 1),
            ("Bottom-Left", 0, 2),
            ("Bottom", 1, 2),
            ("Bottom-Right", 2, 2)
        };

        foreach (var entry in entries)
        {
            var rect = new Int32Rect(entry.Col * tileSize, entry.Row * tileSize, tileSize, tileSize);
            var cropped = new CroppedBitmap(composite, rect);
            cropped.Freeze();
            yield return (entry.Suffix, cropped);
        }
    }

    /// <summary>
    /// Renders a BitmapSource onto a dark background, removing alpha transparency.
    /// </summary>
    public static BitmapSource RemoveAlpha(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(13, 15, 19)), null, new Rect(0, 0, width, height));
            dc.DrawImage(source, new Rect(0, 0, width, height));
        }

        var renderTarget = new RenderTargetBitmap(width, height, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
        renderTarget.Render(drawingVisual);
        renderTarget.Freeze();

        var converted = new FormatConvertedBitmap(renderTarget, PixelFormats.Bgr32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>
    /// Builds a project data snapshot from the current node graph state.
    /// </summary>
    public static ProjectFileService.ProjectData BuildProjectData(
        System.Collections.ObjectModel.ObservableCollection<NodeViewModel> nodes,
        System.Collections.ObjectModel.ObservableCollection<NodeConnectionViewModel> connections,
        int tileSize)
    {
        var data = new ProjectFileService.ProjectData { TileSize = tileSize };

        foreach (var node in nodes)
        {
            var nd = new ProjectFileService.NodeData
            {
                Title = node.Title,
                TypeName = node.TypeName,
                X = node.X,
                Y = node.Y,
                Kind = node.Kind,
                TileType = node.TileType,
                Properties = node.TileProperties?.Clone() ?? new TileProperties()
            };

            if (node.Kind != NodeLibraryItemKind.Tile)
            {
                foreach (var p in node.Parameters)
                {
                    nd.Parameters.Add(new ProjectFileService.NodeParameterData
                    {
                        Name = p.Name, Kind = p.Kind,
                        NumberValue = p.NumberValue, IntValue = p.IntValue,
                        BoolValue = p.BoolValue, SelectedChoice = p.SelectedChoice,
                        PointListData = new List<System.Windows.Point>(p.PointListValue)
                    });
                }
            }

            data.Nodes.Add(nd);
        }

        foreach (var conn in connections.Where(c => !c.IsPreview && c.StartNode != null && c.EndNode != null))
        {
            var startIndex = nodes.IndexOf(conn.StartNode!);
            var endIndex = nodes.IndexOf(conn.EndNode!);
            if (startIndex >= 0 && endIndex >= 0)
            {
                data.Connections.Add(new ProjectFileService.ConnectionData
                {
                    StartNodeIndex = startIndex,
                    StartPortIndex = conn.StartPortIndex,
                    EndNodeIndex = endIndex,
                    EndPortIndex = conn.EndPortIndex
                });
            }
        }

        return data;
    }
}
