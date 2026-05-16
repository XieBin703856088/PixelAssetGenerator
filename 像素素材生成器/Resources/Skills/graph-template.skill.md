---
name: graph-template
displayName:
  zh: 节点图编排模板
  en: Graph Node Arrangement Template
description:
  zh: 节点图的标准编排模式：生成器 → 处理链 → 输出
  en: Standard node graph arrangement: generator → processing chain → output.
category: BuiltIn
tags: [graph, template, workflow, bestpractice]
kind: instructions
---

# 节点图编排模板

## 步骤 1: 基本流程

一个完整的节点图通常遵循：生成器（Source/Nature）→ 滤镜（Adjust/Effect）→ 混合（如果多路）→ 输出（Output）。生成器节点在最左边，Output 在最右边。

**预期结果**: 清晰的节点图布局

## 步骤 2: 端口连接规则

端口类型必须匹配：Image 接 Image，Mask 接 Mask。连接时通常使用索引 0（第一个输出端口到第一个输入端口）。BlendMode 等混合节点需要两个输入（base + top）。

**预期结果**: 连接正确

## 步骤 3: 无缝平铺流程

生成器 → OffsetWrap → SeamlessBlend → Output。SeamlessBlend 需要两个输入：原始图像（port 0）和偏移后的图像（port 1）。也可以在一个节点图中合成多个生成器后统一处理无缝。

**预期结果**: 输出无缝平铺纹理
