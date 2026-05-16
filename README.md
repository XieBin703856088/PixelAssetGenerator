# 像素素材生成器 / Pixel Asset Generator

基于节点的像素艺术材质生成器。通过组合节点图来生成可平铺的像素风格纹理，支持 AI 辅助创建和编辑。

**版本**: 0.6.0.0 | **目标框架**: .NET 9 / WPF | **C#**: 13

## 功能

- **节点图编辑**: 136 个节点，7 个类别的可视化编程
- **GPU 加速**: Direct3D 11 计算着色器 (Vortice，可选) + SharpDX D3DImage 互操作
- **AI 辅助**: OpenAI / Claude API 集成，AI 创建和编辑节点图
- **动画系统**: 帧序列播放、参数动画、粒子系统、物理模拟
- **技能系统**: 33 个内置技能，保存/复用节点子图或指令模板
- **本地化**: 简体中文 / English 界面，运行时切换（resx + 外部 JSON）
- **启动优化**: Splash 进度条 + 后台并行缩略图生成
- **分辨率自适应**: 启动时根据屏幕分辨率自动计算面板布局和缩略图大小
- **经验学习**: 从 AI 交互中自动学习用户偏好，优化 few-shot 示例选择

## 项目结构

```
像素素材生成器/
├── Core/                    # 核心引擎
│   ├── Gpu/                 # Direct3D 11 GPU 加速 (Vortice/SharpDX)
│   │   ├── GpuCompute.cs        # GPU 计算核心
│   │   ├── GpuCompiler.cs       # HLSL 着色器编译与缓存
│   │   ├── GpuScheduler.cs      # GPU 调度器（非阻塞 UI 呈现）
│   │   └── Shaders.hlsl         # 8 个计算着色器入口点
│   ├── Nodes/Sources/       # 节点源（内置/文件/脚本/动态）
│   │   ├── BuiltInNodeSource.cs     # 内置节点
│   │   ├── DynamicNodeSource.cs     # 动态脚本节点
│   │   ├── FileNodeSource.cs        # 文件节点
│   │   └── ResourceNodeInstance.cs  # 资源节点实例（Roslyn 编译）
│   ├── Animation/           # 动画引擎
│   │   ├── AnimationClip.cs          # 动画剪辑定义
│   │   ├── AnimationEvaluator.cs     # 动画求值器
│   │   └── Nodes/                    # 8 个动画节点
│   ├── Particles/           # 粒子系统
│   │   ├── ParticleEmitter.cs        # 粒子发射器
│   │   ├── ParticleSimulator.cs      # 粒子模拟器
│   │   ├── ParticleRenderer.cs       # 粒子渲染器
│   │   └── Nodes/                    # 7 个粒子节点
│   ├── Physics/             # 物理引擎
│   │   ├── PhysicsWorld.cs           # 物理世界
│   │   ├── PhysicsBody.cs            # 物理体
│   │   ├── CollisionDetection.cs     # 碰撞检测
│   │   └── Nodes/                    # 4 个物理节点
│   ├── GraphNodeBase.cs     # 节点基类
│   ├── GraphNodeRegistry.cs # 节点注册表（从 .node.json 加载）
│   ├── NodeGraphEvaluator.cs# 拓扑排序 O(N+E) + 并行求值
│   ├── PixelBuffer.cs       # 像素缓冲区（GPU 零分配互操作）
│   └── PixelBufferPool.cs   # 像素缓冲池
├── Generators/              # 瓦片生成器（编译型 C#）
│   ├── Grass/Stone/Water/Road/SandGenerator.cs
│   └── BaseTileGenerator.cs
├── Resources/
│   ├── AppResources.xaml         # 应用资源字典（主题/样式）
│   ├── Languages/                # 本地化 JSON（回退文件）
│   │   ├── en.json
│   │   └── zh-Hans.json
│   ├── Strings.en.resx           # 英语资源（~440 条）
│   ├── Strings.zh-Hans.resx      # 简体中文资源（~440 条，回退语言）
│   └── Nodes/                    # 136 个 .node.json 节点定义
│       ├── Generate/        # 5
│       ├── ImageProcess/    # 42
│       ├── Logic/           # 14
│       ├── Material/        # 32
│       └── ...
├── Nodes/                   # 19 个扩展节点（动画/粒子/物理）
│   ├── Animation/           # 8 个动画 .node.json
│   ├── Particle/            # 7 个粒子 .node.json
│   └── Physics/             # 4 个物理 .node.json
├── Services/                # 服务层（DI 容器）
│   ├── ServiceLocator.cs    # DI 服务定位器（Microsoft.Extensions.DI）
│   ├── Localization/        # 本地化（ILocalizationService + resx/JSON）
│   ├── Clients/             # AI API 客户端（OpenAI / Anthropic）
│   ├── Learning/            # AI 经验学习与追踪
│   ├── ToolProviders/       # AI 工具提供者（7 个）
│   ├── PixelAgentService.cs # AI 三阶段编排（调研→计划→执行）
│   ├── PlanManager.cs       # 计划生命周期管理
│   └── AnimationPlaybackService.cs
├── Models/                  # 数据模型（11 个文件）
│   ├── AiSettings.cs            # AI 设置
│   ├── ChatMessage.cs           # 聊天消息
│   ├── ChatSession.cs           # 聊天会话
│   ├── SkillDefinition.cs       # 技能定义
│   └── EffectRecipe.cs          # 效果配方
├── Controls/                # 自定义 WPF 控件（7 个）
├── Converters/              # 值转换器（2 个）
├── AI/                      # AI 设置界面
├── MainWindow.*             # 主窗口（8 个 partial 文件，~14K 行）
└── ShapeDrawingWindow.*     # 形状绘制窗口
```

## 节点系统

### 节点类型

- **Compute 节点**: 通过 .node.json 中的 C# 脚本运行时编译执行
- **Tile 节点**: 使用 Generators/ 中的编译型 C# 生成器
- **Animation 节点**: 动画效果（参数动画、路径动画、音视频响应）
- **Particle 节点**: 粒子系统（发射器、力场、碰撞、轨迹）
- **Physics 节点**: 物理模拟（刚体、约束、软体、力场）

### 端口类型

- **Image**: RGBA 像素缓冲区（浮点精度 0-1）
- **Mask**: 单通道灰度遮罩
- **Float**: 浮点数值
- **Color**: RGBA 颜色值
- **Any**: 任意类型（工具/预览节点）

### 参数类型

- **Number**: 浮点数 (min/max/step/default)
- **Integer**: 整数 (min/max/step/default)
- **Choice**: 枚举选择（中英双语显示标签）
- **Boolean**: 开关
- **Color**: 颜色选择器
- **Seed**: 随机种子
- **PointList**: 点坐标列表
- **Text**: 文本字符串

### 节点类别

| 类别 | 节点数 | 说明 |
|------|--------|------|
| Generate | 5 | 瓦片生成 |
| ImageProcess | 42 | 图像处理滤镜 |
| Logic | 14 | 逻辑流程控制 |
| Material | 32 | 材质纹理生成 |
| Noise | 16 | 噪声与有机纹理 |
| Pattern | 20 | 图案与形状生成 |
| Tool | 3 | 工具（注释/输出/预览） |
| Animation | 8 | 参数动画/音视频响应 |
| Particle | 7 | 粒子系统 |
| Physics | 4 | 物理模拟 |

## 快速开始

```bash
dotnet build
dotnet run --project 像素素材生成器
```

## 如何参与

欢迎贡献！无论是修复 bug、添加新节点、改进文档还是提出新功能，都非常欢迎。

- [贡献指南](CONTRIBUTING.md) — 如何提交 PR、代码规范
- [行为准则](CODE_OF_CONDUCT.md) — 社区交流准则
- [报告 Bug](https://github.com) — 提交 Issue（模板在 `.github/ISSUE_TEMPLATE/`）
- [提出新功能](https://github.com) — 先开 Issue 讨论再动手

### 快速参与方式

| 你想做什么 | 入口 |
|-----------|------|
| 报告 Bug | `Issues` → `New Issue` → `Bug Report` |
| 提交代码 | `Fork` → `PR` |
| 添加新节点 | 详见 [节点参考](docs/NODE_REFERENCE.md) |
| 完善翻译 | 修改 `Resources/Languages/` 下的 JSON 文件 |

## 文档

- `docs/ARCHITECTURE.md` — 项目架构
- `docs/NODE_REFERENCE.md` — 节点参考
- `docs/BUILD.md` — 构建指南

## 许可证

本项目使用 **GNU General Public License v3.0 (GPL v3)**。

这意味着：
- ✅ 你可以自由使用、修改和分发本软件
- ✅ 你可以用 AI 修改代码  
- ✅ 可以用于个人和商业项目
- ❌ 修改后的版本必须同样以 GPL v3 开源（"传染性"）
- ❌ 不能将修改版作为闭源软件分发

详见 [LICENSE](LICENSE)（英文）和 [LICENSE_ZH](LICENSE_ZH)（中文）。

## 构建要求

- .NET 9 SDK
- Windows 7+ (WPF 依赖)
- Visual Studio 2022 (可选)
