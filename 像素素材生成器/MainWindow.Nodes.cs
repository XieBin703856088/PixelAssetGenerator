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
using PixelAssetGenerator.Core.Particles.Nodes;
using PixelAssetGenerator.Services;
using ExportFormat = PixelAssetGenerator.Services.ExportService.ExportFormat;

namespace PixelAssetGenerator
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private static Color GetRandomUnusedFlowerColor(IEnumerable<TileProperties.FlowerColorEntry> entries)
        {
            var usedColors = entries.Select(static entry => entry.Color).ToHashSet();
            var availableCandidates = FlowerColorCandidates.Where(color => !usedColors.Contains(color)).ToArray();
            if (availableCandidates.Length > 0)
            {
                return availableCandidates[Random.Shared.Next(availableCandidates.Length)];
            }

            for (var attempt = 0; attempt < 64; attempt++)
            {
                var color = CreateRandomFlowerColor();
                if (!usedColors.Contains(color))
                {
                    return color;
                }
            }

            var hue = (usedColors.Count * 137.508d) % 360d;
            return CreateColorFromHsv(hue, 0.65d, 0.95d);
        }

        private static Color CreateRandomFlowerColor()
        {
            var hue = Random.Shared.NextDouble() * 360d;
            var saturation = 0.45d + (Random.Shared.NextDouble() * 0.35d);
            var value = 0.8d + (Random.Shared.NextDouble() * 0.18d);
            return CreateColorFromHsv(hue, saturation, value);
        }

        private static Color CreateColorFromHsv(double hue, double saturation, double value)
        {
            hue = ((hue % 360d) + 360d) % 360d;
            saturation = Math.Clamp(saturation, 0d, 1d);
            value = Math.Clamp(value, 0d, 1d);

            var chroma = value * saturation;
            var secondary = chroma * (1d - Math.Abs(((hue / 60d) % 2d) - 1d));
            var match = value - chroma;

            var (red, green, blue) = hue switch
            {
                < 60d => (chroma, secondary, 0d),
                < 120d => (secondary, chroma, 0d),
                < 180d => (0d, chroma, secondary),
                < 240d => (0d, secondary, chroma),
                < 300d => (secondary, 0d, chroma),
                _ => (chroma, 0d, secondary)
            };

            return Color.FromRgb(
                (byte)Math.Round((red + match) * 255d),
                (byte)Math.Round((green + match) * 255d),
                (byte)Math.Round((blue + match) * 255d));
        }

        private static string GetSeedTextAfterInput(TextBox textBox, string input)
        {
            var currentText = textBox.Text ?? string.Empty;
            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;
            return currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, input);
        }

        private static bool IsSeedTextValid(string text)
        {
            return string.IsNullOrEmpty(text) || text.All(char.IsDigit);
        }

        private static int GetDefaultSeed(TileType tileType)
        {
            return tileType switch
            {
                TileType.Grass => 1200,
                TileType.Stone => 2200,
                TileType.Water => 3200,
                TileType.Sand => 4200,
                TileType.Road => 5200,
                _ => 1200
            };
        }

        private static int CreateRandomSeed(TileType tileType)
        {
            var rangeStart = tileType switch
            {
                TileType.Grass => 1000,
                TileType.Stone => 2000,
                TileType.Water => 3000,
                TileType.Sand => 4000,
                TileType.Road => 5000,
                _ => 1000
            };

            return Random.Shared.Next(rangeStart, rangeStart + 1000);
        }

        /// <summary>
        /// Checks if a node is a new graph-based node (from Core.GraphNodeRegistry).
        /// </summary>
        private static bool IsGraphNode(NodeViewModel node)
        {
            var result = node.TileType == null && GraphNodeRegistry.Create(node.RegistryKey) != null;
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PixelAssetGenerator_preview_errors.txt"),
                "IsGraphNode: title=" + node.Title + " typeName=" + node.TypeName + " regKey=" + node.RegistryKey + " tileType=" + (node.TileType != null ? node.TileType.ToString() : "null") + " result=" + result + System.Environment.NewLine);
            return result;
        }

        /// <summary>
        /// Evaluates the new graph-based node pipeline for the selected node and returns a BitmapSource.
        /// </summary>
        private BitmapSource? EvaluateGraphPipeline(int size, NodeViewModel? targetNode = null)
        {
            var target = targetNode ?? SelectedNode;
            if (target == null) return null;

            // Build full graph for connected nodes
            var instanceMap = new Dictionary<int, GraphNodeInstance>();
            var instances = new List<GraphNodeInstance>();
            var graphConnections = new List<GraphConnection>();

            foreach (var node in Nodes)
            {
                IGraphNode? graphNode = GraphNodeRegistry.Create(node.RegistryKey);
                if (graphNode is null && node.TileType != null)
                {
                    var tileBuffer = TryRenderTileNodeAsPixelBuffer(node, size);
                    if (tileBuffer != null)
                        graphNode = new PixelBufferSourceNode(tileBuffer);
                }
                if (graphNode is null)
                    continue;

                var instance = new GraphNodeInstance(node.Id, graphNode);

                foreach (var param in node.Parameters)
                {
                    var value = param.Kind switch
                    {
                        NodeParameterKind.Seed => (object)param.IntValue,
                        NodeParameterKind.Integer => (object)param.IntValue,
                        NodeParameterKind.Boolean => param.BoolValue,
                        NodeParameterKind.Choice => param.SelectedChoice ?? string.Empty,
                        NodeParameterKind.PointList => (object)(System.Collections.Generic.IReadOnlyList<System.Windows.Point>)param.PointListValue.ToArray(),
                        NodeParameterKind.Color => (object)param.ColorValue,
                        NodeParameterKind.Text => param.TextValue ?? string.Empty,
                        _ => (object)param.NumberValue
                    };
                    instance.ParameterValues[param.Name] = value;
                }

                instanceMap[node.Id] = instance;
                instances.Add(instance);
            }

            foreach (var conn in NodeConnections.Where(c => !c.IsPreview && c.StartNode != null && c.EndNode != null))
            {
                if (instanceMap.ContainsKey(conn.StartNode!.Id) && instanceMap.ContainsKey(conn.EndNode!.Id))
                {
                    graphConnections.Add(new GraphConnection(
                        conn.StartNode!.Id, conn.StartPortIndex,
                        conn.EndNode!.Id, conn.EndPortIndex));
                }
            }

            if (instances.Count == 0)
                return null;

            var isStillMode = _previewMode == PreviewMode.Still;

            // Still mode with frame slider: use the manually-set frame time instead of stripping animation
            float? animTime;
            int? animFrame;
            int animFrameCount;
            if (isStillMode && _stillFrameSliderActive)
            {
                animTime = _stillFrameNormalizedTime >= 0 ? _stillFrameNormalizedTime : null;
                animFrame = _stillFrameNormalizedTime >= 0
                    ? (int)(_stillFrameNormalizedTime * (_stillFrameCount - 1))
                    : null;
                animFrameCount = _stillFrameCount;
            }
            else if (isStillMode)
            {
                // Still mode without frame slider: strip animation time
                animTime = null;
                animFrame = null;
                animFrameCount = 1;
            }
            else
            {
                animTime = _animationService?.NormalizedTime;
                animFrame = _animationService?.CurrentFrame;
                animFrameCount = _animationService?.FrameCount ?? 1;
            }

            var ctx = new PixelGraphContext
            {
                TileSize = size,
                Seed = target.TileProperties?.Seed
                    ?? target.Parameters.FirstOrDefault(p => p.Name == "seed")?.IntValue
                    ?? 42,
                AnimationTime = animTime,
                AnimationFrame = animFrame,
                AnimationFrameCount = animFrameCount,
                DeltaTime = _animationService != null ? 1f / (float)(_animationService.FrameRate) : 1f / 60f,
            };

            // Use EvaluateAll to get results for all nodes, so particle rendering
            // can overlay particles onto the ParticleRenderNode output.
            // During animation playback, skip EvaluateAll since particles are already
            // simulated by UpdateParticleSimulation — just create output buffers for rendering.
            Dictionary<int, PixelBuffer>? allResults = null;
            var isAnimating = _animationService?.IsPlaying == true;

            if (isAnimating && !isStillMode)
            {
                // Animation playing: skip full graph evaluation, only allocate output buffers
                // for ParticleRenderNode so RenderParticles can draw onto them.
                allResults = new Dictionary<int, PixelBuffer>();
                foreach (var inst in instances)
                {
                    if (inst.Node is ParticleRenderNode)
                    {
                        var buf = PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);
                        allResults[inst.Id] = buf;
                    }
                }
                // Fallback: if there's a target node that's not a particle node, evaluate normally
                if (allResults.Count == 0)
                {
                    allResults = _graphEvaluator.EvaluateAll(instances, graphConnections, ctx);
                }
            }
            else
            {
                allResults = _graphEvaluator.EvaluateAll(instances, graphConnections, ctx);
            }

            if (_particleEvalService == null)
                _particleEvalService = new Services.ParticleEvaluationService();

            // Still mode: always run one frame of particle simulation from cleared state
            if (isStillMode)
            {
                _particleEvalService.ClearState();
                _particleEvalService.RestoreState(instances);
                _particleEvalService.SimulateParticleFrame(instances, new Dictionary<string, object>(), ctx);
                _particleEvalService.SaveState(instances);
            }
            // If animation is not playing, still run one frame of particle simulation
            else if (!isAnimating)
            {
                _particleEvalService.RestoreState(instances);
                _particleEvalService.SimulateParticleFrame(instances, new Dictionary<string, object>(), ctx);
                _particleEvalService.SaveState(instances);
            }

            if (allResults != null)
            {
                _particleEvalService!.RenderParticles(instances, allResults);

                int? targetId = null;
                if (instanceMap.ContainsKey(target.Id))
                    targetId = target.Id;

                if (targetId.HasValue && allResults.TryGetValue(targetId.Value, out var targetResult))
                {
                    return targetResult.ToBitmapSource();
                }

                // Return the last particle render node's output
                foreach (var inst in instances)
                {
                    if (inst.Node is ParticleRenderNode && allResults.TryGetValue(inst.Id, out var prResult))
                    {
                        return prResult.ToBitmapSource();
                    }
                }

                return allResults.Values.LastOrDefault()?.ToBitmapSource();
            }

            var result = _graphEvaluator.Evaluate(instances, graphConnections, ctx);
            return result?.ToBitmapSource();
        }

        /// <summary>
        /// Generates a preview brush for a single graph-based node by evaluating it in isolation.
        /// If previewSize <= 0 the currently selected tile size is used so previews match project tile size.
        /// </summary>
        private Brush? GenerateGraphNodePreviewBrush(NodeViewModel node, int previewSize = 0)
        {
            var lookupKey = string.IsNullOrEmpty(node.TypeName) ? node.Title : node.TypeName;
            var graphNode = GraphNodeRegistry.Create(lookupKey);
            if (graphNode is null) return null;

            var instance = new GraphNodeInstance(node.Id, graphNode);
            foreach (var param in node.Parameters)
            {
                var value = param.Kind switch
                {
                    NodeParameterKind.Seed => (object)param.IntValue,
                    NodeParameterKind.Integer => (object)param.IntValue,
                    NodeParameterKind.Boolean => param.BoolValue,
                    NodeParameterKind.Choice => param.SelectedChoice ?? string.Empty,
                    NodeParameterKind.PointList => (object)(System.Collections.Generic.IReadOnlyList<System.Windows.Point>)param.PointListValue.ToArray(),
                    NodeParameterKind.Color => (object)param.ColorValue,
                    NodeParameterKind.Text => param.TextValue ?? string.Empty,
                    _ => (object)param.NumberValue
                };
                instance.ParameterValues[param.Name] = value;
            }

            if (previewSize <= 0)
            {
                // resolve on UI thread to read selected tile size safely
                previewSize = Dispatcher.CheckAccess() ? GetSelectedTileSize() : Dispatcher.Invoke(() => GetSelectedTileSize());
            }

            var context = new PixelGraphContext
            {
                TileSize = previewSize,
                Seed = node.Parameters.FirstOrDefault(p => p.Name == "seed")?.IntValue ?? 42
            };

            // Some scripts (even zero-input ones) access inputs[0], so always provide at least one null entry
            var inputs = new PixelBuffer?[Math.Max(1, graphNode.InputPorts.Count)];
            try
            {
                var buffer = graphNode.Process(inputs, instance.ParameterValues, context);
                var bmp = buffer.ToBitmapSource();
                return CreatePreviewBrushFromBitmap(bmp, previewSize);
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PixelAssetGenerator_preview_errors.txt"),
                    $"[{System.DateTime.Now:HH:mm:ss}] GenerateGraphNodePreviewBrush failed for '{node.TypeName}': {ex.Message}\n{ex.StackTrace}\n\n");
                return null;
            }
        }

        // Helper to create a simple ImageBrush from a bitmap for thumbnails.
        private Brush CreatePreviewBrushFromBitmap(BitmapSource bmp, int logicalSize)
        {
            var imgBrush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(imgBrush, BitmapScalingMode.NearestNeighbor);
            imgBrush.Freeze();
            return imgBrush;
        }

        private sealed record PatternBitmapData(byte[] Pixels, int Width, int Height, float OpaqueCoverage, bool BackgroundWasKeyedOut);

        /// <summary>
        /// Builds the full graph from the given nodes/connections and evaluates all nodes,
        /// returning a per-node-id PixelBuffer dictionary. This ensures each node's preview
        /// reflects its actual output including all upstream connections.
        /// </summary>
        private Dictionary<int, PixelBuffer>? EvaluateAllGraphNodeBuffers(
            List<NodeViewModel> nodesSnapshot,
            List<NodeConnectionViewModel> connectionsSnapshot,
            int previewSize)
        {
            var instanceMap = new Dictionary<int, GraphNodeInstance>();
            var instances = new List<GraphNodeInstance>();
            var graphConnections = new List<GraphConnection>();

            foreach (var node in nodesSnapshot)
            {
                IGraphNode? graphNode = GraphNodeRegistry.Create(node.RegistryKey);
                if (graphNode is null && node.TileType != null)
                {
                    var tileBuffer = TryRenderTileNodeAsPixelBuffer(node, previewSize);
                    if (tileBuffer != null)
                        graphNode = new PixelBufferSourceNode(tileBuffer);
                }
                if (graphNode is null)
                    continue;

                var instance = new GraphNodeInstance(node.Id, graphNode);
                foreach (var param in node.Parameters)
                {
                    var value = param.Kind switch
                    {
                        NodeParameterKind.Seed => (object)param.IntValue,
                        NodeParameterKind.Integer => (object)param.IntValue,
                        NodeParameterKind.Boolean => param.BoolValue,
                        NodeParameterKind.Choice => param.SelectedChoice ?? string.Empty,
                        NodeParameterKind.PointList => (object)(System.Collections.Generic.IReadOnlyList<System.Windows.Point>)param.PointListValue.ToArray(),
                        NodeParameterKind.Color => (object)param.ColorValue,
                        NodeParameterKind.Text => param.TextValue ?? string.Empty,
                        _ => (object)param.NumberValue
                    };
                    instance.ParameterValues[param.Name] = value;
                }

                instanceMap[node.Id] = instance;
                instances.Add(instance);
            }

            foreach (var conn in connectionsSnapshot.Where(c => !c.IsPreview && c.StartNode != null && c.EndNode != null))
            {
                if (instanceMap.ContainsKey(conn.StartNode!.Id) && instanceMap.ContainsKey(conn.EndNode!.Id))
                {
                    graphConnections.Add(new GraphConnection(
                        conn.StartNode!.Id, conn.StartPortIndex,
                        conn.EndNode!.Id, conn.EndPortIndex));
                }
            }

            if (instances.Count == 0)
                return null;

            var context = new PixelGraphContext
            {
                TileSize = previewSize,
                Seed = 42,
                AnimationTime = _animationService?.NormalizedTime,
                AnimationFrame = _animationService?.CurrentFrame,
                AnimationFrameCount = _animationService?.FrameCount ?? 1,
                DeltaTime = _animationService != null ? 1f / (float)(_animationService.FrameRate) : 1f / 60f,
            };

            var results = _graphEvaluator.EvaluateAll(instances, graphConnections, context);

            // Render particles onto output buffers for thumbnails
            if (_particleEvalService != null && results != null)
            {
                _particleEvalService.RenderParticles(instances, results);
            }

            return results;
        }

        private PortValueType DeterminePortType(string portName, bool isOutput, NodeLibraryItem libraryItem)
        {

            var lower = (portName ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("bitmap") || lower.Contains("tile") || lower.Contains("pattern") || libraryItem.Kind == NodeLibraryItemKind.Tile)
            {
                return PortValueType.Tile;
            }

            if (lower.Contains("image"))
            {
                return PortValueType.Image;
            }

            if (lower.Contains("mask"))
            {
                return PortValueType.Mask;
            }

            if (lower.Contains("float") || lower.Contains("value") || lower.Contains("integer") || lower.Contains("color") || lower.Contains("coefficient"))
            {
                return PortValueType.Float;
            }

            if (lower.Contains("bool") || lower.Contains("boolean") || lower.Contains("gate"))
            {
                return PortValueType.Boolean;
            }

            if (lower.Contains("size") || lower.Contains("width") || lower.Contains("height") || lower.Contains("resolution"))
            {
                return PortValueType.Integer;
            }

            return PortValueType.Float;
        }

        internal NodeViewModel CreateNodeFromLibraryItem(NodeLibraryItem libraryItem, double x, double y)
        {
            var node = new NodeViewModel(libraryItem.Name, x, y, libraryItem.PreviewBrush)
            {
                Kind = libraryItem.Kind,
                TileType = libraryItem.TileType,
                Category = libraryItem.Category,
                TypeName = libraryItem.TypeName
            };

            if (libraryItem.Kind == NodeLibraryItemKind.Tile && libraryItem.TileType != null)
            {
                var existingNode = Nodes.FirstOrDefault(n => n.TileType == libraryItem.TileType && n.TileProperties != null);
                if (existingNode != null)
                {
                    node.TileProperties = existingNode.TileProperties;
                }
                else
                {
                    var defaultProps = new TileProperties();
                    defaultProps.Seed = GetDefaultSeed(libraryItem.TileType.Value);
                    node.TileProperties = defaultProps;
                }
            }

            if (node.TileType != null)
            {
                ConfigureTileNodePorts(node);
            }
            else
            {
                // For graph nodes registered in GraphNodeRegistry, use their typed port definitions
                // so input/output ports carry the correct PortValueType (Image, Mask, etc.).
                var prototype = GraphNodeRegistry.Create(libraryItem.TypeName);
                if (prototype != null)
                {
                    // Use localized port names from libraryItem (populated from catalog),
                    // but get PortValueType from the prototype to maintain type info.
                    var protoInputs = prototype.InputPorts;
                    var protoOutputs = prototype.OutputPorts;
                    for (var i = 0; i < protoInputs.Count; i++)
                    {
                        var localizedName = i < libraryItem.InputPorts.Count
                            ? libraryItem.InputPorts[i] : protoInputs[i].Name;
                        node.InputPorts.Add(new NodePortViewModel(localizedName, MapGraphPortType(protoInputs[i].Type), false));
                    }
                    for (var i = 0; i < protoOutputs.Count; i++)
                    {
                        var localizedName = i < libraryItem.OutputPorts.Count
                            ? libraryItem.OutputPorts[i] : protoOutputs[i].Name;
                        node.OutputPorts.Add(new NodePortViewModel(localizedName, MapGraphPortType(protoOutputs[i].Type), true));
                    }
                }
                else
                {
                    foreach (var port in libraryItem.InputPorts)
                    {
                        var type = DeterminePortType(port, false, libraryItem);
                        node.InputPorts.Add(new NodePortViewModel(port, type, false));
                    }

                    foreach (var port in libraryItem.OutputPorts)
                    {
                        var type = DeterminePortType(port, true, libraryItem);
                        node.OutputPorts.Add(new NodePortViewModel(port, type, true));
                    }
                }
            }

            if (node.InputPorts.Count == 0 && node.OutputPorts.Count == 0)
            {
                node.OutputPorts.Add(new NodePortViewModel("output"));
            }

            ApplyParameterDefinitions(node, libraryItem.Parameters);


            if (node.Kind == NodeLibraryItemKind.Compute)
            {
                try
                {
                    // Graph-based nodes (registered in GraphNodeRegistry) must use the graph
                    // preview generator so they show a real output instead of the noise fallback.
                    if (IsGraphNode(node))
                        node.PreviewBrush = GenerateGraphNodePreviewBrush(node) ?? node.PreviewBrush;
                    else
                        node.PreviewBrush = GenerateComputeNodePreviewBrush(node);
                }
                catch
                {

                }
            }

            return node;
        }

        private void ApplyParameterDefinitions(NodeViewModel node, IEnumerable<NodeParameterDefinition> definitions)
        {
            node.Parameters.Clear();
            foreach (var definition in definitions)
            {
                var parameter = CreateParameterViewModel(definition);
                parameter.PropertyChanged += NodeParameter_PropertyChanged;
                node.Parameters.Add(parameter);
            }
        }

        private static NodeParameterViewModel CreateParameterViewModel(NodeParameterDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            var parameter = new NodeParameterViewModel(definition.Name, definition.DisplayName, definition.Kind, definition.Min, definition.Max, definition.Step, definition.Choices, definition.DisplayChoices)
            {
                // Initialize defaults first, then set current value (same as default)
                DefaultNumberValue = definition.DefaultNumber,
                DefaultIntValue = definition.DefaultInt,
                DefaultBoolValue = definition.DefaultBool,
                DefaultChoiceValue = definition.DefaultChoice ?? (definition.Choices.Count > 0 ? definition.Choices[0] : null),
                DefaultColorValue = definition.DefaultColor,
                DefaultTextValue = definition.DefaultText
            };

            switch (definition.Kind)
            {
                case NodeParameterKind.Seed:
                case NodeParameterKind.Integer:
                    parameter.IntValue = definition.DefaultInt;
                    break;
                case NodeParameterKind.Boolean:
                    parameter.BoolValue = definition.DefaultBool;
                    break;
                case NodeParameterKind.Choice:
                    parameter.SelectedChoice = parameter.DefaultChoiceValue;
                    break;
                case NodeParameterKind.Color:
                    parameter.ColorValue = definition.DefaultColor;
                    break;
                case NodeParameterKind.PointList:
                    break;
                case NodeParameterKind.Text:
                    parameter.TextValue = definition.DefaultText;
                    break;
                default:
                    parameter.NumberValue = definition.DefaultNumber;
                    break;
            }

            return parameter;
        }

        private IReadOnlyList<NodeParameterDefinition> GetParameterDefinitions(string nodeTitle, NodeLibraryItemKind kind)
        {
            var libraryItem = NodeLibrary.FirstOrDefault(item => item.Name == nodeTitle && item.Kind == kind);
            if (libraryItem != null)
            {
                return libraryItem.Parameters;
            }

            return kind switch
            {
                NodeLibraryItemKind.Tile => Array.Empty<NodeParameterDefinition>(),
                NodeLibraryItemKind.Compute => NodeLibraryService.CreateComputeParameters(),
                NodeLibraryItemKind.Composite => NodeLibraryService.CreateCompositeParameters(),
                _ => Array.Empty<NodeParameterDefinition>()
            };
        }

        private void EnsureNodeParametersInitialized(NodeViewModel node)
        {

            var needsInit = node.Parameters.Count == 0 || node.Parameters.All(p =>
                (p.Kind == NodeParameterKind.Number || p.Kind == NodeParameterKind.Integer || p.Kind == NodeParameterKind.Seed) && (p.NumberValue == 0 && p.IntValue == 0));

            if (!needsInit)
            {
                return;
            }

            ApplyParameterDefinitions(node, GetParameterDefinitions(node.Title, node.Kind));
            RequestPreviewRefresh(false);
        }

        private NodeLibraryItem GetNodeLibraryItem(string name, NodeLibraryItemKind kind, Brush previewBrush, TileType? tileType,
            IReadOnlyList<string> inputPorts, IReadOnlyList<string> outputPorts, IReadOnlyList<NodeParameterDefinition> parameters)
        {
            var existing = NodeLibrary.FirstOrDefault(item => item.Name == name);
            if (existing != null)
            {

                if ((existing.Parameters == null || existing.Parameters.Count == 0) && (parameters == null || parameters.Count == 0))
                {
                    var defs = kind switch
                    {
                        NodeLibraryItemKind.Tile => Array.Empty<NodeParameterDefinition>(),
                        NodeLibraryItemKind.Compute => NodeLibraryService.CreateComputeParameters(),
                        NodeLibraryItemKind.Composite => NodeLibraryService.CreateCompositeParameters(),
                        _ => Array.Empty<NodeParameterDefinition>()
                    };

                    return new NodeLibraryItem(existing.Name, existing.Category, existing.Kind, existing.PreviewBrush, existing.TileType, existing.InputPorts, existing.OutputPorts, defs, existing.Subcategory, categoryKey: existing.CategoryKey);
                }

                return existing;
            }

            var useParams = parameters != null && parameters.Count > 0
                ? parameters
                : kind switch
                {
                    NodeLibraryItemKind.Tile => Array.Empty<NodeParameterDefinition>(),
                    NodeLibraryItemKind.Compute => NodeLibraryService.CreateComputeParameters(),
                    NodeLibraryItemKind.Composite => NodeLibraryService.CreateCompositeParameters(),
                    _ => Array.Empty<NodeParameterDefinition>()
                };

            var subcategory = NodeLibraryService.SubcategoryMap.TryGetValue(name, out var sub) ? sub : "";
            return new NodeLibraryItem(name, "Default", kind, previewBrush, tileType, inputPorts, outputPorts, useParams, subcategory, categoryKey: "Default");
        }

        private Brush CreateNodePreviewBrush(TileType tileType, Brush fallbackBrush, int size = 0)
        {
            try
            {
                var previewProps = CreateDefaultTileProperties(tileType);
                var settings = BuildSettings(previewProps);
                // resolve size: if caller didn't pass a size, query UI thread safely
                if (size <= 0)
                {
                    size = Dispatcher.CheckAccess() ? GetSelectedTileSize() : Dispatcher.Invoke(() => GetSelectedTileSize());
                }
                var bitmap = _generator.GenerateTileBitmap(size, new[]
                {
                    new TileLayerDefinition(tileType, settings, LayerBlendMode.Normal, 1f)
                });
                bitmap.Freeze();
                var brush = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return fallbackBrush;
            }
        }

        private static TileProperties CreateDefaultTileProperties(TileType tileType)
        {
            var props = new TileProperties
            {
                Seed = 1200 + (int)tileType
            };

            if (tileType == TileType.Grass)
            {
                props.GrassPreset = GrassPreset.GrassA;
                props.DetailDensity = 0.12;
                props.MacroStrength = 0.2;
                props.MicroStrength = 0.08;
                props.AccentDensity = 0.0;
                props.ColorVariation = 0.18;
                props.GrassBladeDensity = 0.72;
                props.GrassBladeHeight = 0.58;
                props.GrassPatchiness = 0.7;
                props.FlowerDensity = 0.06;
                props.FlowerSize = 0.22;
                props.FlowerColor = Color.FromRgb(248, 236, 160);
                props.FlowerColors = new ObservableCollection<TileProperties.FlowerColorEntry>
                {
                    new TileProperties.FlowerColorEntry(Color.FromRgb(248, 236, 160), 1.0),
                    new TileProperties.FlowerColorEntry(Color.FromRgb(255, 247, 228), 0.45)
                };
            }
            else if (tileType == TileType.Water)
            {
                props.DetailDensity = 0.04;
                props.MacroStrength = 0.06;
                props.MicroStrength = 0.03;
                props.WaterDepthVariation = 0.46;
                props.WaterWaveScale = 0.42;
                props.WaterWaveChoppiness = 0.34;
                props.WaterCurrentDirection = 0.58;
                props.WaterCurrentStrength = 0.34;
                props.WaterFoamDensity = 0.18;
                props.WaterFoamSize = 0.22;
            }
            else if (tileType == TileType.Sand)
            {
                props.DetailDensity = 0.05;
                props.MacroStrength = 0.08;
                props.MicroStrength = 0.04;
                props.ColorVariation = 0.22;
                props.SandDuneScale = 0.52;
                props.SandDuneSharpness = 0.48;
                props.SandRippleStrength = 0.58;
                props.SandRippleDirection = 0.62;
                props.SandRippleScale = 0.38;
                props.SandPebbleDensity = 0.14;
                props.SandPebbleSize = 0.24;
            }

            return props;
        }

        private static Brush CreateComputeNodePreviewBrush()
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(28, 34, 44)), null,
                new RectangleGeometry(new Rect(0, 0, 32, 32))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(72, 88, 128)), null,
                new EllipseGeometry(new Point(11, 11), 5, 5)));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(72, 88, 128)), null,
                new EllipseGeometry(new Point(21, 21), 5, 5)));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(122, 156, 220)), null,
                Geometry.Parse("M 8,19 L 13,14 L 20,21 L 24,17")));
            group.Freeze();
            var brush = new DrawingBrush(group)
            {
                Stretch = Stretch.Uniform
            };
            brush.Freeze();
            return brush;
        }

        private static Brush CreateCompositeNodePreviewBrush()
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(26, 30, 40)), null,
                new RectangleGeometry(new Rect(0, 0, 32, 32))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(90, 72, 126)), null,
                new RectangleGeometry(new Rect(6, 6, 14, 14))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(132, 96, 176)), null,
                new RectangleGeometry(new Rect(12, 12, 14, 14))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(180, 140, 220)), null,
                new EllipseGeometry(new Point(21, 11), 3, 3)));
            group.Freeze();
            var brush = new DrawingBrush(group)
            {
                Stretch = Stretch.Uniform
            };
            brush.Freeze();
            return brush;
        }

        private Brush GenerateComputeNodePreviewBrush(NodeViewModel node)
        {
            const int size = 32;

            var seedParam = node.Parameters.FirstOrDefault(p => p.Name == "seed");
            var scaleParam = node.Parameters.FirstOrDefault(p => p.Name == "scale");
            var octavesParam = node.Parameters.FirstOrDefault(p => p.Name == "octaves");
            var persistenceParam = node.Parameters.FirstOrDefault(p => p.Name == "persistence");
            var lacunarityParam = node.Parameters.FirstOrDefault(p => p.Name == "lacunarity");
            var invert = node.Parameters.FirstOrDefault(p => p.Name == "invert")?.BoolValue ?? false;

            if (seedParam != null && scaleParam != null && octavesParam != null)
            {
                var seed = (seedParam.Kind == NodeParameterKind.Integer || seedParam.Kind == NodeParameterKind.Seed) ? seedParam.IntValue : (int)seedParam.NumberValue;
                var scale = scaleParam.Kind == NodeParameterKind.Integer ? scaleParam.IntValue : scaleParam.NumberValue;
                var octaves = octavesParam.Kind == NodeParameterKind.Integer ? octavesParam.IntValue : (int)octavesParam.NumberValue;
                var persistence = persistenceParam != null ? (persistenceParam.Kind == NodeParameterKind.Integer ? persistenceParam.IntValue : persistenceParam.NumberValue) : 0.5;
                var lacunarity = lacunarityParam != null ? (lacunarityParam.Kind == NodeParameterKind.Integer ? lacunarityParam.IntValue : lacunarityParam.NumberValue) : 2.0;

                var pixels = new byte[size * size * 4];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {

                        var nx = x / (float)size * (float)scale;
                        var ny = y / (float)size * (float)scale;
                        var v = TileableFractalNoise(nx, ny, size, octaves, (float)persistence, (float)lacunarity, seed);
                        v = Math.Clamp(v, 0f, 1f);
                        if (invert) v = 1f - v;

                        var r = (byte)Math.Clamp(60 + v * (200 - 60), 0, 255);
                        var g = (byte)Math.Clamp(70 + v * (220 - 70), 0, 255);
                        var b = (byte)Math.Clamp(90 + v * (240 - 90), 0, 255);
                        var a = (byte)255;
                        var index = (y * size + x) * 4;
                        pixels[index] = b;
                        pixels[index + 1] = g;
                        pixels[index + 2] = r;
                        pixels[index + 3] = a;
                    }
                }

                var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
                bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
                bitmap.Freeze();
                var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                return brush;
            }


            var fallbackGroup = new DrawingGroup();
            fallbackGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(28, 34, 44)), null,
                new RectangleGeometry(new Rect(0, 0, size, size))));
            fallbackGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(72, 88, 128)), null,
                new EllipseGeometry(new Point(11, 11), 5, 5)));
            fallbackGroup.Freeze();
            var fallbackBrush = new DrawingBrush(fallbackGroup) { Stretch = Stretch.UniformToFill };
            fallbackBrush.Freeze();
            return fallbackBrush;
        }

        private static float TileableFractalNoise(float x, float y, int period, int octaves, float persistence, float lacunarity, int seed)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var value = 0f;
            var max = 0f;

            for (var o = 0; o < octaves; o++)
            {
                var octavePeriod = Math.Max(1, (int)MathF.Round(period * frequency));
                value += TileableValueNoise(x * frequency, y * frequency, octavePeriod, seed + o * 1013) * amplitude;
                max += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return max == 0f ? 0f : value / max;
        }

        private static float TileableValueNoise(float x, float y, int period, int seed)
        {
            var x0 = (int)MathF.Floor(x);
            var y0 = (int)MathF.Floor(y);
            var x1 = x0 + 1;
            var y1 = y0 + 1;

            var sx = SmoothStep(x - x0);
            var sy = SmoothStep(y - y0);

            var px0 = Mod(x0, period);
            var py0 = Mod(y0, period);
            var px1 = Mod(x1, period);
            var py1 = Mod(y1, period);

            var n00 = HashToUnit(px0, py0, seed);
            var n10 = HashToUnit(px1, py0, seed);
            var n01 = HashToUnit(px0, py1, seed);
            var n11 = HashToUnit(px1, py1, seed);

            var ix0 = Lerp(n00, n10, sx);
            var ix1 = Lerp(n01, n11, sx);
            return Lerp(ix0, ix1, sy);
        }

        private static float HashToUnit(int x, int y, int seed)
        {
            unchecked
            {
                var hash = x * 374761393 + y * 668265263 + seed * 982451653;
                hash = (hash ^ (hash >> 13)) * 1274126177;
                hash ^= hash >> 16;
                return (hash & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static int Mod(int value, int modulus)
        {
            if (modulus <= 0) return 0;
            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private List<TileLayerDefinition> BuildNodeGraphLayerDefinitions()
        {
            var tileNodes = GetNodeGraphTileNodes();
            var layerDefs = new List<TileLayerDefinition>();

            foreach (var node in tileNodes)
            {
                var layerDefinition = BuildNodeLayerDefinition(node);
                if (layerDefinition.HasValue)
                {
                    layerDefs.Add(layerDefinition.Value);
                }
            }

            return layerDefs;
        }

        private List<TileLayerDefinition> BuildNodePreviewLayerDefinitions()
        {
            if (SelectedNode != null)
            {
                var selectedTiles = GetUpstreamTileNodes(SelectedNode);
                var definitions = new List<TileLayerDefinition>();
                foreach (var tileNode in selectedTiles)
                {
                    var definition = BuildNodeLayerDefinition(tileNode, SelectedNode);
                    if (definition.HasValue)
                    {
                        definitions.Add(definition.Value);
                    }
                }

                // Return definitions for the selected node even if empty. Caller will handle empty case
                // (e.g. show a black preview when there is no upstream content).
                return definitions;
            }

            return BuildNodeGraphLayerDefinitions();
        }

        private List<NodeViewModel> GetUpstreamTileNodes(NodeViewModel target)
        {
            if (target.TileType != null)
            {
                return new List<NodeViewModel> { target };
            }

            var visited = new HashSet<NodeViewModel> { target };
            var stack = new Stack<NodeViewModel>();
            stack.Push(target);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var connection in NodeConnections.Where(connection => !connection.IsPreview && ReferenceEquals(connection.EndNode, current)))
                {
                    if (connection.StartNode != null && visited.Add(connection.StartNode))
                    {
                        stack.Push(connection.StartNode);
                    }
                }
            }

            return visited.Where(node => node.TileType != null).ToList();
        }

        private TileLayerDefinition? BuildNodeLayerDefinition(NodeViewModel node, NodeViewModel? targetNode = null)
        {
            return BuildNodeLayerDefinition(node, targetNode, null);
        }

        private TileLayerDefinition? BuildNodeLayerDefinition(NodeViewModel node, NodeViewModel? targetNode, HashSet<int>? renderingStack)
        {
            if (node.TileType == null)
            {
                return null;
            }

            renderingStack ??= new HashSet<int>();
            if (!renderingStack.Add(node.Id))
            {
                return null;
            }

            try
            {
                // Use TileProperties directly so node-based pipeline does not depend on LayerViewModel.
                var props = node.TileProperties != null ? node.TileProperties.Clone() : new TileProperties();

                var compositeSettings = new CompositeSettings(LayerBlendMode.Normal, 1.0, false);
                var computeSettings = new ComputeSettings(1.0, 1.0, 0.0, false);
                var outputNode = targetNode ?? GetOutputNode();
                var visited = new HashSet<NodeViewModel>();
                var queue = new Queue<(NodeViewModel Node, CompositeSettings Composite, ComputeSettings Compute)>();

                ApplyNodeParameters(props, node);
                queue.Enqueue((node, compositeSettings, computeSettings));
                visited.Add(node);

                while (queue.Count > 0)
                {
                    var (current, composite, compute) = queue.Dequeue();
                    if (outputNode != null && ReferenceEquals(current, outputNode))
                    {
                        compositeSettings = composite;
                        computeSettings = compute;
                        break;
                    }

                    foreach (var connection in NodeConnections.Where(connection => !connection.IsPreview && ReferenceEquals(connection.StartNode, current)))
                    {
                        if (connection.EndNode == null || !visited.Add(connection.EndNode))
                        {
                            continue;
                        }

                        var nextComposite = composite;
                        var nextCompute = compute;
                        if (connection.EndNode.Kind == NodeLibraryItemKind.Composite)
                        {
                            nextComposite = CompositeSettings.FromNode(connection.EndNode, composite);
                        }
                        else if (connection.EndNode.Kind == NodeLibraryItemKind.Compute)
                        {
                            nextCompute = ComputeSettings.FromNode(connection.EndNode, compute);
                        }

                        queue.Enqueue((connection.EndNode, nextComposite, nextCompute));
                    }
                }

                ApplyComputeParameters(props, computeSettings);
                ApplyCompositeParameters(props, compositeSettings);

                var pattern = ResolveGrassFlowerPattern(node, renderingStack);
                var settings = BuildSettings(props, pattern?.Pixels, pattern?.Width ?? 0, pattern?.Height ?? 0);
                return new TileLayerDefinition(node.TileType.Value, settings, compositeSettings.BlendMode, (float)compositeSettings.Opacity);
            }
            finally
            {
                renderingStack.Remove(node.Id);
            }
        }

        private PatternBitmapData? ResolveGrassFlowerPattern(NodeViewModel node, HashSet<int> renderingStack)
        {
            if (node.TileType != TileType.Grass || node.TileProperties?.GrassFlowerMode != GrassFlowerMode.Custom)
            {
                return null;
            }

            var inputIndex = GetInputPortIndex(node, GrassCustomFlowerPortName);
            if (inputIndex < 0)
            {
                return null;
            }

            var sourceNode = NodeConnections
                .LastOrDefault(connection => !connection.IsPreview && ReferenceEquals(connection.EndNode, node) && connection.EndPortIndex == inputIndex)
                ?.StartNode;
            if (sourceNode == null)
            {
                return null;
            }
            // Use the currently selected project tile size so custom flower patterns
            // match the project's resolution instead of a hardcoded 32px.
            var patternSize = TryGetSelectedSize(out var tmpSize) ? tmpSize : 32;
            return CreatePatternBitmapData(sourceNode, patternSize, renderingStack);
        }

        private PatternBitmapData? CreatePatternBitmapData(NodeViewModel node, int size, HashSet<int> renderingStack)
        {
            var bitmap = RenderNodeBitmap(node, size, renderingStack);
            if (bitmap == null)
            {
                return null;
            }

            BitmapSource source = bitmap;
            if (source.Format != PixelFormats.Bgra32)
            {
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = source;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();
                converted.Freeze();
                source = converted;
            }

            var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
            source.CopyPixels(pixels, source.PixelWidth * 4, 0);
            var backgroundWasKeyedOut = OptimizePatternTransparency(pixels, source.PixelWidth, source.PixelHeight);
            var opaqueCoverage = CalculateOpaqueCoverage(pixels);
            if (opaqueCoverage <= 0.01f)
            {
                return null;
            }

            return new PatternBitmapData(pixels, source.PixelWidth, source.PixelHeight, opaqueCoverage, backgroundWasKeyedOut);
        }

        private static bool OptimizePatternTransparency(byte[] pixels, int width, int height)
        {
            if (pixels.Length == 0 || width <= 0 || height <= 0)
            {
                return false;
            }

            var edgeBuckets = new Dictionary<int, (int Count, int SumR, int SumG, int SumB)>();
            var opaqueEdgeCount = 0;

            static void AccumulateEdgePixel(byte[] buffer, int imageWidth, int x, int y, Dictionary<int, (int Count, int SumR, int SumG, int SumB)> buckets, ref int total)
            {
                var index = ((y * imageWidth) + x) * 4;
                var alpha = buffer[index + 3];
                if (alpha < 224)
                {
                    return;
                }

                total++;
                var blue = buffer[index];
                var green = buffer[index + 1];
                var red = buffer[index + 2];
                var key = ((red >> 4) << 8) | ((green >> 4) << 4) | (blue >> 4);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = (0, 0, 0, 0);
                }

                buckets[key] = (bucket.Count + 1, bucket.SumR + red, bucket.SumG + green, bucket.SumB + blue);
            }

            for (var x = 0; x < width; x++)
            {
                AccumulateEdgePixel(pixels, width, x, 0, edgeBuckets, ref opaqueEdgeCount);
                if (height > 1)
                {
                    AccumulateEdgePixel(pixels, width, x, height - 1, edgeBuckets, ref opaqueEdgeCount);
                }
            }

            for (var y = 1; y < height - 1; y++)
            {
                AccumulateEdgePixel(pixels, width, 0, y, edgeBuckets, ref opaqueEdgeCount);
                if (width > 1)
                {
                    AccumulateEdgePixel(pixels, width, width - 1, y, edgeBuckets, ref opaqueEdgeCount);
                }
            }

            if (opaqueEdgeCount < Math.Max(4, Math.Min(width, height)))
            {
                return false;
            }

            var bestBucket = edgeBuckets.Values.OrderByDescending(bucket => bucket.Count).FirstOrDefault();
            if (bestBucket.Count <= 0 || bestBucket.Count < opaqueEdgeCount * 0.45f)
            {
                return false;
            }

            var backgroundR = bestBucket.SumR / bestBucket.Count;
            var backgroundG = bestBucket.SumG / bestBucket.Count;
            var backgroundB = bestBucket.SumB / bestBucket.Count;
            const int hardToleranceSquared = 18 * 18;
            const int softToleranceSquared = 42 * 42;
            var changed = false;

            for (var index = 0; index < pixels.Length; index += 4)
            {
                var alpha = pixels[index + 3];
                if (alpha == 0)
                {
                    continue;
                }

                var deltaR = pixels[index + 2] - backgroundR;
                var deltaG = pixels[index + 1] - backgroundG;
                var deltaB = pixels[index] - backgroundB;
                var distanceSquared = (deltaR * deltaR) + (deltaG * deltaG) + (deltaB * deltaB);
                if (distanceSquared <= hardToleranceSquared)
                {
                    pixels[index + 3] = 0;
                    changed = true;
                    continue;
                }

                if (distanceSquared >= softToleranceSquared)
                {
                    continue;
                }

                var factor = (distanceSquared - hardToleranceSquared) / (float)(softToleranceSquared - hardToleranceSquared);
                var newAlpha = (byte)Math.Clamp(Math.Round(alpha * factor), 0d, 255d);
                if (newAlpha != alpha)
                {
                    pixels[index + 3] = newAlpha;
                    changed = true;
                }
            }

            return changed;
        }

        private bool CanSnapConnectionTarget(NodeViewModel startNode, int startPortIndex, NodeViewModel endNode, int endPortIndex)
        {
            if (endPortIndex < 0 || endPortIndex >= endNode.InputPorts.Count)
            {
                return false;
            }

            var inputPort = endNode.InputPorts[endPortIndex];

            // Reject snap when port types are incompatible
            if (startPortIndex >= 0 && startPortIndex < startNode.OutputPorts.Count)
            {
                var outputPort = startNode.OutputPorts[startPortIndex];
                if (!ArePortTypesCompatible(outputPort.Type, inputPort.Type))
                    return false;
            }

            if (endNode.TileType == TileType.Grass && string.Equals(inputPort.Name, GrassCustomFlowerPortName, StringComparison.Ordinal))
            {
                return startNode.TileType == null;
            }

            return true;
        }

        private bool CanAcceptConnection(NodeViewModel startNode, int startPortIndex, NodeViewModel endNode, int endPortIndex, out string? rejectionMessage)
        {
            rejectionMessage = null;
            if (endPortIndex < 0 || endPortIndex >= endNode.InputPorts.Count)
            {
                rejectionMessage = "Invalid target input port";
                return false;
            }

            var inputPort = endNode.InputPorts[endPortIndex];

            // Type compatibility: only Image?Image/Tile and Mask?Mask are allowed.
            if (startPortIndex >= 0 && startPortIndex < startNode.OutputPorts.Count)
            {
                var outputPort = startNode.OutputPorts[startPortIndex];
                if (!ArePortTypesCompatible(outputPort.Type, inputPort.Type))
                {
                    rejectionMessage = inputPort.Type == PortValueType.Mask
                        ? "Mask ports only accept mask outputs"
                        : "Image ports only accept image outputs";
                    return false;
                }
            }

            // Exclusive-input nodes (e.g. Grayscale) only process one input at a time.
            // Reject a second connection if any other input port is already occupied.
            var graphNode = GraphNodeRegistry.Create(endNode.RegistryKey);
            if (graphNode is IExclusiveInputNode)
            {
                var occupied = NodeConnections.Any(c =>
                    !c.IsPreview &&
                    ReferenceEquals(c.EndNode, endNode) &&
                    c.EndPortIndex != endPortIndex);
                if (occupied)
                {
                    rejectionMessage = "This node can only connect to one input port at a time.";
                    return false;
                }
            }

            if (endNode.TileType != TileType.Grass || !string.Equals(inputPort.Name, GrassCustomFlowerPortName, StringComparison.Ordinal))
            {
                return true;
            }

            if (startNode.TileType != null)
            {
                rejectionMessage = "Custom flower pattern cannot connect to optimized tile nodes. Connect a normal grayscale image node.";
                return false;
            }

            var patternSize = TryGetSelectedSize(out var tmpSize2) ? tmpSize2 : 32;
            var pattern = CreatePatternBitmapData(startNode, patternSize, new HashSet<int>());
            if (pattern == null)
            {
                rejectionMessage = "This node has no available pattern texture.";
                return false;
            }

            if (!pattern.BackgroundWasKeyedOut && pattern.OpaqueCoverage > 0.72f)
            {
                rejectionMessage = "This node's image lacks transparency, not suitable as custom pattern border.";
                return false;
            }

            if (pattern.OpaqueCoverage > 0.82f)
            {
                rejectionMessage = "This node's texture is too dense for custom flower input.";
                return false;
            }

            return true;
        }

        private bool TryFinalizeActiveConnection(NodeViewModel endNode, int endPortIndex)
        {
            if (_activeConnection?.StartNode == null)
            {
                return false;
            }

            RecordUndoSnapshot();

            if (!CanAcceptConnection(_activeConnection.StartNode, _activeConnection.StartPortIndex, endNode, endPortIndex, out var rejectionMessage))
            {
                if (!string.IsNullOrWhiteSpace(rejectionMessage))
                {
                    StatusText.Text = rejectionMessage;
                }

                ClearActiveConnectionPreview();
                return false;
            }

            var existing = NodeConnections.Where(connection => !connection.IsPreview && connection.EndNode == endNode && connection.EndPortIndex == endPortIndex).ToList();
            foreach (var connection in existing)
            {
                NodeConnections.Remove(connection);
            }

            _activeConnection.IsPreview = false;
            _activeConnection.EndNode = endNode;
            _activeConnection.EndPortIndex = endPortIndex;

            try
            {
                NodeConnectionsView?.Refresh();
                UpdateConnectionPositions(endNode);
                NodeConnectionLayer?.InvalidateVisual();
            }
            finally
            {
                _activeConnection = null;
                _activeInputNode = null;
                _activeInputPortIndex = -1;
                _snappedNode = null;
                _snappedPortIndex = -1;
                try { Mouse.Capture(null); } catch { }
            }

            MarkStatsActive();
            RequestPreviewRefresh(false);
            return true;
        }

        /// <summary>
        /// Finalizes a reverse connection: wires the source node's output port to the target input port.
        /// Used when dragging from an input port (reverse direction).
        /// </summary>
        private bool TryFinalizeReverseConnection(NodeViewModel sourceNode, int sourcePortIndex, NodeViewModel targetNode, int targetPortIndex)
        {
            if (sourceNode == null || targetNode == null)
                return false;

            RecordUndoSnapshot();

            if (!CanAcceptConnection(sourceNode, sourcePortIndex, targetNode, targetPortIndex, out var rejectionMessage))
            {
                if (!string.IsNullOrWhiteSpace(rejectionMessage))
                    StatusText.Text = rejectionMessage;
                ClearActiveConnectionPreview();
                return false;
            }

            // Remove existing connection to the target input port
            var existing = NodeConnections.Where(c => !c.IsPreview && c.EndNode == targetNode && c.EndPortIndex == targetPortIndex).ToList();
            foreach (var c in existing)
                NodeConnections.Remove(c);

            // Create the actual connection — use cached positions for accuracy
            var startX = GetCachedPortPosition(sourceNode, true, sourcePortIndex).X;
            var startY = GetCachedPortPosition(sourceNode, true, sourcePortIndex).Y;
            var endX = GetCachedPortPosition(targetNode, false, targetPortIndex).X;
            var endY = GetCachedPortPosition(targetNode, false, targetPortIndex).Y;
            var connection = new NodeConnectionViewModel
            {
                StartNode = sourceNode,
                StartPortIndex = sourcePortIndex,
                StartX = startX,
                StartY = startY,
                EndNode = targetNode,
                EndPortIndex = targetPortIndex,
                EndX = endX,
                EndY = endY,
                IsPreview = false
            };
            NodeConnections.Add(connection);

            // Clean up preview connection
            ClearActiveConnectionPreview();

            _activeConnection = null;
            _activeInputNode = null;
            _activeInputPortIndex = -1;
            _snappedNode = null;
            _snappedPortIndex = -1;
            try { Mouse.Capture(null); } catch { }

            try
            {
                NodeConnectionsView?.Refresh();
                UpdateConnectionPositions(targetNode);
                NodeConnectionLayer?.InvalidateVisual();
            }
            catch { }

            MarkStatsActive();
            RequestPreviewRefresh(false);
            return true;
        }

        private void ClearActiveConnectionPreview()
        {
            var previewConnections = NodeConnections.Where(connection => connection.IsPreview).ToList();
            foreach (var connection in previewConnections)
            {
                NodeConnections.Remove(connection);
            }

            _activeConnection = null;
            _activeInputNode = null;
            _activeInputPortIndex = -1;
            _snappedNode = null;
            _snappedPortIndex = -1;
            try { Mouse.Capture(null); } catch { }

            try
            {
                NodeConnectionsView?.Refresh();
                UpdateConnectionPositions();
                NodeConnectionLayer?.InvalidateVisual();
            }
            catch
            {
            }
        }

        private BitmapSource? RenderNodeBitmap(NodeViewModel node, int size, HashSet<int> renderingStack)
        {
            if (node.TileType != null)
            {
                var definition = BuildNodeLayerDefinition(node, node, renderingStack);
                if (!definition.HasValue)
                {
                    return null;
                }

                var bitmap = _generator.GenerateTileBitmap(size, new[] { definition.Value });
                bitmap.Freeze();
                return bitmap;
            }

            if (IsGraphNode(node))
            {
                var bitmap = EvaluateGraphPipeline(size, node);
                bitmap?.Freeze();
                return bitmap;
            }

            return null;
        }

        private static int GetInputPortIndex(NodeViewModel node, string portName)
        {
            for (var i = 0; i < node.InputPorts.Count; i++)
            {
                if (string.Equals(node.InputPorts[i].Name, portName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void ConfigureTileNodePorts(NodeViewModel node)
        {
            if (node.TileType == null)
            {
                return;
            }

            var desiredInputs = new List<NodePortViewModel>();
            if (node.TileType == TileType.Grass && node.TileProperties?.GrassFlowerMode == GrassFlowerMode.Custom)
            {
                desiredInputs.Add(new NodePortViewModel(GrassCustomFlowerPortName, PortValueType.Image, false));
            }

            var desiredOutputs = new List<NodePortViewModel>
            {
                new NodePortViewModel("Bitmap", PortValueType.Tile, true),
                new NodePortViewModel("Mask", PortValueType.Mask, true)
            };

            if (PortsMatch(node.InputPorts, desiredInputs) && PortsMatch(node.OutputPorts, desiredOutputs))
            {
                return;
            }

            node.InputPorts.Clear();
            foreach (var port in desiredInputs)
            {
                node.InputPorts.Add(port);
            }

            node.OutputPorts.Clear();
            foreach (var port in desiredOutputs)
            {
                node.OutputPorts.Add(port);
            }

            TrimInvalidConnectionsForNode(node);
            try
            {
                NodeConnectionsView?.Refresh();
                UpdateConnectionPositions(node);
            }
            catch
            {
            }
        }

        private void TrimInvalidConnectionsForNode(NodeViewModel node)
        {
            for (var i = NodeConnections.Count - 1; i >= 0; i--)
            {
                var connection = NodeConnections[i];
                if (ReferenceEquals(connection.StartNode, node) && connection.StartPortIndex >= node.OutputPorts.Count)
                {
                    NodeConnections.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(connection.EndNode, node) && connection.EndPortIndex >= node.InputPorts.Count)
                {
                    NodeConnections.RemoveAt(i);
                }
            }
        }

        private static bool PortsMatch(IReadOnlyList<NodePortViewModel> existing, IReadOnlyList<NodePortViewModel> desired)
        {
            if (existing.Count != desired.Count)
            {
                return false;
            }

            for (var i = 0; i < existing.Count; i++)
            {
                if (!string.Equals(existing[i].Name, desired[i].Name, StringComparison.Ordinal) ||
                    existing[i].Type != desired[i].Type ||
                    existing[i].IsOutput != desired[i].IsOutput)
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyNodeParameters(TileProperties props, NodeViewModel node)
        {
            if (node.TileProperties != null)
            {
                props.CopyFrom(node.TileProperties);
            }
        }

        private static void ApplyComputeParameters(TileProperties props, ComputeSettings compute)
        {
            props.Scale = Math.Clamp(props.Scale * compute.Frequency, 0.1, props.Scale);
            props.DetailDensity = Math.Clamp(props.DetailDensity * compute.Amplitude, 0.0, props.DetailDensity);
            props.ColorVariation = Math.Clamp(props.ColorVariation + compute.Offset, 0.0, props.ColorVariation);
            if (compute.Invert)
            {
                props.MaskInvert = true;
            }
        }

        private static void ApplyCompositeParameters(TileProperties props, CompositeSettings composite)
        {
            props.MaskEnabled = composite.MaskEnabled;
            if (composite.MaskEnabled)
            {
                props.MaskElement = MaskElement.Detail;
            }
        }

        private List<NodeViewModel> GetNodeGraphTileNodes()
        {
            var outputNode = GetOutputNode();

            if (outputNode == null)
            {
                return Nodes.Where(node => node.Kind == NodeLibraryItemKind.Tile && node.TileType != null).ToList();
            }

            var visited = new HashSet<NodeViewModel>();
            var stack = new Stack<NodeViewModel>();
            stack.Push(outputNode);
            visited.Add(outputNode);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var connection in NodeConnections.Where(connection => !connection.IsPreview && ReferenceEquals(connection.EndNode, current)))
                {
                    if (connection.StartNode != null && visited.Add(connection.StartNode))
                    {
                        stack.Push(connection.StartNode);
                    }
                }
            }

            var tiles = visited.Where(node => node.Kind == NodeLibraryItemKind.Tile && node.TileType != null).ToList();
            if (tiles.Count == 0)
            {
                tiles.AddRange(Nodes.Where(node => node.Kind == NodeLibraryItemKind.Tile && node.TileType != null));
            }

            return tiles;
        }

        private NodeViewModel? GetOutputNode()
        {
            return Nodes.FirstOrDefault(node => node.Title == "Output")
                ?? Nodes.FirstOrDefault(node => node.OutputPorts.Count == 0 && node.InputPorts.Count > 0);
        }

        private List<NodeViewModel> GetOutputNodes()
        {
            var list = Nodes.Where(node => node.Title == "Output" || (node.OutputPorts.Count == 0 && node.InputPorts.Count > 0)).ToList();
            return list;
        }

        private bool TryGetOutputNodeSize(NodeViewModel output, out int size)
        {
            size = 32;
            if (output == null)
            {
                return TryGetSelectedSize(out size);
            }

            var sizeParam = output.Parameters.FirstOrDefault(item => item.Name == "Output Size")?.SelectedChoice;
            if (!string.IsNullOrWhiteSpace(sizeParam) && int.TryParse(sizeParam, out var parsedSize))
            {
                size = parsedSize;
            }
            else if (!TryGetSelectedSize(out size))
            {
                return false;
            }

            var scale = GetNumberParameter(output, "outputScale", 1.0);
            size = (int)Math.Clamp(Math.Round(size * scale), 8, 512);
            return true;
        }

        private List<TileLayerDefinition> BuildNodeLayerDefinitionsForOutput(NodeViewModel outputNode)
        {
            var defs = new List<TileLayerDefinition>();
            var upstream = GetUpstreamTileNodes(outputNode);
            foreach (var tileNode in upstream)
            {
                var def = BuildNodeLayerDefinition(tileNode, outputNode);
                if (def.HasValue) defs.Add(def.Value);
            }

            return defs;
        }

        private BitmapSource? BuildNineSliceCompositeForOutput(int size, NodeViewModel outputNode)
        {
            var upstream = GetUpstreamTileNodes(outputNode);
            if (upstream.Count == 0) return null;

            var compositeSize = size * 3;
            var layers = new List<(BitmapSource Bitmap, LayerBlendMode BlendMode, float Opacity)>();

            foreach (var node in upstream)
            {
                var def = BuildNodeLayerDefinition(node, outputNode);
                if (!def.HasValue) continue;
                var tile = _generator.GenerateTileBitmap(size, new[] { def.Value });
                tile.Freeze();
                var tiled = TileToGrid(tile, size, compositeSize);
                layers.Add((tiled, LayerBlendMode.Normal, 1f));
            }

            if (layers.Count == 0) return null;
            return _generator.ComposeBitmapLayers(compositeSize, layers);
        }

        private static int GetIntParameter(NodeViewModel node, string name, int fallback)
        {
            var parameter = node.Parameters.FirstOrDefault(item => item.Name == name);
            if (parameter == null)
            {
                return fallback;
            }

            return (parameter.Kind == NodeParameterKind.Integer || parameter.Kind == NodeParameterKind.Seed) ? parameter.IntValue : (int)Math.Round(parameter.NumberValue);
        }

        private static double GetNumberParameter(NodeViewModel node, string name, double fallback)
        {
            var parameter = node.Parameters.FirstOrDefault(item => item.Name == name);
            if (parameter == null)
            {
                return fallback;
            }

            return (parameter.Kind == NodeParameterKind.Integer || parameter.Kind == NodeParameterKind.Seed) ? parameter.IntValue : parameter.NumberValue;
        }

        private static LayerBlendMode GetBlendMode(NodeViewModel node)
        {
            var parameter = node.Parameters.FirstOrDefault(item => item.Name == "blendMode");
            if (parameter?.SelectedChoice == null)
            {
                return LayerBlendMode.Normal;
            }

            return parameter.SelectedChoice switch
            {
                "multiply" => LayerBlendMode.Multiply,
                "screen" => LayerBlendMode.Screen,
                _ => LayerBlendMode.Normal
            };
        }

        private bool TryGetNodeOutputSize(out int size)
        {
            size = 32;
            var output = GetOutputNode();
            if (output == null)
            {
                return TryGetSelectedSize(out size);
            }

            var sizeParam = output.Parameters.FirstOrDefault(item => item.Name == "Output Size")?.SelectedChoice;
            if (!string.IsNullOrWhiteSpace(sizeParam) && int.TryParse(sizeParam, out var parsedSize))
            {
                size = parsedSize;
            }
            else if (!TryGetSelectedSize(out size))
            {
                return false;
            }

            var scale = GetNumberParameter(output, "outputScale", 1.0);
            size = (int)Math.Clamp(Math.Round(size * scale), 8, 512);
            return true;
        }

        private readonly record struct CompositeSettings(LayerBlendMode BlendMode, double Opacity, bool MaskEnabled)
        {
            public static CompositeSettings FromNode(NodeViewModel node, CompositeSettings fallback)
            {
                var blendMode = GetBlendMode(node);
                var opacity = Math.Clamp(GetNumberParameter(node, "opacity", fallback.Opacity), 0.0, 1.0);
                var maskEnabled = node.Parameters.FirstOrDefault(item => item.Name == "invertMask")?.BoolValue ?? fallback.MaskEnabled;
                return new CompositeSettings(blendMode, opacity, maskEnabled);
            }
        }

        private readonly record struct ComputeSettings(double Frequency, double Amplitude, double Offset, bool Invert)
        {
            public static ComputeSettings FromNode(NodeViewModel node, ComputeSettings fallback)
            {
                var frequency = GetNumberParameter(node, "frequency", fallback.Frequency);
                var amplitude = GetNumberParameter(node, "intensity", fallback.Amplitude);
                var offset = GetNumberParameter(node, "offsetX", fallback.Offset);
                var invert = node.Parameters.FirstOrDefault(item => item.Name == "invert")?.BoolValue ?? fallback.Invert;
                return new ComputeSettings(frequency, amplitude, offset, invert);
            }
        }

        private static Point GetPortPosition(NodeViewModel node, bool isOutput, int index)
        {
            const double nodeWidth = 180d;
            const double portSize = 14d;     // port Grid width/height
            const double borderPlusPadding = 7d; // BorderThickness(1) + Padding(6)
            const double headerHeight = 36d;     // Header Border(~32) + margin bottom(4)
            const double portRowHeight = 16d;    // margin(1) + grid(14) + margin(1)

            // Node selection changes BorderThickness 1->2, shifting content inward by 1px
            var selOff = node.IsSelected ? 1 : 0;

            // Content area starts at node.X + borderPlusPadding, adjusted for selection
            var contentLeft = node.X + borderPlusPadding + (isOutput ? 0 : selOff);
            var contentRight = node.X + nodeWidth - borderPlusPadding - (isOutput ? selOff : 0);
            var contentWidth = contentRight - contentLeft;

            if (isOutput)
            {
                // Output port: in the right column of the inner Grid (2 * columns)
                // Port Grid Margin="0,0,-13,0": extends 13px beyond the column's right edge
                // The port Grid center is near the content right edge
                var portCenterX = contentRight + 6;
                var portCenterY = node.Y + borderPlusPadding + headerHeight + (index * portRowHeight) + portSize * 0.5;
                return new Point(portCenterX, portCenterY);
            }
            else
            {
                // Input port: Margin="-13,0,3,0": extends 13px left from the column's left edge
                var portCenterX = contentLeft - 6;
                var portCenterY = node.Y + borderPlusPadding + headerHeight + (index * portRowHeight) + portSize * 0.5;
                return new Point(portCenterX, portCenterY);
            }
        }

        private static bool IsParticleOrPhysicsNode(IGraphNode node)
        {
            return node is PixelAssetGenerator.Core.Particles.Nodes.ParticleEmitterNode
                || node is PixelAssetGenerator.Core.Particles.Nodes.ParticleForceNode
                || node is PixelAssetGenerator.Core.Particles.Nodes.ParticleRenderNode
                || node is PixelAssetGenerator.Core.Physics.Nodes.PhysicsSimulateNode
                || node is PixelAssetGenerator.Core.Physics.Nodes.PhysicsFieldNode;
        }

        /// <summary>
        /// Adapts a pre-rendered PixelBuffer as a graph node so tile nodes can participate
        /// in the graph evaluation pipeline. Produces both the color Bitmap (port 0) and
        /// a grayscale Mask (port 1) from the same source buffer.
        /// </summary>
        private sealed class PixelBufferSourceNode : IGraphNode, IMultiOutputNode, IDisposable
        {
            private readonly PixelBuffer _buffer;
            private readonly PixelBuffer _maskBuffer;
            private static readonly IReadOnlyList<GraphNodePort> _outputPorts = new[]
            {
                new GraphNodePort("Bitmap", GraphPortType.Image),
                new GraphNodePort("Mask", GraphPortType.Mask)
            };

            public PixelBufferSourceNode(PixelBuffer buffer)
            {
                _buffer = buffer;
                // Pre-compute mask from alpha/luminance once
                _maskBuffer = PixelBuffer.CreateMaskView(buffer);
            }

            public string TypeName => "__TileSource__";
            public string Category => "Source";
            public IReadOnlyList<GraphNodePort> InputPorts => Array.Empty<GraphNodePort>();
            public IReadOnlyList<GraphNodePort> OutputPorts => _outputPorts;
            public IReadOnlyList<NodeParameterDefinition> Parameters => Array.Empty<NodeParameterDefinition>();

            /// <summary>Safety fallback — the evaluator always goes through ProcessMulti for IMultiOutputNode.</summary>
            public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context) => _buffer.Clone();

            public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
            {
                return new[] { _buffer.Clone(), _maskBuffer.Clone() };
            }

            public void Dispose()
            {
                _buffer.Dispose();
                _maskBuffer.Dispose();
            }
        }

        /// <summary>
        /// Renders a tile node to a PixelBuffer using the standard tile pipeline so it
        /// can be used as an upstream input for graph-based nodes (e.g. ƫ�ư�װ).
        /// Returns null on failure.
        /// </summary>
        private PixelBuffer? TryRenderTileNodeAsPixelBuffer(NodeViewModel node, int size)
        {
            try
            {
                var def = BuildNodeLayerDefinition(node, node, new HashSet<int>());
                if (!def.HasValue) return null;
                var bitmap = _generator.GenerateTileBitmap(size, new[] { def.Value });
                bitmap.Freeze();
                return PixelBuffer.FromBitmapSource(bitmap);
            }
            catch
            {
                return null;
            }
        }
    }
}
