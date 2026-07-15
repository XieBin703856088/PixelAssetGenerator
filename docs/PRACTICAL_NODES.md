# 实用节点指南

以下节点采用编译型 C# 处理器和 V3 节点契约，不需要运行时编译脚本。所有端口都提供稳定 key，可供工程文件和 AI 工具可靠引用。

## Alpha Tools / Alpha 工具

输入：`image`（必填）、`mask`（可选）；输出：`image`。

支持以下模式：

- `applyMask`：将遮罩乘入原 Alpha，适合抠图和局部透明。
- `luminanceToAlpha`：把图像亮度转换为 Alpha。
- `invertAlpha`：反转透明度。
- `premultiply` / `unpremultiply`：处理渲染管线中的预乘 Alpha。

`amount` 可以在原始结果和处理结果之间连续混合。

## Color Replace / 颜色替换

输入、输出均为 `image`。根据 `sourceColor`、`tolerance` 和 `softness` 选择相近颜色，并替换为 `targetColor`。

开启 `preserveLuminance` 后会保留原像素明暗，适合角色换装、阵营色和同一素材的多配色变体。

## Mask Morphology / 遮罩扩缩

输入、输出均为 `mask`。

- `dilate`：扩张亮区，可生成外扩边缘。
- `erode`：收缩亮区，可消除细小突出。
- `open`：先腐蚀再膨胀，清理孤立噪点。
- `close`：先膨胀再腐蚀，填补小孔。

常见组合：`Threshold → MaskMorphology → AlphaTools.mask`。

## Distance Field / 距离场

输入 `mask`，输出 `distance`。使用双向近似欧氏距离变换，复杂度为 O(宽 × 高)。

- `signed`：边界值为 0.5，内部大于 0.5，外部小于 0.5。
- `inside`：只输出形状内部到边界的距离。
- `outside`：只输出形状外部到边界的距离。

距离场可接入 `GradientMap`、`Threshold` 或 `Glow`，构建软边、内外发光和多层描边。

## Sprite Extrude / 精灵边缘挤出

输入、输出均为 `image`。节点会把邻近不透明像素的 RGB 复制进透明区域，从而避免纹理图集在双线性过滤、缩放或生成 mipmap 时产生黑边和杂色边。

默认仅扩展 RGB、保持 Alpha 不变。只有确实需要扩张可见轮廓时才开启 `extendAlpha`。

推荐放在导出前的最后几个步骤：

```text
Sprite Slice → Pixel Perfect Outline → Sprite Extrude → Output
```

## AI 使用

AI 可以通过能力关键词检索这些节点，例如：

```text
query_info(query_type="node_library", param="alpha mask")
query_info(query_type="node_library", param="sprite atlas bleed")
```

建立连接时使用 `startPortKey` 和 `endPortKey`，不要依赖本地化名称或端口序号。
