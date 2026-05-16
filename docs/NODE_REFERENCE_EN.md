# Node Reference

## Category Overview

| Category | Description | Count |
|----------|-------------|-------|
| Generate | Tile generation | 5 |
| ImageProcess | Image processing | 42 |
| Logic | Logic control | 14 |
| Material | Material textures | 32 |
| Noise | Noise textures | 16 |
| Pattern | Pattern generation | 20 |
| Tool | Tools | 3 |
| Animation | Animation effects | 8 |
| Particle | Particle system | 7 |
| Physics | Physics simulation | 4 |
| **Total** | | **151** |

## Port Types
- **Image**: RGBA pixel buffer (float precision 0-1)
- **Mask**: Single-channel grayscale mask
- **Float**: Floating point value
- **Color**: RGBA color value
- **Any**: Any type (tool/preview nodes)

## Parameter Types
- **Number**: Float (min/max/step/default)
- **Integer**: Integer (min/max/step/default)
- **Choice**: Enum selection (bilingual display labels)
- **Boolean**: Toggle
- **Color**: Color picker
- **Seed**: Random seed
- **PointList**: Point coordinate list
- **Text**: Text string

## Node Definition Locations

Node definitions are spread across two directories:

- `Resources/Nodes/` — 136 core nodes, organized by category in subdirectories
- `Nodes/` — 19 extension nodes (animation/particle/physics)

### Resources/Nodes/ Structure

| Subdirectory | Count | Example Nodes |
|-------------|-------|--------------|
| Generate | 5 | TileGrass, TileRoad, TileSand, TileStone, TileWater |
| ImageProcess | 42 | Bevel, BlendMode, Blur, ChromaticAberration, ColorAdjust, Colorize, ColorQuantize, Convolution, Curves, Displace, Distort, DropShadow, Glow, GradientMap, Grayscale, HslAdjust, Lighting, MaskBlend, MotionBlur, NormalMap, Outline, Pixelate, Scanlines, SeamlessBlend, Sharpen, Threshold, Transform, Twirl, Vignette, etc. |
| Logic | 14 | AiImageGen, Brush, Cache, Condition, Constant, ImageAnalysis, Line, MathOp, RandomSelect, Rectangle, Selector, SemanticControl, Variation |
| Material | 32 | AutoTile, Brick, Bush, Camo, Chainmail, Cobblestone, Crystal, Fabric, Fibers, Flagstone, Floor, Gem, Grass, Ice, LavaFlow, Leather, Lightning, Mosaic, Moss, Mushroom, Rain, Rock, Rune, Shield, Slime, Snow, Text, etc. |
| Noise | 16 | — |
| Pattern | 20 | — |
| Tool | 3 | — |

### Nodes/ Structure

| Subdirectory | Count | Included Nodes |
|-------------|-------|---------------|
| Animation | 8 | AnimatedParameter, AnimationPath, AnimationSequencer, AudioReactive, FrameBlend, NoiseAnimation, Time, Wave |
| Particle | 7 | InteractiveForce, ParticleCollision, ParticleEmitter, ParticleForce, ParticleLight, ParticleRender, ParticleTrail |
| Physics | 4 | PhysicsConstraint, PhysicsField, PhysicsSimulate, PhysicsSoftBody |

## Adding a Script Node
1. Create node definition at `Resources/Nodes/<Category>/<TypeName>.node.json`
2. Set `formatVersion: 2`, `processorType: null` (pure script node)
3. Define identity (multi-language displayName/description), ports, parameters, script.code
4. In script code, use `F()/I()/B()/S()` helper methods to extract parameters
5. Code must return `PixelBuffer`
6. Restart the app or call `GraphNodeRegistry.Reload()` for hot reload

### Script Node Example
```json
{
  "formatVersion": 2,
  "processorType": null,
  "identity": {
    "typeName": "MyNode",
    "displayName": { "zh-Hans": "我的节点", "en": "My Node" },
    "category": "Custom",
    "description": { "zh-Hans": "Description", "en": "Description" }
  },
  "ports": {
    "inputs": [{"name": {"zh-Hans": "Input", "en": "Input"}, "type": "Image"}],
    "outputs": [{"name": {"zh-Hans": "Output", "en": "Output"}, "type": "Image"}]
  },
  "parameters": [
    { "name": { "zh-Hans": "Strength", "en": "Strength" }, "kind": "Number", "default": 0.5, "min": 0, "max": 1 }
  ],
  "script": {
    "language": "csharp",
    "code": "return inputs[0]?.Clone() ?? PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 128, 80, 200);"
  }
}
```

## Node File Locations
- Core nodes: `Resources/Nodes/<Category>/<TypeName>.node.json`
- Extension nodes: `Nodes/<Category>/<TypeName>.node.json`
- Built-in skills: `Resources/Skills/*.skill.md` (33 files)
