---
name: texture-generation
displayName:
  zh: 程序化纹理生成
  en: Procedural Texture Generation
description:
  zh: 程序化纹理生成的系统方法论：从需求分析到节点图实现的分层策略
  en: Systematic methodology for procedural texture generation: layered strategy from requirements to node graph implementation.
category: BuiltIn
tags: [texture, procedural, generation, methodology, bestpractice]
kind: instructions
origin: ECC-inspired
---

# 程序化纹理生成

## 核心原则

1. **分层构建** — 从底层结构逐层叠加：基底 → 细节 → 颜色 → 光影 → 特效
2. **先有结构后有细节** — 先确定整体布局和重复模式，再添加微观细节
3. **参数化思维** — 每个关键数值都应作为暴露参数，便于后续调整
4. **参考现实** — 观察真实材质的分层结构，模仿其形成过程

## 步骤 1: 需求分析

分析用户需求的三个维度：
- **材质类型**：石头/木头/金属/织物/液体/有机体
- **风格方向**：写实/像素风/卡通/手绘
- **使用场景**：地面/墙壁/物品/UI 背景

**预期结果**: 明确的设计方向和节点选择策略

## 步骤 2: 选择基底节点

根据材质类型选择初始生成器：
- **自然纹理**：Noise 类（Perlin/Simplex/Voronoi）→ 地形生成器
- **人造结构**：Pattern 类（Grid/Checker/Stripe）→ 几何布局
- **有机表面**：Nature 类（Wood/Fire/Water）→ 程序化材质

**预期结果**: 生成基础纹理结构

## 步骤 3: 叠加细节层

使用多层结构增加真实感：
1. 第二层噪声叠加（不同频率）
2. 使用 BlendMode 混合各层（Multiply/Overlay/Add）
3. 使用 Adjust 节点调整对比度和色阶

**预期结果**: 纹理具有深度和丰富度

## 步骤 4: 颜色映射

使用 Adjust 类节点的颜色映射功能：
- Colorize / Gradient Map 着色
- HSL 调整色调
- Level / Curve 调整对比度

**预期结果**: 纹理具有正确的色彩方案

## 步骤 5: 无缝处理

使用 OffsetWrap + SeamlessBlend 确保纹理可平铺：
1. OffsetWrap 偏移纹理（偏移量=图块尺寸的一半）
2. SeamlessBlend 混合原始和偏移版本
3. 关键：SeamlessBlend 需要两个输入——port 0 是原始，port 1 是偏移

**预期结果**: 纹理无缝平铺，接缝不可见

## 步骤 6: 评估与迭代

使用 `aesthetic_eval` 工具评估输出质量：
- 检查纹理重复性
- 检查色彩平衡
- 根据评估反馈调整参数
- 必要时回到步骤 2 重新选择生成器

**预期结果**: 高质量的最终纹理
