---
name: bash-commands
displayName:
  zh: Bash 命令行操作
  en: Bash Command Operations
description:
  zh: 使用命令行执行节点文件操作、构建项目、批量处理等任务
  en: Use command line for node file operations, project builds, batch processing, etc.
category: BuiltIn
tags: [bash, shell, command, terminal, build]
kind: instructions
---

# Bash 命令行操作

## 步骤 1: 查看节点文件

使用 `ls` 命令查看所有已安装的节点文件。注意路径中使用 Unix 风格的 forward slash。

```shell
ls ./Nodes/Custom/
```

**预期结果**: 列出 Custom 目录下的所有 `.node.json` 文件

## 步骤 2: 构建项目

编译项目验证节点文件是否正确。

```shell
dotnet build
```

**预期结果**: 生成成功，0 个错误

## 步骤 3: 创建新节点目录

在自定义节点目录中创建新的节点 JSON 文件。

```shell
echo '{"formatVersion":2,"identity":{"typeName":"MyNewNode","displayName":{"zh-Hans":"我的新节点","en":"My New Node"},"category":"Custom"},"ports":{"inputs":[],"outputs":[{"name":{"zh-Hans":"输出","en":"Output"},"type":"Image"}]},"script":{"language":"csharp","code":"return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 128, 80, 200);"}}' > ./Nodes/Custom/MyNewNode.node.json
```

**预期结果**: 新节点文件创建成功
