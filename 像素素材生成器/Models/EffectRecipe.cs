using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Services;

namespace PixelAssetGenerator.Models;

/// <summary>
/// 效果配方中的数据节点（元节点实例）
/// </summary>
public sealed class RecipeNode
{
    public string Id { get; set; } = "";
    /// <summary>节点类型名：如 Noise、BlendMode、HSLAdjust</summary>
    public string Type { get; set; } = "";
    /// <summary>节点在节点库中的分类：Source/Material/Adjust/Effect 等</summary>
    public string Category { get; set; } = "";
    /// <summary>参数表：键=参数名，值=参数值（JSON格式）</summary>
    public Dictionary<string, JsonElement> Params { get; set; } = new();
    /// <summary>画布坐标X（用于可视化，非必需）</summary>
    public double X { get; set; }
    /// <summary>画布坐标Y</summary>
    public double Y { get; set; }
}

/// <summary>
/// 效果配方中的连线
/// </summary>
public sealed class RecipeEdge
{
    /// <summary>源节点ID</summary>
    public string From { get; set; } = "";
    /// <summary>源节点输出端口索引</summary>
    public int OutputPort { get; set; }
    /// <summary>目标节点ID</summary>
    public string To { get; set; } = "";
    /// <summary>目标节点输入端口索引</summary>
    public int InputPort { get; set; }
}

/// <summary>
/// 效果配方（Effect Recipe）— 一个可执行的元节点序列+连线+参数。
/// AI 通过组合元节点生成高级节点效果。
/// </summary>
public sealed class EffectRecipe
{
    public string RecipeId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Untitled Effect";
    public string Description { get; set; } = "";
    public List<RecipeNode> Nodes { get; set; } = new();
    public List<RecipeEdge> Edges { get; set; } = new();

    /// <summary>美学标签（自动提取或手动标注）</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>复杂度评分 1-10</summary>
    public int Complexity { get; set; }
    /// <summary>生成时的种子</summary>
    public int Seed { get; set; }
    /// <summary>美学评分（缓存）</summary>
    public double? CachedScore { get; set; }
    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>用户评分（1-5）</summary>
    public int UserRating { get; set; }

    /// <summary>
    /// 将配方应用到节点图控制器，在画布上创建实际节点和连线。
    /// createNodeFunc 参数：typeName, x, y ——返回创建的 NodeViewModel
    /// </summary>
    public bool ApplyToController(
        NodeGraphController controller,
        ObservableCollection<NodeViewModel> nodes,
        ObservableCollection<NodeConnectionViewModel> connections,
        Func<string, double, double, NodeViewModel?> createNodeFunc,
        double baseX, double baseY)
    {
        try
        {
            var idToNode = new Dictionary<string, NodeViewModel>();

            foreach (var rn in Nodes)
            {
                var node = createNodeFunc(rn.Type, baseX + rn.X, baseY + rn.Y);
                if (node == null) continue;

                // 设置参数
                foreach (var kvp in rn.Params)
                {
                    var param = node.Parameters.FirstOrDefault(p =>
                        string.Equals(p.Name, kvp.Key, StringComparison.OrdinalIgnoreCase));
                    if (param == null) continue;

                    ApplyParamValue(param, kvp.Value);
                }

                nodes.Add(node);
                idToNode[rn.Id] = node;
            }

            // 创建连线
            foreach (var edge in Edges)
            {
                if (!idToNode.TryGetValue(edge.From, out var fromNode)) continue;
                if (!idToNode.TryGetValue(edge.To, out var toNode)) continue;

                if (edge.OutputPort < fromNode.OutputPorts.Count &&
                    edge.InputPort < toNode.InputPorts.Count)
                {
                    NodeGraphController.RemoveConflictingConnections(connections, toNode, edge.InputPort);

                    connections.Add(new NodeConnectionViewModel
                    {
                        StartNode = fromNode,
                        StartPortIndex = edge.OutputPort,
                        EndNode = toNode,
                        EndPortIndex = edge.InputPort,
                        IsPreview = false
                    });
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 将配方转换为 SkillDefinition，持久化到技能库。
    /// </summary>
    public SkillDefinition ToSkill(string name, string description, string category)
    {
        var recipeJson = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        return new SkillDefinition
        {
            Id = RecipeId,
            Name = name,
            Description = description,
            Category = string.IsNullOrEmpty(category) ? "AI Generated" : category,
            Kind = "recipe",
            SerializedGraph = recipeJson,
            Tags = Tags,
            CreatedAt = DateTime.UtcNow,
            Enabled = true
        };
    }

    /// <summary>
    /// 从 SkillDefinition 反序列化为配方。
    /// </summary>
    public static EffectRecipe? FromSkill(SkillDefinition skill)
    {
        if (skill.Kind != "recipe" || string.IsNullOrEmpty(skill.SerializedGraph))
            return null;

        try
        {
            var recipe = JsonSerializer.Deserialize<EffectRecipe>(skill.SerializedGraph);
            if (recipe != null)
            {
                recipe.RecipeId = skill.Id;
                recipe.Name = skill.Name;
                recipe.Description = skill.Description;
                recipe.Tags = skill.Tags;
            }
            return recipe;
        }
        catch { return null; }
    }

    /// <summary>
    /// 从当前画布状态创建配方（反向工程）。
    /// </summary>
    public static EffectRecipe FromCanvas(
        IEnumerable<NodeViewModel> nodes,
        IEnumerable<NodeConnectionViewModel> connections)
    {
        var recipe = new EffectRecipe();

        // 找出最小坐标作为偏移
        var nodeList = nodes.ToList();
        double minX = nodeList.Count > 0 ? nodeList.Min(n => n.X) : 0;
        double minY = nodeList.Count > 0 ? nodeList.Min(n => n.Y) : 0;

        var idMap = new Dictionary<int, string>();

        foreach (var n in nodeList)
        {
            var nodeId = $"n_{n.Id}";
            idMap[n.Id] = nodeId;

            var rn = new RecipeNode
            {
                Id = nodeId,
                Type = n.TypeName ?? n.Title,
                Category = n.Category ?? "",
                X = n.X - minX,
                Y = n.Y - minY
            };

            foreach (var p in n.Parameters)
            {
                rn.Params[p.Name] = GetParamJson(p);
            }

            recipe.Nodes.Add(rn);
        }

        foreach (var c in connections.Where(c => c.StartNode != null && c.EndNode != null && !c.IsPreview))
        {
            if (idMap.TryGetValue(c.StartNode!.Id, out var fromId) &&
                idMap.TryGetValue(c.EndNode!.Id, out var toId))
            {
                recipe.Edges.Add(new RecipeEdge
                {
                    From = fromId,
                    OutputPort = c.StartPortIndex,
                    To = toId,
                    InputPort = c.EndPortIndex
                });
            }
        }

        return recipe;
    }

    /// <summary>
    /// 生成配方的文本摘要（供AI阅读）。
    /// </summary>
    public string ToPromptSummary()
    {
        var lines = new List<string>
        {
            $"配方: {Name}",
            $"描述: {Description}",
            $"节点数: {Nodes.Count}",
            $"标签: {string.Join(", ", Tags)}",
            "",
            "节点序列:"
        };

        // 拓扑排序（简化：假设大部分是线性链）
        var ordered = TopologicalSort();

        foreach (var n in ordered)
        {
            var paramStrs = n.Params.Select(kvp =>
            {
                try { return $"{kvp.Key}={kvp.Value.GetRawText()}"; }
                catch { return $"{kvp.Key}=?"; }
            });
            var incoming = Edges.Where(e => e.To == n.Id).Select(e =>
                $"[从{e.From}:出{e.OutputPort}]");
            lines.Add($"  {n.Id}: [{n.Type}] {string.Join(", ", paramStrs)}");
            if (incoming.Any())
                lines.Add($"    ├─ {string.Join(", ", incoming)}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 简化拓扑排序（DFS实现），用于序列化展示。
    /// </summary>
    private List<RecipeNode> TopologicalSort()
    {
        var visited = new HashSet<string>();
        var result = new List<RecipeNode>();
        var nodeMap = Nodes.ToDictionary(n => n.Id);

        void Dfs(string id)
        {
            if (visited.Contains(id)) return;
            visited.Add(id);

            // 先处理前置节点（输入边指向的节点）
            foreach (var edge in Edges.Where(e => e.To == id))
                Dfs(edge.From);

            if (nodeMap.TryGetValue(id, out var node))
                result.Add(node);
        }

        // 从没有输入边的节点开始
        var hasIncoming = Edges.Select(e => e.To).ToHashSet();
        foreach (var n in Nodes.Where(n => !hasIncoming.Contains(n.Id)))
            Dfs(n.Id);

        // 补全未访问的
        foreach (var n in Nodes)
            Dfs(n.Id);

        return result;
    }

    // ─── 工具方法 ───

    private static void ApplyParamValue(NodeParameterViewModel param, JsonElement value)
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
                    var r = value.TryGetProperty("r", out var rEl) ? (byte)rEl.GetInt32() : (byte)255;
                    var g = value.TryGetProperty("g", out var gEl) ? (byte)gEl.GetInt32() : (byte)255;
                    var b = value.TryGetProperty("b", out var bEl) ? (byte)bEl.GetInt32() : (byte)255;
                    param.ColorValue = System.Windows.Media.Color.FromRgb(r, g, b);
                }
                break;
            case NodeParameterKind.Text:
                if (value.ValueKind == JsonValueKind.String)
                    param.TextValue = value.GetString();
                break;
        }
    }

    private static JsonElement GetParamJson(NodeParameterViewModel param)
    {
        var val = param.Kind switch
        {
            NodeParameterKind.Number => (object)param.NumberValue,
            NodeParameterKind.Seed => param.IntValue,
            NodeParameterKind.Integer => param.IntValue,
            NodeParameterKind.Boolean => param.BoolValue,
            NodeParameterKind.Choice => param.SelectedChoice ?? "",
            NodeParameterKind.Color => new { r = param.ColorValue.R, g = param.ColorValue.G, b = param.ColorValue.B },
            NodeParameterKind.Text => param.TextValue ?? "",
            _ => ""
        };
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(val));
    }
}

/// <summary>
/// AI 驱动的配方生成器：支持四种生成方法。
/// </summary>
public static class RecipeGenerator
{
    private static readonly Random _rng = new();

    // ─── 方法1：需求驱动自动规划 ───

    /// <summary>
    /// 根据用户的 CreationSpec 生成初始配方。
    /// 使用启发式规则从 Spec 映射到节点序列。
    /// </summary>
    public static EffectRecipe GenerateFromSpec(CreationSpec spec, int seed)
    {
        var recipe = new EffectRecipe
        {
            Name = spec.Subject.Length > 40 ? spec.Subject[..40] : spec.Subject,
            Description = $"根据用户需求\"{spec.Subject}\"自动生成",
            Seed = seed,
            Tags = new List<string>(spec.Tags),
            Complexity = spec.Complexity
        };

        if (!string.IsNullOrEmpty(spec.Style))
            recipe.Tags.Add(spec.Style);
        if (!string.IsNullOrEmpty(spec.Mood))
            recipe.Tags.Add(spec.Mood);

        _rng.NextDouble(); // use seed to init rng

        // 阶段1：选择基底生成节点
        var baseNode = PickBaseNode(spec, seed);
        recipe.Nodes.Add(baseNode);

        // 阶段2：根据复杂度添加处理节点
        int processCount = Math.Clamp(spec.Complexity / 2, 1, 6);
        string prevId = baseNode.Id;

        for (int i = 0; i < processCount; i++)
        {
            var processor = PickProcessorNode(spec, seed + i * 7, i);
            processor.Y = i * 120 + 60;
            recipe.Nodes.Add(processor);
            recipe.Edges.Add(new RecipeEdge
            {
                From = prevId,
                OutputPort = 0,
                To = processor.Id,
                InputPort = 0
            });
            prevId = processor.Id;
        }

        // 阶段3：如果有风格约束，追加调色板节点
        if (!string.IsNullOrEmpty(spec.Style) || spec.Complexity >= 5)
        {
            var paletteNode = CreatePaletteNode(spec, seed + 999);
            paletteNode.Y = processCount * 120 + 60;
            recipe.Nodes.Add(paletteNode);
            recipe.Edges.Add(new RecipeEdge
            {
                From = prevId,
                OutputPort = 0,
                To = paletteNode.Id,
                InputPort = 0
            });
        }

        return recipe;
    }

    // ─── 方法2：配方突变 ───

    /// <summary>
    /// 对现有配方进行一次随机突变，生成新变体。
    /// 突变类型：替换节点、修改参数、插入节点、删除节点、重连连线。
    /// </summary>
    public static EffectRecipe Mutate(EffectRecipe source, int seed)
    {
        var rng = new Random(seed);
        var clone = DeepClone(source);
        clone.RecipeId = Guid.NewGuid().ToString("N")[..12];
        clone.Seed = seed;

        if (clone.Nodes.Count == 0) return clone;

        double roll = rng.NextDouble();
        int mutationType;

        if (roll < 0.30) mutationType = 0;         // 修改参数
        else if (roll < 0.55) mutationType = 1;    // 替换节点类型
        else if (roll < 0.75) mutationType = 2;    // 插入节点
        else if (roll < 0.90) mutationType = 3;    // 删除节点（至少保留2个）
        else mutationType = 4;                      // 重连连线

        switch (mutationType)
        {
            case 0: // 修改参数
                MutateParameter(clone, rng);
                break;
            case 1: // 替换节点类型
                ReplaceNode(clone, rng);
                break;
            case 2: // 插入节点
                InsertNode(clone, rng);
                break;
            case 3: // 删除节点
                if (clone.Nodes.Count > 2)
                    DeleteNode(clone, rng);
                break;
            case 4: // 重连连线
                RerouteConnection(clone, rng);
                break;
        }

        return clone;
    }

    /// <summary>生成一批突变体</summary>
    public static List<EffectRecipe> GenerateMutations(EffectRecipe source, int count, int baseSeed)
    {
        var results = new List<EffectRecipe>();
        for (int i = 0; i < count; i++)
            results.Add(Mutate(source, baseSeed + i * 31 + 1));
        return results;
    }

    // ─── 方法3：从画布逆向工程 ───

    /// <summary>从当前画布状态创建配方（见 EffectRecipe.FromCanvas）</summary>

    // ─── 方法4：随机漫步 ───

    /// <summary>生成完全随机的配方</summary>
    public static EffectRecipe RandomWalk(int seed, int nodeCount = 4)
    {
        var rng = new Random(seed);
        var recipe = new EffectRecipe
        {
            Name = $"Random Effect #{seed % 10000:D4}",
            Seed = seed,
            Complexity = Math.Clamp(nodeCount, 1, 10)
        };

        var allTypes = GetAllNodeTypes().ToList();
        if (allTypes.Count == 0) return recipe;

        var usedTypes = new List<(string Name, string Category)>();

        // 随机选择 base 节点（偏向 Source/Noise/Pattern 类别）
        var baseCandidates = allTypes.Where(t =>
            t.Category is "Source" or "Noise" or "Pattern" or "Generate").ToList();
        if (baseCandidates.Count == 0) baseCandidates = allTypes;

        var baseType = baseCandidates[rng.Next(baseCandidates.Count)];
        var baseNode = CreateRecipeNode(baseType.Name, baseType.Category, "n1", 0, 0, rng);
        recipe.Nodes.Add(baseNode);
        usedTypes.Add(baseType);

        string prevId = "n1";

        for (int i = 1; i < nodeCount; i++)
        {
            var candidate = allTypes[rng.Next(allTypes.Count)];
            var nodeId = $"n{i + 1}";
            var procNode = CreateRecipeNode(candidate.Name, candidate.Category, nodeId, 0, i * 120, rng);
            recipe.Nodes.Add(procNode);
            recipe.Edges.Add(new RecipeEdge
            {
                From = prevId,
                OutputPort = 0,
                To = nodeId,
                InputPort = 0
            });
            prevId = nodeId;
            usedTypes.Add(candidate);
        }

        // 提取标签
        recipe.Tags = usedTypes.Select(t => t.Category).Distinct().ToList();

        return recipe;
    }

    // ==================== 私有实现 ====================

    private static readonly (string[] Styles, string[] NodeTypes)[] STYLE_NODE_MAP =
    {
        (new[] { "像素", "8bit", "retro" }, new[] { "Pixelate", "PaletteMap", "ColorQuantize", "Posterize" }),
        (new[] { "暗黑", "黑暗", "gothic", "阴森" }, new[] { "Threshold", "Lighting", "Vignette", "ColorAdjust" }),
        (new[] { "赛博朋克", "cyberpunk", "霓虹" }, new[] { "Glow", "ChromaticAberration", "Scanlines", "HslAdjust" }),
        (new[] { "水墨", "国画", "ink" }, new[] { "Blur", "Threshold", "Posterize", "Colorize" }),
        (new[] { "自然", "森林", "草地" }, new[] { "Noise", "Blur", "ColorAdjust", "GradientMap" }),
        (new[] { "发光", "光", "glow" }, new[] { "Glow", "BlendMode", "HslAdjust", "Lighting" }),
        (new[] { "奇幻", "魔法", "magic" }, new[] { "Rune", "Glow", "EnergyField", "Starfield" }),
        (new[] { "复古", "vintage", "旧" }, new[] { "Grayscale", "Vignette", "Scanlines", "ColorQuantize" }),
        (new[] { "科幻", "sci-fi", "机械" }, new[] { "Circuit", "Scanlines", "Hologram", "Lighting" }),
    };

    private static RecipeNode CreateRecipeNode(string type, string category, string id, double x, double y, Random rng)
    {
        var node = new RecipeNode
        {
            Id = id,
            Type = type,
            Category = category,
            X = x,
            Y = y
        };

        // 根据节点类型添加常见参数（随机值）
        switch (type)
        {
            case "Noise":
                node.Params["type"] = Json("perlin");
                node.Params["scale"] = Json(rng.Next(2, 12));
                node.Params["seed"] = Json(rng.Next(0, 9999));
                break;
            case "SolidColor":
                node.Params["color"] = Json(new { r = rng.Next(30, 230), g = rng.Next(30, 230), b = rng.Next(30, 230) });
                break;
            case "Gradient":
                node.Params["type"] = Json("linear");
                node.Params["angle"] = Json(rng.Next(0, 360));
                break;
            case "BlendMode":
                node.Params["mode"] = Json("screen");
                node.Params["opacity"] = Json(0.5 + rng.NextDouble() * 0.5);
                break;
            case "Blur":
                node.Params["radius"] = Json(rng.Next(1, 5));
                break;
            case "HSLAdjust":
                node.Params["hue"] = Json(rng.Next(-180, 180));
                node.Params["saturation"] = Json(rng.NextDouble() * 0.5);
                node.Params["lightness"] = Json((rng.NextDouble() - 0.5) * 0.4);
                break;
            case "Pixelate":
                node.Params["size"] = Json(rng.Next(2, 8));
                break;
            case "ColorQuantize":
                node.Params["colors"] = Json(rng.Next(4, 16));
                break;
            case "Glow":
                node.Params["radius"] = Json(rng.Next(1, 6));
                node.Params["intensity"] = Json(0.3 + rng.NextDouble() * 0.7);
                break;
            case "Threshold":
                node.Params["level"] = Json(0.3 + rng.NextDouble() * 0.4);
                break;
            case "Displace":
            case "Distort":
                node.Params["strength"] = Json(rng.Next(1, 10));
                break;
            case "Sharpen":
                node.Params["amount"] = Json(0.3 + rng.NextDouble() * 1.0);
                break;
            case "Vignette":
                node.Params["intensity"] = Json(0.2 + rng.NextDouble() * 0.6);
                break;
        }

        return node;
    }

    /// <summary>
    /// 材质标签 → 专用基底节点映射表。精确匹配用户需求，避免随机选择。
    /// </summary>
    /// <summary>
    /// 材质标签 → 专用基底节点映射表（中/英文关键词）。
    /// 这是硬编码的语义领域知识库，不依赖UI本地化系统。
    /// </summary>
    private static readonly Dictionary<string, (string Type, string Category)> MaterialBaseNodeMap = new()
    {
        // 砖块/地砖/瓷砖
        ["砖块"] = ("Brick", "Material"), ["brick"] = ("Brick", "Material"),
        ["地砖"] = ("Brick", "Material"), ["floor_tile"] = ("Brick", "Material"),
        ["瓷砖"] = ("Mosaic", "Material"), ["tile"] = ("Mosaic", "Material"),
        ["马赛克"] = ("Mosaic", "Material"), ["mosaic"] = ("Mosaic", "Material"),
        // 石材
        ["石材"] = ("Cobblestone", "Material"), ["stone"] = ("Cobblestone", "Material"),
        ["石头"] = ("Cobblestone", "Material"), ["rock"] = ("Cobblestone", "Material"),
        ["石板"] = ("Flagstone", "Material"), ["flagstone"] = ("Flagstone", "Material"),
        ["碎石"] = ("Flagstone", "Material"), ["cobblestone"] = ("Cobblestone", "Material"),
        ["大理石"] = ("Flagstone", "Material"), ["marble"] = ("Flagstone", "Material"),
        // 地板
        ["地板"] = ("Floor", "Material"), ["floor"] = ("Floor", "Material"),
        ["地面"] = ("Floor", "Material"), ["ground"] = ("Floor", "Material"),
        // 木质
        ["木质"] = ("Wood", "Noise"), ["wood"] = ("Wood", "Noise"),
        ["木头"] = ("Wood", "Noise"),
        // 草地/植被
        ["草地"] = ("Grass", "Material"), ["grass"] = ("Grass", "Material"),
        ["植被"] = ("Grass", "Material"), ["vegetation"] = ("Grass", "Material"),
        // 金属
        ["金属"] = ("Chainmail", "Material"), ["metal"] = ("Chainmail", "Material"),
        // 冰/雪
        ["冰"] = ("Ice", "Material"), ["ice"] = ("Ice", "Material"),
        ["雪地"] = ("Snow", "Material"), ["snow"] = ("Snow", "Material"),
        // 沙地
        ["沙地"] = ("Sand", "Noise"), ["sand"] = ("Sand", "Noise"),
        ["沙漠"] = ("Sand", "Noise"), ["desert"] = ("Sand", "Noise"),
        // 苔藓
        ["苔藓"] = ("Moss", "Material"), ["moss"] = ("Moss", "Material"),
        // 织物
        ["织物"] = ("Fabric", "Material"), ["fabric"] = ("Fabric", "Material"),
        ["布料"] = ("Fabric", "Material"), ["cloth"] = ("Fabric", "Material"),
        // 皮革
        ["皮革"] = ("Leather", "Material"), ["leather"] = ("Leather", "Material"),
        // 水/熔岩
        ["水流"] = ("WaterFlow", "Material"), ["water"] = ("WaterFlow", "Material"),
        ["熔岩"] = ("LavaFlow", "Material"), ["lava"] = ("LavaFlow", "Material"),
        // 藤蔓/水晶
        ["藤蔓"] = ("Weave", "Material"), ["vine"] = ("Weave", "Material"),
        ["水晶"] = ("Crystal", "Material"), ["crystal"] = ("Crystal", "Material"),
        ["宝石"] = ("Crystal", "Material"), ["gem"] = ("Crystal", "Material"),
    };

    private static RecipeNode PickBaseNode(CreationSpec spec, int seed)
    {
        var rng = new Random(seed);

        // 1. 按材质标签精确匹配专用基底节点（最高优先级）
        foreach (var (tag, (type, category)) in MaterialBaseNodeMap)
        {
            if (spec.Tags.Contains(tag))
            {
                var node = CreateRecipeNode(type, category, "n1", 0, 0, rng);
                return ApplyMoodColor(node, spec);
            }
        }

        // 2. 按 subject 文本匹配（兜底：tag 匹配不到时扫描原始输入）
        foreach (var (tag, (type, category)) in MaterialBaseNodeMap)
        {
            if (spec.Subject.Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                var node = CreateRecipeNode(type, category, "n1", 0, 0, rng);
                return ApplyMoodColor(node, spec);
            }
        }

        // 3. 风格/氛围优先级的基底选择
        string baseType;
        string baseCategory;

        if (spec.Tags.Contains("发光") || spec.Tags.Contains("光"))
        {
            baseType = rng.Next(2) == 0 ? "Noise" : "Starfield";
            baseCategory = "Noise";
        }
        else if (spec.Complexity >= 7)
        {
            baseType = new[] { "Noise", "Gradient", "Plasma", "Marble" }[rng.Next(4)];
            baseCategory = "Noise";
        }
        else
        {
            baseType = new[] { "Noise", "SolidColor", "Gradient", "Checkerboard" }[rng.Next(4)];
            baseCategory = baseType == "Noise" ? "Noise" : "Pattern";
        }

        var fallbackNode = CreateRecipeNode(baseType, baseCategory, "n1", 0, 0, rng);
        return ApplyMoodColor(fallbackNode, spec);
    }

    private static RecipeNode ApplyMoodColor(RecipeNode node, CreationSpec spec)
    {
        if (!string.IsNullOrEmpty(spec.Mood) && spec.Mood.Contains("暗"))
            node.Params["color"] = Json(new { r = 20, g = 15, b = 30 });
        return node;
    }

    private static RecipeNode PickProcessorNode(CreationSpec spec, int seed, int index)
    {
        var rng = new Random(seed);
        var nodeId = $"n{index + 2}";

        // 按阶段分配处理节点类型
        var candidates = index switch
        {
            0 => new[] { "Blur", "Displace", "Distort", "Channel", "MathOp" },
            1 => new[] { "HSLAdjust", "ColorAdjust", "Grayscale", "Colorize" },
            2 => new[] { "BlendMode", "Glow", "Lighting", "DropShadow" },
            3 => new[] { "Sharpen", "Pixelate", "Threshold", "Posterize" },
            4 => new[] { "ColorQuantize", "PaletteMap", "GradientMap" },
            _ => new[] { "BlendMode", "HSLAdjust", "Blur", "Glow" }
        };

        var type = candidates[rng.Next(candidates.Length)];
        return CreateRecipeNode(type, "ImageProcess", nodeId, 0, index * 120, rng);
    }

    private static RecipeNode CreatePaletteNode(CreationSpec spec, int seed)
    {
        var rng = new Random(seed);

        // 根据风格选择调色板模式
        string paletteType;
        if (spec.Style.Contains("暗") || spec.Mood.Contains("暗"))
            paletteType = "dark";
        else if (spec.Style.Contains("明") || spec.Mood.Contains("明"))
            paletteType = "bright";
        else if (spec.Tags.Contains("森") || spec.Tags.Contains("自然"))
            paletteType = "nature";
        else if (spec.Tags.Contains("魔") || spec.Tags.Contains("发光"))
            paletteType = "magic";
        else
            paletteType = new[] { "warm", "cool", "mono", "colorful" }[rng.Next(4)];

        var node = new RecipeNode
        {
            Id = "palette",
            Type = "PaletteMap",
            Category = "ImageProcess",
            Params = new Dictionary<string, JsonElement>
            {
                ["palette"] = Json(paletteType),
                ["dither"] = Json(rng.Next(2) == 0 ? "none" : "ordered")
            }
        };
        return node;
    }

    // ─── 突变操作 ───

    private static void MutateParameter(EffectRecipe recipe, Random rng)
    {
        if (recipe.Nodes.Count == 0) return;
        var node = recipe.Nodes[rng.Next(recipe.Nodes.Count)];
        if (node.Params.Count == 0) return;

        var keys = node.Params.Keys.ToList();
        var key = keys[rng.Next(keys.Count)];
        var existing = node.Params[key];

        // 对数值参数进行扰动
        try
        {
            if (existing.ValueKind == JsonValueKind.Number)
            {
                var val = existing.GetDouble();
                var delta = (rng.NextDouble() - 0.5) * val * 0.3;
                node.Params[key] = Json(Math.Max(0, Math.Round(val + delta, 2)));
            }
            else if (existing.ValueKind == JsonValueKind.String)
            {
                // 对枚举参数，可能切换到另一个值
                var knownOptions = new Dictionary<string, string[]>
                {
                    ["mode"] = new[] { "screen", "multiply", "overlay", "add", "subtract" },
                    ["type"] = new[] { "perlin", "simplex", "worley", "white", "voronoi" },
                    ["dither"] = new[] { "none", "ordered", "floyd" },
                    ["palette"] = new[] { "warm", "cool", "dark", "bright", "nature", "magic", "mono" },
                };
                if (knownOptions.TryGetValue(key, out var options))
                {
                    var current = existing.GetString() ?? "";
                    var filtered = options.Where(o => o != current).ToArray();
                    if (filtered.Length > 0)
                        node.Params[key] = Json(filtered[rng.Next(filtered.Length)]);
                }
            }
        }
        catch { }
    }

    private static void ReplaceNode(EffectRecipe recipe, Random rng)
    {
        if (recipe.Nodes.Count == 0) return;
        int idx = rng.Next(recipe.Nodes.Count);
        var old = recipe.Nodes[idx];

        var types = GetAllNodeTypes().Where(t => t.Category != "Tool" && t.Category != "Generate").ToList();
        if (types.Count == 0) return;

        var newType = types[rng.Next(types.Count)];
        var newNode = CreateRecipeNode(newType.Name, newType.Category, old.Id, old.X, old.Y, rng);
        recipe.Nodes[idx] = newNode;
    }

    private static void InsertNode(EffectRecipe recipe, Random rng)
    {
        if (recipe.Edges.Count == 0) return;

        // 选择一条边的中间位置插入
        var edge = recipe.Edges[rng.Next(recipe.Edges.Count)];
        var types = GetAllNodeTypes().Where(t => t.Category is "ImageProcess" or "Noise" or "Material").ToList();
        if (types.Count == 0) return;

        var newType = types[rng.Next(types.Count)];
        var newNodeId = $"n_ins_{recipe.Nodes.Count + 1}";
        var newNode = CreateRecipeNode(newType.Name, newType.Category, newNodeId, 0, 0, rng);

        // 修改连线：原来 from->to 变成 from->newNode->to
        edge.To = newNodeId;
        edge.InputPort = 0;
        recipe.Edges.Add(new RecipeEdge
        {
            From = newNodeId,
            OutputPort = 0,
            To = edge.To,
            InputPort = edge.InputPort
        });

        recipe.Nodes.Add(newNode);
    }

    private static void DeleteNode(EffectRecipe recipe, Random rng)
    {
        if (recipe.Nodes.Count <= 2) return;

        // 不要删除第一个（基底）节点
        int idx = rng.Next(1, recipe.Nodes.Count);
        var nodeId = recipe.Nodes[idx].Id;

        // 重连：所有指向此节点的边跳到被删除节点的输入源
        var incoming = recipe.Edges.Where(e => e.To == nodeId).ToList();
        var outgoing = recipe.Edges.Where(e => e.From == nodeId).ToList();

        recipe.Nodes.RemoveAt(idx);
        recipe.Edges.RemoveAll(e => e.From == nodeId || e.To == nodeId);

        // 如果有 incoming 和 outgoing，将输入连到输出
        foreach (var inc in incoming)
        {
            foreach (var outg in outgoing)
            {
                recipe.Edges.Add(new RecipeEdge
                {
                    From = inc.From,
                    OutputPort = inc.OutputPort,
                    To = outg.To,
                    InputPort = outg.InputPort
                });
            }
        }
    }

    private static void RerouteConnection(EffectRecipe recipe, Random rng)
    {
        if (recipe.Edges.Count < 2 || recipe.Nodes.Count < 3) return;

        var edge = recipe.Edges[rng.Next(recipe.Edges.Count)];
        var candidates = recipe.Nodes
            .Where(n => n.Id != edge.From && n.Id != edge.To)
            .ToList();

        if (candidates.Count == 0) return;

        // 随机将连线重定向到另一个节点
        var newTarget = candidates[rng.Next(candidates.Count)];
        edge.To = newTarget.Id;
        edge.InputPort = 0;
    }

    // ─── 工具方法 ───

    private static List<(string Name, string Category)> GetAllNodeTypes()
    {
        // 从 NodeResourceRegistry 获取所有可用节点类型
        var registry = NodeResourceRegistry.Instance;
        var types = new List<(string Name, string Category)>();

        foreach (var meta in registry.GetAll())
        {
            if (string.IsNullOrEmpty(meta.TypeName)) continue;
            if (meta.TypeName == "Output" || meta.TypeName == "Preview" || meta.TypeName == "Comment")
                continue; // 排除工具节点
            types.Add((meta.TypeName, meta.Category ?? ""));
        }

        // 如果注册表为空，返回内置常见节点作为 fallback
        if (types.Count == 0)
        {
            types.AddRange(new[]
            {
                ("Noise", "Noise"), ("SolidColor", "Pattern"), ("Gradient", "Pattern"),
                ("BlendMode", "ImageProcess"), ("Blur", "ImageProcess"), ("HSLAdjust", "ImageProcess"),
                ("ColorAdjust", "ImageProcess"), ("Pixelate", "ImageProcess"), ("Glow", "ImageProcess"),
                ("Threshold", "ImageProcess"), ("Displace", "ImageProcess"), ("Sharpen", "ImageProcess"),
                ("Vignette", "ImageProcess"), ("Channel", "ImageProcess"), ("Shape", "Pattern"),
                ("Checkerboard", "Pattern"), ("PaletteMap", "ImageProcess"), ("ColorQuantize", "ImageProcess"),
                ("Lighting", "ImageProcess"), ("DropShadow", "ImageProcess"), ("Posterize", "ImageProcess"),
                ("Grayscale", "ImageProcess"), ("Colorize", "ImageProcess"), ("Rune", "Material"),
            });
        }

        return types;
    }

    private static EffectRecipe DeepClone(EffectRecipe source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<EffectRecipe>(json) ?? new EffectRecipe();
    }

    private static JsonElement Json(string value)
    {
        return JsonSerializer.Deserialize<JsonElement>($"\"{value.Replace("\"", "\\\"")}\"");
    }

    private static JsonElement Json(double value)
    {
        return JsonSerializer.Deserialize<JsonElement>(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static JsonElement Json(int value)
    {
        return JsonSerializer.Deserialize<JsonElement>(value.ToString());
    }

    private static JsonElement Json(object obj)
    {
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(obj));
    }
}
