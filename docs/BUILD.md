# 构建指南

## 前置要求
- .NET 9 SDK
- Windows 7+ (WPF 依赖)
- Visual Studio 2022 (可选)

## 构建命令

```bash
# Debug 构建
dotnet build

# Release 构建
dotnet build -c Release
```

## 版本号
`version.txt` — 当前版本 (格式 `major.minor.build.revision`)

当前版本: **0.6.0.0**

## GPU 加速构建

默认构建包含 SharpDX（WPF D3DImage 互操作）并已启用 Vortice 包引用。
VORTICE 编译符号控制 GPU 计算着色器路径：

```bash
# 启用 VORTICE 编译符号（启用 GPU 计算路径）
dotnet build -p:DefineConstants=VORTICE

# 默认构建（CPU 回退路径 + SharpDX D3DImage 互操作）
dotnet build
```

`VORTICE` 编译符号默认未定义（csproj 中 EnableVortice=true，DefineVorticeSymbol=true 但注释说明需手动指定）。

GPU 代码路径使用 `#if VORTICE` 条件编译，未定义时自动回退到 CPU 路径。

### HLSL 着色器预编译（可选）
预编译着色器可避免运行时编译开销：

```bash
dotnet build -p:HlslCompiler="dxc.exe"
```

预编译的 `.cso` 文件会输出到构建目录，运行时优先加载。

着色器入口点（8 个）：
- CS_SolidColorMain, CS_GradientMain, CS_NoiseMain
- CS_FibersMain, CS_WeaveMain, CS_ConvolutionMain
- CS_ColorAdjustMain, CS_ShapeMain

## 条件编译符号

| 符号 | 用途 | 启用方式 |
|------|------|----------|
| `VORTICE` | Vortice.Direct3D 11 GPU 计算 | `-p:DefineConstants=VORTICE` |

## 项目文件
- `像素素材生成器.slnx` — 解决方案文件
- `像素素材生成器/像素素材生成器.csproj` — 项目文件（AssemblyName: PixelAssetGenerator）

## 程序集名称
程序集输出名为 **PixelAssetGenerator**（由 csproj 中 `<AssemblyName>PixelAssetGenerator</AssemblyName>` 定义）。

## 构建输出
```
bin/Release/net9.0-windows7.0/
├── PixelAssetGenerator.exe
├── PixelAssetGenerator.dll
├── Core/Gpu/Shaders.*.cso    # 预编译着色器（可选）
└── Resources/Nodes/          # 节点 .node.json 文件
```

## NuGet 依赖

| 包 | 版本 | 用途 |
|----|------|------|
| Microsoft.CodeAnalysis.CSharp | 4.14.0 | 运行时 C# 脚本编译 |
| Microsoft.Extensions.DependencyInjection | 10.0.8 | DI 容器 |
| SharpNoise | 0.12.1.1 | 噪声生成 |
| SharpDX.Direct3D9 | 4.2.0 | D3DImage WPF 互操作 |
| SharpDX.Direct3D11 | 4.2.0 | Direct3D 11 |
| SharpDX.DXGI | 4.2.0 | DXGI 互操作 |
| Vortice.Direct3D11 *(条件)* | 1.8.3 | GPU 计算着色器 |
| Vortice.DXGI *(条件)* | 1.8.3 | GPU DXGI |
| Vortice.D3DCompiler *(条件)* | 1.8.3 | HLSL 编译 |
| Vortice.Mathematics *(条件)* | 1.8.0 | GPU 数学库 |
