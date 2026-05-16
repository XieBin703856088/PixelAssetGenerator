# Pixel Asset Generator

A node-based pixel art material generator. Create tileable pixel-style textures by composing node graphs, with AI-assisted creation and editing.

**Version**: 0.6.0.0 | **Target**: .NET 9 / WPF | **C#**: 13

## Features

- **Node Graph Editor**: 151 nodes across 10 categories for visual programming
- **GPU Acceleration**: Direct3D 11 compute shaders (Vortice, optional) + SharpDX D3DImage interop
- **AI Assistant**: OpenAI / Claude API integration for AI-driven node graph creation and editing
- **Animation System**: Frame sequence playback, parameter animation, particle system, physics simulation
- **Skill System**: 33 built-in skills for saving/reusing node subgraphs or command templates
- **Localization**: Simplified Chinese / English UI, runtime switching (resx + external JSON)
- **Startup Optimization**: Splash progress bar + background parallel thumbnail generation
- **Resolution Adaptive**: Auto-calculates panel layout and thumbnail size based on screen resolution
- **Experience Learning**: Learns user preferences from AI interactions to optimize few-shot example selection

## Project Structure

```
像素素材生成器/
├── Core/                    # Core Engine
│   ├── Gpu/                 # Direct3D 11 GPU Acceleration (Vortice/SharpDX)
│   ├── Animation/           # Animation Engine
│   ├── Particles/           # Particle System
│   ├── Physics/             # Physics Engine
│   ├── Nodes/Sources/       # Node Sources (built-in/file/script/dynamic)
│   ├── GraphNodeRegistry.cs # Node Registry (loads from .node.json)
│   ├── NodeGraphEvaluator.cs# Topological Sort O(N+E) + Parallel Evaluation
│   ├── PixelBuffer.cs       # Pixel Buffer (zero-alloc GPU interop)
│   └── ...
├── Generators/              # Tile Generators (compiled C#)
├── Resources/               # App resources, localization, node definitions
├── Nodes/                   # 19 extension nodes (animation/particle/physics)
├── Services/                # Service Layer (DI container + AI services)
├── Models/                  # Data models
├── Controls/                # Custom WPF controls
├── Converters/              # Value converters
├── AI/                      # AI settings UI
├── MainWindow.*             # Main Window (8 partial files, ~14K lines)
└── ShapeDrawingWindow.*     # Shape Drawing Window
```

## Quick Start

```bash
dotnet build
dotnet run --project 像素素材生成器
```

## Node System

### Port Types
- **Image**: RGBA pixel buffer (float precision 0-1)
- **Mask**: Single-channel grayscale mask
- **Float**: Floating point value
- **Color**: RGBA color value
- **Any**: Any type (tool/preview nodes)

### Parameter Types
- **Number**: Float (min/max/step/default)
- **Integer**: Integer (min/max/step/default)
- **Choice**: Enum selection (bilingual display labels)
- **Boolean**: Toggle
- **Color**: Color picker
- **Seed**: Random seed
- **PointList**: Point coordinate list
- **Text**: Text string

### Node Categories

| Category | Count | Description |
|----------|-------|-------------|
| Generate | 5 | Tile generation |
| ImageProcess | 42 | Image processing filters |
| Logic | 14 | Logic flow control |
| Material | 32 | Material texture generation |
| Noise | 16 | Noise and organic textures |
| Pattern | 20 | Pattern and shape generation |
| Tool | 3 | Tools (annotation/output/preview) |
| Animation | 8 | Parameter animation/audio reactive |
| Particle | 7 | Particle system |
| Physics | 4 | Physics simulation |

## How to Contribute

Contributions are welcome! Whether it's fixing bugs, adding new nodes, improving docs, or suggesting features.

- [Contributing Guide](CONTRIBUTING_EN.md) — How to submit PRs, code standards
- [Code of Conduct](CODE_OF_CONDUCT.md) — Community guidelines
- [Report a Bug](https://github.com) — Open an Issue (templates in `.github/ISSUE_TEMPLATE/`)
- [Suggest a Feature](https://github.com) — Discuss before implementing

### Quick Entry

| What you want to do | Entry |
|---------------------|-------|
| Report a Bug | `Issues` → `New Issue` → `Bug Report` |
| Submit Code | `Fork` → `PR` |
| Add a New Node | See [Node Reference](docs/NODE_REFERENCE_EN.md) |
| Improve Translations | Edit JSON files under `Resources/Languages/` |

## Documentation

- `docs/ARCHITECTURE.md` — Project Architecture (Chinese)
- `docs/ARCHITECTURE_EN.md` — Project Architecture (English)
- `docs/NODE_REFERENCE.md` — Node Reference (Chinese)
- `docs/NODE_REFERENCE_EN.md` — Node Reference (English)
- `docs/BUILD.md` — Build Guide (Chinese)
- `docs/BUILD_EN.md` — Build Guide (English)

## Build Requirements

- .NET 9 SDK
- Windows 7+ (WPF dependency)
- Visual Studio 2022 (optional)

## License

This project is licensed under the **GNU General Public License v3.0 (GPL v3)**.

This means:
- ✅ You may freely use, modify, and distribute this software
- ✅ You may modify the code using AI tools
- ✅ Commercial and personal use is allowed
- ❌ Modified versions must also be open source under GPL v3 (copyleft)
- ❌ You cannot distribute modified versions as closed-source software

See [LICENSE](LICENSE) (English) and [LICENSE_ZH](LICENSE_ZH) (Chinese) for details.
