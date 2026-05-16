---
name: script-to-node
displayName:
  zh: 从脚本创建自定义节点
  en: Create Custom Node from Script
description:
  zh: 使用 create_resource_node AI 工具从自然语言描述或脚本代码创建自定义节点
  en: Use the create_resource_node AI tool to create custom nodes from natural language or script code.
category: BuiltIn
tags: [ai, create, script, tool, workflow]
kind: instructions
---

# 从脚本创建自定义节点

## 步骤 1: 使用 create_resource_node 工具

直接调用 `create_resource_node` 工具创建节点。`typeName` 使用英文 PascalCase。`displayName` 为中文显示名。`category` 推荐使用 Custom。`code` 字段包含 C# 处理逻辑。

**预期结果**: AI 自动创建 `.node.json` 文件并注册节点

## 步骤 2: 参数定义

`parameters` 数组定义节点的可调参数。每个参数包含 `name`（中英文）、`kind`（Number/Integer/Boolean/Choice/Color/Seed/Text）、`default`、`min`、`max` 等。`choice` 类型需要提供 `choices` 数组。

**预期结果**: 节点出现在节点库中，参数可在 UI 中调节

## 步骤 3: 脚本代码编写指南

code 中使用 `F()/I()/B()/S()` 提取参数值。使用 `PixelBuffer` 的 `SetPixel/GetPixel/Clone/ParallelForEachPixel` 方法处理像素。可使用 `GraphNodeBase` 的 `SmoothStep/Lerp/TileableFractalNoise/HashToUnit` 等工具函数。必须返回 `PixelBuffer`。

```csharp
var input = inputs[0];
var strength = F("strength", 0.5);
if (input == null) return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 128, 80, 200);
var result = input.Clone();
result.ParallelForEachPixel((x, y) => {
    var px = result.GetPixel(x, y);
    result.SetPixel(x, y, px.R, px.G, px.B, px.A * strength);
});
return result;
```

**预期结果**: 节点能正确处理输入并输出结果
