using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Diagnostics;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.Gpu;
using PixelAssetGenerator.Services;
using ExportFormat = PixelAssetGenerator.Services.ExportService.ExportFormat;

namespace PixelAssetGenerator
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPreview(true);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var optionsDialog = new ExportOptionsDialog
            {
                Owner = this
            };

            if (optionsDialog.ShowDialog() != true)
            {
                return;
            }

            var format = ParseExportFormat(optionsDialog.SelectedFormatKey);
            var saveAlpha = optionsDialog.SaveAlpha;

            // Determine output nodes in the graph. If none found, fall back to last generated preview.
            var outputNodes = GetOutputNodes();

            // If there are no explicit output nodes, inform the user so they can add one.
            if (outputNodes.Count == 0)
            {
                try
                {
                    // Use dark themed transient dialog for consistency with application theme
                    DarkMessageBox.Show(this, "The current project has no output node.", "Notice");
                }
                catch
                {
                    // Ignore UI failures and continue.
                }
            }
            // If the user provided a path in the export options dialog, use it and skip additional save dialogs.
            if (!string.IsNullOrWhiteSpace(optionsDialog.SelectedFilePath))
            {
                var selectedPath = optionsDialog.SelectedFilePath!;

                // If there are no explicit output nodes, save the last generated bitmap to the selected path.
                if (outputNodes.Count == 0)
                {
                    if (_lastGenerated is null)
                    {
                        StatusText.Text = "No image generated";
                        return;
                    }

                    if (_nineSliceMode)
                    {
                        if (!TryGetSelectedSize(out var size))
                        {
                            StatusText.Text = "Invalid Size";
                            return;
                        }

                        var composite = GetNineSliceExportComposite(size);
                        if (composite == null)
                        {
                            StatusText.Text = "Select a tile to enable 9-slice";
                            return;
                        }

                        ExportNineSliceTiles(selectedPath, composite, size, format, saveAlpha);
                        StatusText.Text = "9-slice grid saved";
                        return;
                    }

                    ExportBitmap(selectedPath, _lastGenerated, format, saveAlpha);
                    StatusText.Text = "Saved";
                    return;
                }

                // Multiple outputs: use selected path as base and write one file per output node.
                var directory = Path.GetDirectoryName(selectedPath) ?? string.Empty;
                var baseName = Path.GetFileNameWithoutExtension(selectedPath);
                var extension = Path.GetExtension(selectedPath);
                if (string.IsNullOrEmpty(extension)) extension = GetDefaultExtension(format);

                for (int i = 0; i < outputNodes.Count; i++)
                {
                    var outputNode = outputNodes[i];
                    if (!TryGetOutputNodeSize(outputNode, out var size))
                    {
                        StatusText.Text = "Invalid Size";
                        return;
                    }

                    var sanitized = SanitizeFileName(outputNode.Title);
                    var indexedName = $"{baseName}_{i + 1}_{sanitized}{extension}";

                    if (_nineSliceMode)
                    {
                        var composite = BuildNineSliceCompositeForOutput(size, outputNode);
                        if (composite == null)
                        {
                            composite = CreateBlackGrid(size * 3, size);
                        }

                        var targetPath = Path.Combine(directory, indexedName);
                        ExportNineSliceTiles(targetPath, composite, size, format, saveAlpha);
                        continue;
                    }

                    BitmapSource? finalBitmap = null;

                    // If the output node is a graph-based node, evaluate the graph pipeline
                    // targeting that node so the exported image matches the node's final result.
                    if (IsGraphNode(outputNode))
                    {
                        try
                        {
                            finalBitmap = EvaluateGraphPipeline(size, outputNode);
                        }
                        catch { finalBitmap = null; }
                    }

                    // Fallback to layer-based pipeline when graph evaluation produced nothing
                    if (finalBitmap == null)
                    {
                        var defs = BuildNodeLayerDefinitionsForOutput(outputNode);
                        if (defs.Count == 0)
                        {
                            finalBitmap = CreateBlackBitmap(size, size);
                        }
                        else
                        {
                            finalBitmap = _generator.GenerateTileBitmap(size, defs);
                        }
                    }

                    var outPath = Path.Combine(directory, indexedName);
                    ExportBitmap(outPath, finalBitmap, format, saveAlpha);
                }

                StatusText.Text = outputNodes.Count > 1 ? "Saved output image" : "Saved";
                return;
            }

            // Use save dialogs when no export path was provided.
            if (outputNodes.Count == 0)
            {
                if (_lastGenerated is null)
                {
                    StatusText.Text = "No image generated";
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = GetFilterForFormat(format),
                    DefaultExt = GetDefaultExtension(format),
                    FileName = $"tile{GetDefaultExtension(format)}",
                    AddExtension = true
                };

                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                if (_nineSliceMode)
                {
                    if (!TryGetSelectedSize(out var size))
                    {
                        StatusText.Text = "Invalid Size";
                        return;
                    }

                    var composite = GetNineSliceExportComposite(size);
                    if (composite == null)
                    {
                        StatusText.Text = "Select a tile to enable 9-slice";
                        return;
                    }

                    ExportNineSliceTiles(dialog.FileName, composite, size, format, saveAlpha);
                    StatusText.Text = "9-slice grid saved";
                    return;
                }

                ExportBitmap(dialog.FileName, _lastGenerated, format, saveAlpha);
                StatusText.Text = "Saved";
                return;
            }

            // If multiple output nodes exist, use the chosen base filename as a pattern and write one file per output.
            var baseDialog = new SaveFileDialog
            {
                Filter = GetFilterForFormat(format),
                DefaultExt = GetDefaultExtension(format),
                FileName = $"tile{GetDefaultExtension(format)}",
                AddExtension = true
            };

            if (baseDialog.ShowDialog(this) != true)
            {
                return;
            }

            var baseDirectory = Path.GetDirectoryName(baseDialog.FileName) ?? string.Empty;
            var baseBaseName = Path.GetFileNameWithoutExtension(baseDialog.FileName);
            var baseExtension = Path.GetExtension(baseDialog.FileName);

            foreach (var outputNode in outputNodes)
            {
                if (!TryGetOutputNodeSize(outputNode, out var size))
                {
                    StatusText.Text = "Invalid Size";
                    return;
                }

                if (_nineSliceMode)
                {
                    var composite = BuildNineSliceCompositeForOutput(size, outputNode);
                    if (composite == null)
                    {
                        composite = CreateBlackGrid(size * 3, size);
                    }

                    var targetPath = Path.Combine(baseDirectory, $"{baseBaseName}_{SanitizeFileName(outputNode.Title)}{baseExtension}");
                    ExportNineSliceTiles(targetPath, composite, size, format, saveAlpha);
                    continue;
                }

                BitmapSource? finalBitmap = null;

                if (IsGraphNode(outputNode))
                {
                    try
                    {
                        finalBitmap = EvaluateGraphPipeline(size, outputNode);
                    }
                    catch { finalBitmap = null; }
                }

                if (finalBitmap == null)
                {
                    var defs = BuildNodeLayerDefinitionsForOutput(outputNode);
                    if (defs.Count == 0)
                    {
                        finalBitmap = CreateBlackBitmap(size, size);
                    }
                    else
                    {
                        finalBitmap = _generator.GenerateTileBitmap(size, defs);
                    }
                }

                var outPath = Path.Combine(baseDirectory, $"{baseBaseName}_{SanitizeFileName(outputNode.Title)}{baseExtension}");
                ExportBitmap(outPath, finalBitmap, format, saveAlpha);
            }

            StatusText.Text = outputNodes.Count > 1 ? "Saved output image" : "Saved";
        }

        private static ExportFormat ParseExportFormat(string? key)
        {
            return ExportService.ParseExportFormat(key);
        }

        private static string GetDefaultExtension(ExportFormat format)
        {
            return ExportService.GetDefaultExtension(format);
        }

        private static string GetFilterForFormat(ExportFormat format)
        {
            return ExportService.GetFilterForFormat(format);
        }

        private static bool SupportsAlpha(ExportFormat format)
        {
            return ExportService.SupportsAlpha(format);
        }

        private static BitmapEncoder CreateEncoder(ExportFormat format)
        {
            return ExportService.CreateEncoder(format);
        }

        private void ExportBitmap(string path, BitmapSource source, ExportFormat format, bool saveAlpha)
        {
            ExportService.ExportBitmap(path, source, format, saveAlpha);
        }

        private void ExportNineSliceTiles(string basePath, BitmapSource composite, int tileSize, ExportFormat format, bool saveAlpha)
        {
            ExportService.ExportNineSliceTiles(basePath, composite, tileSize, format, saveAlpha);
        }

        private static IEnumerable<(string Suffix, BitmapSource Bitmap)> GetNineSliceTiles(BitmapSource composite, int tileSize)
        {
            return ExportService.GetNineSliceTiles(composite, tileSize);
        }

        private BitmapSource? GetNineSliceExportComposite(int size)
        {
            if (_nineSliceStackMode)
            {
                return BuildNineSliceComposite(size);
            }

            if (SelectedNode != null && SelectedNode.TileType != null)
            {
                var def = BuildNodeLayerDefinition(SelectedNode);
                if (def.HasValue)
                {
                    var bitmap = _generator.GenerateTileBitmap(size, new[] { def.Value });
                    return TileToGrid(bitmap, size, size * 3);
                }
            }

            return BuildNineSliceComposite(size);
        }

        private static BitmapSource RemoveAlpha(BitmapSource source)
        {
            return ExportService.RemoveAlpha(source);
        }

        private int GetSelectedTileSize()
        {
            return TryGetSelectedSize(out var size) ? size : 32;
        }

        private void SetSelectedTileSize(int size)
        {
            foreach (var item in TileSizeCombo.Items)
            {
                if (item is ComboBoxItem comboItem && int.TryParse(comboItem.Tag?.ToString(), out var itemSize) && itemSize == size)
                {
                    TileSizeCombo.SelectedItem = comboItem;
                    return;
                }
            }
        }

        private void ParameterChanged(object sender, SelectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(AiImageOutputSizeText));

            // When global parameters change (e.g. tile size), ensure previews update
            RequestPreviewRefresh(false);

            // Update node library thumbnails so they match the selected tile size.
            _ = RefreshNodeLibraryThumbnailsAsync();

            // If the user changed the global tile size, update any open ShapeDrawingWindow instances
            try
            {
                var newSize = GetSelectedTileSize();
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is ShapeDrawingWindow sdw)
                    {
                        sdw.UpdateCanvasSize(newSize);
                    }
                }
            }
            catch
            {
                // ignore errors updating auxiliary windows
            }
        }

        private bool TryGetSelectedSize(out int size)
        {
            size = 32;
            if (TileSizeCombo.SelectedItem is not ComboBoxItem item)
            {
                return false;
            }

            return int.TryParse(item.Tag?.ToString(), out size);
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name);
            for (int i = 0; i < sb.Length; i++)
            {
                if (invalid.Contains(sb[i])) sb[i] = '_';
            }
            return sb.ToString();
        }

        private static BitmapSource CreateBlackBitmap(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0;     // B
                pixels[i + 1] = 0; // G
                pixels[i + 2] = 0; // R
                pixels[i + 3] = 255; // A
            }

            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            bmp.Freeze();
            return bmp;
        }

        private BitmapSource CreateBlackGrid(int outputSize, int tileSize)
        {
            var blackTile = CreateBlackBitmap(tileSize, tileSize);
            return TileToGrid(blackTile, tileSize, outputSize);
        }

        private async void BatchSeedExport_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetNodeOutputSize(out var size))
            {
                DarkMessageBox.Show(this, "Invalid output size.", "Batch Export");
                return;
            }

            // Find all seed parameters in graph nodes
            var seedParams = Nodes
                .SelectMany(n => n.Parameters.Where(p => p.Name == "seed" && (p.Kind == NodeParameterKind.Seed || p.Kind == NodeParameterKind.Integer)))
                .ToList();

            if (seedParams.Count == 0)
            {
                DarkMessageBox.Show(this, "No seed parameter found in current project.\nAdd a node with a \"seed\" parameter.", "Batch Export");
                return;
            }

            // Ask for count
            var countStr = Microsoft.VisualBasic.Interaction.InputBox("How many seed variants to export?", "Batch Export", "8", -1, -1);
            if (!int.TryParse(countStr, out var count) || count < 1 || count > 100)
            {
                DarkMessageBox.Show(this, "Enter a number between 1-100。", "Batch Export");
                return;
            }

            // Pick folder and base name
            var dialog = new SaveFileDialog
            {
                Filter = "PNG Images (*.png)|*.png",
                DefaultExt = ".png",
                FileName = "batch_export.png",
                AddExtension = true
            };

            if (dialog.ShowDialog(this) != true) return;

            var directory = Path.GetDirectoryName(dialog.FileName) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var format = ExportFormat.Png;

            StatusText.Text = "Batch Exporting...";

            // Save original seed values to restore later
            var originalSeeds = seedParams.Select(p => p.IntValue).ToList();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    // Set seed values for this iteration
                    for (int j = 0; j < seedParams.Count; j++)
                    {
                        seedParams[j].IntValue = originalSeeds[j] + i;
                    }

                    // Evaluate graph
                    BitmapSource? bitmap = null;

                    if (SelectedNode != null && IsGraphNode(SelectedNode))
                    {
                        bitmap = await Task.Run(() =>
                        {
                            var bmp = EvaluateGraphPipeline(size);
                            bmp?.Freeze();
                            return bmp;
                        });
                    }
                    else if (_lastGenerated != null)
                    {
                        bitmap = _lastGenerated;
                    }

                    if (bitmap == null)
                    {
                        StatusText.Text = $"Variant {i + 1} generation failed";
                        continue;
                    }

                    // Save
                    var fileName = $"{baseName}_seed{originalSeeds[0] + i:D4}.png";
                    var filePath = System.IO.Path.Combine(directory, fileName);
                    ExportService.ExportBitmap(filePath, bitmap, format, true);

                    StatusText.Text = $"Batch exporting... {i + 1}/{count}";
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }

                StatusText.Text = $"Batch export completed: {count} files";
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show(this, $"Export error: {ex.Message}", "Batch Export");
                StatusText.Text = "Batch Export Error";
            }
            finally
            {
                // Restore original seed values
                for (int j = 0; j < seedParams.Count; j++)
                {
                    seedParams[j].IntValue = originalSeeds[j];
                }
            }
        }
    }
}
