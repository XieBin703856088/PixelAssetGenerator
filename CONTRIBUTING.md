# 贡献指南

感谢你愿意为像素素材生成器贡献力量！无论是修复 bug、添加新节点、改进文档，还是提出新功能建议，都非常欢迎。

## 如何参与

### 1. 报告 Bug

如果你发现了 bug，请先搜索 [Issues](https://github.com) 看是否已被报告。

提交 Issue 时请包含：
- **系统信息**：Windows 版本、.NET 版本
- **问题描述**：发生了什么，期望的行为是什么
- **复现步骤**：尽可能详细
- **日志/截图**：如有错误日志或截图请附上

### 2. 提出新功能

先开一个 Issue 描述你的想法，标注 `enhancement`。讨论清楚后再动手实现，避免做了大量工作后被拒绝。

### 3. 提交代码

#### 工作流

```
1. Fork 本仓库
2. 创建特性分支: git checkout -b feat/my-feature
3. 修改代码
4. 确保构建通过: dotnet build
5. 提交并推送到你的 Fork
6. 创建 Pull Request
```

#### 分支命名

| 场景 | 分支名示例 |
|------|-----------|
| 新功能 | `feat/xxx` |
| Bug 修复 | `fix/xxx` |
| 重构 | `refactor/xxx` |
| 文档 | `docs/xxx` |
| 节点 | `node/xxx` |

### 4. 贡献新节点

项目的主要价值在于节点生态。添加新节点的步骤：

1. 在 `Resources/Nodes/<Category>/` 下创建 `.node.json`
2. 遵循已有的节点格式（详见 [NODE_REFERENCE.md](docs/NODE_REFERENCE.md)）
3. 如果节点需要 C# 后端代码，添加到对应的 `Core/` 或 `Nodes/` 目录
4. 确保节点名称和描述有中英双语
5. 提交 PR

## 代码规范

### 通用原则

- **中英双语**：所有面向用户的文本（节点名称、描述、UI 标签）都需要中英双语
- **不可变性**：优先创建新对象而非修改现有对象
- **小函数**：每个函数尽量控制在 50 行以内
- **清晰命名**：变量和方法名要能自解释

### C# 规范

- 使用 `.NET 9` / `C# 13` 特性
- 遵循 `dotnet format` 的代码风格
- `using` 引用放在文件顶部，删除未使用的引用
- public API 添加 XML 文档注释

### 节点 JSON 规范

```json
{
  "formatVersion": 2,
  "identity": {
    "displayName": { "zh-Hans": "中文名", "en": "English Name" },
    "description": { "zh-Hans": "中文描述", "en": "English description" }
  }
}
```

## Pull Request 规范

- PR 标题清晰说明改动内容
- PR 描述中链接相关 Issue（如 `Fixes #123`）
- 确保 `dotnet build` 通过
- 如果添加了新功能/节点，同时更新相关文档
- 保持 PR 聚焦：一个 PR 只做一件事

## 行为准则

本项目采用 [贡献者公约](CODE_OF_CONDUCT.md)。请保持友善、尊重的交流环境。

## 许可证

通过提交 PR，你同意你的贡献将同样以 **GPL v3** 许可证发布。
