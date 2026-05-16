---
name: project-file
displayName:
  zh: 项目文件操作
  en: Project File Operations
description:
  zh: 保存和加载 .pxtile 项目文件，管理存储的节点图
  en: Save and load .pxtile project files to manage stored node graphs.
category: BuiltIn
tags: [project, file, save, load, serialization]
kind: instructions
---

# 项目文件操作

## 步骤 1: 保存项目

点击「文件 → 保存项目文件」保存当前节点图为 `.pxtile` 文件。下次可以通过「打开项目文件」恢复所有节点和连接。扩展名固定为 `.pxtile`。

**预期结果**: 项目文件保存成功

## 步骤 2: 项目文件结构

项目文件包含 TileSize、所有节点的位置/参数/端口、以及所有连接。打开项目文件时自动还原完整的节点编辑状态。注意 Output 节点是项目文件的终点。

**预期结果**: 了解项目文件结构
