---
name: batch-export
displayName:
  zh: 批量导出多个种子
  en: Batch Export Seeds
description:
  zh: 使用命令行或 Python 脚本批量导出多个种子的图块输出
  en: Use command line or Python scripts to batch export tiles for multiple seeds.
category: BuiltIn
tags: [batch, export, seed, automation, pipeline]
kind: instructions
---

# 批量导出多个种子

## 步骤 1: 设置参数

在节点图中确保关键节点（如 Noise、Terrain 等）的 `seed` 参数被暴露。使用 `query_info graph_summary` 查看当前画布状态，使用 `set_parameter` 调整节点参数。

**预期结果**: 节点图配置正确

## 步骤 2: 使用批量导出功能

点击主菜单的「批量导出种子…」功能。设置起始种子值、导出数量、输出目录。程序会自动为每个种子生成一个文件。

**预期结果**: 批量导出完成
