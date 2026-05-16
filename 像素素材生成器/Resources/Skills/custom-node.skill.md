---
name: custom-node
displayName:
  zh: 创建自定义节点
  en: Create Custom Node
description:
  zh: 通过 .node.json 文件创建自定义节点，无需修改 C# 代码即可扩展功能
  en: Create custom nodes via .node.json files. Extend capabilities without modifying C# code.
category: BuiltIn
tags: [node, custom, extension, development, plugin]
kind: instructions
---

# 创建自定义节点

## 步骤 1: 创建节点文件

在自定义节点目录中创建一个 `.node.json` 文件。文件名即节点类型名。文件包含节点定义（身份、端口、参数）和脚本代码。使用 `create_resource_node` 工具或手动创建。

```json
{
  "formatVersion": 2,
  "identity": {
    "typeName": "MyNode",
    "displayName": { "zh-Hans": "我的节点", "en": "My Node" },
    "category": "Custom",
    "description": { "zh-Hans": "描述", "en": "Description" }
  },
  "ports": {
    "inputs": [{ "name": { "zh-Hans": "输入", "en": "Input" }, "type": "Image" }],
    "outputs": [{ "name": { "zh-Hans": "输出", "en": "Output" }, "type": "Image" }]
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

**预期结果**: 节点出现在节点库中，可在画布上使用

## 步骤 2: 编写脚本逻辑

`script.code` 字段包含节点处理逻辑。可用 `F(name)/I(name)/B(name)/S(name)` 提取参数。可用 `SmoothStep()`、`Lerp()`、`TileableFractalNoise()` 等 GraphNodeBase 工具方法。代码必须返回 PixelBuffer。

```csharp
// 示例：反转颜色滤镜
var input = inputs[0];
if (input == null) return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 0, 0, 0);
var result = input.Clone();
var strength = F("strength", 1.0);
result.ParallelForEachPixel((x, y) => {
    var c = result.GetPixel(x, y);
    result.SetPixel(x, y, (1-c.R)*strength + c.R*(1-strength), (1-c.G)*strength + c.G*(1-strength), (1-c.B)*strength + c.B*(1-strength), c.A);
});
return result;
```

**预期结果**: 节点能正确执行自定义逻辑

## 步骤 3: 热重载

修改 `.node.json` 文件后调用 `GraphNodeRegistry.Reload()` 或重启应用即可刷新节点库。也可以在节点文件的 `script.code` 中修改代码后直接在画布上拖入新实例使用。

**预期结果**: 修改后的节点立即可用
