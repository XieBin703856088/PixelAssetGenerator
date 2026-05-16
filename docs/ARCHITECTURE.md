# 像素素材生成器 — 项目架构文档

## 技术栈
- **语言**: C# 13 (.NET 9)
- **UI 框架**: WPF (Windows 7+)
- **GPU 加速**: Vortice.Direct3D 11 (可选，通过 VORTICE 编译符号启用) + SharpDX.Direct3D9/Direct3D11/DXGI (D3DImage 互操作)
- **AI 集成**: OpenAI / Anthropic Claude API
- **动态编译**: Roslyn (Microsoft.CodeAnalysis.CSharp) 4.14.0
- **DI 容器**: Microsoft.Extensions.DependencyInjection 10.0.8

## 项目结构

```
像素素材生成器/
├── Core/                    # 核心引擎
│   ├── Gpu/                 # GPU 加速子系统
│   │   ├── GpuCompute.cs        # GPU 计算核心 (VORTICE，零分配缓冲传输)
│   │   ├── GpuCompiler.cs       # HLSL 着色器编译与缓存
│   │   ├── GpuBufferHelpers.cs  # GPU 缓冲区工具
│   │   ├── GpuScheduler.cs      # GPU 调度器（非阻塞 UI 呈现）
│   │   ├── Shaders.hlsl         # 8 个计算着色器入口点
│   │   ├── IGpuAcceleratedNode.cs   # GPU 加速节点接口
│   │   ├── IGpuNativeNode.cs        # 原生 GPU 节点接口
│   │   ├── NodeGpuGraphEvaluator.cs # GPU 图求值器
│   │   └── NodeGpuPreviewManager.cs # GPU 预览管理器
│   ├── Animation/           # 动画引擎
│   │   ├── AnimationClip.cs          # 动画剪辑（帧列表/模式/帧率）
│   │   ├── AnimationEvaluator.cs     # 动画求值器
│   │   ├── Easing.cs                # 缓动函数
│   │   └── Nodes/                    # 8 个动画节点
│   │       ├── TimeNode.cs              # 时间节点
│   │       ├── AnimatedParameterNode.cs # 参数动画
│   │       ├── AnimationWaveNode.cs     # 波形动画
│   │       ├── AnimationNoiseNode.cs    # 噪声动画
│   │       ├── AnimationPathNode.cs     # 路径动画
│   │       ├── AnimationSequencerNode.cs# 序列动画
│   │       ├── FrameBlendNode.cs        # 帧混合
│   │       └── AudioReactiveNode.cs     # 音频响应
│   ├── Particles/           # 粒子系统
│   │   ├── ParticleData.cs           # 粒子数据结构
│   │   ├── ParticleEmitter.cs        # 粒子发射器
│   │   ├── ParticleSimulator.cs      # 粒子模拟器（位置/速度/生命周期）
│   │   ├── ParticleRenderer.cs       # 粒子渲染器
│   │   └── Nodes/                    # 7 个粒子节点
│   │       ├── ParticleEmitterNode.cs    # 粒子发射器节点
│   │       ├── ParticleForceNode.cs      # 粒子力场节点
│   │       ├── ParticleCollisionNode.cs  # 粒子碰撞节点
│   │       ├── ParticleTrailNode.cs      # 粒子轨迹节点
│   │       ├── ParticleLightNode.cs      # 粒子光照节点
│   │       ├── ParticleRenderNode.cs     # 粒子渲染节点
│   │       └── InteractiveForceNode.cs   # 交互式力场节点
│   ├── Physics/             # 物理引擎
│   │   ├── PhysicsWorld.cs           # 物理世界（空间划分/迭代求解）
│   │   ├── PhysicsBody.cs            # 物理体定义
│   │   ├── CollisionDetection.cs     # 碰撞检测
│   │   ├── Constraints.cs            # 约束求解
│   │   └── Nodes/                    # 4 个物理节点
│   │       ├── PhysicsSimulateNode.cs    # 物理模拟节点
│   │       ├── PhysicsConstraintNode.cs  # 物理约束节点
│   │       ├── PhysicsFieldNode.cs       # 物理力场节点
│   │       └── PhysicsSoftBodyNode.cs    # 物理软体节点
│   ├── Nodes/Sources/       # 节点源管理
│   │   ├── INodeSource.cs          # 节点源接口
│   │   ├── BuiltInNodeSource.cs    # 内置节点源
│   │   ├── DynamicNodeSource.cs    # 动态脚本节点源
│   │   ├── FileNodeSource.cs       # 文件节点源
│   │   └── ResourceNodeInstance.cs # 资源节点实例（Roslyn 编译 + 磁盘缓存）
│   ├── GpuProcessor.cs          # CPU 回退 GPU 处理 (条件编译)
│   ├── GraphNodeBase.cs         # 节点基类
│   ├── GraphNodeRegistry.cs     # 节点注册表（从 .node.json 加载）
│   ├── IGraphNode.cs            # 节点接口
│   ├── NodeGraphEvaluator.cs    # 拓扑排序 (Kahn O(N+E)) + 并行求值
│   ├── PixelBuffer.cs           # 像素缓冲区（CopyFrom/CopyTo 零分配 GPU 互操作）
│   ├── PixelBufferPool.cs       # 像素缓冲池
│   ├── PixelGraphContext.cs     # 图求值上下文
│   ├── PixelRaster.cs           # 像素光栅化
│   └── PreviewMode.cs           # 预览模式枚举
├── Generators/              # 瓦片生成器（编译型 C#）
│   ├── ITileGenerator.cs        # 瓦片生成器接口
│   ├── BaseTileGenerator.cs     # 瓦片生成器基类
│   ├── GrassGenerator.cs        # 草地瓦片
│   ├── StoneGenerator.cs        # 石块瓦片
│   ├── WaterGenerator.cs        # 水瓦片
│   ├── RoadGenerator.cs         # 道路瓦片
│   └── SandGenerator.cs         # 沙地瓦片
├── Interop/                 # 原生互操作
│   └── D3D11SwapChainHost.cs    # D3D11 ←→ WPF 交换链宿主
├── Models/                  # 数据模型（11 个文件）
│   ├── AiSettings.cs            # AI 设置模型
│   ├── AiPlanModels.cs          # AI 计划模型
│   ├── ChatMessage.cs           # 聊天消息
│   ├── ChatSession.cs           # 聊天会话
│   ├── ConsoleEntry.cs          # 控制台条目
│   ├── CreationSpec.cs          # 创建规范
│   ├── EffectRecipe.cs          # 效果配方
│   ├── MdTaskPlan.cs            # Markdown 任务计划
│   ├── NodeResource.cs          # 节点资源模型（NodeLocText 多语言）
│   ├── SkillDefinition.cs       # 技能定义（内置/用户，含 IsBuiltIn 标记）
│   └── AestheticScore.cs        # 美学评分模型
├── Services/                # 服务层
│   ├── ServiceLocator.cs        # DI 容器（Microsoft.Extensions.DependencyInjection）
│   ├── IConsoleService.cs       # 控制台服务接口
│   ├── IStreamingChatClient.cs  # 流式聊天接口
│   ├── IToolProvider.cs         # AI 工具提供者接口
│   ├── Clients/                 # AI API 客户端
│   │   ├── OpenAiChatClient.cs       # OpenAI API 客户端
│   │   └── AnthropicChatClient.cs    # Anthropic Claude API 客户端
│   ├── Learning/                 # 经验学习系统
│   │   ├── ExperienceDb.cs          # 经验数据库
│   │   ├── ExperienceTracker.cs     # 经验追踪器
│   │   ├── FewShotSelector.cs       # Few-shot 示例选择
│   │   ├── Models.cs                # 学习数据模型
│   │   └── UserProfileService.cs    # 用户画像服务
│   ├── Localization/              # 本地化
│   │   ├── ILocalizationService.cs    # 本地化服务接口
│   │   └── LocalizationService.cs     # 实现（resx + 外部 JSON）
│   ├── ToolProviders/             # AI 工具提供者（7 个）
│   │   ├── GraphToolProvider.cs        # 节点图 CRUD 工具
│   │   ├── SkillToolProvider.cs        # 技能管理工具
│   │   ├── DynamicNodeToolProvider.cs  # 动态脚本节点工具
│   │   ├── ResourceNodeToolProvider.cs # .node.json 节点管理
│   │   ├── PlanToolProvider.cs         # 计划管理工具
│   │   ├── AestheticToolProvider.cs    # 美学评估工具
│   │   └── RecipeToolProvider.cs       # 效果配方工具
│   ├── PixelAgentService.cs       # AI 三阶段编排（调研→计划→执行→报告）
│   ├── PlanManager.cs              # 计划生命周期管理 + MD文件持久化
│   ├── AiService.cs               # AI 服务入口（HttpClient 复用）
│   ├── AiConfigManager.cs         # AI 配置管理
│   ├── AiContextBuilder.cs        # AI 上下文构建
│   ├── AiToolService.cs           # AI 工具服务
│   ├── AnimationPlaybackService.cs # 动画播放（帧序列/循环/乒乓/单次）
│   ├── ConversationHistoryManager.cs # 对话历史管理（token感知裁剪）
│   ├── DynamicNodeService.cs      # 动态节点服务
│   ├── ExportService.cs           # 导出服务
│   ├── GraphEvaluationService.cs  # 图求值服务
│   ├── IntentionParser.cs         # 用户意图解析器
│   ├── MdPlanParser.cs            # Markdown 计划解析
│   ├── NodeGraphController.cs     # 节点图控制器
│   ├── NodeLibraryService.cs      # 节点库服务（中英双语描述）
│   ├── NodeResourceRegistry.cs    # 节点资源注册表
│   ├── OutputParser.cs            # AI 输出解析
│   ├── ParticleEvaluationService.cs # 粒子求值服务
│   ├── PinyinHelper.cs            # 拼音辅助
│   ├── PixelPostProcessor.cs      # 像素后处理器
│   ├── ProjectFileService.cs      # 项目文件服务 (.pxtile)
│   ├── SkillLoader.cs             # 技能加载器
│   ├── SkillMarkdownParser.cs     # 技能 Markdown 解析器
│   ├── SkillService.cs            # 技能管理（33 个内置技能）
│   ├── UndoRedoService.cs         # 撤销/重做
│   └── AestheticEvaluator.cs      # 美学评估器
├── Controls/                # 自定义 WPF 控件
│   ├── AiMessageTemplateSelector.cs    # AI 消息模板选择器
│   ├── AiSessionListControl.cs        # AI 会话列表控件
│   ├── ConsoleControl.xaml(.cs)       # 控制台控件
│   ├── MarkdownViewer.cs              # Markdown 渲染器
│   ├── NodePreviewControl.cs          # 节点预览控件
│   └── PlanViewer.cs                  # 计划查看器
├── Converters/               # 值转换器
│   ├── ParameterMaxConverter.cs       # 参数最大值转换器
│   └── SliderFillWidthConverter.cs    # 滑块填充宽度转换器
├── Utilities/                # 工具类
│   └── AiHelpers.cs                  # AI 辅助方法
├── AI/                       # AI 设置界面
│   └── AiSettingsWindow.xaml(.cs)
├── Resources/                # 应用资源
│   ├── AppResources.xaml         # 应用资源字典（主题/样式）
│   ├── Strings.en.resx           # 英语资源（~440 条）
│   ├── Strings.zh-Hans.resx      # 简体中文（~440 条，回退语言）
│   ├── Languages/                # 外部 JSON 本地化文件
│   │   ├── en.json
│   │   └── zh-Hans.json
│   └── Nodes/                    # 136 个 .node.json 节点定义
│       ├── Generate/             # 5
│       ├── ImageProcess/         # 42
│       ├── Logic/                # 14
│       ├── Material/             # 32
│       ├── Noise/                # 16
│       ├── Pattern/              # 20
│       └── Tool/                 # 3
├── Nodes/                    # 扩展节点（19 个 .node.json）
│   ├── Animation/            # 8 个动画节点
│   ├── Particle/             # 7 个粒子节点
│   └── Physics/              # 4 个物理节点
├── MainWindow.*              # 主窗口（8 个 partial 文件，~14K 行）
│   ├── MainWindow.xaml(.cs)       # 主窗口基础（XAML 3.6K 行，code-behind 3.2K 行）
│   ├── MainWindow.Export.cs      # 导出功能
│   ├── MainWindow.IO.cs          # 文件 I/O
│   ├── MainWindow.Library.cs     # 节点库管理
│   ├── MainWindow.NodeCanvas.cs  # 节点画布
│   ├── MainWindow.Nodes.cs       # 节点操作
│   └── MainWindow.Preview.cs     # 预览面板
├── ShapeDrawingWindow.*      # 形状绘制窗口（6 个 partial 文件）
│   ├── ShapeDrawingWindow.xaml(.cs)
│   ├── ShapeDrawingWindow.Drawing.cs    # 绘制逻辑
│   ├── ShapeDrawingWindow.Input.cs      # 输入处理
│   ├── ShapeDrawingWindow.Layers.cs     # 图层管理
│   ├── ShapeDrawingWindow.Models.cs     # 形状模型
│   ├── ShapeDrawingWindow.PathEdit.cs   # 路径编辑
│   └── ShapeDrawingWindow.Rendering.cs  # 渲染
├── App.xaml(.cs)             # 应用入口（Splash + DI 初始化 + 全局异常处理）
├── AppSettings.cs            # 应用设置
├── AiChatViewModel.cs        # AI 聊天视图模型
├── ColorPickerDialog.xaml(.cs)    # 颜色选择器
├── ColorWheelWindow.xaml(.cs)     # 色轮窗口
├── DarkInputBox.xaml.cs           # 深色输入框
├── DarkMessageBox.xaml.cs         # 深色消息框
├── ExportOptionsDialog.xaml.cs    # 导出选项对话框
├── InfiniteGrid.cs                # 无限网格控件
├── NodeConnectionViewModel.cs     # 节点连接视图模型
├── NodeLibraryCategory.cs         # 节点库分类
├── NodeLibraryEntry.cs            # 节点库条目
├── NodeLibraryEntryTemplateSelector.cs # 节点库模板选择器
├── NodeLibraryItem.cs             # 节点库项
├── NodeParameterModels.cs         # 节点参数模型
├── NodeViewModel.cs               # 节点视图模型
├── SettingsWindow.xaml(.cs)       # 设置窗口
├── SplashWindow.xaml(.cs)         # 启动画面
├── TileCommon.cs                  # 瓦片公共
├── TileGenerator.cs               # 瓦片生成器
├── TileProperties.cs              # 瓦片属性
├── ColorConverters.cs             # 颜色转换
├── InverseScaleConverter.cs       # 缩放转换器
├── MaxSlider.cs                   # 最大滑块
├── ThumbnailSizeConverter.cs      # 缩略图大小转换器
└── 像素素材生成器.csproj         # 项目文件（AssemblyName: PixelAssetGenerator）
```

## 核心架构决策

### 0.5.x → 0.6.0 关键变更

| 变更 | 说明 |
|------|------|
| **DI 容器引入** | ServiceLocator + Microsoft.Extensions.DependencyInjection |
| **拓扑排序优化** | Kahn 算法从 O(N*E) 优化到 O(N+E)，百节点图加速约 60x |
| **GPU 零分配传输** | PixelBuffer.CopyFrom/CopyTo 消除 `AsSpan().ToArray()` 全量拷贝 |
| **非阻塞 GPU 呈现** | Dispatcher.Invoke → BeginInvoke，不阻塞 GPU 调度器 |
| **本地化完善** | 中英双语 + resx/JSON 双后端 + 语言变体回退 |
| **智能布局** | 启动时根据屏幕分辨率计算面板宽度和缩略图大小 |
| **动画/粒子/物理** | 新增 Core/Animation、Core/Particles、Core/Physics 子系统 |

### 依赖注入

当前状态：ServiceLocator 作为过渡容器，逐步迁移更多服务到 DI：
- `ILocalizationService` — ✅ DI 注入
- `ISettingsService` — ⏳ 待进行
- `IGraphEvaluationService`, `INodeGraphController` — ⏳ 待进行

### 节点求值流程
1. 拓扑排序 (Kahn 算法 O(N+E) + 环检测)
2. 层级分配（同层节点无依赖）
3. `Parallel.ForEach` 逐层并行执行
4. 每节点: GPU 路径 (VORTICE) → CPU 路径回退
5. 源节点缓存（深拷贝，避免池回收竞争）

## AI 系统

### 执行流程（三阶段）

AI 代理收到用户需求后，按三阶段执行：

```
用户消息 → 阶段0: Research → 阶段1: Plan → 阶段2: Execute → 完成报告
```

#### 阶段 0: 前置查询（Research）
- AI 直接使用内联提供的完整节点库信息进行需求分析
- 输出调研报告并注入 PlanManager.ResearchContext

#### 阶段 1: 生成计划（Plan）
- 使用 `BuildPlanningPrompt` 生成 MD 计划（`[plan_start]...[plan_end]` 格式）
- 通过 `MdPlanParser` 解析为 `ActivePlan`
- 计划同时保存为 JSON 和 MD 两种格式到 Plans/ 目录

#### 阶段 2: 按步骤执行（Execute）
- 每步循环：注入当前步骤提示 → AI 调用工具 → 执行 → 处理结果
- 自动重试机制（节点不存在时查库重试、端口错误时查详情重试）
- **MD 文件实时更新**：每次步骤推进时自动更新进度
- 完成后生成执行报告 `report_xxx.md`

### 工具提供者（7 个）

- **GraphToolProvider**: 节点/连接 CRUD
- **SkillToolProvider**: 技能管理（33 个内置技能）
- **DynamicNodeToolProvider**: 动态脚本节点
- **ResourceNodeToolProvider**: .node.json 节点管理
- **PlanToolProvider**: 计划管理（update_plan / get_plan）
- **AestheticToolProvider**: 美学评估
- **RecipeToolProvider**: 效果配方生成与应用

### 内置技能

| 技能 | 说明 |
|------|------|
| 创建自定义节点 | 通过 .node.json 创建节点 |
| Bash 命令行操作 | Unix shell 命令 |
| CMD 命令行操作 | Windows CMD 命令 |
| Python 脚本处理 | Python 批处理 |
| 导出瓦片 | 导出图像文件 |
| 批量导出 | 多种子批量导出 |
| 从脚本创建节点 | 从 C# 脚本创建节点 |
| 图模板 | 保存子图模板 |
| 项目文件 | .pxtile 项目文件操作 |
| *其他 24 个* | ... |

### 经验学习
ExperienceDb + FewShotSelector + UserProfileService：自动从 AI 交互中学习用户偏好，优化 Few-shot 示例选择。

## GPU 子系统

GPU 加速通过 VORTICE 编译符号控制：

| 符号 | 效果 |
|------|------|
| 未定义 | CPU 回退路径（SharpDX D3DImage 互操作仍可用） |
| `VORTICE` | 启用 Vortice.Direct3D 11 计算着色器路径 |

PixelBuffer 新增 `CopyFrom(IntPtr, int)` / `CopyTo(IntPtr, int)` 方法，使用 `Buffer.MemoryCopy` 实现 GPU 缓冲区零分配互操作。

HLSL 着色器包含 8 个计算入口点：`CS_SolidColorMain`、`CS_GradientMain`、`CS_NoiseMain`、`CS_FibersMain`、`CS_WeaveMain`、`CS_ConvolutionMain`、`CS_ColorAdjustMain`、`CS_ShapeMain`。

## 本地化系统

- **双后端**: 内置 `.resx` + 外部 `JSON`（%APPDATA%/PixelAssetGenerator/Languages/*.json）
- **语言自动发现**: 从程序集 manifest 扫描 `.resources` 文件，自动发现内置语言
- **语言变体协商**: `fr-CA` → `fr` → `zh-Hans` → `en` → 任意
- **CultureInfo 线程级**: 使用 `Thread.CurrentThread.CurrentUICulture`

## 启动流程
1. `App.OnStartup` — 初始化 DI 容器、设置主题/语言
2. 显示 Splash 窗口
3. 异步初始化管线：
   - 渲染引擎和节点控制器
   - 根据屏幕 `WorkArea.Width * 28%` 计算左侧面板宽度（clamp 260-480）
   - 根据面板宽度计算缩略图大小（4列布局）
   - 加载节点库、编译脚本节点
   - 后台并行生成所有节点缩略图
4. 隐藏 Splash，显示主窗口
5. 窗口关闭时持久化缩略图大小到 `settings.json`

## 动画系统

基于 DispatcherTimer 的帧播放驱动：
- AnimationPlaybackService: 播放/暂停/循环/乒乓/单次
- 检测画布中的动画节点自动显示控制栏
- 帧率可调 (6-30 FPS)
- 支持参数动画、路径动画、波形动画、噪声动画、序列动画、帧混合、音频响应
