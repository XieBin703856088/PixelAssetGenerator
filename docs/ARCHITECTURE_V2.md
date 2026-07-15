# 节点与 AI 架构 V2

本文描述 0.7 方向的节点内核。它采用兼容式迁移：现有 V1/V2 `.node.json` 和工程文件仍可加载，新节点逐步采用 V3 契约。

## 设计目标

1. 节点的身份、显示文本和端口身份彼此分离，切换语言或调整端口顺序不会破坏工作流。
2. 画布、求值器和 AI 工具共享同一套类型、基数和环路校验。
3. 参数变更只重算当前节点及其下游；目标预览只计算目标的祖先子图。
4. AI 先检索能力、再修改、最后验证，不再依赖完整节点清单和脆弱的端口序号。

## 节点契约 V3

端口新增稳定 `key`、必填标记和连接基数。`name` 只用于本地化显示。

```json
{
  "formatVersion": 3,
  "processorType": "MaskBlend",
  "identity": {
    "typeName": "MaskBlend",
    "displayName": { "zh-Hans": "遮罩混合", "en": "Mask Blend" },
    "category": "ImageProcess"
  },
  "ports": {
    "inputs": [
      { "key": "foreground", "name": { "zh-Hans": "前景", "en": "Foreground" }, "type": "Image", "required": true },
      { "key": "mask", "name": { "zh-Hans": "遮罩", "en": "Mask" }, "type": "Mask", "required": true }
    ],
    "outputs": [
      { "key": "image", "name": { "zh-Hans": "图像", "en": "Image" }, "type": "Image" }
    ]
  }
}
```

旧资源未提供 `key` 时会从名称生成兼容 key。新增或重写节点时必须显式提供英文小写 key，并且后续版本不得修改它。

节点可通过 `GraphNodeTraits` 声明执行语义：

- `Pure | Deterministic`：允许增量缓存，旧节点的默认值。
- `TimeDependent`：每个时间采样重新计算，并使下游失效。
- `Stateful`：粒子、物理等持久状态节点，不跨帧复用结果。
- `Expensive`：供后续调度器决定优先级和后台执行策略。

## 端口兼容规则

端口兼容由 `GraphValidator.AreCompatible` 统一管理：

| 输出 | 可连接输入 |
|---|---|
| Image | Image、Mask、Any |
| Mask | Mask、Any |
| Float | Float、Any |
| Color | Color、Any |
| Particle | Particle、Any |
| Any | 任意 |

Image 到 Mask 会在求值阶段创建灰度视图。其它隐式转换必须通过明确的转换节点完成，避免工作流“能连但结果不可预测”。

## 求值管线

```text
图快照 → 结构诊断 → 祖先裁剪 → 编译拓扑计划 → 分层并行求值 → 结果缓存 → 预览/导出
```

`GraphRuntimeSnapshotBuilder` 是编辑层进入运行时内核的唯一投影入口，统一负责参数值转换、Tile 节点适配和连接过滤。主预览、节点预览与导出不得各自重新实现这段转换。

缓存指纹包含节点类型、参数、画布上下文、语义覆盖和全部上游指纹。改变一个参数时，未受影响的分支直接复用缓存；拓扑未变化时复用已编译执行计划。缓存为每个活动节点保留一份不可变快照，节点删除后立即回收。

`NodeGraphEvaluator.LastMetrics` 暴露耗时、实际执行节点数、缓存命中数、计划复用状态和诊断数量，可用于性能面板和回归测试。

## AI 工作流

推荐调用顺序：

1. `query_info(node_library, param=能力关键词)`：最多返回 24 个相关节点。
2. `query_info(node_detail, param=节点类型)`：读取准确端口和参数。
3. `modify_nodes` / `set_parameter`：创建和配置节点。
4. `modify_connections`：使用 `startPortKey`、`endPortKey` 连接；工具会先检查类型、端口基数和环路。
5. `query_info(graph_validation)`：读取结构化错误、警告和修复建议。

AI 不应猜测端口序号，也不应在未验证图的情况下宣告任务完成。

## 迁移顺序

1. 为高频节点补齐 V3 端口 key、required 和 AI capability 元数据。
2. 工程文件下一版本同时保存端口 key 与旧 index；读取时优先 key，index 仅作兼容回退。
3. 将 `MainWindow` 中剩余的节点编辑逻辑逐步收敛到 `NodeGraphController`，UI 只负责输入和呈现。
4. 将 CPU/GPU 节点统一到同一执行结果契约，减少当前 GPU 分支中的读回和重复调度。
5. 在性能面板显示 `LastMetrics`，用真实项目建立 P50/P95 预览延迟基线。

## 编译型实用节点

新增的 Alpha Tools、Color Replace、Mask Morphology、Distance Field 和 Sprite Extrude 位于 `Core/Nodes/PracticalImageNodes.cs`。它们通过 V3 JSON 提供本地化、稳定端口和 AI 元数据，但像素处理由编译型 C# 完成，避免增加 Roslyn 启动与首次使用成本。使用方法见 [实用节点指南](PRACTICAL_NODES.md)。

## 验证

运行核心冒烟测试：

```powershell
dotnet run --project tools/GraphCoreSmoke/GraphCoreSmoke.csproj
```

测试覆盖全缓存命中、局部失效、目标祖先裁剪、环路诊断和端口类型诊断。
