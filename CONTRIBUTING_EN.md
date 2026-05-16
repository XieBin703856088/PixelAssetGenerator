# Contributing Guide

Thank you for considering contributing to Pixel Asset Generator! Whether it's fixing bugs, adding new nodes, improving documentation, or proposing new features, all contributions are welcome.

## How to Contribute

### 1. Report Bugs

Search [Issues](https://github.com) first to see if it has already been reported.

When submitting a bug report, please include:
- **System info**: Windows version, .NET version
- **Description**: What happened, what you expected
- **Steps to reproduce**: As detailed as possible
- **Logs/screenshots**: Attach if available

### 2. Suggest Features

Open an Issue with the `enhancement` label to discuss your idea before implementing. This avoids wasted effort if the feature is not a good fit.

### 3. Submit Code

#### Workflow

```
1. Fork this repository
2. Create a feature branch: git checkout -b feat/my-feature
3. Make your changes
4. Verify the build: dotnet build
5. Commit and push to your fork
6. Create a Pull Request
```

#### Branch Naming

| Scenario | Branch Example |
|----------|---------------|
| New feature | `feat/xxx` |
| Bug fix | `fix/xxx` |
| Refactoring | `refactor/xxx` |
| Documentation | `docs/xxx` |
| Node | `node/xxx` |

### 4. Contribute Nodes

The project's value lies in its node ecosystem. Steps to add a new node:

1. Create `.node.json` under `Resources/Nodes/<Category>/`
2. Follow the existing node format (see [NODE_REFERENCE_EN.md](docs/NODE_REFERENCE_EN.md))
3. If the node requires C# backend code, add it to the appropriate `Core/` or `Nodes/` directory
4. Ensure node names and descriptions are bilingual (Chinese + English)
5. Submit a PR

## Code Standards

### General Principles

- **Bilingual**: All user-facing text (node names, descriptions, UI labels) should be in both Chinese and English
- **Immutability**: Prefer creating new objects over mutating existing ones
- **Small functions**: Keep functions under 50 lines where possible
- **Clear naming**: Variable and method names should be self-explanatory

### C# Standards

- Use `.NET 9` / `C# 13` features
- Follow `dotnet format` code style
- Keep `using` directives organized, remove unused imports
- Add XML documentation comments for public APIs

### Node JSON Standards

```json
{
  "formatVersion": 2,
  "identity": {
    "displayName": { "zh-Hans": "Chinese Name", "en": "English Name" },
    "description": { "zh-Hans": "Chinese description", "en": "English description" }
  }
}
```

## Pull Request Guidelines

- Clear PR title describing the change
- Link related issues (e.g., `Fixes #123`)
- Ensure `dotnet build` passes
- Update relevant documentation for new features/nodes
- Keep PRs focused: one PR, one concern

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). Please maintain a friendly and respectful environment.

## License

By submitting a PR, you agree that your contributions will be licensed under **GPL v3** as well.
