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
        private ProjectFileService.ProjectData BuildProjectData()
        {
            return ExportService.BuildProjectData(Nodes, NodeConnections, GetSelectedTileSize());
        }

        private void LoadProjectData(ProjectFileService.ProjectData data)
        {
            // Clear existing nodes/connections
            Nodes.Clear();
            NodeConnections.Clear();

            // Recreate nodes
            var created = new List<NodeViewModel>();
            foreach (var nd in data.Nodes)
            {
                var typeName = string.IsNullOrWhiteSpace(nd.TypeName) ? nd.Title : nd.TypeName;
                var libraryItem = FindNodeLibraryItem(typeName)
                    ?? NodeLibrary.FirstOrDefault(item =>
                        string.Equals(item.Name, nd.Title, StringComparison.CurrentCulture));

                var node = libraryItem != null
                    ? CreateNodeFromLibraryItem(libraryItem, nd.X, nd.Y)
                    : new NodeViewModel(nd.Title, nd.X, nd.Y)
                    {
                        Kind = nd.Kind,
                        TileType = nd.TileType,
                        TypeName = typeName
                    };

                node.Title = nd.Title;
                node.Kind = nd.Kind;
                node.TileType = nd.TileType;
                node.TypeName = typeName;
                if (libraryItem != null) node.Category = libraryItem.Category;

                if (nd.Properties != null)
                {
                    node.TileProperties = nd.Properties.Clone();
                }

                ApplyPersistedParameterValues(node, nd.Parameters);

                // Initialize ports so connection type validation works after loading
                if (node.InputPorts.Count == 0 && node.OutputPorts.Count == 0 && node.TileType != null)
                {
                    ConfigureTileNodePorts(node);
                }
                else if (node.InputPorts.Count == 0 && node.OutputPorts.Count == 0)
                {
                    var proto = GraphNodeRegistry.Create(node.RegistryKey);
                    if (proto != null)
                    {
                        foreach (var port in proto.InputPorts)
                            node.InputPorts.Add(new NodePortViewModel(port.Name, MapGraphPortType(port.Type), false, port.StableKey));
                        foreach (var port in proto.OutputPorts)
                            node.OutputPorts.Add(new NodePortViewModel(port.Name, MapGraphPortType(port.Type), true, port.StableKey));
                    }
                }

                Nodes.Add(node);
                created.Add(node);
            }

            // Recreate connections
            foreach (var cd in data.Connections)
            {
                if (cd.StartNodeIndex < 0 || cd.StartNodeIndex >= Nodes.Count) continue;
                if (cd.EndNodeIndex < 0 || cd.EndNodeIndex >= Nodes.Count) continue;
                var conn = new NodeConnectionViewModel
                {
                    StartNode = Nodes[cd.StartNodeIndex],
                    StartPortIndex = cd.StartPortIndex,
                    EndNode = Nodes[cd.EndNodeIndex],
                    EndPortIndex = cd.EndPortIndex,
                    IsPreview = false
                };
                NodeConnections.Add(conn);
            }

            SelectedNode = Nodes.FirstOrDefault();
            UpdateNodeCanvasExtent();
            RequestPreviewRefresh(true);
            // Settings subsystem removed — no settings file will be loaded here.
        }

        private static void WriteProjectFile(string path, ProjectFileService.ProjectData data)
            => ProjectFileService.WriteProjectFile(path, data);

        private static ProjectFileService.ProjectData? ReadProjectFile(string path)
            => ProjectFileService.ReadProjectFile(path);

        private void SaveProjectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PixelAssetProject (*.pxtile)|*.pxtile",
                FileName = "project.pxtile",
                AddExtension = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var data = BuildProjectData();
            WriteProjectFile(dialog.FileName, data);
            StatusText.Text = "Project saved";
        }

        private void OpenProjectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PixelAssetProject (*.pxtile)|*.pxtile|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            ProjectFileService.ProjectData? data;
            try
            {
                data = ReadProjectFile(dialog.FileName);
            }
            catch (Exception)
            {
                StatusText.Text = "Cannot read project file";
                return;
            }

            if (data is null)
            {
                StatusText.Text = "Invalid project file";
                return;
            }

            LoadProjectData(data);
            StatusText.Text = "Project opened";
        }
    }
}
