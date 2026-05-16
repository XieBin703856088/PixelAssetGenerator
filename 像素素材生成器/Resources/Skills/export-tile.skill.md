---
name: export-tile
displayName:
  zh: 导出图块
  en: Export Tile
description:
  zh: 将节点图的结果导出为图像文件，支持 PNG/BMP 格式
  en: Export the node graph result as an image file in PNG or BMP format.
category: BuiltIn
tags: [export, image, output, file, png]
kind: instructions
---

# 导出图块

## 步骤 1: 添加输出节点

在节点图的末尾添加 Output 节点。所有节点图必须连接到 Output 节点才能导出。设置 `outputSize` 参数选择导出尺寸（32/64/128 等），`outputFormat` 选择 PNG 或 BMP。

**预期结果**: 节点图末端有 Output 节点且连接正确

## 步骤 2: 执行导出

点击主窗口的「导出图块」菜单项或使用快捷键。在弹出的对话框中选择文件路径和格式。也可以使用批量导出来导出多个种子变体。

**预期结果**: 图像文件保存到指定路径

## 步骤 3: 批量导出变体

使用「批量导出种子」功能，设置起始种子和数量，自动生成多个变体。每个变体使用不同的随机种子值，适用于生成 tileset。

**预期结果**: 生成多个变体图像文件
