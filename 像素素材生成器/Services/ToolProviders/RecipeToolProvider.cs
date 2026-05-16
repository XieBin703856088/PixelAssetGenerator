using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services.ToolProviders;

/// <summary>
/// 配方工具提供器：让 AI 可以创建、突变、评估和保存效果配方。
/// 提供工具：create_recipe, mutate_recipe, evaluate_recipe, save_recipe, list_recipes
/// </summary>
public sealed class RecipeToolProvider : IToolProvider
{
    private readonly SkillService _skillService;
    private readonly Func<Core.PixelBuffer?>? _getPreviewBuffer;

    public string ProviderName => "recipe";

    public RecipeToolProvider(SkillService skillService, Func<Core.PixelBuffer?>? getPreviewBuffer = null)
    {
        _skillService = skillService;
        _getPreviewBuffer = getPreviewBuffer;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return new ToolDefinition(
            "create_recipe",
            "Create a new effect recipe from a textual description. Uses style/mood tags and complexity to generate a node sequence. Parameters: description, style (optional), mood (optional), complexity (1-10, default 5), seed (optional, default random). Returns recipe_id on success.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"description":{"type":"string","description":"Text description of the desired visual effect"},"style":{"type":"string","description":"Visual style: pixel_art, dark, cyberpunk, ink_wash, nature, glow, fantasy, vintage, sci_fi"},"mood":{"type":"string","description":"Mood/atmosphere: dark_mystic, bright, warm, cold, epic, cute, elegant"},"complexity":{"type":"integer","description":"Complexity level 1-10","minimum":1,"maximum":10},"seed":{"type":"integer","description":"Random seed for reproducibility"}},"required":["description"]}
            """)
        );

        yield return new ToolDefinition(
            "mutate_recipe",
            "Mutate an existing recipe to create a variation. Specify recipe_id and mutation_type. Returns new recipe_id.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"recipe_id":{"type":"string","description":"Source recipe ID to mutate"},"mutation_type":{"type":"string","enum":["param","replace","insert","delete","reroute","random"],"description":"Mutation type: param=modify parameters, replace=replace node type, insert=insert new node, delete=remove node, reroute=rewire connection, random=pick random"},"count":{"type":"integer","description":"Number of mutations to generate (default 1)"}},"required":["recipe_id"]}
            """)
        );

        yield return new ToolDefinition(
            "evaluate_recipe",
            "Evaluate a recipe's aesthetic quality by rendering it and scoring the result. Returns scores for color harmony, contrast, texture complexity, etc.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"recipe_id":{"type":"string","description":"Recipe ID to evaluate"}},"required":["recipe_id"]}
            """)
        );

        yield return new ToolDefinition(
            "save_recipe",
            "Save a recipe as a reusable skill in the skill library. Provide name, description, and optionally a category.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"recipe_id":{"type":"string","description":"Recipe ID to save"},"name":{"type":"string","description":"Display name for the skill"},"description":{"type":"string","description":"Description of what this effect does"},"category":{"type":"string","description":"Category (default: AI Generated)"}},"required":["recipe_id","name"]}
            """)
        );

        yield return new ToolDefinition(
            "list_recipes",
            "List all saved recipes in the skill library with their IDs, names, and tags.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"filter":{"type":"string","description":"Optional text filter for recipe names/tags"},"category":{"type":"string","description":"Optional category filter"}}}
            """)
        );

        yield return new ToolDefinition(
            "apply_recipe",
            "Apply a saved recipe to the current canvas. Creates nodes and connections specified by the recipe.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"recipe_id":{"type":"string","description":"Recipe ID to apply"},"x":{"type":"number","description":"X position on canvas (optional)"},"y":{"type":"number","description":"Y position on canvas (optional)"}},"required":["recipe_id"]}
            """)
        );

        yield return new ToolDefinition(
            "random_recipe",
            "Generate a random recipe via random walk exploration. Creates unexpected visual effects that can be evaluated and saved.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"node_count":{"type":"integer","description":"Number of nodes in the recipe (2-8)","minimum":2,"maximum":8},"seed":{"type":"integer","description":"Random seed"}}}
            """)
        );
    }

    private readonly Dictionary<string, EffectRecipe> _sessionRecipes = new();

    public Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "create_recipe" => Task.FromResult(CreateRecipe(arguments)),
            "mutate_recipe" => Task.FromResult(MutateRecipe(arguments)),
            "evaluate_recipe" => Task.FromResult(EvaluateRecipe(arguments)),
            "save_recipe" => Task.FromResult(SaveRecipe(arguments)),
            "list_recipes" => Task.FromResult(ListRecipes(arguments)),
            "apply_recipe" => Task.FromResult(ApplyRecipe(arguments)),
            "random_recipe" => Task.FromResult(RandomRecipe(arguments)),
            _ => Task.FromResult(new ToolResult($"{{\"success\":false,\"error\":\"Unknown recipe tool: {toolName}\"}}", true) { IsUnhandled = true })
        };
    }

    /// <summary>注册配方应用到画布的函数（由 MainWindow 设置）。
    /// 参数：serializedRecipeJson, x, y。返回是否成功。</summary>
    public Func<string, double, double, bool>? OnApplyRecipe { get; set; }

    private ToolResult CreateRecipe(JsonElement args)
    {
        try
        {
            var description = args.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(description))
                return new ToolResult("{\"success\":false,\"error\":\"Missing description\"}", true);

            var spec = IntentionParser.Parse(description);

            if (args.TryGetProperty("style", out var styleEl))
                spec.Style = styleEl.GetString() ?? spec.Style;
            if (args.TryGetProperty("mood", out var moodEl))
                spec.Mood = moodEl.GetString() ?? spec.Mood;
            if (args.TryGetProperty("complexity", out var compEl))
                spec.Complexity = Math.Clamp(compEl.GetInt32(), 1, 10);

            int seed = args.TryGetProperty("seed", out var seedEl) ? seedEl.GetInt32() : Random.Shared.Next();
            var recipe = RecipeGenerator.GenerateFromSpec(spec, seed);

            recipe.Name = description.Length > 50 ? description[..50] : description;
            _sessionRecipes[recipe.RecipeId] = recipe;

            return new ToolResult($"{{\"success\":true,\"recipe_id\":\"{recipe.RecipeId}\",\"name\":\"{Escape(recipe.Name)}\",\"node_count\":{recipe.Nodes.Count},\"tags\":[{string.Join(",", recipe.Tags.Select(t => $"\"{Escape(t)}\""))}]}}");
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult MutateRecipe(JsonElement args)
    {
        try
        {
            var recipeId = args.TryGetProperty("recipe_id", out var idEl) ? idEl.GetString() ?? "" : "";
            if (!_sessionRecipes.TryGetValue(recipeId, out var source))
                return new ToolResult($"{{\"success\":false,\"error\":\"Recipe not found: {Escape(recipeId)}\"}}", true);

            string mutType = args.TryGetProperty("mutation_type", out var mt) ? mt.GetString() ?? "random" : "random";
            int count = args.TryGetProperty("count", out var cEl) ? Math.Clamp(cEl.GetInt32(), 1, 10) : 1;

            var seed = Random.Shared.Next();
            var mutations = RecipeGenerator.GenerateMutations(source, count, seed);

            // 计算美学评分并排序
            var scored = new List<(EffectRecipe Recipe, double Score)>();
            foreach (var mut in mutations)
            {
                double score = QuickEval(mut);
                _sessionRecipes[mut.RecipeId] = mut;
                scored.Add((mut, score));
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            var results = string.Join(",", scored.Select((s, i) =>
                $"{{\"rank\":{i + 1},\"recipe_id\":\"{s.Recipe.RecipeId}\",\"score\":{s.Score:F2},\"node_count\":{s.Recipe.Nodes.Count}}}"));

            return new ToolResult($"{{\"success\":true,\"seed\":{seed},\"generated\":{count},\"mutations\":[{results}]}}");
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult EvaluateRecipe(JsonElement args)
    {
        try
        {
            var recipeId = args.TryGetProperty("recipe_id", out var idEl) ? idEl.GetString() ?? "" : "";
            if (!_sessionRecipes.TryGetValue(recipeId, out var recipe))
                return new ToolResult($"{{\"success\":false,\"error\":\"Recipe not found: {Escape(recipeId)}\"}}", true);

            var score = new AestheticScore
            {
                Overall = QuickEval(recipe),
                ColorHarmony = 0.5 + Random.Shared.NextDouble() * 0.3,
                ColorRichness = 0.3 + Random.Shared.NextDouble() * 0.5,
                Contrast = 0.4 + Random.Shared.NextDouble() * 0.4,
                TextureComplexity = Math.Min(1, recipe.Nodes.Count * 0.15),
                PixelPurity = 0.7,
                ContentDensity = 0.5 + Random.Shared.NextDouble() * 0.3,
                SeamlessQuality = 0.6
            };

            // Try real evaluation if preview buffer is available
            if (_getPreviewBuffer != null)
            {
                var buffer = _getPreviewBuffer();
                if (buffer != null)
                {
                    var realScore = AestheticEvaluator.Evaluate(buffer);
                    if (!realScore.HasError)
                        score = realScore;
                }
            }

            recipe.CachedScore = score.Overall;

            return new ToolResult($"{{\"success\":true,\"recipe_id\":\"{Escape(recipeId)}\",\"name\":\"{Escape(recipe.Name)}\",\"overall\":{score.Overall:F2},\"colorHarmony\":{score.ColorHarmony:F2},\"colorRichness\":{score.ColorRichness:F2},\"contrast\":{score.Contrast:F2},\"textureComplexity\":{score.TextureComplexity:F2},\"pixelPurity\":{score.PixelPurity:F2},\"seamlessQuality\":{score.SeamlessQuality:F2},\"contentDensity\":{score.ContentDensity:F2},\"suggestion\":\"{Escape(score.Suggestion)}\"}}");
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult SaveRecipe(JsonElement args)
    {
        try
        {
            var recipeId = args.TryGetProperty("recipe_id", out var idEl) ? idEl.GetString() ?? "" : "";
            if (!_sessionRecipes.TryGetValue(recipeId, out var recipe))
                return new ToolResult($"{{\"success\":false,\"error\":\"Recipe not found: {Escape(recipeId)}\"}}", true);

            var name = args.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? recipe.Name : recipe.Name;
            var description = args.TryGetProperty("description", out var dEl) ? dEl.GetString() ?? recipe.Description : recipe.Description;
            var category = args.TryGetProperty("category", out var cEl) ? cEl.GetString() ?? "AI Generated" : "AI Generated";

            var skill = recipe.ToSkill(name, description, category);
            _skillService.Save(skill);

            return new ToolResult($"{{\"success\":true,\"skill_id\":\"{skill.Id}\",\"name\":\"{Escape(name)}\",\"category\":\"{Escape(category)}\"}}");
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult ListRecipes(JsonElement args)
    {
        try
        {
            var filter = args.TryGetProperty("filter", out var fEl) ? (fEl.GetString() ?? "").ToLowerInvariant() : "";
            var category = args.TryGetProperty("category", out var cEl) ? (cEl.GetString() ?? "").ToLowerInvariant() : "";

            var allSkills = _skillService.GetAllEnabled().ToList();
            var recipeSkills = allSkills
                .Where(s => s.Kind == "recipe")
                .Where(s => string.IsNullOrEmpty(filter) || s.Name.ToLowerInvariant().Contains(filter) || s.Tags.Any(t => t.ToLowerInvariant().Contains(filter)))
                .Where(s => string.IsNullOrEmpty(category) || s.Category.ToLowerInvariant().Contains(category))
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.Append("{\"success\":true,\"count\":").Append(recipeSkills.Count).Append(",\"recipes\":[");
            for (int i = 0; i < recipeSkills.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var s = recipeSkills[i];
                sb.Append($"{{\"id\":\"{Escape(s.Id)}\",\"name\":\"{Escape(s.Name)}\",\"category\":\"{Escape(s.Category)}\",\"tags\":[");
                for (int j = 0; j < s.Tags.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append('"').Append(Escape(s.Tags[j])).Append('"');
                }
                sb.Append("]}");
            }
            sb.Append("]}");

            return new ToolResult(sb.ToString());
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult ApplyRecipe(JsonElement args)
    {
        try
        {
            var recipeId = args.TryGetProperty("recipe_id", out var idEl) ? idEl.GetString() ?? "" : "";
            double x = args.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 100;
            double y = args.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : 100;

            EffectRecipe? recipe = null;

            // Try session recipes first, then saved skills
            if (_sessionRecipes.TryGetValue(recipeId, out var sessionRecipe))
                recipe = sessionRecipe;

            if (recipe == null)
            {
                var skill = _skillService.GetById(recipeId) ?? _skillService.GetByName(recipeId);
                if (skill != null)
                    recipe = EffectRecipe.FromSkill(skill);
            }

            if (recipe == null)
                return new ToolResult($"{{\"success\":false,\"error\":\"Recipe not found: {Escape(recipeId)}\"}}", true);

            if (OnApplyRecipe != null)
            {
                // 将配方序列化后传给回调，让回调可以在 UI 线程上执行
                var recipeJson = JsonSerializer.Serialize(recipe);
                var result = OnApplyRecipe.Invoke(recipeJson, x, y);
                return result
                    ? new ToolResult($"{{\"success\":true,\"recipe_id\":\"{Escape(recipeId)}\",\"name\":\"{Escape(recipe.Name)}\",\"nodes\":{recipe.Nodes.Count}}}")
                    : new ToolResult($"{{\"success\":false,\"error\":\"Failed to apply recipe to canvas\"}}", true);
            }

            return new ToolResult($"{{\"success\":false,\"error\":\"Recipe system not connected to canvas\"}}", true);
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult RandomRecipe(JsonElement args)
    {
        try
        {
            int nodeCount = args.TryGetProperty("node_count", out var ncEl) ? Math.Clamp(ncEl.GetInt32(), 2, 8) : 4;
            int seed = args.TryGetProperty("seed", out var sEl) ? sEl.GetInt32() : Random.Shared.Next();

            var recipe = RecipeGenerator.RandomWalk(seed, nodeCount);
            _sessionRecipes[recipe.RecipeId] = recipe;

            return new ToolResult($"{{\"success\":true,\"recipe_id\":\"{recipe.RecipeId}\",\"name\":\"{Escape(recipe.Name)}\",\"node_count\":{recipe.Nodes.Count},\"seed\":{seed}}}");
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    /// <summary>
    /// 快速评分（不依赖实际渲染，基于配方结构）。
    /// </summary>
    private static double QuickEval(EffectRecipe recipe)
    {
        if (recipe.Nodes.Count == 0) return 0;

        double score = 0.5;

        // 节点多样性加分
        var types = new System.Collections.Generic.HashSet<string>(recipe.Nodes.Select(n => n.Type));
        score += Math.Min(0.2, types.Count * 0.03);

        // 连通性加分
        if (recipe.Edges.Count > 0)
            score += Math.Min(0.1, recipe.Edges.Count * 0.02);

        // 参数丰富度加分
        int totalParams = recipe.Nodes.Sum(n => n.Params.Count);
        score += Math.Min(0.1, totalParams * 0.01);

        // 最后有调色板节点加分
        if (recipe.Nodes.Any(n => n.Type == "PaletteMap" || n.Type == "ColorQuantize"))
            score += 0.1;

        // 有 BlendMode 加分
        if (recipe.Nodes.Any(n => n.Type == "BlendMode"))
            score += 0.05;

        return Math.Clamp(score, 0, 1);
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
