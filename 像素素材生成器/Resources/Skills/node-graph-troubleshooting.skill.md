---
name: node-graph-troubleshooting
displayName:
  zh: 节点图调试
  en: Node Graph Troubleshooting
description:
  zh: 系统化诊断和修复节点图问题的流程
  en: Systematic workflow for diagnosing and fixing node graph issues.
category: BuiltIn
tags: [debug, troubleshooting, node, graph, error]
kind: instructions
origin: ECC-inspired
---

# 节点图调试

## 核心原则

1. **先理解再修改** — 在理解根因之前修改参数是猜测，不是调试
2. **一次只改一个变量** — 同时改多个参数，你不知道哪个起了作用
3. **从输出往回推** — 从问题表现开始，沿着节点链往回追踪
4. **30 分钟法则** — 如果 30 分钟没有进展，退一步重新审视假设

## 步骤 1: 收集信息

在修改任何东西之前，先了解当前状态：
1. 用 `query_info(graph_summary)` 查看完整节点图结构
2. 确认 Output 节点连接是否正确
3. 检查所有节点的参数值是否在合理范围

**预期结果**: 明确的调试起点

## 步骤 2: 定位问题

常见问题及定位方法：
- **全黑输出**：检查源节点是否生成内容，检查连接是否断裂
- **颜色异常**：检查颜色参数值，检查 BlendMode 模式是否正确
- **锯齿/ artifacts**：检查是否有未做无缝处理的节点，检查 UV 坐标
- **性能问题**：检查是否有不必要的复杂计算节点
- **输出空白**：检查节点是否禁用了，检查 seed 值是否冲突

**预期结果**: 定位到问题根因

## 步骤 3: 隔离测试

1. 断开有问题的节点链
2. 用简单源节点（纯色/渐变）替换测试
3. 一次加回一个节点，观察变化
4. 确认问题在具体哪两个节点之间

**预期结果**: 明确问题范围

## 步骤 4: 修复与验证

1. 修改参数或连接
2. 使用 `aesthetic_eval` 验证修复效果
3. 如果问题未解决，回到步骤 2
4. 问题解决后记录经验和心得

**预期结果**: 节点图正常工作
