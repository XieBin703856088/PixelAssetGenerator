---
name: cmd-commands
displayName:
  zh: CMD 命令行操作
  en: CMD Command Operations
description:
  zh: 使用 Windows CMD（命令提示符）执行文件操作、构建项目、批量处理像素素材
  en: Use Windows CMD (Command Prompt) for file operations, project builds, and batch processing.
category: BuiltIn
tags: [cmd, windows, command, terminal, build, batch]
kind: instructions
---

# CMD 命令行操作

## 步骤 1: 查看节点文件

使用 `dir` 命令查看自定义节点目录中的所有 `.node.json` 文件。

```shell
dir /b .\Nodes\Custom\
```

**预期结果**: 列出 Custom 目录下的所有节点文件

## 步骤 2: 构建项目

使用 `dotnet build` 编译项目，确保所有代码修改正确。

```shell
dotnet build
```

**预期结果**: 生成成功，0 个错误

## 步骤 3: 创建新节点文件

在自定义节点目录中创建新的 `.node.json` 节点文件。

```shell
echo {"formatVersion":2,"identity":{"typeName":"CmdNode","displayName":{"zh-Hans":"CMD节点","en":"CMD Node"},"category":"Custom"},"ports":{"inputs":[],"outputs":[{"name":{"zh-Hans":"输出","en":"Output"},"type":"Image"}]},"script":{"language":"csharp","code":"return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 100, 150, 200);"}} > .\Nodes\Custom\CmdNode.node.json
```

**预期结果**: 新节点文件创建成功，可在节点库中看到

## 步骤 4: 清理生成缓存

删除项目的 `bin` 和 `obj` 目录，清理编译缓存。

```shell
if exist bin rmdir /s /q bin && if exist obj rmdir /s /q obj
```

**预期结果**: bin 和 obj 目录被删除

## 步骤 5: 查看所有节点

递归查看 Nodes 目录下的所有 `.node.json` 文件。

```shell
dir /s /b .\Nodes\*.node.json
```

**预期结果**: 列出项目中所有节点定义文件
