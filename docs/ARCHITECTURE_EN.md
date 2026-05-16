# Pixel Asset Generator — Architecture Document

## Tech Stack
- **Language**: C# 13 (.NET 9)
- **UI Framework**: WPF (Windows 7+)
- **GPU Acceleration**: Vortice.Direct3D 11 (optional, via VORTICE compile symbol) + SharpDX.Direct3D9/Direct3D11/DXGI (D3DImage interop)
- **AI Integration**: OpenAI / Anthropic Claude API
- **Dynamic Compilation**: Roslyn (Microsoft.CodeAnalysis.CSharp) 4.14.0
- **DI Container**: Microsoft.Extensions.DependencyInjection 10.0.8

## Project Structure

```
像素素材生成器/
├── Core/                    # Core Engine
│   ├── Gpu/                 # GPU Acceleration Subsystem
│   │   ├── GpuCompute.cs        # GPU compute core (VORTICE, zero-alloc buffer transfer)
│   │   ├── GpuCompiler.cs       # HLSL shader compilation and caching
│   │   ├── GpuBufferHelpers.cs  # GPU buffer utilities
│   │   ├── GpuScheduler.cs      # GPU scheduler (non-blocking UI rendering)
│   │   ├── Shaders.hlsl         # 8 compute shader entry points
│   │   ├── IGpuAcceleratedNode.cs   # GPU accelerated node interface
│   │   ├── IGpuNativeNode.cs        # Native GPU node interface
│   │   ├── NodeGpuGraphEvaluator.cs # GPU graph evaluator
│   │   └── NodeGpuPreviewManager.cs # GPU preview manager
│   ├── Animation/           # Animation Engine
│   │   ├── AnimationClip.cs          # Animation clip (frame list/mode/framerate)
│   │   ├── AnimationEvaluator.cs     # Animation evaluator
│   │   ├── Easing.cs                # Easing functions
│   │   └── Nodes/                    # 8 animation nodes
│   │       ├── TimeNode.cs              # Time node
│   │       ├── AnimatedParameterNode.cs # Parameter animation
│   │       ├── AnimationWaveNode.cs     # Wave animation
│   │       ├── AnimationNoiseNode.cs    # Noise animation
│   │       ├── AnimationPathNode.cs     # Path animation
│   │       ├── AnimationSequencerNode.cs# Sequencer animation
│   │       ├── FrameBlendNode.cs        # Frame blending
│   │       └── AudioReactiveNode.cs     # Audio reactive
│   ├── Particles/           # Particle System
│   │   ├── ParticleData.cs           # Particle data structure
│   │   ├── ParticleEmitter.cs        # Particle emitter
│   │   ├── ParticleSimulator.cs      # Particle simulator (position/velocity/lifetime)
│   │   ├── ParticleRenderer.cs       # Particle renderer
│   │   └── Nodes/                    # 7 particle nodes
│   │       ├── ParticleEmitterNode.cs    # Particle emitter node
│   │       ├── ParticleForceNode.cs      # Particle force field node
│   │       ├── ParticleCollisionNode.cs  # Particle collision node
│   │       ├── ParticleTrailNode.cs      # Particle trail node
│   │       ├── ParticleLightNode.cs      # Particle lighting node
│   │       ├── ParticleRenderNode.cs     # Particle render node
│   │       └── InteractiveForceNode.cs   # Interactive force field node
│   ├── Physics/             # Physics Engine
│   │   ├── PhysicsWorld.cs           # Physics world (spatial partitioning/iterative solver)
│   │   ├── PhysicsBody.cs            # Physics body definition
│   │   ├── CollisionDetection.cs     # Collision detection
│   │   ├── Constraints.cs            # Constraint solver
│   │   └── Nodes/                    # 4 physics nodes
│   │       ├── PhysicsSimulateNode.cs    # Physics simulation node
│   │       ├── PhysicsConstraintNode.cs  # Physics constraint node
│   │       ├── PhysicsFieldNode.cs       # Physics force field node
│   │       └── PhysicsSoftBodyNode.cs    # Physics soft body node
│   ├── Nodes/Sources/       # Node Source Management
│   │   ├── INodeSource.cs          # Node source interface
│   │   ├── BuiltInNodeSource.cs    # Built-in node source
│   │   ├── DynamicNodeSource.cs    # Dynamic script node source
│   │   ├── FileNodeSource.cs       # File node source
│   │   └── ResourceNodeInstance.cs # Resource node instance (Roslyn compile + disk cache)
│   ├── GpuProcessor.cs          # CPU fallback GPU processing (conditional compilation)
│   ├── GraphNodeBase.cs         # Node base class
│   ├── GraphNodeRegistry.cs     # Node registry (loads from .node.json)
│   ├── IGraphNode.cs            # Node interface
│   ├── NodeGraphEvaluator.cs    # Topological sort (Kahn O(N+E)) + parallel evaluation
│   ├── PixelBuffer.cs           # Pixel buffer (CopyFrom/CopyTo zero-alloc GPU interop)
│   ├── PixelBufferPool.cs       # Pixel buffer pool
│   ├── PixelGraphContext.cs     # Graph evaluation context
│   ├── PixelRaster.cs           # Pixel rasterization
│   └── PreviewMode.cs           # Preview mode enum
├── Generators/              # Tile Generators (compiled C#)
│   ├── ITileGenerator.cs        # Tile generator interface
│   ├── BaseTileGenerator.cs     # Tile generator base class
│   ├── GrassGenerator.cs        # Grass tile
│   ├── StoneGenerator.cs        # Stone tile
│   ├── WaterGenerator.cs        # Water tile
│   ├── RoadGenerator.cs         # Road tile
│   └── SandGenerator.cs         # Sand tile
├── Interop/                 # Native Interop
│   └── D3D11SwapChainHost.cs    # D3D11 ←→ WPF swap chain host
├── Models/                  # Data Models (11 files)
│   ├── AiSettings.cs            # AI settings model
│   ├── AiPlanModels.cs          # AI plan models
│   ├── ChatMessage.cs           # Chat message
│   ├── ChatSession.cs           # Chat session
│   ├── ConsoleEntry.cs          # Console entry
│   ├── CreationSpec.cs          # Creation specification
│   ├── EffectRecipe.cs          # Effect recipe
│   ├── MdTaskPlan.cs            # Markdown task plan
│   ├── NodeResource.cs          # Node resource model (NodeLocText multi-language)
│   ├── SkillDefinition.cs       # Skill definition (built-in/user, with IsBuiltIn flag)
│   └── AestheticScore.cs        # Aesthetic score model
├── Services/                # Service Layer
│   ├── ServiceLocator.cs        # DI container (Microsoft.Extensions.DependencyInjection)
│   ├── IConsoleService.cs       # Console service interface
│   ├── IStreamingChatClient.cs  # Streaming chat interface
│   ├── IToolProvider.cs         # AI tool provider interface
│   ├── Clients/                 # AI API clients
│   │   ├── OpenAiChatClient.cs       # OpenAI API client
│   │   └── AnthropicChatClient.cs    # Anthropic Claude API client
│   ├── Learning/                 # Experience learning system
│   │   ├── ExperienceDb.cs          # Experience database
│   │   ├── ExperienceTracker.cs     # Experience tracker
│   │   ├── FewShotSelector.cs       # Few-shot example selector
│   │   ├── Models.cs                # Learning data models
│   │   └── UserProfileService.cs    # User profile service
│   ├── Localization/              # Localization
│   │   ├── ILocalizationService.cs    # Localization service interface
│   │   └── LocalizationService.cs     # Implementation (resx + external JSON)
│   ├── ToolProviders/             # AI tool providers (7)
│   │   ├── GraphToolProvider.cs        # Node graph CRUD tools
│   │   ├── SkillToolProvider.cs        # Skill management tools
│   │   ├── DynamicNodeToolProvider.cs  # Dynamic script node tools
│   │   ├── ResourceNodeToolProvider.cs # .node.json node management
│   │   ├── PlanToolProvider.cs         # Plan management tools
│   │   ├── AestheticToolProvider.cs    # Aesthetic evaluation tools
│   │   └── RecipeToolProvider.cs       # Effect recipe tools
│   ├── PixelAgentService.cs       # AI 3-phase orchestration (Research→Plan→Execute→Report)
│   ├── PlanManager.cs              # Plan lifecycle management + MD file persistence
│   ├── AiService.cs               # AI service entry point (HttpClient reuse)
│   ├── AiConfigManager.cs         # AI configuration management
│   ├── AiContextBuilder.cs        # AI context builder
│   ├── AiToolService.cs           # AI tool service
│   ├── AnimationPlaybackService.cs # Animation playback (frame sequence/loop/ping-pong/single)
│   ├── ConversationHistoryManager.cs # Conversation history management (token-aware trimming)
│   ├── DynamicNodeService.cs      # Dynamic node service
│   ├── ExportService.cs           # Export service
│   ├── GraphEvaluationService.cs  # Graph evaluation service
│   ├── IntentionParser.cs         # User intention parser
│   ├── MdPlanParser.cs            # Markdown plan parser
│   ├── NodeGraphController.cs     # Node graph controller
│   ├── NodeLibraryService.cs      # Node library service (bilingual descriptions)
│   ├── NodeResourceRegistry.cs    # Node resource registry
│   ├── OutputParser.cs            # AI output parser
│   ├── ParticleEvaluationService.cs # Particle evaluation service
│   ├── PinyinHelper.cs            # Pinyin helper
│   ├── PixelPostProcessor.cs      # Pixel post-processor
│   ├── ProjectFileService.cs      # Project file service (.pxtile)
│   ├── SkillLoader.cs             # Skill loader
│   ├── SkillMarkdownParser.cs     # Skill Markdown parser
│   ├── SkillService.cs            # Skill management (33 built-in skills)
│   ├── UndoRedoService.cs         # Undo/Redo
│   └── AestheticEvaluator.cs      # Aesthetic evaluator
├── Controls/                # Custom WPF Controls
│   ├── AiMessageTemplateSelector.cs    # AI message template selector
│   ├── AiSessionListControl.cs        # AI session list control
│   ├── ConsoleControl.xaml(.cs)       # Console control
│   ├── MarkdownViewer.cs              # Markdown renderer
│   ├── NodePreviewControl.cs          # Node preview control
│   └── PlanViewer.cs                  # Plan viewer
├── Converters/               # Value Converters
│   ├── ParameterMaxConverter.cs       # Parameter max value converter
│   └── SliderFillWidthConverter.cs    # Slider fill width converter
├── Utilities/                # Utilities
│   └── AiHelpers.cs                  # AI helper methods
├── AI/                       # AI Settings UI
│   └── AiSettingsWindow.xaml(.cs)
├── Resources/                # Application Resources
│   ├── AppResources.xaml         # App resource dictionary (themes/styles)
│   ├── Strings.en.resx           # English resources (~440 strings)
│   ├── Strings.zh-Hans.resx      # Simplified Chinese (~440 strings, fallback language)
│   ├── Languages/                # External JSON localization files
│   │   ├── en.json
│   │   └── zh-Hans.json
│   └── Nodes/                    # 136 .node.json node definitions
│       ├── Generate/             # 5
│       ├── ImageProcess/         # 42
│       ├── Logic/                # 14
│       ├── Material/             # 32
│       ├── Noise/                # 16
│       ├── Pattern/              # 20
│       └── Tool/                 # 3
├── Nodes/                    # Extension nodes (19 .node.json)
│   ├── Animation/            # 8 animation nodes
│   ├── Particle/             # 7 particle nodes
│   └── Physics/              # 4 physics nodes
├── MainWindow.*              # Main Window (8 partial files, ~14K lines)
│   ├── MainWindow.xaml(.cs)       # Main window base (XAML 3.6K lines, code-behind 3.2K lines)
│   ├── MainWindow.Export.cs      # Export functionality
│   ├── MainWindow.IO.cs          # File I/O
│   ├── MainWindow.Library.cs     # Node library management
│   ├── MainWindow.NodeCanvas.cs  # Node canvas
│   ├── MainWindow.Nodes.cs       # Node operations
│   └── MainWindow.Preview.cs     # Preview panel
├── ShapeDrawingWindow.*      # Shape Drawing Window (6 partial files)
│   ├── ShapeDrawingWindow.xaml(.cs)
│   ├── ShapeDrawingWindow.Drawing.cs    # Drawing logic
│   ├── ShapeDrawingWindow.Input.cs      # Input handling
│   ├── ShapeDrawingWindow.Layers.cs     # Layer management
│   ├── ShapeDrawingWindow.Models.cs     # Shape models
│   ├── ShapeDrawingWindow.PathEdit.cs   # Path editing
│   └── ShapeDrawingWindow.Rendering.cs  # Rendering
├── App.xaml(.cs)             # App entry point (Splash + DI init + global exception handling)
├── AppSettings.cs            # Application settings
├── AiChatViewModel.cs        # AI chat view model
├── ColorPickerDialog.xaml(.cs)    # Color picker
├── ColorWheelWindow.xaml(.cs)     # Color wheel window
├── DarkInputBox.xaml.cs           # Dark input box
├── DarkMessageBox.xaml.cs         # Dark message box
├── ExportOptionsDialog.xaml.cs    # Export options dialog
├── InfiniteGrid.cs                # Infinite grid control
├── NodeConnectionViewModel.cs     # Node connection view model
├── NodeLibraryCategory.cs         # Node library category
├── NodeLibraryEntry.cs            # Node library entry
├── NodeLibraryEntryTemplateSelector.cs # Node library template selector
├── NodeLibraryItem.cs             # Node library item
├── NodeParameterModels.cs         # Node parameter models
├── NodeViewModel.cs               # Node view model
├── SettingsWindow.xaml(.cs)       # Settings window
├── SplashWindow.xaml(.cs)         # Splash screen
├── TileCommon.cs                  # Tile common
├── TileGenerator.cs               # Tile generator
├── TileProperties.cs              # Tile properties
├── ColorConverters.cs             # Color conversion
├── InverseScaleConverter.cs       # Scale converter
├── MaxSlider.cs                   # Max slider
├── ThumbnailSizeConverter.cs      # Thumbnail size converter
└── 像素素材生成器.csproj         # Project file (AssemblyName: PixelAssetGenerator)
```

## Key Architecture Decisions

### 0.5.x → 0.6.0 Major Changes

| Change | Description |
|--------|-------------|
| **DI Container** | ServiceLocator + Microsoft.Extensions.DependencyInjection |
| **Topological Sort Optimization** | Kahn algorithm from O(N\*E) to O(N+E), ~60x speedup for 100-node graphs |
| **GPU Zero-Alloc Transfer** | PixelBuffer.CopyFrom/CopyTo eliminate `AsSpan().ToArray()` full copy |
| **Non-blocking GPU Rendering** | Dispatcher.Invoke → BeginInvoke, no longer blocks GPU scheduler |
| **Localization** | Bilingual + resx/JSON dual backend + language variant fallback |
| **Smart Layout** | Auto-calculates panel width and thumbnail size based on screen resolution |
| **Animation/Particles/Physics** | New Core/Animation, Core/Particles, Core/Physics subsystems |

### Dependency Injection

Current state: ServiceLocator acts as a transitional container. Gradually migrating services to DI:
- `ILocalizationService` — ✅ DI injected
- `ISettingsService` — ⏳ Pending
- `IGraphEvaluationService`, `INodeGraphController` — ⏳ Pending

### Node Evaluation Flow
1. Topological sort (Kahn algorithm O(N+E) + cycle detection)
2. Level assignment (same-level nodes have no dependencies)
3. `Parallel.ForEach` layer-by-layer parallel execution
4. Per node: GPU path (VORTICE) → CPU fallback path
5. Source node caching (deep copy to avoid pool recycling races)

## AI System

### Execution Flow (3-Phase)

```
User message → Phase 0: Research → Phase 1: Plan → Phase 2: Execute → Report
```

#### Phase 0: Research
- AI analyzes requirements using the inlined full node library (no tool queries needed)
- Outputs research report and injects into PlanManager.ResearchContext

#### Phase 1: Plan
- Uses `BuildPlanningPrompt` to generate an MD plan (`[plan_start]...[plan_end]` format)
- Parsed by `MdPlanParser` into `ActivePlan`
- Plan saved in both JSON and MD formats to the Plans/ directory

#### Phase 2: Execute
- Each step loop: inject current step prompt → AI calls tools → execute → process results
- Auto-retry mechanism (retry with library search when node not found, retry with detail query on port errors)
- **Real-time MD file updates**: auto-updates progress on each step advancement
- Generates execution report `report_xxx.md` on completion

### Tool Providers (7)

- **GraphToolProvider**: Node/connection CRUD
- **SkillToolProvider**: Skill management (33 built-in skills)
- **DynamicNodeToolProvider**: Dynamic script nodes
- **ResourceNodeToolProvider**: .node.json node management
- **PlanToolProvider**: Plan management (update_plan / get_plan)
- **AestheticToolProvider**: Aesthetic evaluation
- **RecipeToolProvider**: Effect recipe generation and application

### Built-in Skills

| Skill | Description |
|-------|-------------|
| Create Custom Node | Create nodes via .node.json |
| Bash Commands | Unix shell commands |
| CMD Commands | Windows CMD commands |
| Python Scripts | Python batch processing |
| Export Tiles | Export image files |
| Batch Export | Multi-seed batch export |
| Create Node from Script | Create nodes from C# scripts |
| Graph Template | Save sub-graph templates |
| Project File | .pxtile project file operations |
| *24 more* | ... |

### Experience Learning
ExperienceDb + FewShotSelector + UserProfileService: automatically learns user preferences from AI interactions to optimize few-shot example selection.

## GPU Subsystem

GPU acceleration is controlled by the VORTICE compile symbol:

| Symbol | Effect |
|--------|--------|
| Undefined | CPU fallback path (SharpDX D3DImage interop still available) |
| `VORTICE` | Enables Vortice.Direct3D 11 compute shader path |

PixelBuffer adds `CopyFrom(IntPtr, int)` / `CopyTo(IntPtr, int)` methods, using `Buffer.MemoryCopy` for zero-alloc GPU buffer interop.

HLSL shader includes 8 compute entry points: `CS_SolidColorMain`, `CS_GradientMain`, `CS_NoiseMain`, `CS_FibersMain`, `CS_WeaveMain`, `CS_ConvolutionMain`, `CS_ColorAdjustMain`, `CS_ShapeMain`.

## Localization System

- **Dual Backend**: Built-in `.resx` + external `JSON` (%APPDATA%/PixelAssetGenerator/Languages/*.json)
- **Auto Language Discovery**: Scans `.resources` files from assembly manifest, auto-discovers built-in languages
- **Language Variant Negotiation**: `fr-CA` → `fr` → `zh-Hans` → `en` → any
- **CultureInfo Thread-level**: Uses `Thread.CurrentThread.CurrentUICulture`

## Startup Flow
1. `App.OnStartup` — Initialize DI container, apply theme/language
2. Show Splash window
3. Async initialization pipeline:
   - Render engine and node controller
   - Calculate left panel width based on screen `WorkArea.Width * 28%` (clamped 260-480)
   - Calculate thumbnail size based on panel width (4-column layout)
   - Load node library, compile script nodes
   - Background parallel thumbnail generation for all nodes
4. Hide Splash, show main window
5. On window close, persist thumbnail size to `settings.json`

## Animation System

DispatcherTimer-based frame playback:
- AnimationPlaybackService: play/pause/loop/ping-pong/single
- Auto-detects animation nodes on canvas and shows control bar
- Adjustable frame rate (6-30 FPS)
- Supports parameter animation, path animation, wave animation, noise animation, sequencer animation, frame blending, audio reactive
