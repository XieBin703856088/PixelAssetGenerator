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
using System.Windows.Shapes;
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
        private void InitializeNodeLibrary()
        {
            string Lc(string key) => Loc.GetString(key);

            NodeLibraryCategories.Clear();
            NodeLibrary.Clear();

            var allLabel = Lc("LibCat_All");
            var totalAllCount = 0;
            var catCounts = new Dictionary<string, int>();

            // Scan Nodes/ subdirectories for categories
            var nodesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nodes");
            var categoryDirs = new List<string>();
            if (System.IO.Directory.Exists(nodesDir))
            {
                foreach (var dir in System.IO.Directory.EnumerateDirectories(nodesDir))
                {
                    var catName = System.IO.Path.GetFileName(dir);
                    categoryDirs.Add(catName);
                }
            }
            categoryDirs.Sort(); // Alphabetical order

            // Count nodes per category from catalog
            foreach (var prototype in Core.GraphNodeRegistry.GetAllPrototypes())
            {
                var cat = prototype.Category;
                if (string.IsNullOrEmpty(cat)) continue;
                catCounts.TryGetValue(cat, out var count);
                catCounts[cat] = count + 1;
                totalAllCount++;
            }

            // Add "All" category
            NodeLibraryCategories.Add(new NodeLibraryCategory(allLabel, totalAllCount));

            // Add dynamic categories (use directory name as key for filtering)
            foreach (var cat in categoryDirs)
            {
                var displayName = NodeLibraryService.GetCategoryDisplayName(cat);
                var count = catCounts.TryGetValue(cat, out var c) ? c : 0;
                NodeLibraryCategories.Add(new NodeLibraryCategory(displayName, count, cat));
            }

            System.Diagnostics.Debug.WriteLine($"[InitNodeLib] Prototypes loaded: {Core.GraphNodeRegistry.GetAllPrototypes().Count}");
            foreach (var __p in Core.GraphNodeRegistry.GetAllPrototypes())
                System.Diagnostics.Debug.WriteLine($"[InitNodeLib]  Proto: '{__p.TypeName}' Cat='{__p.Category}'");
            foreach (var __c in catCounts)
                System.Diagnostics.Debug.WriteLine($"[InitNodeLib]  CatCount: '{__c.Key}'={__c.Value}");
            foreach (var __d in categoryDirs)
                System.Diagnostics.Debug.WriteLine($"[InitNodeLib]  Dir: '{__d}'");

            // Diagnostic: Check Animation category explicitly
            if (catCounts.TryGetValue("Animation", out var animCount))
                System.Diagnostics.Debug.WriteLine($"[InitNodeLib] Animation category count from catCounts: {animCount}");
            else
                System.Diagnostics.Debug.WriteLine($"[InitNodeLib] 'Animation' key NOT found in catCounts!");
            var animProtos = Core.GraphNodeRegistry.GetByCategory("Animation").ToList();
            System.Diagnostics.Debug.WriteLine($"[InitNodeLib] GetByCategory('Animation') returned {animProtos.Count} nodes");
            foreach (var __ap in animProtos)
                System.Diagnostics.Debug.WriteLine($"[InitNodeLib]  Animation proto: '{__ap.TypeName}'");

            if (NodeLibraryCategories.Count > 0)
                SelectedNodeCategory = NodeLibraryCategories[0];

            // Build node library items from prototypes
            var addedCount = 0;
            var catalog = Core.GraphNodeRegistry.GetCatalog();
            var catalogLookup = catalog.ToLookup(r => r.Identity.TypeName, StringComparer.OrdinalIgnoreCase);
            var currentLocale = System.Globalization.CultureInfo.CurrentUICulture.Name;
            foreach (var prototype in Core.GraphNodeRegistry.GetAllPrototypes())
            {
                try
                {
                    var catKey = prototype.Category;
                    if (string.IsNullOrEmpty(catKey)) continue;

                    var category = NodeLibraryService.GetCategoryDisplayName(catKey);
                    var kind = NodeLibraryItemKind.Compute;

                    var previewBrush = CreateComputeNodePreviewBrush();

                    // Get localized port names from catalog if available
                    var catalogEntry = catalogLookup[prototype.TypeName].FirstOrDefault();
                    var inputPortNames = new List<string>();
                    var outputPortNames = new List<string>();
                    if (catalogEntry != null)
                    {
                        var catPorts = catalogEntry.Ports;
                        if (catPorts != null)
                        {
                            for (var i = 0; i < catPorts.Inputs.Count && i < prototype.InputPorts.Count; i++)
                                inputPortNames.Add(catPorts.Inputs[i].GetName(currentLocale));
                            for (var i = 0; i < catPorts.Outputs.Count && i < prototype.OutputPorts.Count; i++)
                                outputPortNames.Add(catPorts.Outputs[i].GetName(currentLocale));
                        }
                    }
                    // Fall back to prototype port names if no catalog entry
                    if (inputPortNames.Count == 0)
                    {
                        foreach (var port in prototype.InputPorts) inputPortNames.Add(port.Name);
                    }
                    if (outputPortNames.Count == 0)
                    {
                        foreach (var port in prototype.OutputPorts) outputPortNames.Add(port.Name);
                    }

                    var dn = Lc("NodeName_" + prototype.TypeName);
                    if (string.IsNullOrEmpty(dn) || dn == "NodeName_" + prototype.TypeName)
                        dn = GetNodeDisplayNameFromCatalog(prototype.TypeName) ?? prototype.TypeName;

                    var nodeDesc = catalogEntry?.Identity.Description.Get(currentLocale);
                    var aiRaw = catalogEntry?.Ai;
                    NodeAiMetadata? aiMeta = null;
                    if (aiRaw != null)
                    {
                        var triggers = new List<string>();
                        if (aiRaw.Triggers != null)
                        {
                            foreach (var kv in aiRaw.Triggers)
                                if (!string.IsNullOrWhiteSpace(kv.Value))
                                    triggers.Add(kv.Value);
                        }
                        aiMeta = new NodeAiMetadata
                        {
                            Capabilities = aiRaw.Capabilities,
                            Triggers = triggers.Count > 0 ? string.Join(", ", triggers) : null,
                            SuggestedInputs = aiRaw.SuggestedInputs,
                            ExampleUsage = aiRaw.ExampleUsage?.Get(currentLocale)
                        };
                    }
                    NodeLibrary.Add(new NodeLibraryItem(
                        dn, category, kind, previewBrush, null,
                        inputPortNames, outputPortNames, prototype.Parameters,
                        NodeLibraryService.SubcategoryMap.TryGetValue(prototype.TypeName, out var sub) ? sub : "",
                        prototype.TypeName, categoryKey: catKey,
                        description: nodeDesc, aiMetadata: aiMeta));
                    addedCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[InitNodeLib] ERROR adding '{prototype.TypeName}': {ex.GetType().Name}: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"[InitNodeLib] Added {addedCount} items to NodeLibrary");
        }

        private void NodeLibraryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is NodeLibraryCategory category)
            {
                System.Diagnostics.Debug.WriteLine($"[TreeView] Selected: '{category.Name}' Key='{category.Key}' Count={category.Count}");
                SelectedNodeCategory = category;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[TreeView] SelectedItemChanged but NewValue is {e.NewValue?.GetType().Name ?? "null"}");
            }
        }

        private void NodeLibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is NodeLibraryEntry entry && entry.Item != null)
            {
                var desc = GetNodeDescriptionFromCatalog(entry.Item.TypeName);
                if (!string.IsNullOrEmpty(desc))
                    StatusText.Text = desc;
                else
                    StatusText.Text = entry.Item.Name;
            }
        }

        /// <summary>
        /// Gets the description for a node type from .node.json catalog using the current UI language,
        /// falling back to the English description dictionary.
        /// </summary>
        private static string GetNodeDescriptionFromCatalog(string typeName)
        {
            var catalog = Core.GraphNodeRegistry.GetCatalog();
            foreach (var r in catalog)
            {
                if (string.Equals(r.Identity.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    var currentLocale = Loc.CurrentCulture;
                    var desc = r.Identity.Description.Get(currentLocale);
                    if (!string.IsNullOrEmpty(desc))
                        return desc;
                    break;
                }
            }
            return NodeLibraryService.GetNodeDescription(typeName);
        }

        /// <summary>
        /// Creates a flat parameter value dictionary from <see cref="NodeParameterDefinition"/> list,
        /// usable for direct <c>graphNode.Process()</c> calls on background threads.
        /// </summary>
        internal static Dictionary<string, object> CreateParameterValues(IReadOnlyList<NodeParameterDefinition> parameters)
        {
            var values = new Dictionary<string, object>();
            foreach (var def in parameters)
            {
                switch (def.Kind)
                {
                    case NodeParameterKind.Seed:
                    case NodeParameterKind.Integer:
                        values[def.Name] = def.DefaultInt;
                        break;
                    case NodeParameterKind.Boolean:
                        values[def.Name] = def.DefaultBool;
                        break;
                    case NodeParameterKind.Choice:
                        values[def.Name] = def.DefaultChoice ?? (def.Choices.Count > 0 ? def.Choices[0] : "");
                        break;
                    case NodeParameterKind.Color:
                        values[def.Name] = def.DefaultColor;
                        break;
                    case NodeParameterKind.PointList:
                        values[def.Name] = Array.Empty<Point>();
                        break;
                    case NodeParameterKind.Text:
                        values[def.Name] = "";
                        break;
                    default:
                        values[def.Name] = def.DefaultNumber;
                        break;
                }
            }
            return values;
        }

        private void SaveAsTemplateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedNodes();
            if (selected.Count == 0)
            {
                return;
            }

            var loc = Loc;
            var dialog = new SaveFileDialog
            {
                Title = loc.GetString("MW_SaveAsTemplate"),
                Filter = "PixelGraphTemplate (*.pixelgraph)|*.pixelgraph",
                FileName = "template.pixelgraph",
                InitialDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var data = BuildTemplateData(selected);
                    var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(dialog.FileName, json);

                    // Generate预览图并保存为同名 .png 文件
                    try
                    {
                        var previewPath = System.IO.Path.ChangeExtension(dialog.FileName, ".png");
                        SaveTemplatePreviewImage(previewPath, selected);
                    }
                    catch { }

                    NodeLibraryService.RefreshTemplateFiles(TemplateFiles);
                }
                catch (Exception ex)
                {
                    DarkMessageBox.Show(this, $"{loc.GetString("MW_FailedToSaveTemplate")} {ex.Message}", loc.GetString("MW_Error"));
                }
            }
        }

        private void SaveTemplatePreviewImage(string previewPath, List<NodeViewModel> selectedNodes)
        {
            // 尝试Generate一个缩略图预览：遍历选中节点，尝试Generate预览
            const int previewSize = 64;
            var drawingGroup = new DrawingGroup();

            foreach (var node in selectedNodes)
            {
                try
                {
                    var brush = node.PreviewBrush;
                    if (brush != null)
                    {
                        var rect = new System.Windows.Rect(0, 0, previewSize, previewSize);
                        var geo = new RectangleGeometry(rect);
                        drawingGroup.Children.Add(new GeometryDrawing(brush, null, geo));
                    }
                }
                catch { }
            }

            if (drawingGroup.Children.Count == 0) return;

            drawingGroup.Freeze();
            var renderSize = previewSize * 2;
            var target = new RenderTargetBitmap(renderSize, renderSize, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawDrawing(drawingGroup);
            }
            target.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using (var stream = System.IO.File.Open(previewPath, System.IO.FileMode.Create))
            {
                encoder.Save(stream);
            }
        }

        private void DeleteTemplateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var loc = Loc;
            if (sender is System.Windows.FrameworkElement fe && fe.DataContext is TemplateFileInfo template)
            {
                var result = DarkMessageBox.ShowConfirm(this,
                    $"{loc.GetString("MW_ConfirmDeleteTemplate")} \"{template.DisplayName}\"?\n\n{template.FullPath}",
                    loc.GetString("MW_DeleteTemplate"));
                if (result != true) return;

                try
                {
                    if (System.IO.File.Exists(template.FullPath))
                        System.IO.File.Delete(template.FullPath);
                    var previewPath = System.IO.Path.ChangeExtension(template.FullPath, ".png");
                    if (System.IO.File.Exists(previewPath))
                        System.IO.File.Delete(previewPath);
                    NodeLibraryService.RefreshTemplateFiles(TemplateFiles);
                }
                catch (Exception ex)
                {
                    DarkMessageBox.Show(this, $"{loc.GetString("MW_FailedToDeleteTemplate")} {ex.Message}", loc.GetString("MW_Error"));
                }
            }
        }

        private void RenameTemplateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var loc = Loc;
            if (sender is System.Windows.FrameworkElement fe && fe.DataContext is TemplateFileInfo template)
            {
                var newName = DarkInputBox.Show(this,
                    loc.GetString("MW_EnterTemplateName"),
                    loc.GetString("MW_RenameTemplate"),
                    template.DisplayName);
                if (string.IsNullOrWhiteSpace(newName) || newName == template.DisplayName) return;

                var safeName = newName.Trim();
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                    safeName = safeName.Replace(ch, '_');

                var baseDir = System.IO.Path.GetDirectoryName(template.FullPath);
                if (baseDir == null) return;

                var newJsonPath = System.IO.Path.Combine(baseDir, safeName + ".pixelgraph");
                var oldPreviewPath = System.IO.Path.ChangeExtension(template.FullPath, ".png");
                var newPreviewPath = System.IO.Path.Combine(baseDir, safeName + ".png");

                try
                {
                    if (System.IO.File.Exists(newJsonPath) && !template.FullPath.Equals(newJsonPath, StringComparison.OrdinalIgnoreCase))
                    {
                        DarkMessageBox.Show(this, $"{loc.GetString("MW_FileAlreadyExists")} \"{safeName}.pixelgraph\"", loc.GetString("MW_RenameFailed"));
                        return;
                    }
                    System.IO.File.Move(template.FullPath, newJsonPath);
                    if (System.IO.File.Exists(oldPreviewPath))
                        System.IO.File.Move(oldPreviewPath, newPreviewPath);
                    NodeLibraryService.RefreshTemplateFiles(TemplateFiles);
                }
                catch (Exception ex)
                {
                    DarkMessageBox.Show(this, $"{loc.GetString("MW_FailedToRename")} {ex.Message}", loc.GetString("MW_Error"));
                }
            }
        }

        private ProjectFileService.ProjectData BuildTemplateData(List<NodeViewModel> selectedNodes)
        {
            return _nodeGraphController.BuildTemplateData(selectedNodes, GetSelectedTileSize());
        }

        private void LoadTemplateData(ProjectFileService.ProjectData data, double dropX, double dropY)
        {
            if (data.Nodes.Count == 0) return;
            var createdNodes = _nodeGraphController.PasteClipboardAtMouse(
                data, NodeCanvasScale,
                () => ContentToHost(new Point(dropX, dropY)),
                () => double.MaxValue,
                () => double.MaxValue);

            foreach (var node in createdNodes)
            {
                var libraryItem = FindNodeLibraryItem(node.TypeName);
                if (libraryItem == null) continue;
                node.PreviewBrush = libraryItem.PreviewBrush;
                node.Category = libraryItem.Category;
            }

            if (createdNodes.Count > 0) SelectedNode = createdNodes[^1];
        }

        private void Nodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<NodeViewModel>())
                {
                    item.PropertyChanged -= Node_PropertyChanged;
                    foreach (var parameter in item.Parameters)
                    {
                        parameter.PropertyChanged -= NodeParameter_PropertyChanged;
                    }

                    if (_nodeTilePropertiesMap.TryGetValue(item, out var tp) && tp != null && _nodeTilePropertiesHandlers.TryGetValue(item, out var h))
                    {
                        try
                        {
                            tp.PropertyChanged -= h;
                        }
                        catch
                        {

                        }

                        _nodeTilePropertiesMap.Remove(item);
                        _nodeTilePropertiesHandlers.Remove(item);
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<NodeViewModel>())
                {
                    item.PropertyChanged += Node_PropertyChanged;
                    foreach (var parameter in item.Parameters)
                    {
                        parameter.PropertyChanged += NodeParameter_PropertyChanged;
                    }

                    if (item.TileProperties != null)
                    {
                        _nodeTilePropertiesMap[item] = item.TileProperties;
                        // When tile properties change, update node preview immediately and refresh the main preview.
                        PropertyChangedEventHandler handler = (_, __) =>
                        {
                            ConfigureTileNodePorts(item);

                            if (item.TileType != null)
                            {
                                var tileType = item.TileType.Value;
                                var fallback = item.PreviewBrush;
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        var brush = CreateNodePreviewBrush(tileType, fallback);
                                        // assign on UI thread
                                        Dispatcher.Invoke(() =>
                                        {
                                            try { item.PreviewBrush = brush; } catch { }
                                        });
                                    }
                                    catch { }
                                });
                            }

                            RequestPreviewRefresh(false);
                        };
                        _nodeTilePropertiesHandlers[item] = handler;
                        item.TileProperties.PropertyChanged += handler;
                    }
                }
            }

            UpdateNodeCanvasExtent();
            try
            {
                NodeConnectionsView?.Refresh();
            }
            catch
            {
            }

            UpdateAnimationUI();
            ScheduleConnectionGeometryRefresh();

            // When nodes change, schedule a debounced preview refresh (batch-safe for AI tool calls).
            ScheduleNodePreviewUpdate();
        }

        private void NodeConnections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                // Connection topology changed — update visuals and node previews
                UpdateConnectionPositions();
            }
            catch { }

            ScheduleConnectionGeometryRefresh();
            OnPropertyChanged(nameof(AiImageReferenceStatus));
            ScheduleNodePreviewUpdate();
        }

        private void Node_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {

            if (e.PropertyName is nameof(NodeViewModel.X) or nameof(NodeViewModel.Y))
            {
                if (sender is NodeViewModel node)
                {
                    UpdateConnectionPositions(node);
                }

                if (_draggingNode == null)
                {
                    UpdateNodeCanvasExtent();
                }
                return;
            }


            try
            {
                if (_isSyncing) return;
                RequestPreviewRefresh(false);
            }
            catch
            {
            }
        }

        private void UpdateNodeCanvasExtent()
        {
            const double minWidth = 800d;
            const double minHeight = 600d;
            const double padding = 320d;
            const double nodeWidth = 200d;

            if (Nodes.Count == 0)
            {
                NodeCanvasWidth = minWidth;
                NodeCanvasHeight = minHeight;
                return;
            }

            var minX = Nodes.Min(node => node.X);
            var minY = Nodes.Min(node => node.Y);
            var maxX = Nodes.Max(node => node.X);
            var maxY = Nodes.Max(node => node.Y);

            var width = Math.Max(minWidth, (maxX - Math.Min(0d, minX)) + nodeWidth + padding);
            var height = Math.Max(minHeight, (maxY - Math.Min(0d, minY)) + nodeWidth + padding);

            NodeCanvasWidth = width;
            NodeCanvasHeight = height;
        }

        private void UpdateConnectionPositions(NodeViewModel? targetNode = null)
        {
            foreach (var connection in NodeConnections)
            {
                if (connection.IsPreview)
                {
                    if (connection.StartNode != null && (targetNode == null || ReferenceEquals(connection.StartNode, targetNode)))
                    {
                        var startPoint = GetCachedPortPosition(connection.StartNode, true, connection.StartPortIndex);
                        connection.StartX = startPoint.X;
                        connection.StartY = startPoint.Y;
                    }

                    continue;
                }

                if (connection.StartNode != null && (targetNode == null || ReferenceEquals(connection.StartNode, targetNode)))
                {
                    var startPoint = GetCachedPortPosition(connection.StartNode, true, connection.StartPortIndex);
                    connection.StartX = startPoint.X;
                    connection.StartY = startPoint.Y;
                }

                if (connection.EndNode != null && (targetNode == null || ReferenceEquals(connection.EndNode, targetNode)))
                {
                    var endPoint = GetCachedPortPosition(connection.EndNode, false, connection.EndPortIndex);
                    connection.EndX = endPoint.X;
                    connection.EndY = endPoint.Y;
                }
            }
        }

        /// <summary>
        /// Returns cached actual port position if available, otherwise falls back to manual calculation.
        /// </summary>
        private static Point GetCachedPortPosition(NodeViewModel node, bool isOutput, int portIndex)
        {
            // Cache local offsets rather than absolute canvas coordinates so wires stay
            // attached to the exact port center while a node or a selected group moves.
            if (node.CachedPortPositions.TryGetValue((isOutput, portIndex), out var localOffset))
                return new Point(node.X + localOffset.X, node.Y + localOffset.Y);
            return GetPortPosition(node, isOutput, portIndex);
        }

        /// <summary>
        /// Stores the actual port position from a FrameworkElement into the node's cache.
        /// Call this from port mouse handlers to ensure connection endpoints align with visual ports.
        /// </summary>
        private void CachePortPosition(FrameworkElement portElement, NodeViewModel node, bool isOutput, int portIndex)
        {
            try
            {
                var center = GetPortCenter(portElement);
                node.CachedPortPositions[(isOutput, portIndex)] =
                    new Point(center.X - node.X, center.Y - node.Y);
            }
            catch { }
        }

        /// <summary>
        /// Populates cached port positions for all nodes by walking the visual tree
        /// of NodeCanvasItems. Call after layout is complete (e.g. after Loaded event).
        /// </summary>
        private void CacheAllPortPositions()
        {
            if (NodeCanvasItems == null) return;
            try
            {
                foreach (var item in NodeCanvasItems.Items)
                {
                    if (item is not NodeViewModel node) continue;
                    var presenter = NodeCanvasItems.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                    if (presenter == null) continue;
                    CachePortPositionsForNode(node, presenter);
                }
            }
            catch { }
        }

        /// <summary>
        /// Walks the visual tree of a node's ContentPresenter to find and cache port element positions.
        /// </summary>
        private void CachePortPositionsForNode(NodeViewModel node, FrameworkElement nodeContainer)
        {
            // Find the Border (node template root) inside the ContentPresenter
            var border = FindVisualChild<System.Windows.Controls.Border>(nodeContainer);
            if (border == null) return;

            // Find all port Grids (Ellipse parent Grids) and match them by binding context
            var portGrids = FindVisualChildren<System.Windows.Controls.Grid>(border)
                .Where(g => g.DataContext is NodePortViewModel)
                .ToList();

            foreach (var grid in portGrids)
            {
                if (grid.DataContext is not NodePortViewModel port) continue;
                var node2 = FindNodeFromPort(port);
                if (node2 == null || node2.Id != node.Id) continue;
                CachePortPosition(grid, node2, port.IsOutput, port.IsOutput
                    ? node2.OutputPorts.IndexOf(port)
                    : node2.InputPorts.IndexOf(port));
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private static List<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            var results = new List<T>();
            FindVisualChildrenRecursive(parent, results);
            return results;
        }

        private static void FindVisualChildrenRecursive<T>(DependencyObject parent, List<T> results) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) results.Add(t);
                FindVisualChildrenRecursive(child, results);
            }
        }

        /// <summary>
        /// Clears all cached port positions. Call after node drag or when layout changes.
        /// </summary>
        private void ClearAllPortPositionCache()
        {
            foreach (var node in Nodes)
            {
                node.CachedPortPositions.Clear();
            }
        }

        private void UpdateBuildingTilePreview()
        {
            BuildingTilePreviewCells.Clear();

            if (SelectedNode == null || !IsBuildingNode(SelectedNode))
                return;

            var cols = 4;
            var rows = 4;

            var wallColor = Color.FromRgb(200, 200, 210);
            var roofColor = Color.FromRgb(150, 80, 60);
            var windowColor = Color.FromRgb(100, 180, 255);
            var doorColor = Color.FromRgb(120, 80, 50);

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    Brush color;
                    if (row == 0)
                    {
                        color = new SolidColorBrush(roofColor);
                    }
                    else if (row == rows - 1 && col == cols / 2)
                    {
                        color = new SolidColorBrush(doorColor);
                    }
                    else if (row > 0 && (row + col) % 2 == 0)
                    {
                        color = new SolidColorBrush(windowColor);
                    }
                    else
                    {
                        color = new SolidColorBrush(wallColor);
                    }

                    BuildingTilePreviewCells.Add(new TilePreviewCell { Color = color });
                }
            }
        }

        private void UpdateVariationPreviews()
        {
            VariationPreviews.Clear();

            if (SelectedNode == null || SelectedNode.Title != "variant")
                return;

            var seedParam = SelectedNode.Parameters.FirstOrDefault(p => p.Kind == NodeParameterKind.Seed);
            var baseSeed = seedParam?.IntValue ?? 42;
            var count = 8;

            for (var i = 0; i < count; i++)
            {
                var variantSeed = baseSeed + i * 100 + 1;
                try
                {
                    // Create a temporary copy of the node with the variant seed
                    var tempNode = new NodeViewModel(SelectedNode.Title, 0, 0, SelectedNode.PreviewBrush)
                    {
                        Kind = SelectedNode.Kind,
                        TileType = SelectedNode.TileType
                    };

                    foreach (var p in SelectedNode.Parameters)
                    {
                        var pvm = new NodeParameterViewModel(p.Name, p.Kind, p.Min, p.Max, p.Step, p.Choices.ToList())
                        {
                            NumberValue = p.NumberValue,
                            IntValue = p.Kind == NodeParameterKind.Seed ? variantSeed : p.IntValue,
                            BoolValue = p.BoolValue,
                            SelectedChoice = p.SelectedChoice
                        };
                        tempNode.Parameters.Add(pvm);
                    }

                    var preview = GenerateComputeNodePreviewBrush(tempNode);
                    if (preview != null)
                    {
                        VariationPreviews.Add(new VariationPreviewItem { Preview = preview, Seed = variantSeed });
                    }
                    else
                    {
                        // Fallback: draw a colored swatch
                        var hue = (variantSeed * 0.618f) % 1f;
                        var fallbackColor = Color.FromRgb(
                            (byte)(Math.Sin(hue * Math.PI * 2) * 127 + 128),
                            (byte)(Math.Sin((hue + 0.33f) * Math.PI * 2) * 127 + 128),
                            (byte)(Math.Sin((hue + 0.67f) * Math.PI * 2) * 127 + 128));
                        var fallbackBrush = new SolidColorBrush(fallbackColor);
                        fallbackBrush.Freeze();
                        VariationPreviews.Add(new VariationPreviewItem { Preview = fallbackBrush, Seed = variantSeed });
                    }
                }
                catch
                {
                    // Skip failed variation
                }
            }
        }

        private void VariationPreview_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int seed)
            {
                if (SelectedNode != null && SelectedNode.Title == "variant")
                {
                    var seedParam = SelectedNode.Parameters.FirstOrDefault(p => p.Kind == NodeParameterKind.Seed);
                    if (seedParam != null)
                    {
                        seedParam.IntValue = seed;
                        RequestPreviewRefresh(false);
                    }
                }
            }
        }

        private void NodeParameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is NodeParameterViewModel param)
            {
                var node = Nodes.FirstOrDefault(n => n.Parameters.Contains(param));
                if (node != null)
                {
                    MarkAiImageParametersDirty(node, param, e.PropertyName);

                    if (node.Kind == NodeLibraryItemKind.Compute)
                    {
                        try
                        {
                            if (IsGraphNode(node))
                                node.PreviewBrush = GenerateGraphNodePreviewBrush(node) ?? node.PreviewBrush;
                            else
                                node.PreviewBrush = GenerateComputeNodePreviewBrush(node);
                        }
                        catch
                        {

                        }
                    }

                    if (IsBuildingNode(node))
                    {
                        UpdateBuildingTilePreview();
                    }

                    if (node.Title == "variant")
                    {
                        UpdateVariationPreviews();
                    }

                    // Keep simulation continuous while ordinary sliders move. Only
                    // structural changes that cannot be applied in-place restart state.
                    if (RequiresSimulationRestart(node, param))
                    {
                        _particleEvalService?.ClearState(node.Id);
                    }

                    if (node.TypeName == "AnimationWorkflowOutput")
                    {
                        if (param.Name == "workflowName" && e.PropertyName == nameof(NodeParameterViewModel.TextValue)
                            && !string.IsNullOrWhiteSpace(param.TextValue))
                            node.Title = param.TextValue.Trim();
                        ApplyActiveWorkflowPlaybackSettings();
                    }
                }
            }

            RequestPreviewRefresh(false);
        }

        private static bool RequiresSimulationRestart(NodeViewModel node, NodeParameterViewModel parameter)
        {
            return node.TypeName switch
            {
                "ParticleEmitter" => parameter.Name is "preset" or "maxParticles" or "prewarm"
                    or "prewarmSeconds" or "oneShot",
                "ParticleEffectMeta" => parameter.Name is "effect" or "seed" or "prewarm",
                // PhysicsSoftBody performs a targeted topology rebuild internally.
                // Physics/force/render controls are synchronized every frame.
                _ => false
            };
        }

        private void TemplateList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is TemplateFileInfo template)
            {
                var data = new DataObject("pixelgraph-template", template.FullPath);
                DragDrop.DoDragDrop(listBox, data, DragDropEffects.Copy);
            }
        }

        /// <summary>
        /// Looks up the localized display name for a node type from .node.json files.
        /// Falls back to null.
        /// </summary>
        private static string? GetNodeDisplayNameFromCatalog(string typeName)
        {
            try
            {
                var nodesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nodes");
                if (!System.IO.Directory.Exists(nodesDir)) return null;

                var twoLetter = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                var alreadyRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var files = System.IO.Directory.GetFiles(nodesDir, "*.node.json", System.IO.SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    var json = System.IO.File.ReadAllText(file);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("processorType", out var pt) || pt.ValueKind != System.Text.Json.JsonValueKind.String)
                        continue;
                    var procType = pt.GetString();
                    if (string.IsNullOrEmpty(procType)) continue;
                    if (alreadyRead.Add(procType) && string.Equals(procType, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!root.TryGetProperty("identity", out var identity)
                            || !identity.TryGetProperty("displayName", out var displayName))
                            continue;

                        // Try exact culture match (e.g. "zh-CN")
                        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
                        if (displayName.TryGetProperty(culture, out var localized))
                            return localized.GetString();

                        // Try two-letter prefix match (e.g. "zh-Hans" when culture is "zh-CN")
                        foreach (var prop in displayName.EnumerateObject())
                        {
                            if (prop.Name.StartsWith(twoLetter, StringComparison.OrdinalIgnoreCase))
                                return prop.Value.GetString();
                        }

                        // Fall back to English
                        if (displayName.TryGetProperty("en", out var english))
                            return english.GetString();
                        // Fall back to first available
                        foreach (var prop in displayName.EnumerateObject())
                            return prop.Value.GetString();
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Maps a <see cref="GraphPortType"/> from the core graph model to the UI <see cref="PortValueType"/>.
        /// </summary>
        private static PortValueType MapGraphPortType(GraphPortType graphPortType) => graphPortType switch
        {
            GraphPortType.Image => PortValueType.Image,
            GraphPortType.Mask  => PortValueType.Mask,
            GraphPortType.Float => PortValueType.Float,
            GraphPortType.Color => PortValueType.Color,
            GraphPortType.Any   => PortValueType.Any,
            GraphPortType.Particle => PortValueType.Particle,
            _                   => PortValueType.Float
        };

        /// <summary>
        /// Returns true when an output port of <paramref name="outputType"/> can feed an input port of <paramref name="inputType"/>.
        /// Image and Tile are both RGB pixel data and are considered mutually compatible.
        /// Image/Tile outputs can also connect to Mask inputs (the mask reads the R channel).
        /// </summary>
        private static bool ArePortTypesCompatible(PortValueType outputType, PortValueType inputType)
        {
            if (inputType == PortValueType.Any) return true;
            if (outputType == inputType) return true;
            // Image and Tile are both RGB buffers — interchangeable in connections
            bool outputIsImage = outputType is PortValueType.Image or PortValueType.Tile;
            bool inputIsImage  = inputType  is PortValueType.Image or PortValueType.Tile;
            if (outputIsImage && inputIsImage) return true;
            // Image/Tile can connect to Mask — mask blending reads the R channel
            if (outputIsImage && inputType == PortValueType.Mask) return true;
            // Particle ports only connect to other particle ports
            if (outputType == PortValueType.Particle || inputType == PortValueType.Particle)
                return false;
            return false;
        }

        /// <summary>
        /// Checks whether a UI-level <see cref="PortValueType"/> (from an output port)
        /// is compatible with a core-level <see cref="GraphPortType"/> (from a prototype input port).
        /// </summary>
        private static bool IsGraphPortTypeCompatible(PortValueType outputType, GraphPortType inputType)
        {
            if (inputType == GraphPortType.Any) return true;
            var outputIsImage = outputType is PortValueType.Image or PortValueType.Tile;
            var inputIsImage = inputType is GraphPortType.Image;
            if (outputIsImage && inputIsImage) return true;
            if (outputIsImage && inputType == GraphPortType.Mask) return true;
            return outputType switch
            {
                PortValueType.Mask => inputType == GraphPortType.Mask,
                PortValueType.Float => inputType == GraphPortType.Float,
                PortValueType.Integer => inputType == GraphPortType.Float,
                PortValueType.Color => inputType == GraphPortType.Color,
                PortValueType.Boolean => inputType == GraphPortType.Float,
                PortValueType.Any => true,
                PortValueType.Tile => inputType == GraphPortType.Image,
                PortValueType.Particle => inputType == GraphPortType.Particle,
                _ => false
            };
        }

        /// <summary>
        /// Maps category string to the UI category string used in <see cref="NodeViewModel.Category"/>.
        /// </summary>
        private static string GetCategoryString(string cat) => cat;

        /// <summary>
        /// Maps category string to the UI <see cref="NodeLibraryItemKind"/>.
        /// </summary>
        private static NodeLibraryItemKind GetKindForCategory(string cat) => NodeLibraryItemKind.Compute;

        private static System.Windows.Media.Color GetCategoryColor(string category)
        {
            if (string.IsNullOrEmpty(category))
                return System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55);
            unchecked
            {
                var hash = category.GetHashCode();
                var r = (byte)(((hash >> 16) & 0xFF) * 0.5 + 80);
                var g = (byte)(((hash >> 8) & 0xFF) * 0.5 + 80);
                var b = (byte)((hash & 0xFF) * 0.5 + 80);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
        }

        private static bool IsBuildingNode(NodeViewModel node) =>
            node.TypeName is "Wall" or "Floor";

        /// <summary>
        /// Shows a popup listing all graph prototypes whose first input port
        /// is compatible with <paramref name="outputType"/>. Clicking a row
        /// creates the node, places it at <paramref name="canvasPos"/>, and wires the
        /// active connection to its first input port. Includes a search box for filtering.
        /// </summary>
        private void ShowConnectionCreationMenu(Point canvasPos, NodeViewModel sourceNode, int sourcePortIndex, PortValueType outputType)
        {
            // Collect compatible prototypes that have at least one compatible input port
            var compatibleNodes = new List<(string typeName, IGraphNode proto)>();
            foreach (var proto in GraphNodeRegistry.GetAllPrototypes())
            {
                if (proto.InputPorts.Count > 0 && IsGraphPortTypeCompatible(outputType, proto.InputPorts[0].Type))
                {
                    compatibleNodes.Add((proto.TypeName, proto));
                }
            }

            if (compatibleNodes.Count == 0) return;

            // Build a preview cache from the node library (already generated thumbnails).
            var previewCache = new Dictionary<string, Brush?>();
            foreach (var libItem in NodeLibrary)
            {
                if (!previewCache.ContainsKey(libItem.Name))
                    previewCache[libItem.Name] = libItem.PreviewBrush;
            }

            // Pre-compute localized names for all compatible nodes
            var loc = Loc;
            var localizedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in compatibleNodes)
            {
                var resxName = loc.GetString("NodeName_" + n.typeName);
                if (!string.IsNullOrEmpty(resxName) && resxName != "NodeName_" + n.typeName)
                    localizedNames[n.typeName] = resxName;
                else
                    localizedNames[n.typeName] = GetNodeDisplayNameFromCatalog(n.typeName) ?? n.typeName;
            }

            var sRgb = Color.FromRgb;

            // Declare popup and originalMenu before lambdas that capture them
            System.Windows.Controls.Primitives.Popup? popup = null;
            var originalMenu = NodeCanvasHost.ContextMenu;

            // ── Grouped data (search will filter this) ──
            var groupedData = compatibleNodes
                .GroupBy(n => GetCategoryString(n.proto.Category))
                .OrderBy(g => g.Key)
                .Select(g => (
                    Category: g.Key,
                    Items: g.OrderBy(n => n.typeName).ToList()
                ))
                .ToList();

            // ── Search box ──
            var searchBox = new TextBox
            {
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush(sRgb(0x14, 0x18, 0x22)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(sRgb(0x3B, 0x41, 0x50)),
                BorderThickness = new Thickness(1),
                CaretBrush = Brushes.White,
                Text = "",
                MinWidth = 160
            };
            var placeholder = new TextBlock
            {
                Text = Loc.GetString("MW_SearchNodes"),
                Foreground = new SolidColorBrush(sRgb(0x88, 0x88, 0x88)),
                FontSize = 13,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var searchBorder = new Border { Child = searchBox };
            var searchGrid = new Grid();
            searchGrid.Children.Add(searchBorder);
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            placeholder.Margin = new Thickness(8, 0, 0, 0);
            searchGrid.Children.Add(placeholder);
            // Show/hide placeholder
            searchBox.TextChanged += (_, _) =>
                placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            // ── Item list container ──
            var itemStack = new StackPanel();
            var scroll = new ScrollViewer
            {
                MaxHeight = 360,
                Content = itemStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Rebuild the visible list from the filter string
            string currentFilter = "";
            int selectedIndex = -1;
            List<Border> itemBorders = new();

            void RebuildList()
            {
                itemStack.Children.Clear();
                itemBorders.Clear();
                selectedIndex = -1;

                foreach (var group in groupedData)
                {
                    var filtered = group.Items
                        .Where(i =>
                        {
                            if (string.IsNullOrWhiteSpace(currentFilter)) return true;
                            var localized = localizedNames.TryGetValue(i.typeName, out var ln) ? ln : i.typeName;
                            return i.typeName.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   localized.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   PinyinHelper.MatchesPinyin(localized, currentFilter);
                        })
                        .ToList();
                    if (filtered.Count == 0) continue;

                    // Category header
                    itemStack.Children.Add(new TextBlock
                    {
                        Text = $"── {group.Category} ──",
                        Foreground = new SolidColorBrush(sRgb(0x66, 0x77, 0x88)),
                        FontSize = 11,
                        Margin = new Thickness(8, 4, 0, 2)
                    });

                    foreach (var (name, proto) in filtered)
                    {
                        var border = new Border
                        {
                            Padding = new Thickness(8d, 4d, 8d, 4d),
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Background = Brushes.Transparent
                        };
                        var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        var thumb = previewCache.TryGetValue(name, out var tb) ? tb : null;
                        if (thumb != null)
                        {
                            itemPanel.Children.Add(new Rectangle
                            {
                                Width = 20, Height = 20,
                                Fill = thumb,
                                RadiusX = 2, RadiusY = 2,
                                Margin = new Thickness(0, 0, 6, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            });
                        }
                        else
                        {
                            var catColor = GetCategoryColor(proto.Category);
                            itemPanel.Children.Add(new Border
                            {
                                Width = 10, Height = 10,
                                CornerRadius = new CornerRadius(2),
                                Background = new SolidColorBrush(catColor),
                                Margin = new Thickness(0, 0, 6, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            });
                        }
                        var displayName = localizedNames.TryGetValue(name, out var localized) ? localized : name;
                        var text = new TextBlock
                        {
                            Text = displayName,
                            Foreground = new SolidColorBrush(sRgb(0xCC, 0xCC, 0xCC)),
                            FontSize = 12
                        };
                        itemPanel.Children.Add(text);
                        border.Child = itemPanel;

                        var capturedName = name;
                        border.MouseLeftButtonDown += (s, e) =>
                        {
                            CreateAndConnectNode(capturedName, canvasPos, sourceNode, sourcePortIndex);
                            if (popup != null) popup.IsOpen = false;
                            e.Handled = true;
                        };
                        border.MouseEnter += (s, e) =>
                        {
                            border.Background = new SolidColorBrush(sRgb(0x2B, 0x31, 0x40));
                            // Update keyboard selection highlight
                            foreach (var b in itemBorders)
                                b.Background = ReferenceEquals(b, border)
                                    ? new SolidColorBrush(sRgb(0x2B, 0x31, 0x40))
                                    : Brushes.Transparent;
                        };
                        border.MouseLeave += (s, e) =>
                        {
                            if (!ReferenceEquals(border, itemBorders.ElementAtOrDefault(selectedIndex)))
                                border.Background = Brushes.Transparent;
                        };
                        itemStack.Children.Add(border);
                        itemBorders.Add(border);
                    }
                }
            }

            RebuildList();

            // Filter on typing
            searchBox.TextChanged += (_, _) =>
            {
                currentFilter = searchBox.Text;
                RebuildList();
            };

            // Keyboard navigation
            searchBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Down)
                {
                    if (itemBorders.Count > 0)
                    {
                        var next = Math.Min(selectedIndex + 1, itemBorders.Count - 1);
                        SetSelectedItem(next, itemBorders, sRgb);
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Up)
                {
                    if (itemBorders.Count > 0)
                    {
                        var prev = selectedIndex <= 0 ? itemBorders.Count - 1 : selectedIndex - 1;
                        SetSelectedItem(prev, itemBorders, sRgb);
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    if (selectedIndex >= 0 && selectedIndex < itemBorders.Count)
                    {
                        var border = itemBorders[selectedIndex];
                        var text = (border.Child as TextBlock)?.Text;
                        if (text != null)
                        {
                            CreateAndConnectNode(text, canvasPos, sourceNode, sourcePortIndex);
                            if (popup != null) popup.IsOpen = false;
                        }
                    }
                    e.Handled = true;
                }
            };

            var outerStack = new StackPanel();
            outerStack.Children.Add(searchGrid);
            outerStack.Children.Add(scroll);

            var contentBorder = new Border
            {
                Background = new SolidColorBrush(sRgb(0x1A, 0x1E, 0x28)),
                BorderBrush = new SolidColorBrush(sRgb(0x2B, 0x31, 0x40)),
                BorderThickness = new Thickness(1d),
                CornerRadius = new CornerRadius(4d),
                Padding = new Thickness(4d),
                Child = outerStack,
                MinWidth = 160,
                Focusable = true,
                FocusVisualStyle = null
            };

            // Close popup on Escape
            contentBorder.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && popup != null)
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                }
            };

            // Block mouse events inside popup from closing it
            contentBorder.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = false;
            };

            popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = NodeCanvasHost,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = contentBorder,
                StaysOpen = true
            };

            // Close when clicking on the canvas (outside the popup).
            void CanvasClickHandler(object s, MouseButtonEventArgs e)
            {
                if (popup != null && popup.IsOpen)
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                }
            }
            NodeCanvasHost.PreviewMouseLeftButtonDown += CanvasClickHandler;

            popup.Closed += (_, _) =>
            {
                NodeCanvasHost.PreviewMouseLeftButtonDown -= CanvasClickHandler;
                if (NodeCanvasHost.ContextMenu != originalMenu)
                {
                    NodeCanvasHost.ContextMenu = originalMenu;
                }
            };

            // Close popup when main window loses activation (user clicks elsewhere)
            void WindowDeactivatedHandler(object? s, EventArgs e)
            {
                if (popup != null && popup.IsOpen) popup.IsOpen = false;
            }
            Deactivated += WindowDeactivatedHandler;

            popup.Closed += (_, _) => Deactivated -= WindowDeactivatedHandler;

            // Focus the search box when the popup opens
            contentBorder.Loaded += (_, _) =>
            {
                searchBox.Focus();
                Keyboard.Focus(searchBox);
            };

            // Temporarily clear the context menu so the popup doesn't conflict
            NodeCanvasHost.ContextMenu = null;

            popup.IsOpen = true;
        }

        private static void SetSelectedItem(int index, List<Border> borders, Func<byte, byte, byte, Color> sRgb)
        {
            foreach (var b in borders)
                b.Background = Brushes.Transparent;
            if (index >= 0 && index < borders.Count)
            {
                borders[index].Background = new SolidColorBrush(sRgb(0x2B, 0x31, 0x40));
            }
        }

        /// <summary>
        /// Shows a popup listing all graph prototypes whose output port
        /// is compatible with <paramref name="inputType"/>. Clicking a row
        /// creates the node, places it at <paramref name="canvasPos"/>, and wires the
        /// new node's first compatible output port to the target input port.
        /// </summary>
        private void ShowInputConnectionCreationMenu(Point canvasPos, NodeViewModel targetNode, int targetPortIndex, PortValueType inputType)
        {
            // Collect compatible prototypes that have at least one compatible output port
            var compatibleNodes = new List<(string typeName, IGraphNode proto)>();
            foreach (var proto in GraphNodeRegistry.GetAllPrototypes())
            {
                foreach (var port in proto.OutputPorts)
                {
                    var outputType = MapGraphPortType(port.Type);
                    if (ArePortTypesCompatible(outputType, inputType) ||
                        port.Type == GraphPortType.Any)
                    {
                        compatibleNodes.Add((proto.TypeName, proto));
                        break;
                    }
                }
            }

            if (compatibleNodes.Count == 0) return;
            // Build a preview cache from the node library (already generated thumbnails).
            var previewCache = new Dictionary<string, Brush?>();
            foreach (var libItem in NodeLibrary)
            {
                if (!previewCache.ContainsKey(libItem.Name))
                    previewCache[libItem.Name] = libItem.PreviewBrush;
            }

            // Pre-compute localized names for all compatible nodes
            var loc = Loc;
            var localizedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in compatibleNodes)
            {
                var resxName = loc.GetString("NodeName_" + n.typeName);
                if (!string.IsNullOrEmpty(resxName) && resxName != "NodeName_" + n.typeName)
                    localizedNames[n.typeName] = resxName;
                else
                    localizedNames[n.typeName] = GetNodeDisplayNameFromCatalog(n.typeName) ?? n.typeName;
            }

            var sRgb = Color.FromRgb;

            // Declare popup and originalMenu before lambdas that capture them
            System.Windows.Controls.Primitives.Popup? popup = null;
            var originalMenu = NodeCanvasHost.ContextMenu;

            // ── Grouped data (search will filter this) ──
            var groupedData = compatibleNodes
                .GroupBy(n => GetCategoryString(n.proto.Category))
                .OrderBy(g => g.Key)
                .Select(g => (
                    Category: g.Key,
                    Items: g.OrderBy(n => n.typeName).ToList()
                ))
                .ToList();

            // ── Search box ──
            var searchBox = new TextBox
            {
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush(sRgb(0x14, 0x18, 0x22)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(sRgb(0x3B, 0x41, 0x50)),
                BorderThickness = new Thickness(1),
                CaretBrush = Brushes.White,
                Text = "",
                MinWidth = 160
            };
            var placeholder = new TextBlock
            {
                Text = Loc.GetString("MW_SearchNodes"),
                Foreground = new SolidColorBrush(sRgb(0x88, 0x88, 0x88)),
                FontSize = 13,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var searchBorder = new Border { Child = searchBox };
            var searchGrid = new Grid();
            searchGrid.Children.Add(searchBorder);
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            placeholder.Margin = new Thickness(8, 0, 0, 0);
            searchGrid.Children.Add(placeholder);
            // Show/hide placeholder
            searchBox.TextChanged += (_, _) =>
                placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            // ── Item list container ──
            var itemStack = new StackPanel();
            var scroll = new ScrollViewer
            {
                MaxHeight = 360,
                Content = itemStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Rebuild the visible list from the filter string
            string currentFilter = "";
            int selectedIndex = -1;
            List<Border> itemBorders = new();

            void RebuildList()
            {
                itemStack.Children.Clear();
                itemBorders.Clear();
                selectedIndex = -1;

                foreach (var group in groupedData)
                {
                    var filtered = group.Items
                        .Where(i =>
                        {
                            if (string.IsNullOrWhiteSpace(currentFilter)) return true;
                            var localized = localizedNames.TryGetValue(i.typeName, out var ln) ? ln : i.typeName;
                            return i.typeName.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   localized.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   PinyinHelper.MatchesPinyin(localized, currentFilter);
                        })
                        .ToList();
                    if (filtered.Count == 0) continue;

                    // Category header
                    itemStack.Children.Add(new TextBlock
                    {
                        Text = $"── {group.Category} ──",
                        Foreground = new SolidColorBrush(sRgb(0x66, 0x77, 0x88)),
                        FontSize = 11,
                        Margin = new Thickness(8, 4, 0, 2)
                    });

                    foreach (var (name, proto) in filtered)
                    {
                        var border = new Border
                        {
                            Padding = new Thickness(8d, 4d, 8d, 4d),
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Background = Brushes.Transparent
                        };
                        var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        var thumb = previewCache.TryGetValue(name, out var tb) ? tb : null;
                        if (thumb != null)
                        {
                            itemPanel.Children.Add(new Rectangle
                            {
                                Width = 20, Height = 20,
                                Fill = thumb,
                                RadiusX = 2, RadiusY = 2,
                                Margin = new Thickness(0, 0, 6, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            });
                        }
                        else
                        {
                            var catColor = GetCategoryColor(proto.Category);
                            itemPanel.Children.Add(new Border
                            {
                                Width = 10, Height = 10,
                                CornerRadius = new CornerRadius(2),
                                Background = new SolidColorBrush(catColor),
                                Margin = new Thickness(0, 0, 6, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            });
                        }
                        var displayName = localizedNames.TryGetValue(name, out var localized) ? localized : name;
                        var text = new TextBlock
                        {
                            Text = displayName,
                            Foreground = new SolidColorBrush(sRgb(0xCC, 0xCC, 0xCC)),
                            FontSize = 12
                        };
                        itemPanel.Children.Add(text);
                        border.Child = itemPanel;

                        var capturedName = name;
                        border.MouseLeftButtonDown += (s, e) =>
                        {
                            CreateAndConnectToInput(capturedName, canvasPos, targetNode, targetPortIndex);
                            if (popup != null) popup.IsOpen = false;
                            e.Handled = true;
                        };
                        border.MouseEnter += (s, e) =>
                        {
                            border.Background = new SolidColorBrush(sRgb(0x2B, 0x31, 0x40));
                            foreach (var b in itemBorders)
                                b.Background = ReferenceEquals(b, border)
                                    ? new SolidColorBrush(sRgb(0x2B, 0x31, 0x40))
                                    : Brushes.Transparent;
                        };
                        border.MouseLeave += (s, e) =>
                        {
                            if (!ReferenceEquals(border, itemBorders.ElementAtOrDefault(selectedIndex)))
                                border.Background = Brushes.Transparent;
                        };
                        itemStack.Children.Add(border);
                        itemBorders.Add(border);
                    }
                }
            }

            RebuildList();

            // Filter on typing
            searchBox.TextChanged += (_, _) =>
            {
                currentFilter = searchBox.Text;
                RebuildList();
            };

            // Keyboard navigation
            searchBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Down)
                {
                    if (itemBorders.Count > 0)
                    {
                        var next = Math.Min(selectedIndex + 1, itemBorders.Count - 1);
                        SetSelectedItem(next, itemBorders, sRgb);
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Up)
                {
                    if (itemBorders.Count > 0)
                    {
                        var prev = selectedIndex <= 0 ? itemBorders.Count - 1 : selectedIndex - 1;
                        SetSelectedItem(prev, itemBorders, sRgb);
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    if (selectedIndex >= 0 && selectedIndex < itemBorders.Count)
                    {
                        var border = itemBorders[selectedIndex];
                        var text = (border.Child as TextBlock)?.Text;
                        if (text != null)
                        {
                            CreateAndConnectToInput(text, canvasPos, targetNode, targetPortIndex);
                            if (popup != null) popup.IsOpen = false;
                        }
                    }
                    e.Handled = true;
                }
            };

            var outerStack = new StackPanel();
            outerStack.Children.Add(searchGrid);
            outerStack.Children.Add(scroll);

            var contentBorder = new Border
            {
                Background = new SolidColorBrush(sRgb(0x1A, 0x1E, 0x28)),
                BorderBrush = new SolidColorBrush(sRgb(0x2B, 0x31, 0x40)),
                BorderThickness = new Thickness(1d),
                CornerRadius = new CornerRadius(4d),
                Padding = new Thickness(4d),
                Child = outerStack,
                MinWidth = 160,
                Focusable = true,
                FocusVisualStyle = null
            };

            // Close popup on Escape
            contentBorder.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && popup != null)
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                }
            };

            // Block mouse events inside popup from closing it
            contentBorder.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = false;
            };

            popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = NodeCanvasHost,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = contentBorder,
                StaysOpen = true
            };

            // Close when clicking on the canvas (outside the popup).
            void CanvasClickHandler(object s, MouseButtonEventArgs e)
            {
                if (popup != null && popup.IsOpen)
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                }
            }
            NodeCanvasHost.PreviewMouseLeftButtonDown += CanvasClickHandler;

            popup.Closed += (_, _) =>
            {
                NodeCanvasHost.PreviewMouseLeftButtonDown -= CanvasClickHandler;
                if (NodeCanvasHost.ContextMenu != originalMenu)
                {
                    NodeCanvasHost.ContextMenu = originalMenu;
                }
            };

            // Close popup when main window loses activation (user clicks elsewhere)
            void WindowDeactivatedHandler(object? s, EventArgs e)
            {
                if (popup != null && popup.IsOpen) popup.IsOpen = false;
            }
            Deactivated += WindowDeactivatedHandler;

            popup.Closed += (_, _) => Deactivated -= WindowDeactivatedHandler;

            // Focus the search box when the popup opens
            contentBorder.Loaded += (_, _) =>
            {
                searchBox.Focus();
                Keyboard.Focus(searchBox);
            };

            // Temporarily clear the context menu so the popup doesn't conflict
            NodeCanvasHost.ContextMenu = null;

            popup.IsOpen = true;
        }

        /// <summary>
        /// Creates a graph node by <paramref name="typeName"/>, adds it to the canvas at
        /// <paramref name="pos"/>, wires the new node's first compatible output port to the
        /// target input port, and selects the new node.
        /// </summary>
        private void CreateAndConnectToInput(string typeName, Point pos, NodeViewModel targetNode, int targetPortIndex)
        {
            var graphNode = GraphNodeRegistry.Create(typeName);
            if (graphNode == null) return;

            var nx = Math.Max(0, pos.X - 60);
            var ny = Math.Max(0, pos.Y - 20);

            var libItem = NodeLibrary.FirstOrDefault(item => item.TypeName == typeName);
            var displayTitle = libItem?.Name ?? graphNode.TypeName;

            var node = new NodeViewModel(displayTitle, nx, ny)
            {
                Kind = GetKindForCategory(graphNode.Category),
                Category = GetCategoryString(graphNode.Category),
                TileType = null
            };

            // Set up ports from prototype, using localized names from NodeLibrary if available
            var protoInputs = graphNode.InputPorts;
            var protoOutputs = graphNode.OutputPorts;
            for (var i = 0; i < protoInputs.Count; i++)
            {
                var localizedName = (libItem != null && i < libItem.InputPorts.Count)
                    ? libItem.InputPorts[i] : protoInputs[i].Name;
                node.InputPorts.Add(new NodePortViewModel(localizedName, MapGraphPortType(protoInputs[i].Type), false, protoInputs[i].StableKey));
            }
            for (var i = 0; i < protoOutputs.Count; i++)
            {
                var localizedName = (libItem != null && i < libItem.OutputPorts.Count)
                    ? libItem.OutputPorts[i] : protoOutputs[i].Name;
                node.OutputPorts.Add(new NodePortViewModel(localizedName, MapGraphPortType(protoOutputs[i].Type), true, protoOutputs[i].StableKey));
            }

            // Set up parameters from prototype definitions
            if (graphNode.Parameters != null)
            {
                foreach (var def in graphNode.Parameters)
                {
                    var pvm = CreateParameterViewModel(def);
                    node.Parameters.Add(pvm);
                }
            }

            RecordUndoSnapshot();
            Nodes.Add(node);

            // Find a compatible output port on the new node to connect to the target input
            var targetInputType = targetNode.InputPorts[targetPortIndex].Type;
            var sourcePortIndex = 0;
            for (var i = 0; i < graphNode.OutputPorts.Count; i++)
            {
                var outputType = MapGraphPortType(graphNode.OutputPorts[i].Type);
                if (ArePortTypesCompatible(outputType, targetInputType) ||
                    graphNode.OutputPorts[i].Type == GraphPortType.Any)
                {
                    sourcePortIndex = i;
                    break;
                }
            }

            // Wire up connection from new node's output to target's input
            var connection = new NodeConnectionViewModel
            {
                StartNode = node,
                StartPortIndex = sourcePortIndex,
                EndNode = targetNode,
                EndPortIndex = targetPortIndex,
                IsPreview = false
            };

            var startPos = GetPortPosition(node, true, sourcePortIndex);
            var endPos = GetPortPosition(targetNode, false, targetPortIndex);
            connection.StartX = startPos.X;
            connection.StartY = startPos.Y;
            connection.EndX = endPos.X;
            connection.EndY = endPos.Y;

            NodeConnections.Add(connection);

            SelectedNode = node;
            UpdateConnectionPositions();
            NodeConnectionLayer?.InvalidateVisual();
            MarkStatsActive();
            RequestPreviewRefresh(false);
        }

        /// <summary>
        /// Creates a graph node by <paramref name="typeName"/>, adds it to the canvas at
        /// <paramref name="pos"/>, wires <paramref name="sourceNode"/>'s output port to the
        /// new node's first input port, and selects the new node.
        /// </summary>
        private void CreateAndConnectNode(string typeName, Point pos, NodeViewModel sourceNode, int sourcePortIndex)
        {
            var graphNode = GraphNodeRegistry.Create(typeName);
            if (graphNode == null) return;

            // Keep the node within reasonable canvas bounds
            var nx = Math.Max(0, pos.X - 60);
            var ny = Math.Max(0, pos.Y - 20);

            var libItem = NodeLibrary.FirstOrDefault(item => item.TypeName == typeName);
            var displayTitle = libItem?.Name ?? graphNode.TypeName;

            var node = new NodeViewModel(displayTitle, nx, ny)
            {
                Kind = GetKindForCategory(graphNode.Category),
                Category = GetCategoryString(graphNode.Category),
                TileType = null
            };

            // Set up ports from prototype, using localized names from NodeLibrary if available
            var protoInputs = graphNode.InputPorts;
            var protoOutputs = graphNode.OutputPorts;
            for (var i = 0; i < protoInputs.Count; i++)
            {
                var localizedName = (libItem != null && i < libItem.InputPorts.Count)
                    ? libItem.InputPorts[i] : protoInputs[i].Name;
                node.InputPorts.Add(new NodePortViewModel(localizedName, MapGraphPortType(protoInputs[i].Type), false, protoInputs[i].StableKey));
            }
            for (var i = 0; i < protoOutputs.Count; i++)
            {
                var localizedName = (libItem != null && i < libItem.OutputPorts.Count)
                    ? libItem.OutputPorts[i] : protoOutputs[i].Name;
                node.OutputPorts.Add(new NodePortViewModel(localizedName, MapGraphPortType(protoOutputs[i].Type), true, protoOutputs[i].StableKey));
            }

            // Set up parameters from prototype definitions
            if (graphNode.Parameters != null)
            {
                foreach (var def in graphNode.Parameters)
                {
                    var pvm = CreateParameterViewModel(def);
                    node.Parameters.Add(pvm);
                }
            }

            RecordUndoSnapshot();
            Nodes.Add(node);

            // Wire up connection from source to new node's first input
            var connection = new NodeConnectionViewModel
            {
                StartNode = sourceNode,
                StartPortIndex = sourcePortIndex,
                EndNode = node,
                EndPortIndex = 0,
                IsPreview = false
            };

            var startPos = GetPortPosition(sourceNode, true, sourcePortIndex);
            var endPos = GetPortPosition(node, false, 0);
            connection.StartX = startPos.X;
            connection.StartY = startPos.Y;
            connection.EndX = endPos.X;
            connection.EndY = endPos.Y;

            NodeConnections.Add(connection);

            SelectedNode = node;
            UpdateConnectionPositions();
            NodeConnectionLayer?.InvalidateVisual();
            MarkStatsActive();
            RequestPreviewRefresh(false);
        }
    }
}
