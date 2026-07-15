using PixelAssetGenerator;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.Animation.Nodes;
using PixelAssetGenerator.Core.Nodes;
using PixelAssetGenerator.Core.Particles;
using PixelAssetGenerator.Core.Particles.Nodes;
using PixelAssetGenerator.Core.PixelArt;
using PixelAssetGenerator.Services;
using System.IO;

var source = new CountingNode("Source", false);
var middle = new CountingNode("Middle", true);
var output = new CountingNode("Output", true);
var nodes = new[]
{
    new GraphNodeInstance(1, source),
    new GraphNodeInstance(2, middle),
    new GraphNodeInstance(3, output)
};
var edges = new[]
{
    new GraphConnection(1, 0, 2, 0),
    new GraphConnection(2, 0, 3, 0)
};
var evaluator = new NodeGraphEvaluator();
var context = new PixelGraphContext { TileSize = 8, Seed = 7 };

Dispose(evaluator.EvaluateAll(nodes, edges, context));
Assert(evaluator.LastMetrics.EvaluatedNodeCount == 3, "first evaluation should process every node");

Dispose(evaluator.EvaluateAll(nodes, edges, context));
Assert(evaluator.LastMetrics.CacheHitCount == 3, "unchanged graph should be served entirely from cache");
Assert(evaluator.LastMetrics.ReusedExecutionPlan, "unchanged graph should reuse its topology plan");

nodes[1].ParameterValues["gain"] = 0.5;
Dispose(evaluator.EvaluateAll(nodes, edges, context));
Assert(evaluator.LastMetrics.CacheHitCount == 1, "only the unchanged upstream source should hit cache");
Assert(evaluator.LastMetrics.EvaluatedNodeCount == 2, "changed node and downstream node should be recomputed");

var unrelated = new GraphNodeInstance(4, new CountingNode("Unrelated", false));
var targetedEvaluator = new NodeGraphEvaluator();
var targetedResult = targetedEvaluator.Evaluate(nodes.Append(unrelated).ToArray(), edges, context, 3);
targetedResult?.Dispose();
Assert(targetedEvaluator.LastMetrics.NodeCount == 3,
    "target preview should evaluate only the target and its transitive ancestors");

var cycle = GraphValidator.Validate(nodes,
    [new GraphConnection(2, 0, 3, 0), new GraphConnection(3, 0, 2, 0)]);
Assert(!cycle.IsValid && cycle.Diagnostics.Any(diagnostic => diagnostic.Code == "cycle"),
    "cycle validation should fail before evaluation");

var incompatible = new[] { new GraphConnection(1, 0, 2, 0) };
var typedNodes = new[]
{
    new GraphNodeInstance(1, new TypedNode("ColorSource", GraphPortType.Color, false)),
    new GraphNodeInstance(2, new TypedNode("ImageTarget", GraphPortType.Image, true))
};
var typeValidation = GraphValidator.Validate(typedNodes, incompatible);
Assert(typeValidation.Diagnostics.Any(diagnostic => diagnostic.Code == "incompatible_ports"),
    "incompatible port types should be reported");

PixelBufferPool.Clear();
var pooledOnce = PixelBufferPool.Borrow(7, 9);
pooledOnce.Dispose();
var pooledTwice = PixelBufferPool.Borrow(7, 9);
Assert(ReferenceEquals(pooledOnce, pooledTwice), "disposed pixel buffers should be reused");
pooledTwice.Dispose();
var pooledThird = PixelBufferPool.Borrow(7, 9);
Assert(ReferenceEquals(pooledTwice, pooledThird), "reused buffers must be returnable to the pool again");
pooledThird.Dispose();

Assert(GraphNodeRegistry.Create("AlphaTools") is AlphaToolsNode,
    "V3 practical nodes should be discoverable through the registry");
foreach (var typeName in new[] { "ColorReplace", "MaskMorphology", "DistanceField", "SpriteExtrude", "PixelArtStyle" })
    Assert(GraphNodeRegistry.Create(typeName) != null, $"{typeName} should be discoverable through the registry");
Assert(GraphNodeRegistry.Create("Grass") is PixelGrassNode, "Grass should use the discrete pixel material processor");
Assert(GraphNodeRegistry.Create("Rock") is PixelRockNode, "Rock should use the discrete pixel material processor");
Assert(GraphNodeRegistry.Create("Cobblestone") is PixelCobblestoneNode,
    "Cobblestone should use the discrete pixel material processor");
Assert(GraphNodeRegistry.Create("WaterFlow") is PixelWaterFlowNode,
    "WaterFlow should use the discrete pixel material processor");
Assert(GraphNodeRegistry.Create("LavaFlow") is PixelLavaFlowNode,
    "LavaFlow should use the discrete pixel material processor");
Assert(GraphNodeRegistry.Create("Tree") is PixelTreeNode,
    "Tree should use the layered RPG sprite processor");
Assert(GraphNodeRegistry.Create("Bush") is PixelBushNode,
    "Bush should use the hard-edge RPG sprite processor");
Assert(GraphNodeRegistry.Create("Mushroom") is PixelMushroomNode,
    "Mushroom should use the hard-edge RPG sprite processor");
Assert(GraphNodeRegistry.Create("Fabric") is PixelFabricNode,
    "Fabric should use the discrete over-under weave processor");
Assert(GraphNodeRegistry.Create("Leather") is PixelLeatherNode,
    "Leather should use the clustered RPG surface processor");
Assert(GraphNodeRegistry.Create("Floor") is PixelFloorNode,
    "Floor should use the structural RPG region processor");
Assert(GraphNodeRegistry.Create("Flagstone") is PixelFlagstoneNode,
    "Flagstone should use the staggered slab processor");
Assert(GraphNodeRegistry.Create("Wall") is PixelWallNode,
    "Wall should use the dedicated masonry/plaster processor");
Assert(GraphNodeRegistry.Create("Chainmail") is PixelChainmailNode,
    "Chainmail should use the discrete interlocking-ring processor");
Assert(GraphNodeRegistry.Create("Lightning") is PixelLightningNode,
    "Lightning should use the hard-edge branched bolt processor");
Assert(GraphNodeRegistry.Create("Slime") is PixelSlimeNode,
    "Slime should use the transparent RPG character processor");
Assert(GraphNodeRegistry.Create("Icon") is PixelIconNode,
    "Icon should use the hard-edge UI sprite processor");
Assert(GraphNodeRegistry.Create("Rain") is PixelRainNode,
    "Rain should use the animated hard-edge weather processor");
Assert(GraphNodeRegistry.Create("Marble") is PixelMarbleNode,
    "Marble should use the discrete mineral-vein processor");
Assert(GraphNodeRegistry.Create("Scales") is PixelScalesNode,
    "Scales should use the staggered shield-scale processor");
Assert(GraphNodeRegistry.Create("Rust") is PixelRustNode,
    "Rust should use the clustered corrosion processor");
Assert(GraphNodeRegistry.Create("Circuit") is PixelCircuitNode,
    "Circuit should use the orthogonal routed-trace processor");
Assert(GraphNodeRegistry.Create("Honeycomb") is PixelHoneycombNode,
    "Honeycomb should use the hex-neighbor wall processor");
Assert(GraphNodeRegistry.Create("Fence") is PixelFenceNode,
    "Fence should use the transparent RPG prop processor");
Assert(GraphNodeRegistry.Create("Smoke") is PixelSmokeNode,
    "Smoke should use the layered pixel plume processor");
Assert(GraphNodeRegistry.Create("AnimatedTransform") is AnimatedTransformNode,
    "AnimatedTransform should be discoverable through the registry");
Assert(GraphNodeRegistry.Create("Pixelate") is PixelateNode,
    "Pixelate should use the discrete block processor");
Assert(GraphNodeRegistry.Create("ColorQuantize") is ColorQuantizeNode,
    "ColorQuantize should use the adaptive limited-palette processor");
Assert(GraphNodeRegistry.Create("Outline") is PixelOutlineNode,
    "Outline should use the crisp morphology processor");
Assert(GraphNodeRegistry.Create("Lighting") is PixelLightingNode,
    "Lighting should use band-limited pixel lighting");
Assert(PixelArtStyleProfile.ForLegacyNode("Material", "Grass", 32).Enabled,
    "legacy visual material nodes should use the shared pixel-art compatibility layer");
Assert(!PixelArtStyleProfile.ForLegacyNode("Pattern", "SpriteSheet", 32).Enabled,
    "technical sprite data nodes must not be recolored by the compatibility layer");
var scriptInstanceA = GraphNodeRegistry.Create("Threshold");
var scriptInstanceB = GraphNodeRegistry.Create("Threshold");
Assert(scriptInstanceA != null && scriptInstanceB != null && !ReferenceEquals(scriptInstanceA, scriptInstanceB),
    "script nodes should have isolated runtime wrappers");

var alphaViewModel = new NodeViewModel("Alpha Tools", 0, 0) { TypeName = "AlphaTools" };
foreach (var definition in new AlphaToolsNode().Parameters)
    alphaViewModel.Parameters.Add(definition.CreateViewModel());
using (var snapshot = GraphRuntimeSnapshotBuilder.Build([alphaViewModel], [], 8, null))
{
    Assert(snapshot.Instances.Count == 1 && snapshot.InstanceMap.ContainsKey(alphaViewModel.Id),
        "runtime snapshot should project editable nodes through one shared path");
    Assert(snapshot.Instances[0].ParameterValues.ContainsKey("mode"),
        "runtime snapshot should copy canonical parameter values");
    var editingValidation = GraphValidator.Validate(snapshot.Instances, snapshot.Connections);
    var strictValidation = GraphValidator.Validate(snapshot.Instances, snapshot.Connections, requireCompleteGraph: true);
    Assert(editingValidation.IsValid && !strictValidation.IsValid,
        "incomplete nodes should remain previewable but fail strict AI graph validation");
}

using (var image = PixelBuffer.CreateSolid(3, 3, 1, 0, 0, 1))
using (var mask = PixelBuffer.CreateGrayscale(3, 3, 0.5f))
using (var alphaResult = new AlphaToolsNode().Process([image, mask],
           new Dictionary<string, object> { ["mode"] = "applyMask", ["amount"] = 1d },
           new PixelGraphContext { TileSize = 3 }))
{
    AssertNear(alphaResult.GetPixel(1, 1).A, 0.5f, "AlphaTools should apply mask values");
}

using (var image = PixelBuffer.CreateSolid(2, 2, 1, 0, 1, 1))
using (var replaced = new ColorReplaceNode().Process([image], new Dictionary<string, object>
       {
           ["sourceColor"] = System.Windows.Media.Colors.Magenta,
           ["targetColor"] = System.Windows.Media.Colors.Cyan,
           ["tolerance"] = 0.1d,
           ["softness"] = 0.01d,
           ["amount"] = 1d
       }, new PixelGraphContext { TileSize = 2 }))
{
    var pixel = replaced.GetPixel(0, 0);
    AssertNear(pixel.R, 0, "ColorReplace should replace the selected color");
    AssertNear(pixel.G, 1, "ColorReplace should replace the selected color");
}

using (var mask = PixelBuffer.CreateGrayscale(3, 3, 0))
{
    mask.SetPixel(1, 1, 1, 1, 1, 1);
    using var dilated = new MaskMorphologyNode().Process([mask], new Dictionary<string, object>
    {
        ["operation"] = "dilate", ["radius"] = 1, ["iterations"] = 1, ["shape"] = "diamond"
    }, new PixelGraphContext { TileSize = 3 });
    var activePixels = 0;
    for (var y = 0; y < 3; y++)
    for (var x = 0; x < 3; x++)
        if (dilated.GetValue(x, y) > 0.9f) activePixels++;
    Assert(activePixels == 5, "diamond dilation radius 1 should activate five pixels");
}

using (var mask = PixelBuffer.CreateGrayscale(5, 5, 0))
{
    mask.SetPixel(2, 2, 1, 1, 1, 1);
    using var distance = new DistanceFieldNode().Process([mask], new Dictionary<string, object>
    {
        ["mode"] = "signed", ["threshold"] = 0.5d, ["maxDistance"] = 4d
    }, new PixelGraphContext { TileSize = 5 });
    Assert(distance.GetValue(2, 2) > 0.5f && distance.GetValue(0, 0) < 0.5f,
        "signed distance should distinguish inside and outside");
}

using (var sprite = PixelBuffer.CreateSolid(3, 3, 0, 0, 0, 0))
{
    sprite.SetPixel(1, 1, 1, 0, 0, 1);
    using var extruded = new SpriteExtrudeNode().Process([sprite], new Dictionary<string, object>
    {
        ["radius"] = 1, ["alphaThreshold"] = 0.01d, ["extendAlpha"] = false
    }, new PixelGraphContext { TileSize = 3 });
    var adjacent = extruded.GetPixel(1, 0);
    AssertNear(adjacent.R, 1, "SpriteExtrude should copy edge RGB into transparent neighbors");
    AssertNear(adjacent.A, 0, "SpriteExtrude should preserve alpha by default");
}

using (var noisy = PixelBuffer.CreateSolid(5, 5, 0.8f, 0.12f, 0.12f, 1f))
{
    noisy.SetPixel(2, 2, 0.1f, 0.9f, 0.15f, 1f);
    using var stylized = new PixelArtStyleNode().Process([noisy], new Dictionary<string, object>
    {
        ["paletteSize"] = 4,
        ["minimumClusterSize"] = 2,
        ["contrast"] = 1d,
        ["saturation"] = 1d,
        ["dither"] = "none",
        ["ditherStrength"] = 0d,
        ["preserveAlpha"] = true
    }, new PixelGraphContext { TileSize = 5 });
    Assert(CountUniqueColors(stylized) == 1,
        "pixel-art cluster cleanup should remove isolated one-pixel color noise");
}

foreach (var material in new IGraphNode[]
         {
             new PixelGrassNode(), new PixelCobblestoneNode(), new PixelFabricNode(), new PixelLeatherNode(),
             new PixelWaterFlowNode(), new PixelLavaFlowNode()
         })
{
    using var materialResult = material.Process([], DefaultParameters(material), new PixelGraphContext
    {
        TileSize = 32,
        Seed = 42
    });
    var uniqueColors = CountUniqueColors(materialResult);
    Assert(uniqueColors >= 2 && uniqueColors <= 8,
        $"{material.TypeName} should use a deliberate limited palette, got {uniqueColors} colors");
}

foreach (var tileSize in new[] { 32, 64 })
foreach (var material in new IGraphNode[]
         {
             new PixelGrassNode(), new PixelCobblestoneNode(), new PixelFabricNode(), new PixelLeatherNode()
         })
{
    using var tile = material.Process([], DefaultParameters(material),
        new PixelGraphContext { TileSize = tileSize, Seed = 42 });
    Assert(CountUniqueColors(tile) is >= 3 and <= 8,
        $"{material.TypeName} {tileSize}px should stay within an intentional RPG tile palette");
    Assert(IsFullyOpaque(tile), $"{material.TypeName} should produce a fully covered ground/surface tile");
    var (seamEnergy, interiorEnergy) = MeasureTileSeam(tile);
    Assert(seamEnergy <= interiorEnergy * 3.2f + 0.08f,
        $"{material.TypeName} {tileSize}px should not introduce a strong visible repeat seam");
}

using (var rock = new PixelRockNode().Process([], DefaultParameters(new PixelRockNode()),
           new PixelGraphContext { TileSize = 32, Seed = 42 }))
{
    var opaque = 0;
    var transparent = 0;
    for (var y = 0; y < rock.Height; y++)
    for (var x = 0; x < rock.Width; x++)
    {
        if (rock.GetPixel(x, y).A > 0.5f) opaque++; else transparent++;
    }
    Assert(opaque > 0 && transparent > 0, "Rock should produce a readable sprite silhouette with transparent background");
}

foreach (var spriteSize in new[] { 32, 64 })
foreach (var spriteNode in new IGraphNode[] { new PixelTreeNode(), new PixelBushNode(), new PixelMushroomNode() })
{
    var defaults = DefaultParameters(spriteNode);
    var spriteContext = new PixelGraphContext { TileSize = spriteSize, Seed = 42 };
    using var first = spriteNode.Process([], defaults, spriteContext);
    using var second = spriteNode.Process([], defaults, spriteContext);
    var colors = CountUniqueColors(first);
    var bounds = OpaqueBounds(first);
    Assert(colors >= 4 && colors <= 10,
        $"{spriteNode.TypeName} {spriteSize}px should use a deliberate sprite palette, got {colors} colors");
    Assert(bounds.Opaque > spriteSize * spriteSize / 30 && bounds.Transparent > 0,
        $"{spriteNode.TypeName} {spriteSize}px should have a readable opaque silhouette on transparency");
    Assert(bounds.MaxY >= spriteSize * 3 / 4,
        $"{spriteNode.TypeName} {spriteSize}px should visually contact the lower ground region");
    Assert(HasBinaryAlpha(first),
        $"{spriteNode.TypeName} {spriteSize}px should not introduce soft anti-aliased edge alpha");
    Assert(BuffersEqual(first, second),
        $"{spriteNode.TypeName} {spriteSize}px should be deterministic for a fixed seed");
}

foreach (var treeType in new[] { "broadleaf", "pine", "layered", "palm", "dead" })
{
    var tree = new PixelTreeNode();
    var parameters = DefaultParameters(tree);
    parameters["treeType"] = treeType;
    using var sprite = tree.Process([], parameters, new PixelGraphContext { TileSize = 32, Seed = 17 });
    var bounds = OpaqueBounds(sprite);
    Assert(bounds.Opaque > 20 && bounds.Transparent > 0,
        $"Tree style '{treeType}' should produce a non-empty RPG sprite silhouette");
}

foreach (var spriteSize in new[] { 32, 64 })
foreach (var spriteNode in new IGraphNode[] { new PixelLightningNode(), new PixelSlimeNode(), new PixelIconNode() })
{
    var parameters = DefaultParameters(spriteNode);
    using var first = spriteNode.Process([], parameters,
        new PixelGraphContext { TileSize = spriteSize, Seed = 42 });
    using var second = spriteNode.Process([], parameters,
        new PixelGraphContext { TileSize = spriteSize, Seed = 42 });
    var bounds = OpaqueBounds(first);
    Assert(bounds.Opaque > 10 && bounds.Transparent > 0,
        $"{spriteNode.TypeName} {spriteSize}px should produce a readable transparent sprite");
    Assert(CountUniqueColors(first) is >= 3 and <= 10,
        $"{spriteNode.TypeName} {spriteSize}px should use a compact sprite palette");
    Assert(HasBinaryAlpha(first), $"{spriteNode.TypeName} should keep hard binary edge alpha");
    Assert(BuffersEqual(first, second), $"{spriteNode.TypeName} should be deterministic for a fixed seed");
}

foreach (var tileSize in new[] { 32, 64 })
foreach (var material in new IGraphNode[] { new PixelFlagstoneNode(), new PixelChainmailNode() })
{
    using var tile = material.Process([], DefaultParameters(material),
        new PixelGraphContext { TileSize = tileSize, Seed = 42 });
    Assert(IsFullyOpaque(tile), $"{material.TypeName} should fully cover a material tile");
    Assert(CountUniqueColors(tile) is >= 3 and <= 8,
        $"{material.TypeName} should use a compact RPG material palette");
    var (seamEnergy, interiorEnergy) = MeasureTileSeam(tile);
    Assert(seamEnergy <= interiorEnergy * 3.2f + 0.08f,
        $"{material.TypeName} should not introduce a strong repeat seam");
}

foreach (var regionNode in new IGraphNode[] { new PixelFloorNode(), new PixelWallNode() })
{
    var parameters = DefaultParameters(regionNode);
    using var transparentRegion = regionNode.Process([], parameters,
        new PixelGraphContext { TileSize = 32, Seed = 42 });
    var bounds = OpaqueBounds(transparentRegion);
    Assert(bounds.Opaque > 0 && bounds.Transparent > 0,
        $"{regionNode.TypeName} should preserve its configurable transparent region bounds");
    using var background = PixelBuffer.CreateSolid(32, 32, 0.1f, 0.2f, 0.3f, 1f);
    using var composed = regionNode.Process([background], parameters,
        new PixelGraphContext { TileSize = 32, Seed = 42 });
    Assert(IsFullyOpaque(composed), $"{regionNode.TypeName} should composite cleanly over its background input");
}

using (var water = new PixelWaterFlowNode().Process([], DefaultParameters(new PixelWaterFlowNode()),
           new PixelGraphContext { TileSize = 32, Seed = 42, AnimationTime = 0.25f }))
{
    Assert(CountUniqueColors(water) is >= 3 and <= 7, "WaterFlow should use discrete directional color bands");
    var alpha = water.GetPixel(0, 0).A;
    AssertNear(alpha, 0.8f, "WaterFlow should preserve its opacity parameter");
    var (seamEnergy, interiorEnergy) = MeasureTileSeam(water);
    Assert(seamEnergy <= interiorEnergy * 3.2f + 0.08f,
        "WaterFlow should remain seamless across tile boundaries");
}

foreach (var iconType in new[] { "heart", "star", "gear", "mapMarker", "trophy", "skull" })
{
    var icon = new PixelIconNode();
    var parameters = DefaultParameters(icon);
    parameters["iconType"] = iconType;
    using var sprite = icon.Process([], parameters, new PixelGraphContext { TileSize = 32, Seed = 42 });
    Assert(OpaqueBounds(sprite).Opaque > 12, $"Icon style '{iconType}' should produce a visible silhouette");
}

foreach (var floorType in new[] { "planks", "herringbone", "stoneTile", "carpet" })
{
    var floor = new PixelFloorNode();
    var parameters = DefaultParameters(floor);
    parameters["floorType"] = floorType;
    parameters["width"] = 1d;
    parameters["height"] = 1d;
    using var tile = floor.Process([], parameters, new PixelGraphContext { TileSize = 32, Seed = 42 });
    Assert(IsFullyOpaque(tile) && CountUniqueColors(tile) >= 3,
        $"Floor style '{floorType}' should produce a complete structured tile");
}

foreach (var wallType in new[] { "stone", "plaster", "planks", "adobe" })
{
    var wall = new PixelWallNode();
    var parameters = DefaultParameters(wall);
    parameters["wallType"] = wallType;
    parameters["width"] = 1d;
    parameters["height"] = 1d;
    using var tile = wall.Process([], parameters, new PixelGraphContext { TileSize = 32, Seed = 42 });
    Assert(IsFullyOpaque(tile) && CountUniqueColors(tile) >= 3,
        $"Wall style '{wallType}' should produce a complete structured tile");
}

var lightningNode = new PixelLightningNode();
var lightningParameters = DefaultParameters(lightningNode);
using (var frameA = lightningNode.Process([], lightningParameters,
           new PixelGraphContext { TileSize = 32, Seed = 42 }))
{
    lightningParameters["time"] = 0.5d;
    using var frameB = lightningNode.Process([], lightningParameters,
        new PixelGraphContext { TileSize = 32, Seed = 42 });
    Assert(!BuffersEqual(frameA, frameB), "Lightning time should rebuild the bolt path for animation");
}

foreach (var tileSize in new[] { 32, 64 })
foreach (var material in new IGraphNode[]
         {
             new PixelMarbleNode(), new PixelScalesNode(), new PixelRustNode(),
             new PixelCircuitNode(), new PixelHoneycombNode()
         })
{
    using var tile = material.Process([], DefaultParameters(material),
        new PixelGraphContext { TileSize = tileSize, Seed = 42 });
    Assert(IsFullyOpaque(tile), $"{material.TypeName} should fully cover a material tile");
    Assert(CountUniqueColors(tile) is >= 3 and <= 8,
        $"{material.TypeName} {tileSize}px should use a compact RPG material palette");
    var (seamEnergy, interiorEnergy) = MeasureTileSeam(tile);
    Assert(seamEnergy <= interiorEnergy * 3.5f + 0.1f,
        $"{material.TypeName} {tileSize}px should not introduce a strong repeat seam");
}

foreach (var spriteSize in new[] { 32, 64 })
foreach (var spriteNode in new IGraphNode[] { new PixelRainNode(), new PixelFenceNode() })
{
    var parameters = DefaultParameters(spriteNode);
    using var first = spriteNode.Process([], parameters,
        new PixelGraphContext { TileSize = spriteSize, Seed = 42 });
    using var second = spriteNode.Process([], parameters,
        new PixelGraphContext { TileSize = spriteSize, Seed = 42 });
    var bounds = OpaqueBounds(first);
    Assert(bounds.Opaque > 12 && bounds.Transparent > 0,
        $"{spriteNode.TypeName} {spriteSize}px should produce a readable transparent effect or prop");
    Assert(CountUniqueColors(first) is >= 3 and <= 10,
        $"{spriteNode.TypeName} should use a compact sprite palette");
    Assert(HasBinaryAlpha(first), $"{spriteNode.TypeName} should keep hard binary edge alpha");
    Assert(BuffersEqual(first, second), $"{spriteNode.TypeName} should be deterministic for a fixed seed");
}

var rainNode = new PixelRainNode();
var rainParameters = DefaultParameters(rainNode);
using (var frameA = rainNode.Process([], rainParameters,
           new PixelGraphContext { TileSize = 32, Seed = 42, AnimationTime = 0f }))
using (var frameB = rainNode.Process([], rainParameters,
           new PixelGraphContext { TileSize = 32, Seed = 42, AnimationTime = 0.5f }))
{
    Assert(!BuffersEqual(frameA, frameB), "Rain animation time should move the streak field");
}

foreach (var topShape in new[] { "flat", "pointed", "rounded", "vShape" })
{
    var fence = new PixelFenceNode();
    var parameters = DefaultParameters(fence);
    parameters["topShape"] = topShape;
    using var sprite = fence.Process([], parameters, new PixelGraphContext { TileSize = 32, Seed = 42 });
    Assert(OpaqueBounds(sprite).Opaque > 40,
        $"Fence top style '{topShape}' should produce a visible prop silhouette");
}

foreach (var corrosionType in new[] { "ironRust", "patina", "mixed" })
{
    var rust = new PixelRustNode();
    var parameters = DefaultParameters(rust);
    parameters["corrosionType"] = corrosionType;
    using var tile = rust.Process([], parameters, new PixelGraphContext { TileSize = 32, Seed = 42 });
    Assert(CountUniqueColors(tile) >= 4,
        $"Rust mode '{corrosionType}' should preserve metal and layered corrosion colors");
}

using (var playback = new AnimationPlaybackService { FrameCount = 8, FrameRate = 12 })
{
    playback.CurrentFrame = 7;
    AssertNear(playback.NormalizedTime, 0.875f,
        "loop playback should not duplicate normalized time 1 at the seam");
    playback.PlayMode = AnimationPlayMode.PingPong;
    AssertNear(playback.NormalizedTime, 1f,
        "ping-pong playback should reach both endpoints");
    AssertNear(playback.CurrentTimeSeconds, 7f / 12f,
        "playback should expose elapsed seconds for time-driven nodes");
}

var timeNode = new TimeNode();
var timeOutputs = timeNode.ProcessMulti([], DefaultParameters(timeNode),
    new PixelGraphContext { TileSize = 1, AnimationTime = 0.5f, AnimationFrame = 4, AnimationFrameCount = 8 });
try
{
    Assert(timeOutputs.Length == 3, "Time should expose independent Time, Frame and Speed buffers");
    AssertNear(timeOutputs[0].GetPixel(0, 0).R, 0.5f, "Time output should carry normalized time");
    Assert(timeOutputs[1].GetPixel(0, 0).R != timeOutputs[0].GetPixel(0, 0).R,
        "Time multi-output ports must not all alias port zero");
}
finally
{
    foreach (var outputBuffer in timeOutputs) outputBuffer.Dispose();
}

var waveNode = new AnimationWaveNode();
var waveParameters = DefaultParameters(waveNode);
using (var waveStart = waveNode.Process([], waveParameters,
           new PixelGraphContext { TileSize = 1, GlobalTime = 0f }))
using (var waveQuarter = waveNode.Process([], waveParameters,
           new PixelGraphContext { TileSize = 1, GlobalTime = 0.25f }))
{
    Assert(!BuffersEqual(waveStart, waveQuarter), "Wave should react to accumulated global time");
}

using (var compactBuffer = new ParticleBuffer(3))
{
    var particles = compactBuffer.AsSpan();
    particles[0] = TestParticle(0.2f, 0.5f);
    particles[1] = ParticleData.Dead();
    particles[2] = TestParticle(0.8f, 0.5f);
    compactBuffer.ActiveCount = 3;
    new ParticleSimulator { GravityY = 0f, Damping = 1f }.Update(0.01f, compactBuffer);
    Assert(compactBuffer.ActiveCount == 2,
        "particle compaction should update ActiveCount after dead particles are removed");
}

using (var additiveBuffer = PixelBuffer.CreateSolid(16, 16, 0f, 0f, 0f, 0f))
using (var particleBuffer = new ParticleBuffer(1))
{
    particleBuffer.AsSpan()[0] = TestParticle(0.5f, 0.5f, 0.35f);
    particleBuffer.ActiveCount = 1;
    new ParticleRenderer
    {
        TextureType = ParticleTextureType.PixelCircle,
        BlendMode = ParticleBlendMode.Additive,
        PixelSnap = true
    }.Render(particleBuffer.ActiveSpan(), additiveBuffer, 1);
    Assert(OpaqueBounds(additiveBuffer).Opaque > 0,
        "additive particle rendering should write alpha on a transparent background");
}

var emitterInstance = new GraphNodeInstance(101, new ParticleEmitterNode());
var forceInstance = new GraphNodeInstance(102, new ParticleForceNode());
var renderInstance = new GraphNodeInstance(103, new ParticleRenderNode());
emitterInstance.ParameterValues["burstCount"] = 12;
emitterInstance.ParameterValues["gravity"] = 0d;
forceInstance.ParameterValues["forceType"] = "wind";
forceInstance.ParameterValues["strength"] = 0.15d;
renderInstance.ParameterValues["texture"] = "smoke";
var particleInstances = new[] { emitterInstance, forceInstance, renderInstance };
var particleConnections = new[]
{
    new GraphConnection(101, 0, 102, 0),
    new GraphConnection(102, 0, 103, 2)
};
var particleRuntime = new ParticleEvaluationService();
var particleEvaluator = new NodeGraphEvaluator();
Dictionary<int, PixelBuffer>? lastParticleResults = null;
for (var frame = 0; frame < 6; frame++)
{
    lastParticleResults = particleEvaluator.EvaluateAll(particleInstances, particleConnections,
        new PixelGraphContext
        {
            TileSize = 32, Seed = 42, AnimationFrame = frame, AnimationFrameCount = 8,
            AnimationTime = frame / 8f, GlobalTime = frame / 12f, DeltaTime = 1f / 12f
        });
    particleRuntime.SimulateParticleFrame(particleInstances, particleConnections,
        new PixelGraphContext
        {
            TileSize = 32, Seed = 42, AnimationFrame = frame, AnimationFrameCount = 8,
            AnimationTime = frame / 8f, GlobalTime = frame / 12f, DeltaTime = 1f / 12f
        });
    particleRuntime.SaveState(particleInstances);
    if (frame < 5)
    {
        Dispose(lastParticleResults);
        lastParticleResults = null;
    }
}
try
{
    particleRuntime.RenderParticles(particleInstances, particleConnections, lastParticleResults!);
    Assert(OpaqueBounds(lastParticleResults![103]).Opaque > 0,
        "a connected emitter-force-render graph should produce visible particles");
    var emitterState = (ParticleEmitterNode.EmitterState)emitterInstance.PersistentState!;
    Assert(emitterState.Simulator.Forces.Count == 1,
        "the same particle force should be rebuilt once per frame instead of accumulating");
}
finally
{
    if (lastParticleResults != null) Dispose(lastParticleResults);
    particleRuntime.ClearState();
}

foreach (var smokeSize in new[] { 32, 64 })
foreach (var smokeType in new[] { "thick", "mist", "steam", "dust" })
{
    var smoke = new PixelSmokeNode();
    var parameters = DefaultParameters(smoke);
    parameters["smokeType"] = smokeType;
    using var first = smoke.Process([], parameters,
        new PixelGraphContext { TileSize = smokeSize, Seed = 42, AnimationTime = 0.15f });
    using var repeated = smoke.Process([], parameters,
        new PixelGraphContext { TileSize = smokeSize, Seed = 42, AnimationTime = 0.15f });
    using var later = smoke.Process([], parameters,
        new PixelGraphContext { TileSize = smokeSize, Seed = 42, AnimationTime = 0.65f });
    var visiblePixels = CountVisiblePixels(first, 0.05f);
    Assert(visiblePixels > smokeSize / 2 && visiblePixels < smokeSize * smokeSize - smokeSize,
        $"Smoke '{smokeType}' {smokeSize}px should form a readable transparent silhouette");
    Assert(CountUniqueColors(first) is >= 3 and <= 20,
        $"Smoke '{smokeType}' should use a compact pixel-art palette");
    Assert(BuffersEqual(first, repeated),
        $"Smoke '{smokeType}' should be deterministic for a fixed seed and time");
    Assert(!BuffersEqual(first, later),
        $"Smoke '{smokeType}' should visibly evolve over animation time");
}

var spritePreviewPath = Environment.GetEnvironmentVariable("PIXEL_SPRITE_QA_PATH");
if (!string.IsNullOrWhiteSpace(spritePreviewPath))
    ExportSpritePreview(spritePreviewPath);

var animationPreviewPath = Environment.GetEnvironmentVariable("PIXEL_ANIMATION_QA_PATH");
if (!string.IsNullOrWhiteSpace(animationPreviewPath))
    ExportAnimationPreview(animationPreviewPath);

Console.WriteLine("Graph core smoke tests passed.");

static void Dispose(Dictionary<int, PixelBuffer> buffers)
{
    foreach (var buffer in buffers.Values) buffer.Dispose();
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertNear(float actual, float expected, string message)
{
    if (Math.Abs(actual - expected) > 0.001f)
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}

static ParticleData TestParticle(float x, float y, float size = 0.12f)
    => ParticleData.Create(x, y, 0f, 0f, 10f, size, 0f, 0f,
        1f, 1f, 1f, 1f, 1f, 1f, 1f, 0f, size, size);

static int CountVisiblePixels(PixelBuffer buffer, float alphaThreshold)
{
    var count = 0;
    for (var y = 0; y < buffer.Height; y++)
    for (var x = 0; x < buffer.Width; x++)
        if (buffer.GetPixel(x, y).A > alphaThreshold)
            count++;
    return count;
}

static Dictionary<string, object> DefaultParameters(IGraphNode node)
{
    var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    foreach (var definition in node.Parameters)
    {
        result[definition.Name] = definition.Kind switch
        {
            NodeParameterKind.Seed or NodeParameterKind.Integer => definition.DefaultInt,
            NodeParameterKind.Boolean => definition.DefaultBool,
            NodeParameterKind.Choice => definition.DefaultChoice ?? definition.Choices.FirstOrDefault() ?? "",
            NodeParameterKind.Color => definition.DefaultColor,
            NodeParameterKind.Text => definition.DefaultText,
            _ => definition.DefaultNumber
        };
    }
    return result;
}

static int CountUniqueColors(PixelBuffer buffer)
{
    var colors = new HashSet<int>();
    for (var y = 0; y < buffer.Height; y++)
    for (var x = 0; x < buffer.Width; x++)
    {
        var (r, g, b, a) = buffer.GetPixel(x, y);
        var key = ((int)MathF.Round(Math.Clamp(r, 0f, 1f) * 255f) << 24)
                  | ((int)MathF.Round(Math.Clamp(g, 0f, 1f) * 255f) << 16)
                  | ((int)MathF.Round(Math.Clamp(b, 0f, 1f) * 255f) << 8)
                  | (int)MathF.Round(Math.Clamp(a, 0f, 1f) * 255f);
        colors.Add(key);
    }
    return colors.Count;
}

static bool HasBinaryAlpha(PixelBuffer buffer)
{
    for (var y = 0; y < buffer.Height; y++)
    for (var x = 0; x < buffer.Width; x++)
    {
        var alpha = buffer.GetPixel(x, y).A;
        if (alpha > 0.001f && alpha < 0.999f)
            return false;
    }
    return true;
}

static bool IsFullyOpaque(PixelBuffer buffer)
{
    for (var y = 0; y < buffer.Height; y++)
    for (var x = 0; x < buffer.Width; x++)
        if (buffer.GetPixel(x, y).A < 0.999f)
            return false;
    return true;
}

static (float Seam, float Interior) MeasureTileSeam(PixelBuffer buffer)
{
    static float Difference((float R, float G, float B, float A) left,
        (float R, float G, float B, float A) right)
        => (MathF.Abs(left.R - right.R) + MathF.Abs(left.G - right.G) + MathF.Abs(left.B - right.B)) / 3f;

    var seam = 0f;
    for (var y = 0; y < buffer.Height; y++)
        seam += Difference(buffer.GetPixel(0, y), buffer.GetPixel(buffer.Width - 1, y));
    for (var x = 0; x < buffer.Width; x++)
        seam += Difference(buffer.GetPixel(x, 0), buffer.GetPixel(x, buffer.Height - 1));
    seam /= buffer.Width + buffer.Height;

    var interior = 0f;
    var samples = 0;
    for (var y = 0; y < buffer.Height - 1; y++)
    for (var x = 0; x < buffer.Width - 1; x++)
    {
        interior += Difference(buffer.GetPixel(x, y), buffer.GetPixel(x + 1, y));
        interior += Difference(buffer.GetPixel(x, y), buffer.GetPixel(x, y + 1));
        samples += 2;
    }
    return (seam, interior / Math.Max(1, samples));
}

static bool BuffersEqual(PixelBuffer left, PixelBuffer right)
    => left.Width == right.Width && left.Height == right.Height &&
       left.AsReadOnlySpan().SequenceEqual(right.AsReadOnlySpan());

static (int MinX, int MinY, int MaxX, int MaxY, int Opaque, int Transparent) OpaqueBounds(PixelBuffer buffer)
{
    var minX = buffer.Width;
    var minY = buffer.Height;
    var maxX = -1;
    var maxY = -1;
    var opaque = 0;
    for (var y = 0; y < buffer.Height; y++)
    for (var x = 0; x < buffer.Width; x++)
    {
        if (buffer.GetPixel(x, y).A < 0.5f)
            continue;
        opaque++;
        minX = Math.Min(minX, x);
        minY = Math.Min(minY, y);
        maxX = Math.Max(maxX, x);
        maxY = Math.Max(maxY, y);
    }
    return (minX, minY, maxX, maxY, opaque, buffer.Width * buffer.Height - opaque);
}

static void ExportSpritePreview(string path)
{
    const int cellSize = 144;
    var treeTypes = new[] { "broadleaf", "pine", "layered", "palm", "dead" };
    const int columns = 8;
    const int rows = 8;
    using var sheet = PixelBuffer.CreateSolid(columns * cellSize, rows * cellSize, 0.035f, 0.045f, 0.065f, 1f);
    for (var y = 0; y < sheet.Height; y++)
    for (var x = 0; x < sheet.Width; x++)
    {
        var checker = ((x / 8 + y / 8) & 1) == 0 ? 0.055f : 0.075f;
        sheet.SetPixel(x, y, checker, checker + 0.008f, checker + 0.02f, 1f);
    }

    for (var row = 0; row < rows; row++)
    {
        if (row >= 2)
            break;
        var spriteSize = row == 0 ? 32 : 64;
        var scale = row == 0 ? 4 : 2;
        for (var column = 0; column < columns; column++)
        {
            IGraphNode node;
            Dictionary<string, object> parameters;
            if (column < treeTypes.Length)
            {
                node = new PixelTreeNode();
                parameters = DefaultParameters(node);
                parameters["treeType"] = treeTypes[column];
            }
            else if (column == 5)
            {
                node = new PixelBushNode();
                parameters = DefaultParameters(node);
            }
            else
            {
                node = new PixelMushroomNode();
                parameters = DefaultParameters(node);
            }

            using var sprite = node.Process([], parameters,
                new PixelGraphContext { TileSize = spriteSize, Seed = 42 });
            var drawWidth = sprite.Width * scale;
            var drawHeight = sprite.Height * scale;
            var offsetX = column * cellSize + (cellSize - drawWidth) / 2;
            var offsetY = row * cellSize + (cellSize - drawHeight) / 2;
            for (var sy = 0; sy < sprite.Height; sy++)
            for (var sx = 0; sx < sprite.Width; sx++)
            {
                var pixel = sprite.GetPixel(sx, sy);
                if (pixel.A < 0.5f)
                    continue;
                for (var oy = 0; oy < scale; oy++)
                for (var ox = 0; ox < scale; ox++)
                    sheet.SetPixel(offsetX + sx * scale + ox, offsetY + sy * scale + oy,
                        pixel.R, pixel.G, pixel.B, 1f);
            }
        }
    }

    for (var row = 2; row < 4; row++)
    {
        var tileSize = row == 2 ? 32 : 64;
        var scale = row == 2 ? 4 : 2;
        var materials = new IGraphNode[]
        {
            new PixelGrassNode(), new PixelCobblestoneNode(), new PixelFabricNode(), new PixelLeatherNode()
        };
        for (var column = 0; column < materials.Length; column++)
        {
            var node = materials[column];
            using var tile = node.Process([], DefaultParameters(node),
                new PixelGraphContext { TileSize = tileSize, Seed = 42 });
            var drawWidth = tile.Width * scale;
            var drawHeight = tile.Height * scale;
            var offsetX = column * cellSize + (cellSize - drawWidth) / 2;
            var offsetY = row * cellSize + (cellSize - drawHeight) / 2;
            for (var sy = 0; sy < tile.Height; sy++)
            for (var sx = 0; sx < tile.Width; sx++)
            {
                var pixel = tile.GetPixel(sx, sy);
                for (var oy = 0; oy < scale; oy++)
                for (var ox = 0; ox < scale; ox++)
                    sheet.SetPixel(offsetX + sx * scale + ox, offsetY + sy * scale + oy,
                        pixel.R, pixel.G, pixel.B, 1f);
            }
        }
    }

    for (var row = 4; row < rows; row++)
    {
        if (row >= 6)
            break;
        var previewSize = row == 4 ? 32 : 64;
        var scale = row == 4 ? 4 : 2;
        var nodes = new IGraphNode[]
        {
            new PixelFloorNode(), new PixelFlagstoneNode(), new PixelLightningNode(),
            new PixelWaterFlowNode(), new PixelSlimeNode(), new PixelWallNode(),
            new PixelIconNode(), new PixelChainmailNode()
        };
        for (var column = 0; column < nodes.Length; column++)
        {
            var node = nodes[column];
            using var preview = node.Process([], DefaultParameters(node),
                new PixelGraphContext { TileSize = previewSize, Seed = 42, AnimationTime = 0.25f });
            var drawWidth = preview.Width * scale;
            var drawHeight = preview.Height * scale;
            var offsetX = column * cellSize + (cellSize - drawWidth) / 2;
            var offsetY = row * cellSize + (cellSize - drawHeight) / 2;
            for (var sy = 0; sy < preview.Height; sy++)
            for (var sx = 0; sx < preview.Width; sx++)
            {
                var pixel = preview.GetPixel(sx, sy);
                if (pixel.A < 0.05f)
                    continue;
                for (var oy = 0; oy < scale; oy++)
                for (var ox = 0; ox < scale; ox++)
                    sheet.SetPixel(offsetX + sx * scale + ox, offsetY + sy * scale + oy,
                        pixel.R, pixel.G, pixel.B, 1f);
            }
        }
    }


    for (var row = 6; row < rows; row++)
    {
        var previewSize = row == 6 ? 32 : 64;
        var scale = row == 6 ? 4 : 2;
        var nodes = new IGraphNode[]
        {
            new PixelRainNode(), new PixelMarbleNode(), new PixelScalesNode(), new PixelRustNode(),
            new PixelCircuitNode(), new PixelHoneycombNode(), new PixelFenceNode()
        };
        for (var column = 0; column < nodes.Length; column++)
        {
            var node = nodes[column];
            using var preview = node.Process([], DefaultParameters(node),
                new PixelGraphContext { TileSize = previewSize, Seed = 42, AnimationTime = 0.25f });
            var drawWidth = preview.Width * scale;
            var drawHeight = preview.Height * scale;
            var offsetX = column * cellSize + (cellSize - drawWidth) / 2;
            var offsetY = row * cellSize + (cellSize - drawHeight) / 2;
            for (var sy = 0; sy < preview.Height; sy++)
            for (var sx = 0; sx < preview.Width; sx++)
            {
                var pixel = preview.GetPixel(sx, sy);
                if (pixel.A < 0.05f)
                    continue;
                for (var oy = 0; oy < scale; oy++)
                for (var ox = 0; ox < scale; ox++)
                    sheet.SetPixel(offsetX + sx * scale + ox, offsetY + sy * scale + oy,
                        pixel.R, pixel.G, pixel.B, 1f);
            }
        }
    }

    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(sheet.ToBitmapSource()));
    using var stream = File.Create(fullPath);
    encoder.Save(stream);
}

static void ExportAnimationPreview(string path)
{
    const int cellSize = 144;
    const int columns = 4;
    const int rows = 5;
    using var sheet = PixelBuffer.CreateSolid(columns * cellSize, rows * cellSize, 0.04f, 0.05f, 0.075f, 1f);
    for (var y = 0; y < sheet.Height; y++)
    for (var x = 0; x < sheet.Width; x++)
    {
        var checker = ((x / 8 + y / 8) & 1) == 0 ? 0.06f : 0.1f;
        sheet.SetPixel(x, y, checker, checker + 0.01f, checker + 0.025f, 1f);
    }

    var smokeTypes = new[] { "thick", "mist", "steam", "dust" };
    for (var row = 0; row < 4; row++)
    {
        var previewSize = row < 2 ? 32 : 64;
        var scale = row < 2 ? 4 : 2;
        var time = (row & 1) == 0 ? 0.15f : 0.65f;
        for (var column = 0; column < columns; column++)
        {
            var smoke = new PixelSmokeNode();
            var parameters = DefaultParameters(smoke);
            parameters["smokeType"] = smokeTypes[column];
            parameters["wind"] = column == 3 ? 0.3d : 0d;
            using var frame = smoke.Process([], parameters,
                new PixelGraphContext { TileSize = previewSize, Seed = 42, AnimationTime = time });
            DrawScaled(frame, sheet, column * cellSize + (cellSize - frame.Width * scale) / 2,
                row * cellSize + (cellSize - frame.Height * scale) / 2, scale);
        }
    }

    var textures = new[]
    {
        ParticleTextureType.PixelCircle, ParticleTextureType.SmokePuff,
        ParticleTextureType.Spark, ParticleTextureType.Streak
    };
    for (var column = 0; column < columns; column++)
    {
        using var particles = new ParticleBuffer(28);
        for (var i = 0; i < 28; i++)
        {
            var x = 0.18f + ((i * 37) % 61) / 100f;
            var y = 0.82f - i / 38f + MathF.Sin(i * 1.7f) * 0.045f;
            var particle = TestParticle(x, y, 0.06f + (i % 4) * 0.012f);
            particle.R = column >= 2 ? 1f : 0.76f;
            particle.G = column >= 2 ? 0.65f : 0.8f;
            particle.B = column >= 2 ? 0.18f : 0.86f;
            particle.A = 0.45f + (i % 4) * 0.15f;
            particle.Rotation = i * 0.41f;
            particles.AsSpan()[i] = particle;
        }
        particles.ActiveCount = 28;
        using var frame = PixelBuffer.CreateSolid(64, 64, 0f, 0f, 0f, 0f);
        new ParticleRenderer
        {
            TextureType = textures[column], PixelSnap = true, SoftEdges = false,
            AlphaSteps = 4, BlendMode = column >= 2 ? ParticleBlendMode.Additive : ParticleBlendMode.Alpha
        }.Render(particles.ActiveSpan(), frame, particles.ActiveCount);
        DrawScaled(frame, sheet, column * cellSize + 8, 4 * cellSize + 8, 2);
    }

    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(sheet.ToBitmapSource()));
    using var stream = File.Create(fullPath);
    encoder.Save(stream);

    static void DrawScaled(PixelBuffer source, PixelBuffer target, int offsetX, int offsetY, int scale)
    {
        for (var sy = 0; sy < source.Height; sy++)
        for (var sx = 0; sx < source.Width; sx++)
        {
            var pixel = source.GetPixel(sx, sy);
            if (pixel.A <= 0f) continue;
            for (var oy = 0; oy < scale; oy++)
            for (var ox = 0; ox < scale; ox++)
            {
                var x = offsetX + sx * scale + ox;
                var y = offsetY + sy * scale + oy;
                if (x < 0 || y < 0 || x >= target.Width || y >= target.Height) continue;
                var background = target.GetPixel(x, y);
                target.SetPixel(x, y,
                    pixel.R * pixel.A + background.R * (1f - pixel.A),
                    pixel.G * pixel.A + background.G * (1f - pixel.A),
                    pixel.B * pixel.A + background.B * (1f - pixel.A), 1f);
            }
        }
    }
}

sealed class CountingNode(string typeName, bool hasInput) : IGraphNode
{
    public string TypeName => typeName;
    public string Category => "Test";
    public IReadOnlyList<GraphNodePort> InputPorts { get; } = hasInput
        ? [new GraphNodePort("Input", GraphPortType.Image)]
        : [];
    public IReadOnlyList<GraphNodePort> OutputPorts { get; } =
        [new GraphNodePort("Output", GraphPortType.Image)];
    public IReadOnlyList<NodeParameterDefinition> Parameters => [];

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => inputs.FirstOrDefault()?.Clone()
            ?? PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 0.25f, 0.5f, 0.75f);
}

sealed class TypedNode(string typeName, GraphPortType portType, bool hasInput) : IGraphNode
{
    public string TypeName => typeName;
    public string Category => "Test";
    public IReadOnlyList<GraphNodePort> InputPorts { get; } = hasInput
        ? [new GraphNodePort("Input", portType)]
        : [];
    public IReadOnlyList<GraphNodePort> OutputPorts { get; } = hasInput
        ? [new GraphNodePort("Output", GraphPortType.Image)]
        : [new GraphNodePort("Output", portType)];
    public IReadOnlyList<NodeParameterDefinition> Parameters => [];
    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelBuffer.CreateSolid(1, 1, 0, 0, 0);
}
