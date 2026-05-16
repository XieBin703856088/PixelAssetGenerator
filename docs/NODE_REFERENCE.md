# 节点参考文档

## 类别概览

| 类别 | 说明 | 节点数 |
|------|------|--------|
| Generate | 瓦片生成 | 5 |
| ImageProcess | 图像处理 | 42 |
| Logic | 逻辑控制 | 14 |
| Material | 材质纹理 | 32 |
| Noise | 噪声纹理 | 16 |
| Pattern | 图案生成 | 20 |
| Tool | 工具 | 3 |
| Animation | 动画效果 | 8 |
| Particle | 粒子系统 | 7 |
| Physics | 物理模拟 | 4 |
| **总计** | | **151** |

## 端口类型
- **Image**: RGBA 像素缓冲区（浮点精度 0-1）
- **Mask**: 单通道灰度遮罩
- **Float**: 浮点数值
- **Color**: RGBA 颜色值
- **Any**: 任意类型（工具/预览节点）

## 参数类型
- **Number**: 浮点数 (min/max/step/default)
- **Integer**: 整数 (min/max/step/default)
- **Choice**: 枚举选择（中英双语显示标签）
- **Boolean**: 开关
- **Color**: 颜色选择器
- **Seed**: 随机种子
- **PointList**: 点坐标列表
- **Text**: 文本字符串

## 节点定义位置

节点定义分布在两个目录：

- `Resources/Nodes/` — 136 个核心节点，按类别组织子目录
- `Nodes/` — 19 个扩展节点（动画/粒子/物理）

### Resources/Nodes/ 结构

| 子目录 | 节点数 | 包含节点示例 |
|--------|--------|-------------|
| Generate | 5 | TileGrass, TileRoad, TileSand, TileStone, TileWater |
| ImageProcess | 42 | Bevel, BlendMode, Blur, ChromaticAberration, ColorAdjust, Colorize, ColorQuantize, Convolution, Curves, Displace, Distort, DropShadow, Glow, GradientMap, Grayscale, HslAdjust, Lighting, MaskBlend, MotionBlur, NormalMap, Outline, Pixelate, Scanlines, SeamlessBlend, Sharpen, Threshold, Transform, Twirl, Vignette 等 |
| Logic | 14 | AiImageGen, Brush, Cache, Condition, Constant, ImageAnalysis, Line, MathOp, RandomSelect, Rectangle, Selector, SemanticControl, Variation |
| Material | 32 | AutoTile, Brick, Bush, Camo, Chainmail, Cobblestone, Crystal, Fabric, Fibers, Flagstone, Floor, Gem, Grass, Ice, LavaFlow, Leather, Lightning, Mosaic, Moss, Mushroom, Rain, Rock, Rune, Shield, Slime, Snow, Text 等 |
| Noise | 16 | — |
| Pattern | 20 | — |
| Tool | 3 | — |

### Nodes/ 结构

| 子目录 | 节点数 | 包含节点 |
|--------|--------|---------|
| Animation | 8 | AnimatedParameter, AnimationPath, AnimationSequencer, AudioReactive, FrameBlend, NoiseAnimation, Time, Wave |
| Particle | 7 | InteractiveForce, ParticleCollision, ParticleEmitter, ParticleForce, ParticleLight, ParticleRender, ParticleTrail |
| Physics | 4 | PhysicsConstraint, PhysicsField, PhysicsSimulate, PhysicsSoftBody |

## 添加脚本节点
1. 在 `Resources/Nodes/<Category>/<TypeName>.node.json` 创建节点定义
2. 设置 `formatVersion: 2`、`processorType: null`（纯脚本节点）
3. 定义 identity（多语言 displayName/description）、ports、parameters、script.code
4. 脚本代码中可使用 `F()/I()/B()/S()` 辅助方法提取参数
5. 代码必须返回 `PixelBuffer`
6. 重启应用或调用 `GraphNodeRegistry.Reload()` 热重载

### 脚本节点示例
```json
{
  "formatVersion": 2,
  "processorType": null,
  "identity": {
    "typeName": "MyNode",
    "displayName": { "zh-Hans": "我的节点", "en": "My Node" },
    "category": "Custom",
    "description": { "zh-Hans": "描述", "en": "Description" }
  },
  "ports": {
    "inputs": [{"name": {"zh-Hans": "输入", "en": "Input"}, "type": "Image"}],
    "outputs": [{"name": {"zh-Hans": "输出", "en": "Output"}, "type": "Image"}]
  },
  "parameters": [
    { "name": { "zh-Hans": "强度", "en": "Strength" }, "kind": "Number", "default": 0.5, "min": 0, "max": 1 }
  ],
  "script": {
    "language": "csharp",
    "code": "return inputs[0]?.Clone() ?? PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 128, 80, 200);"
  }
}
```

## 节点文件定位
- 核心节点：`Resources/Nodes/<Category>/<TypeName>.node.json`
- 扩展节点：`Nodes/<Category>/<TypeName>.node.json`
- 内置技能：`Resources/Skills/*.skill.md`（33 个文件）
