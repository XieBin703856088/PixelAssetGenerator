using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Services;

namespace PixelAssetGenerator
{
    public partial class MainWindow
    {
        private bool _connectionGeometryRefreshPending;

        private static readonly string[] CommonCanvasNodeTypes =
        {
            "AiImageGen", "Output", "Preview",
            "AnimationWorkflowOutput", "SpriteMotionMeta", "ParticleEffectMeta",
            "MaterialEffectStackMeta", "TileVariationMeta",
            "SmartSpriteFramer", "SmartPixelPolish", "SmartPaletteTransfer",
            "SmartMaterialWeathering", "SmartCrackDamage",
            "AnimationTimeline", "MotionPreset", "SpriteAnimator", "AnimatedTransform", "SpriteEffectAnimator",
            "ParticleEmitter", "ParticleForce", "ParticleBehavior", "ParticleTrail", "ParticleRender",
            "PhysicsSprite", "PhysicsSimulate", "PhysicsField", "PhysicsConstraint", "PhysicsSoftBody"
        };

        private void ScheduleConnectionGeometryRefresh()
        {
            if (_connectionGeometryRefreshPending) return;
            _connectionGeometryRefreshPending = true;
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _connectionGeometryRefreshPending = false;
                try
                {
                    NodeCanvasItems?.UpdateLayout();
                    ClearAllPortPositionCache();
                    CacheAllPortPositions();
                    UpdateConnectionPositions();
                    NodeConnectionsView?.Refresh();
                    NodeConnectionLayer?.InvalidateVisual();
                }
                catch
                {
                    // A subsequent layout/collection change will schedule another pass.
                }
            }), DispatcherPriority.Loaded);
        }

        private void NodeCanvasHost_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try { _lastNodeCanvasRightClick = Mouse.GetPosition(NodeCanvasHost); }
            catch { _lastNodeCanvasRightClick = new Point(0, 0); }
        }

        private void CanvasContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;

            var addRoot = FindTopLevelMenuItem(menu, "CanvasAddRoot");
            if (addRoot != null)
                BuildCanvasAddNodeMenu(addRoot);

            var presetRoot = FindTopLevelMenuItem(menu, "CanvasPresetRoot");
            if (presetRoot != null)
                BuildCanvasPresetMenu(presetRoot);

            var paste = FindTopLevelMenuItem(menu, "CanvasPaste");
            if (paste != null) paste.IsEnabled = _nodeClipboard?.Nodes.Count > 0;

            var delete = FindTopLevelMenuItem(menu, "CanvasDelete");
            if (delete != null) delete.IsEnabled = Nodes.Any(node => node.IsSelected) || SelectedNode != null;
        }

        private static MenuItem? FindTopLevelMenuItem(ContextMenu menu, string tag)
            => menu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                string.Equals(item.Tag as string, tag, StringComparison.Ordinal));

        private void BuildCanvasAddNodeMenu(MenuItem root)
        {
            root.Items.Clear();

            var common = new MenuItem { Header = "常用" };
            foreach (var typeName in CommonCanvasNodeTypes)
            {
                var item = FindNodeLibraryItem(typeName);
                if (item != null) common.Items.Add(CreateAddNodeMenuItem(item));
            }
            if (common.Items.Count > 0)
            {
                root.Items.Add(common);
                root.Items.Add(new Separator());
            }

            var groups = NodeLibrary
                .GroupBy(item => string.IsNullOrWhiteSpace(item.CategoryKey) ? item.Category : item.CategoryKey)
                .Select(group => new
                {
                    Key = group.Key,
                    DisplayName = string.IsNullOrWhiteSpace(group.Key)
                        ? group.First().Category
                        : NodeLibraryService.GetCategoryDisplayName(group.Key),
                    Items = group.OrderBy(item => item.Name, StringComparer.CurrentCulture).ToList()
                })
                .OrderBy(group => group.DisplayName, StringComparer.CurrentCulture)
                .ToList();

            foreach (var group in groups)
            {
                var categoryMenu = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(group.DisplayName) ? group.Key : group.DisplayName
                };
                foreach (var item in group.Items)
                    categoryMenu.Items.Add(CreateAddNodeMenuItem(item));
                root.Items.Add(categoryMenu);
            }

            root.IsEnabled = root.Items.Count > 0;
        }

        private MenuItem CreateAddNodeMenuItem(NodeLibraryItem libraryItem)
        {
            var menuItem = new MenuItem
            {
                Header = libraryItem.Name,
                Tag = libraryItem.TypeName,
                ToolTip = string.IsNullOrWhiteSpace(libraryItem.Description)
                    ? $"在右键位置创建 {libraryItem.Name}"
                    : libraryItem.Description
            };
            menuItem.Click += Canvas_AddNode_Click;
            return menuItem;
        }

        private void BuildCanvasPresetMenu(MenuItem root)
        {
            root.Items.Clear();

            var meta = new MenuItem { Header = "元节点" };
            AddPresetMenuItem(meta, "角色动作", "meta-character-animation", "精灵素材、像素动作、附加光效和独立工作流输出一次生成。" );
            AddPresetMenuItem(meta, "完整粒子", "meta-particle-effect", "一个节点完成粒子发射、运动、纹理渲染和预热。" );
            AddPresetMenuItem(meta, "遗迹材质", "meta-ruins-material", "石材、破损、苔藓和综合遮罩的一体化材质流程。" );
            AddPresetMenuItem(meta, "图块变体", "meta-tile-variations", "从一个基础图块快速得到四个可平铺的像素变体。" );
            root.Items.Add(meta);

            var animation = new MenuItem { Header = "动画" };
            AddPresetMenuItem(animation, "待机", "animation-idle", "创建呼吸待机与动画变换；只需把素材接入图像端口。" );
            AddPresetMenuItem(animation, "漂浮", "animation-float", "适合掉落物、幽灵、提示图标等循环漂浮。" );
            AddPresetMenuItem(animation, "受击", "animation-hit", "带衰减的位移、旋转与缩放反馈。" );
            AddPresetMenuItem(animation, "跳跃", "animation-hop", "创建像素对齐的抛物线跳跃动作。" );
            AddPresetMenuItem(animation, "呼吸光效", "animation-glow", "创建一个可直接播放的图标呼吸发光演示。" );
            AddPresetMenuItem(animation, "像素溶解", "animation-dissolve", "创建一个可直接播放的精灵溶解演示。" );
            AddPresetMenuItem(animation, "流光", "animation-shimmer", "创建一个可直接播放的像素流光演示。" );
            AddPresetMenuItem(animation, "关键帧序列", "animation-sequence", "三段缓动强度控制，可直接观察关键帧过渡。" );
            AddPresetMenuItem(animation, "精灵表", "animation-sprite-sheet", "时间轴自动驱动精灵表帧；只需接入精灵表图像。" );
            root.Items.Add(animation);

            var particles = new MenuItem { Header = "粒子" };
            AddPresetMenuItem(particles, "火焰", "particle-fire", "火焰发射器、闪烁行为和自动纹理渲染。" );
            AddPresetMenuItem(particles, "烟雾", "particle-smoke", "烟雾发射器、湍流力场、脉动行为和烟雾渲染。" );
            AddPresetMenuItem(particles, "雨", "particle-rain", "高密度雨滴发射与自动雨滴渲染。" );
            AddPresetMenuItem(particles, "雪", "particle-snow", "雪花发射、曲折飘动与自动雪花纹理。" );
            AddPresetMenuItem(particles, "魔法", "particle-magic", "魔法粒子、色相循环与加法混合渲染。" );
            AddPresetMenuItem(particles, "爆炸", "particle-explosion", "单次爆发、闪烁衰减和火花渲染。" );
            AddPresetMenuItem(particles, "火花", "particle-sparks", "火花发射、重力力场、闪烁和火花纹理。" );
            AddPresetMenuItem(particles, "尘土", "particle-dust", "适合脚步、落地和地表扬尘。" );
            AddPresetMenuItem(particles, "气泡", "particle-bubbles", "水下气泡上升与左右漂移。" );
            AddPresetMenuItem(particles, "落叶", "particle-leaves", "落叶旋转和曲折飘落。" );
            AddPresetMenuItem(particles, "能量拖尾", "particle-energy-trail", "带轨迹残影的魔法能量粒子。" );
            AddPresetMenuItem(particles, "路径发射", "particle-path", "发射器沿样条路径移动并持续喷射魔法粒子。" );
            root.Items.Add(particles);

            var physics = new MenuItem { Header = "物理" };
            AddPresetMenuItem(physics, "史莱姆软体", "physics-jelly", "用物理软体形变快速制作史莱姆、凝胶和弹性道具动画。" );
            AddPresetMenuItem(physics, "弹跳落地", "physics-bounce", "带重力、地面接触与回弹压缩的循环落地动作。" );
            AddPresetMenuItem(physics, "悬挂摆动", "physics-swing", "从顶部固定点产生适合吊牌、武器和钟摆的摆动。" );
            AddPresetMenuItem(physics, "旗帜布料", "physics-cloth", "生成像素对齐的布料波动和软体网格演示。" );
            AddPresetMenuItem(physics, "翻滚掉落", "physics-tumble", "组合抛物线位移、旋转和地面接触的掉落动作。" );
            AddPresetMenuItem(physics, "粒子弹跳", "physics-particle-bounce", "发射粒子经过刚体重力与地面碰撞后再渲染。" );
            root.Items.Add(physics);

            var smart = new MenuItem { Header = "智能" };
            AddPresetMenuItem(smart, "自动整理", "smart-cleanup", "自动裁掉空白、统一主体位置并清理杂乱像素。" );
            AddPresetMenuItem(smart, "砖墙腐蚀", "smart-corroded-brick", "直接生成砖墙、裂纹崩边和锈蚀斑块。" );
            AddPresetMenuItem(smart, "苔藓石板", "smart-moss-stone", "直接生成石板并让苔藓沿缝隙成片生长。" );
            AddPresetMenuItem(smart, "潮湿地面", "smart-damp-floor", "直接生成石砖地面和潮湿水渍。" );
            AddPresetMenuItem(smart, "霜冻鹅卵石", "smart-frost-cobble", "直接生成带冰霜覆盖的鹅卵石图块。" );
            root.Items.Add(smart);
        }

        private void AddPresetMenuItem(MenuItem parent, string title, string presetId, string description)
        {
            var item = new MenuItem { Header = title, Tag = presetId, ToolTip = description };
            item.Click += Canvas_AddPreset_Click;
            parent.Items.Add(item);
        }

        private NodeLibraryItem? FindNodeLibraryItem(string typeName)
            => NodeLibrary.FirstOrDefault(item =>
                string.Equals(item.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

        private void Canvas_AddNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string typeName) return;
            var libraryItem = FindNodeLibraryItem(typeName);
            if (libraryItem == null)
            {
                DarkMessageBox.Show(this, $"节点类型 {typeName} 当前不可用。", "无法创建节点");
                return;
            }

            RecordUndoSnapshot();
            var position = HostToContent(_lastNodeCanvasRightClick);
            var node = CreateNodeFromLibraryItem(libraryItem, position.X, position.Y);
            ClearNodeSelection();
            Nodes.Add(node);
            node.IsSelected = true;
            SelectedNode = node;
            UpdateNodeCanvasExtent();
            RequestPreviewRefresh(false);
            StatusText.Text = $"已创建节点：{node.Title}";
        }

        private void Context_Paste_Click(object sender, RoutedEventArgs e)
        {
            if (_nodeClipboard == null) return;
            var created = _nodeGraphController.PasteClipboardAtMouse(
                _nodeClipboard, NodeCanvasScale,
                () => _lastNodeCanvasRightClick,
                () => NodeCanvasHost?.ActualWidth ?? 0,
                () => NodeCanvasHost?.ActualHeight ?? 0);
            if (created.Count > 0) SelectedNode = created[^1];
        }

        private void Canvas_AddPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string presetId) return;
            CreateBuiltinPreset(presetId, HostToContent(_lastNodeCanvasRightClick));
        }

        private void CreateBuiltinPreset(string presetId, Point origin)
        {
            var requiredTypes = GetPresetRequiredTypes(presetId);
            var missingTypes = requiredTypes.Where(type => FindNodeLibraryItem(type) == null).ToList();
            if (missingTypes.Count > 0)
            {
                DarkMessageBox.Show(this,
                    $"预设所需节点尚未加载：{string.Join("、", missingTypes)}",
                    "无法创建预设");
                return;
            }

            RecordUndoSnapshot();
            var created = new List<NodeViewModel>();
            string completionHint;

            switch (presetId)
            {
                case "meta-character-animation":
                    created.AddRange(CreateCharacterMetaPreset(origin));
                    completionHint = "角色动作元节点已创建；选择动作与附加特效即可快速得到完整动画。";
                    break;
                case "meta-particle-effect":
                    created.AddRange(CreateParticleMetaPreset(origin));
                    completionHint = "完整粒子元节点已创建；切换效果即可直接预览火焰、烟雾、天气或魔法粒子。";
                    break;
                case "meta-ruins-material":
                    created.AddRange(CreateMaterialMetaPreset(origin));
                    completionHint = "遗迹材质元节点已创建；可在六种材质效果栈间切换，并使用综合遮罩继续混合。";
                    break;
                case "meta-tile-variations":
                    created.AddRange(CreateTileVariationMetaPreset(origin));
                    completionHint = "图块变体元节点已创建；四个输出分别提供稳定、可复现的像素变体。";
                    break;
                case "animation-idle":
                    created.AddRange(CreateMotionPreset(origin, "idle", 0.10));
                    completionHint = "待机呼吸已创建并自动播放；可替换最左侧演示素材。";
                    break;
                case "animation-float":
                    created.AddRange(CreateMotionPreset(origin, "float", 0.08));
                    completionHint = "漂浮预设已创建并自动播放；可替换最左侧演示素材。";
                    break;
                case "animation-hit":
                    created.AddRange(CreateMotionPreset(origin, "hit", 0.22));
                    completionHint = "受击反馈预设已创建；可调动作幅度和速度。";
                    break;
                case "animation-hop":
                    created.AddRange(CreateMotionPreset(origin, "hop", 0.16));
                    completionHint = "跳跃预设已创建并自动播放；可替换最左侧演示素材。";
                    break;
                case "animation-glow":
                    created.AddRange(CreateSpriteEffectPreset(origin, "Icon", "pulseGlow"));
                    completionHint = "呼吸光效已创建并自动播放；可替换最左侧图标节点。";
                    break;
                case "animation-dissolve":
                    created.AddRange(CreateSpriteEffectPreset(origin, "Slime", "dissolve"));
                    completionHint = "像素溶解已创建并自动播放；颜色和像素块大小可直接调整。";
                    break;
                case "animation-shimmer":
                    created.AddRange(CreateSpriteEffectPreset(origin, "Icon", "shimmer"));
                    completionHint = "流光动画已创建并自动播放。";
                    break;
                case "animation-sequence":
                    created.AddRange(CreateSequencedEffectPreset(origin));
                    completionHint = "关键帧序列已创建；中间节点可编辑每段时长、数值与缓动。";
                    break;
                case "animation-sprite-sheet":
                    created.AddRange(CreateSpriteSheetPreset(origin));
                    completionHint = "精灵表播放预设已创建；把精灵表接入并设置行列数。";
                    break;
                case "particle-fire":
                    created.AddRange(CreateParticlePreset(origin, "fire", null, "flicker"));
                    completionHint = "像素火焰预设已创建，切到动画预览后按播放即可查看。";
                    break;
                case "particle-smoke":
                    created.AddRange(CreateParticlePreset(origin, "smoke", "turbulence", "pulse"));
                    completionHint = "烟雾预设已创建；湍流强度控制烟雾摆动。";
                    break;
                case "particle-rain":
                    created.AddRange(CreateParticlePreset(origin, "rain", null, null));
                    completionHint = "雨水预设已创建，渲染器会自动使用雨滴纹理。";
                    break;
                case "particle-snow":
                    created.AddRange(CreateParticlePreset(origin, "snow", null, "zigzag"));
                    completionHint = "飘雪预设已创建；行为强度控制左右飘动。";
                    break;
                case "particle-magic":
                    created.AddRange(CreateParticlePreset(origin, "magic", null, "colorCycle"));
                    completionHint = "魔法能量预设已创建；渲染器会自动使用符文纹理和发光混合。";
                    break;
                case "particle-explosion":
                    created.AddRange(CreateParticlePreset(origin, "explosion", null, "flicker"));
                    completionHint = "爆炸预设已创建；停止再播放可重新触发单次爆发。";
                    break;
                case "particle-sparks":
                    created.AddRange(CreateParticlePreset(origin, "sparks", "gravity", "flicker", true));
                    completionHint = "火花预设已创建；可调整重力强度和粒子寿命。";
                    break;
                case "particle-dust":
                    created.AddRange(CreateParticlePreset(origin, "dust", "turbulence", "drag"));
                    completionHint = "扬尘预设已创建；适合脚步、落地和破坏反馈。";
                    break;
                case "particle-bubbles":
                    created.AddRange(CreateParticlePreset(origin, "bubbles", null, "zigzag"));
                    completionHint = "气泡预设已创建；上升速度和左右漂动均可调整。";
                    break;
                case "particle-leaves":
                    created.AddRange(CreateParticlePreset(origin, "leaves", null, "zigzag", true));
                    completionHint = "落叶预设已创建；包含曲折运动和短拖尾。";
                    break;
                case "particle-energy-trail":
                    created.AddRange(CreateParticlePreset(origin, "magic", "vortex", "colorCycle", true));
                    completionHint = "能量拖尾已创建；包含漩涡、色相循环与残影。";
                    break;
                case "particle-path":
                    created.AddRange(CreatePathParticlePreset(origin));
                    completionHint = "路径发射预设已创建；动画路径会同时驱动发射器 X/Y 位置。";
                    break;
                case "physics-jelly":
                    created.AddRange(CreatePhysicsSpritePreset(origin, "Slime", "jelly"));
                    completionHint = "史莱姆软体预设已创建并自动播放；可调回弹、阻尼和运动强度。";
                    break;
                case "physics-bounce":
                    created.AddRange(CreatePhysicsSpritePreset(origin, "Rock", "bounce"));
                    completionHint = "弹跳落地预设已创建；接触遮罩可继续驱动扬尘或火花。";
                    break;
                case "physics-swing":
                    created.AddRange(CreatePhysicsSpritePreset(origin, "Icon", "swing"));
                    completionHint = "悬挂摆动预设已创建；固定点默认位于精灵顶部。";
                    break;
                case "physics-cloth":
                    created.AddRange(CreatePhysicsSpritePreset(origin, "Fence", "cloth"));
                    completionHint = "旗帜布料预设已创建；也可单独添加“软体网格”查看弹簧质点结构。";
                    break;
                case "physics-tumble":
                    created.AddRange(CreatePhysicsSpritePreset(origin, "Rock", "tumble"));
                    completionHint = "翻滚掉落预设已创建并自动播放。";
                    break;
                case "physics-particle-bounce":
                    created.AddRange(CreateParticlePhysicsPreset(origin));
                    completionHint = "粒子刚体弹跳预设已创建；重力、弹性、摩擦和地面高度均可调整。";
                    break;
                case "smart-cleanup":
                    created.AddRange(CreateSmartCleanupPreset(origin));
                    completionHint = "智能整理预设已创建；把生成或导入的图像接到第一个节点。";
                    break;
                case "smart-corroded-brick":
                    created.AddRange(CreateSmartTexturePreset(origin, "Wall", "corrosion", true));
                    completionHint = "砖墙腐蚀已生成；最后两个节点分别控制裂纹和腐蚀。";
                    break;
                case "smart-moss-stone":
                    created.AddRange(CreateSmartTexturePreset(origin, "Flagstone", "moss", false));
                    completionHint = "苔藓石板已生成；调节覆盖量和缝隙附着即可快速变化。";
                    break;
                case "smart-damp-floor":
                    created.AddRange(CreateSmartTexturePreset(origin, "Floor", "damp", false));
                    completionHint = "潮湿地面已生成；可调整上下偏向形成积水方向。";
                    break;
                case "smart-frost-cobble":
                    created.AddRange(CreateSmartTexturePreset(origin, "Cobblestone", "frost", false));
                    completionHint = "霜冻鹅卵石已生成；效果遮罩可继续接入其他混合节点。";
                    break;
                default:
                    return;
            }

            FinishPresetCreation(created, completionHint,
                presetId.StartsWith("animation-", StringComparison.Ordinal)
                || presetId.StartsWith("particle-", StringComparison.Ordinal)
                || presetId.StartsWith("physics-", StringComparison.Ordinal)
                || presetId is "meta-character-animation" or "meta-particle-effect");
        }

        private static IReadOnlyList<string> GetPresetRequiredTypes(string presetId)
        {
            if (presetId.StartsWith("meta-", StringComparison.Ordinal))
            {
                return presetId switch
                {
                    "meta-character-animation" => new[] { "Slime", "SpriteMotionMeta", "AnimationWorkflowOutput" },
                    "meta-particle-effect" => new[] { "ParticleEffectMeta", "AnimationWorkflowOutput" },
                    "meta-ruins-material" => new[] { "Flagstone", "MaterialEffectStackMeta" },
                    "meta-tile-variations" => new[] { "Grass", "TileVariationMeta" },
                    _ => Array.Empty<string>()
                };
            }
            if (presetId.StartsWith("animation-", StringComparison.Ordinal))
            {
                return presetId == "animation-sprite-sheet"
                    ? new[] { "AnimationTimeline", "SpriteAnimator" }
                    : presetId == "animation-sequence"
                        ? new[] { "Icon", "AnimationSequencer", "SpriteEffectAnimator" }
                    : presetId is "animation-glow" or "animation-dissolve" or "animation-shimmer"
                        ? new[] { presetId == "animation-dissolve" ? "Slime" : "Icon", "SpriteEffectAnimator" }
                        : new[] { "Slime", "MotionPreset", "AnimatedTransform" };
            }
            if (presetId.StartsWith("particle-", StringComparison.Ordinal))
            {
                var list = new List<string> { "ParticleEmitter", "ParticleRender" };
                if (presetId is "particle-smoke" or "particle-sparks" or "particle-dust" or "particle-energy-trail") list.Add("ParticleForce");
                if (presetId is not "particle-rain") list.Add("ParticleBehavior");
                if (presetId is "particle-sparks" or "particle-leaves" or "particle-energy-trail" or "particle-path") list.Add("ParticleTrail");
                if (presetId == "particle-path") list.Add("AnimationPath");
                return list;
            }
            if (presetId.StartsWith("physics-", StringComparison.Ordinal))
            {
                if (presetId == "physics-particle-bounce")
                    return new[] { "ParticleEmitter", "PhysicsSimulate", "ParticleRender" };
                var source = presetId switch
                {
                    "physics-jelly" => "Slime",
                    "physics-swing" => "Icon",
                    "physics-cloth" => "Fence",
                    _ => "Rock"
                };
                return new[] { source, "PhysicsSprite", "AnimationWorkflowOutput" };
            }
            return presetId switch
            {
                "smart-cleanup" => new[] { "SmartSpriteFramer", "SmartPixelPolish" },
                "smart-corroded-brick" => new[] { "Wall", "SmartCrackDamage", "SmartMaterialWeathering" },
                "smart-moss-stone" => new[] { "Flagstone", "SmartMaterialWeathering" },
                "smart-damp-floor" => new[] { "Floor", "SmartMaterialWeathering" },
                "smart-frost-cobble" => new[] { "Cobblestone", "SmartMaterialWeathering" },
                _ => Array.Empty<string>()
            };
        }

        private List<NodeViewModel> CreatePhysicsSpritePreset(Point origin, string sourceType, string preset)
        {
            const double startOffset = 360;
            var source = AddPresetNode(sourceType, origin.X - startOffset, origin.Y);
            var physics = AddPresetNode("PhysicsSprite", origin.X - startOffset + 250, origin.Y);
            var output = AddPresetNode("AnimationWorkflowOutput", origin.X - startOffset + 520, origin.Y);
            SetChoiceParameter(physics, "preset", preset);
            SetNumberParameter(physics, "strength", preset is "jelly" or "cloth" ? 0.72 : 0.62);
            SetChoiceParameter(physics, "pivot", preset is "swing" or "cloth" ? "top" : "center");
            ConnectPresetNodes(source, "image", physics, "image");
            ConnectPresetNodes(physics, "image", output, "image");
            return [source, physics, output];
        }

        private List<NodeViewModel> CreateParticlePhysicsPreset(Point origin)
        {
            const double startOffset = 420;
            var emitter = AddPresetNode("ParticleEmitter", origin.X - startOffset, origin.Y);
            var physics = AddPresetNode("PhysicsSimulate", origin.X - startOffset + 260, origin.Y);
            var render = AddPresetNode("ParticleRender", origin.X - startOffset + 520, origin.Y);
            SetChoiceParameter(emitter, "preset", "sparks");
            SetNumberParameter(emitter, "positionY", 0.22);
            SetNumberParameter(physics, "gravityY", 0.72);
            SetNumberParameter(physics, "restitution", 0.72);
            SetNumberParameter(physics, "friction", 0.18);
            SetNumberParameter(physics, "groundY", 0.88);
            ConnectPresetNodes(emitter, "particles", physics, "particles");
            ConnectPresetNodes(physics, "particles", render, "particles");
            return [emitter, physics, render];
        }

        private List<NodeViewModel> CreateMotionPreset(Point origin, string motion, double strength)
        {
            const double startOffset = 330;
            var sourceNode = AddPresetNode("Slime", origin.X - startOffset, origin.Y);
            var motionNode = AddPresetNode("MotionPreset", origin.X - startOffset + 230, origin.Y);
            var transformNode = AddPresetNode("AnimatedTransform", origin.X - startOffset + 480, origin.Y);
            SetChoiceParameter(motionNode, "preset", motion);
            SetNumberParameter(motionNode, "strength", strength);
            ConnectPresetNodes(sourceNode, "image", transformNode, "image");
            ConnectPresetNodes(motionNode, "positionX", transformNode, "positionX");
            ConnectPresetNodes(motionNode, "positionY", transformNode, "positionY");
            ConnectPresetNodes(motionNode, "rotation", transformNode, "rotation");
            ConnectPresetNodes(motionNode, "scale", transformNode, "scale");
            return new List<NodeViewModel> { sourceNode, motionNode, transformNode };
        }

        private List<NodeViewModel> CreateCharacterMetaPreset(Point origin)
        {
            const double startOffset = 330;
            var source = AddPresetNode("Slime", origin.X - startOffset, origin.Y);
            var motion = AddPresetNode("SpriteMotionMeta", origin.X - startOffset + 250, origin.Y);
            var output = AddPresetNode("AnimationWorkflowOutput", origin.X - startOffset + 520, origin.Y);
            SetChoiceParameter(motion, "motion", "idle");
            SetChoiceParameter(motion, "effect", "pulseGlow");
            SetNumberParameter(motion, "motionStrength", 0.12);
            SetNumberParameter(motion, "effectStrength", 0.56);
            ConnectPresetNodes(source, "image", motion, "image");
            ConnectPresetNodes(motion, "image", output, "image");
            return new List<NodeViewModel> { source, motion, output };
        }

        private List<NodeViewModel> CreateParticleMetaPreset(Point origin)
        {
            const double startOffset = 215;
            var effect = AddPresetNode("ParticleEffectMeta", origin.X - startOffset, origin.Y);
            var output = AddPresetNode("AnimationWorkflowOutput", origin.X - startOffset + 270, origin.Y);
            SetChoiceParameter(effect, "effect", "magic");
            SetNumberParameter(effect, "intensity", 0.82);
            SetNumberParameter(effect, "scale", 0.9);
            SetNumberParameter(output, "duration", 1.6);
            SetIntegerParameter(output, "frameRate", 15);
            ConnectPresetNodes(effect, "image", output, "image");
            return new List<NodeViewModel> { effect, output };
        }

        private List<NodeViewModel> CreateMaterialMetaPreset(Point origin)
        {
            const double startOffset = 215;
            var source = AddPresetNode("Flagstone", origin.X - startOffset, origin.Y);
            var material = AddPresetNode("MaterialEffectStackMeta", origin.X - startOffset + 270, origin.Y);
            SetChoiceParameter(material, "preset", "mossyRuins");
            SetNumberParameter(material, "effectAmount", 0.58);
            SetNumberParameter(material, "damageAmount", 0.34);
            ConnectPresetNodes(source, "image", material, "image");
            return new List<NodeViewModel> { source, material };
        }

        private List<NodeViewModel> CreateTileVariationMetaPreset(Point origin)
        {
            const double startOffset = 215;
            var source = AddPresetNode("Grass", origin.X - startOffset, origin.Y);
            var variants = AddPresetNode("TileVariationMeta", origin.X - startOffset + 270, origin.Y);
            SetChoiceParameter(variants, "style", "natural");
            SetNumberParameter(variants, "variation", 0.20);
            ConnectPresetNodes(source, "image", variants, "image");
            return new List<NodeViewModel> { source, variants };
        }

        private List<NodeViewModel> CreateSpriteEffectPreset(Point origin, string sourceType, string effect)
        {
            const double startOffset = 215;
            var source = AddPresetNode(sourceType, origin.X - startOffset, origin.Y);
            var animator = AddPresetNode("SpriteEffectAnimator", origin.X - startOffset + 250, origin.Y);
            SetChoiceParameter(animator, "effect", effect);
            SetNumberParameter(animator, "strength", effect == "dissolve" ? 1.0 : 0.78);
            ConnectPresetNodes(source, "image", animator, "image");
            return new List<NodeViewModel> { source, animator };
        }

        private List<NodeViewModel> CreateSequencedEffectPreset(Point origin)
        {
            const double startOffset = 330;
            var source = AddPresetNode("Icon", origin.X - startOffset, origin.Y);
            var sequencer = AddPresetNode("AnimationSequencer", origin.X - startOffset + 230, origin.Y);
            var animator = AddPresetNode("SpriteEffectAnimator", origin.X - startOffset + 480, origin.Y);
            SetIntegerParameter(sequencer, "segments", 3);
            SetNumberParameter(sequencer, "segment_1_duration", 0.35);
            SetNumberParameter(sequencer, "segment_1_startValue", 0.15);
            SetNumberParameter(sequencer, "segment_1_endValue", 1.0);
            SetChoiceParameter(sequencer, "segment_1_easing", "easeOut");
            SetNumberParameter(sequencer, "segment_2_duration", 0.25);
            SetNumberParameter(sequencer, "segment_2_startValue", 1.0);
            SetNumberParameter(sequencer, "segment_2_endValue", 0.35);
            SetChoiceParameter(sequencer, "segment_2_easing", "bounceOut");
            SetNumberParameter(sequencer, "segment_3_duration", 0.55);
            SetNumberParameter(sequencer, "segment_3_startValue", 0.35);
            SetNumberParameter(sequencer, "segment_3_endValue", 0.72);
            SetChoiceParameter(sequencer, "segment_3_easing", "easeInOut");
            SetChoiceParameter(animator, "effect", "pulseGlow");
            ConnectPresetNodes(source, "image", animator, "image");
            ConnectPresetNodes(sequencer, "value", animator, "strength");
            return new List<NodeViewModel> { source, sequencer, animator };
        }

        private List<NodeViewModel> CreateSpriteSheetPreset(Point origin)
        {
            const double startOffset = 215;
            var timeline = AddPresetNode("AnimationTimeline", origin.X - startOffset, origin.Y);
            var animator = AddPresetNode("SpriteAnimator", origin.X - startOffset + 250, origin.Y);
            ConnectPresetNodes(timeline, "frame", animator, "frame");
            return new List<NodeViewModel> { timeline, animator };
        }

        private List<NodeViewModel> CreateParticlePreset(
            Point origin, string emitterPreset, string? forceType, string? behavior, bool includeTrail = false)
        {
            var nodes = new List<NodeViewModel>();
            var nodeCount = 2 + (forceType != null ? 1 : 0) + (behavior != null ? 1 : 0) + (includeTrail ? 1 : 0);
            var totalWidth = 180 + (nodeCount - 1) * 230;
            var x = origin.X - totalWidth / 2;
            var emitter = AddPresetNode("ParticleEmitter", x, origin.Y);
            nodes.Add(emitter);
            SetChoiceParameter(emitter, "preset", emitterPreset);
            if (emitterPreset == "explosion") SetBooleanParameter(emitter, "oneShot", true);

            NodeViewModel previous = emitter;
            if (forceType != null)
            {
                x += 230;
                var force = AddPresetNode("ParticleForce", x, origin.Y);
                nodes.Add(force);
                SetChoiceParameter(force, "forceType", forceType);
                SetNumberParameter(force, "strength", forceType == "turbulence" ? 0.35 : 0.65);
                ConnectPresetNodes(previous, "particles", force, "particles");
                previous = force;
            }

            if (behavior != null)
            {
                x += 230;
                var behaviorNode = AddPresetNode("ParticleBehavior", x, origin.Y);
                nodes.Add(behaviorNode);
                SetChoiceParameter(behaviorNode, "behavior", behavior);
                SetNumberParameter(behaviorNode, "strength", behavior == "zigzag" ? 0.22 : 0.35);
                ConnectPresetNodes(previous, "particles", behaviorNode, "particles");
                previous = behaviorNode;
            }

            if (includeTrail)
            {
                x += 230;
                var trail = AddPresetNode("ParticleTrail", x, origin.Y);
                nodes.Add(trail);
                SetNumberParameter(trail, "trailLength", emitterPreset == "leaves" ? 0.22 : 0.38);
                SetIntegerParameter(trail, "segments", emitterPreset == "leaves" ? 2 : 4);
                SetNumberParameter(trail, "fadeAlpha", 0.68);
                ConnectPresetNodes(previous, "particles", trail, "particles");
                previous = trail;
            }

            x += 230;
            var render = AddPresetNode("ParticleRender", x, origin.Y);
            nodes.Add(render);
            SetChoiceParameter(render, "texture", "auto");
            SetChoiceParameter(render, "blendMode", "auto");
            ConnectPresetNodes(previous, "particles", render, "particles");
            return nodes;
        }

        private List<NodeViewModel> CreatePathParticlePreset(Point origin)
        {
            const double startOffset = 560;
            var path = AddPresetNode("AnimationPath", origin.X - startOffset, origin.Y);
            var emitter = AddPresetNode("ParticleEmitter", origin.X - startOffset + 230, origin.Y);
            var behavior = AddPresetNode("ParticleBehavior", origin.X - startOffset + 460, origin.Y);
            var trail = AddPresetNode("ParticleTrail", origin.X - startOffset + 690, origin.Y);
            var render = AddPresetNode("ParticleRender", origin.X - startOffset + 920, origin.Y);
            SetNumberParameter(path, "duration", 2.4);
            SetChoiceParameter(emitter, "preset", "magic");
            SetNumberParameter(emitter, "presetIntensity", 0.72);
            SetChoiceParameter(behavior, "behavior", "colorCycle");
            SetNumberParameter(behavior, "strength", 0.42);
            SetNumberParameter(trail, "trailLength", 0.34);
            SetIntegerParameter(trail, "segments", 4);
            SetChoiceParameter(render, "texture", "auto");
            SetChoiceParameter(render, "blendMode", "auto");
            ConnectPresetNodes(path, "positionX", emitter, "positionX");
            ConnectPresetNodes(path, "positionY", emitter, "positionY");
            ConnectPresetNodes(emitter, "particles", behavior, "particles");
            ConnectPresetNodes(behavior, "particles", trail, "particles");
            ConnectPresetNodes(trail, "particles", render, "particles");
            return new List<NodeViewModel> { path, emitter, behavior, trail, render };
        }

        private List<NodeViewModel> CreateSmartCleanupPreset(Point origin)
        {
            const double startOffset = 215;
            var framer = AddPresetNode("SmartSpriteFramer", origin.X - startOffset, origin.Y);
            var polish = AddPresetNode("SmartPixelPolish", origin.X - startOffset + 250, origin.Y);
            ConnectPresetNodes(framer, "image", polish, "image");
            return new List<NodeViewModel> { framer, polish };
        }

        private List<NodeViewModel> CreateSmartTexturePreset(Point origin, string sourceType,
            string effect, bool includeCracks)
        {
            var nodes = new List<NodeViewModel>();
            var startOffset = includeCracks ? 330 : 215;
            var source = AddPresetNode(sourceType, origin.X - startOffset, origin.Y);
            nodes.Add(source);

            if (sourceType == "Wall")
            {
                SetChoiceParameter(source, "wallType", "stone");
                SetColorParameter(source, "mainColor", System.Windows.Media.Color.FromRgb(142, 73, 55));
                SetColorParameter(source, "mortarColor", System.Windows.Media.Color.FromRgb(75, 62, 57));
            }
            else if (sourceType == "Floor")
                SetChoiceParameter(source, "floorType", "stoneTile");

            NodeViewModel previous = source;
            var x = origin.X - startOffset;
            if (includeCracks)
            {
                x += 230;
                var cracks = AddPresetNode("SmartCrackDamage", x, origin.Y);
                nodes.Add(cracks);
                SetChoiceParameter(cracks, "material", "brick");
                SetNumberParameter(cracks, "damage", 0.64);
                SetNumberParameter(cracks, "chips", 0.48);
                ConnectPresetNodes(previous, "image", cracks, "image");
                previous = cracks;
            }

            x += 250;
            var weathering = AddPresetNode("SmartMaterialWeathering", x, origin.Y);
            nodes.Add(weathering);
            SetChoiceParameter(weathering, "effect", effect);
            SetNumberParameter(weathering, "amount", effect == "moss" ? 0.62 : 0.56);
            SetNumberParameter(weathering, "edgeAffinity", effect is "moss" or "corrosion" ? 0.78 : 0.55);
            ConnectPresetNodes(previous, "image", weathering, "image");
            return nodes;
        }

        private NodeViewModel AddPresetNode(string typeName, double x, double y)
        {
            var libraryItem = FindNodeLibraryItem(typeName)
                ?? throw new InvalidOperationException($"Missing node type: {typeName}");
            var node = CreateNodeFromLibraryItem(libraryItem, x, y);
            Nodes.Add(node);
            return node;
        }

        private void ConnectPresetNodes(NodeViewModel startNode, string outputKey,
            NodeViewModel endNode, string inputKey)
        {
            var startIndex = startNode.OutputPorts
                .Select((port, index) => (port, index))
                .FirstOrDefault(item => string.Equals(item.port.Key, outputKey, StringComparison.OrdinalIgnoreCase)).index;
            var endIndex = endNode.InputPorts
                .Select((port, index) => (port, index))
                .FirstOrDefault(item => string.Equals(item.port.Key, inputKey, StringComparison.OrdinalIgnoreCase)).index;

            var hasStart = startNode.OutputPorts.Any(port =>
                string.Equals(port.Key, outputKey, StringComparison.OrdinalIgnoreCase));
            var hasEnd = endNode.InputPorts.Any(port =>
                string.Equals(port.Key, inputKey, StringComparison.OrdinalIgnoreCase));
            if (!hasStart || !hasEnd) return;

            if (!_nodeGraphController.CanAcceptConnection(
                    startNode, startIndex, endNode, endIndex, out _)) return;

            NodeGraphController.RemoveConflictingConnections(NodeConnections, endNode, endIndex);
            NodeConnections.Add(new NodeConnectionViewModel
            {
                StartNode = startNode,
                StartPortIndex = startIndex,
                EndNode = endNode,
                EndPortIndex = endIndex,
                IsPreview = false
            });
        }

        private static void SetChoiceParameter(NodeViewModel node, string name, string value)
        {
            var parameter = node.Parameters.FirstOrDefault(item => item.Name == name);
            if (parameter != null) parameter.SelectedChoice = value;
        }

        private static void SetNumberParameter(NodeViewModel node, string name, double value)
        {
            var parameter = node.Parameters.FirstOrDefault(item => item.Name == name);
            if (parameter != null) parameter.NumberValue = value;
        }

        private static void SetBooleanParameter(NodeViewModel node, string name, bool value)
        {
            var parameter = node.Parameters.FirstOrDefault(item => item.Name == name);
            if (parameter != null) parameter.BoolValue = value;
        }

        private static void SetIntegerParameter(NodeViewModel node, string name, int value)
        {
            var parameter = node.Parameters.FirstOrDefault(item => item.Name == name);
            if (parameter != null) parameter.IntValue = value;
        }

        private static void SetColorParameter(NodeViewModel node, string name, System.Windows.Media.Color value)
        {
            var parameter = node.Parameters.FirstOrDefault(item => item.Name == name);
            if (parameter != null) parameter.ColorValue = value;
        }

        private static void ApplyPersistedParameterValues(
            NodeViewModel node, IEnumerable<ProjectFileService.NodeParameterData> savedParameters)
        {
            var definitions = GraphNodeRegistry.Create(node.RegistryKey)?.Parameters;
            foreach (var saved in savedParameters)
            {
                var parameter = node.Parameters.FirstOrDefault(item =>
                    string.Equals(item.Name, saved.Name, StringComparison.Ordinal));
                if (parameter == null)
                {
                    var definition = definitions?.FirstOrDefault(item =>
                        string.Equals(item.Name, saved.Name, StringComparison.Ordinal));
                    parameter = definition?.CreateViewModel()
                        ?? new NodeParameterViewModel(
                            saved.Name, saved.Kind, 0, 1, 0.01, Array.Empty<string>());
                    node.Parameters.Add(parameter);
                }

                parameter.NumberValue = saved.NumberValue;
                parameter.IntValue = saved.IntValue;
                parameter.BoolValue = saved.BoolValue;
                parameter.SelectedChoice = saved.SelectedChoice;
                if (saved.TextValue != null) parameter.TextValue = saved.TextValue;
                if (saved.ColorArgb is uint argb)
                {
                    parameter.ColorValue = System.Windows.Media.Color.FromArgb(
                        (byte)(argb >> 24), (byte)(argb >> 16),
                        (byte)(argb >> 8), (byte)argb);
                }
                parameter.PointListValue.Clear();
                foreach (var point in saved.PointListData) parameter.PointListValue.Add(point);
            }
        }

        private void FinishPresetCreation(List<NodeViewModel> created, string completionHint, bool useAnimationPreview)
        {
            if (created.Count == 0) return;
            ClearNodeSelection();
            foreach (var node in created) node.IsSelected = true;
            SelectedNode = created[^1];
            UpdateNodeCanvasExtent();
            NodeConnectionsView?.Refresh();
            UpdateConnectionPositions();
            NodeConnectionLayer?.InvalidateVisual();
            _particleEvalService?.ClearState();
            _lastParticleSimulationFrame = -1;
            UpdateAnimationUI();

            if (useAnimationPreview && AnimPreviewRadio != null)
                AnimPreviewRadio.IsChecked = true;

            RequestPreviewRefresh(false);
            StatusText.Text = useAnimationPreview
                ? $"{completionHint} 已自动播放。"
                : completionHint;
            ScheduleConnectionGeometryRefresh();
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsurePresetVisible(created);
                if (useAnimationPreview) StartPresetPlayback(created);
            }), DispatcherPriority.Loaded);
        }

        private void StartPresetPlayback(IReadOnlyList<NodeViewModel> created)
        {
            if (AnimPreviewRadio != null) AnimPreviewRadio.IsChecked = true;
            if (StillPreviewRadio != null) StillPreviewRadio.IsChecked = false;

            var isParticlePreset = created.Any(node =>
                node.TypeName.StartsWith("Particle", StringComparison.Ordinal));
            var service = EnsureAnimationService();
            service.FrameCount = isParticlePreset ? 32 : 16;
            service.FrameRate = isParticlePreset ? 15 : 12;
            SelectComboBoxTag(AnimFrameCountCombo, service.FrameCount.ToString());
            SelectComboBoxTag(AnimFpsCombo, ((int)service.FrameRate).ToString());
            ApplyActiveWorkflowPlaybackSettings();

            _particleEvalService?.ClearState();
            _lastParticleSimulationFrame = -1;
            service.Stop();
            service.Play();
            UpdateAnimationTimelineUi(service.CurrentFrame);
            UpdateAnimationPlaybackUi();
        }

        private static void SelectComboBoxTag(ComboBox? comboBox, string tag)
        {
            if (comboBox == null) return;
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (!string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal)) continue;
                comboBox.SelectedItem = item;
                return;
            }
            var customItem = new ComboBoxItem { Content = tag, Tag = tag };
            comboBox.Items.Add(customItem);
            comboBox.SelectedItem = customItem;
        }

        private void EnsurePresetVisible(IReadOnlyList<NodeViewModel> created)
        {
            if (created.Count == 0 || NodeCanvasHost == null) return;
            const double nodeWidth = 180;
            const double nodeHeight = 120;
            var contentWidth = created.Max(node => node.X + nodeWidth) - created.Min(node => node.X);
            var contentHeight = created.Max(node => node.Y + nodeHeight) - created.Min(node => node.Y);
            if (contentWidth * NodeCanvasScale > NodeCanvasHost.ActualWidth - 48
                || contentHeight * NodeCanvasScale > NodeCanvasHost.ActualHeight - 48)
            {
                ZoomToSelectedNodes();
                ScheduleConnectionGeometryRefresh();
            }
        }

        private void Context_ShowAnimationParticleHelp_Click(object sender, RoutedEventArgs e)
        {
            const string message =
                "最快用法\n\n" +
                "1. 在画布空白处右键，打开“预设”。\n" +
                "2. 动画预设：把素材图像接到动画变换/精灵表节点的蓝色图像输入。\n" +
                "3. 粒子预设：发射器 → 力场/行为 → 粒子渲染已经自动连好，可选接一张背景图。\n" +
                "4. 物理预设：精灵物理可直接接成品素材；粒子刚体需要放在发射器和渲染器之间。\n" +
                "5. 接触遮罩可继续驱动扬尘、火花或其他二次特效。\n" +
                "6. 新建预设会自动切换到动画预览并播放；顶部可暂停、停止和逐帧查看。\n" +
                "7. 选中整个节点组后，可右键保存为自己的预设模板。\n\n" +
                "提示：粒子渲染是最终图像输出；发射器和中间粒子/物理节点本身不会直接显示完整画面。";
            DarkMessageBox.Show(this, message, "动画、粒子与物理节点使用说明");
        }

        private void NodeContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu
                && menu.PlacementTarget is FrameworkElement target
                && target.DataContext is NodeViewModel node)
                PrepareContextNode(node);
        }

        private void PrepareContextNode(NodeViewModel node)
        {
            if (!node.IsSelected)
            {
                ClearNodeSelection();
                node.IsSelected = true;
            }
            SelectedNode = node;
        }

        private static NodeViewModel? GetContextNode(object sender)
            => sender is MenuItem menuItem ? menuItem.DataContext as NodeViewModel : null;

        private void RenameNode(NodeViewModel node)
        {
            PrepareContextNode(node);
            var title = DarkInputBox.Show(this, "请输入新的节点名称：", "重命名节点", node.Title);
            if (string.IsNullOrWhiteSpace(title) || string.Equals(title.Trim(), node.Title, StringComparison.Ordinal)) return;
            RecordUndoSnapshot();
            node.Title = title.Trim();
            StatusText.Text = $"节点已重命名为：{node.Title}";
        }

        private void Node_Context_Copy_Click(object sender, RoutedEventArgs e)
        {
            var node = GetContextNode(sender);
            if (node == null) return;
            PrepareContextNode(node);
            CopySelectedNodes();
            StatusText.Text = $"已复制 {GetSelectedNodes().Count} 个节点";
        }

        private void DuplicateContextSelection(NodeViewModel node)
        {
            PrepareContextNode(node);
            var selected = GetSelectedNodes();
            var data = _nodeGraphController.CopySelectedNodes(SelectedNode, GetSelectedTileSize());
            if (data == null || selected.Count == 0) return;

            var target = new Point(selected.Min(item => item.X) + 24, selected.Min(item => item.Y) + 24);
            var created = _nodeGraphController.PasteClipboardAtMouse(
                data, NodeCanvasScale, () => ContentToHost(target),
                () => double.MaxValue, () => double.MaxValue);
            if (created.Count > 0)
            {
                SelectedNode = created[^1];
                StatusText.Text = $"已创建 {created.Count} 个节点副本";
            }
        }

        private void Node_Context_MoveToViewCenter_Click(object sender, RoutedEventArgs e)
        {
            var node = GetContextNode(sender);
            if (node == null) return;
            PrepareContextNode(node);
            var selected = GetSelectedNodes();
            if (selected.Count == 0) return;

            var minX = selected.Min(item => item.X);
            var maxX = selected.Max(item => item.X + 180);
            var minY = selected.Min(item => item.Y);
            var maxY = selected.Max(item => item.Y + 120);
            var viewportCenter = HostToContent(new Point(
                (NodeCanvasHost?.ActualWidth ?? 0) / 2,
                (NodeCanvasHost?.ActualHeight ?? 0) / 2));
            MoveSelectedNodes(selected,
                viewportCenter.X - (minX + maxX) / 2,
                viewportCenter.Y - (minY + maxY) / 2);
        }

        private void Node_Context_Nudge_Click(object sender, RoutedEventArgs e)
        {
            var node = GetContextNode(sender);
            if (node == null || sender is not MenuItem menuItem) return;
            PrepareContextNode(node);
            var (dx, dy) = (menuItem.Tag as string) switch
            {
                "left" => (-16d, 0d),
                "right" => (16d, 0d),
                "up" => (0d, -16d),
                "down" => (0d, 16d),
                _ => (0d, 0d)
            };
            if (dx != 0 || dy != 0) MoveSelectedNodes(GetSelectedNodes(), dx, dy);
        }

        private void MoveSelectedNodes(IReadOnlyList<NodeViewModel> nodes, double dx, double dy)
        {
            if (nodes.Count == 0) return;
            RecordUndoSnapshot();
            foreach (var item in nodes)
            {
                item.X += dx;
                item.Y += dy;
            }
            UpdateNodeCanvasExtent();
            UpdateConnectionPositions();
            NodeConnectionLayer?.InvalidateVisual();
            RequestPreviewRefresh(false);
            StatusText.Text = $"已移动 {nodes.Count} 个节点";
        }

        private void Node_Context_Disconnect_Click(object sender, RoutedEventArgs e)
        {
            var node = GetContextNode(sender);
            if (node == null) return;
            PrepareContextNode(node);
            var selected = new HashSet<NodeViewModel>(GetSelectedNodes());
            var connections = NodeConnections.Where(connection =>
                (connection.StartNode != null && selected.Contains(connection.StartNode))
                || (connection.EndNode != null && selected.Contains(connection.EndNode))).ToList();
            if (connections.Count == 0) return;

            RecordUndoSnapshot();
            foreach (var connection in connections) NodeConnections.Remove(connection);
            NodeConnectionsView?.Refresh();
            NodeConnectionLayer?.InvalidateVisual();
            RequestPreviewRefresh(false);
            StatusText.Text = $"已断开 {connections.Count} 条连接";
        }

        private void Node_Context_SaveAsTemplate_Click(object sender, RoutedEventArgs e)
        {
            var node = GetContextNode(sender);
            if (node == null) return;
            PrepareContextNode(node);
            SaveAsTemplateMenuItem_Click(sender, e);
        }
    }
}
