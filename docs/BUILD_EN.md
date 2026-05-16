# Build Guide

## Prerequisites
- .NET 9 SDK
- Windows 7+ (WPF dependency)
- Visual Studio 2022 (optional)

## Build Commands

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release
```

## Version
`version.txt` — Current version (format `major.minor.build.revision`)

Current version: **0.6.0.0**

## GPU-Accelerated Build

The default build includes SharpDX (WPF D3DImage interop) with Vortice package references enabled.
The VORTICE compile symbol controls the GPU compute shader path:

```bash
# Enable VORTICE compile symbol (enable GPU compute path)
dotnet build -p:DefineConstants=VORTICE

# Default build (CPU fallback path + SharpDX D3DImage interop)
dotnet build
```

The `VORTICE` compile symbol is undefined by default (csproj sets EnableVortice=true, DefineVorticeSymbol=true but comments indicate it must be specified manually).

GPU code paths use `#if VORTICE` conditional compilation, automatically falling back to CPU when undefined.

### HLSL Shader Precompilation (Optional)
Precompiling shaders avoids runtime compilation overhead:

```bash
dotnet build -p:HlslCompiler="dxc.exe"
```

Precompiled `.cso` files are output to the build directory and loaded at runtime with priority.

Shader entry points (8):
- CS_SolidColorMain, CS_GradientMain, CS_NoiseMain
- CS_FibersMain, CS_WeaveMain, CS_ConvolutionMain
- CS_ColorAdjustMain, CS_ShapeMain

## Conditional Compilation Symbols

| Symbol | Purpose | Enable Method |
|--------|---------|--------------|
| `VORTICE` | Vortice.Direct3D 11 GPU compute | `-p:DefineConstants=VORTICE` |

## Project Files
- `像素素材生成器.slnx` — Solution file
- `像素素材生成器/像素素材生成器.csproj` — Project file (AssemblyName: PixelAssetGenerator)

## Assembly Name
The assembly outputs as **PixelAssetGenerator** (defined by `<AssemblyName>PixelAssetGenerator</AssemblyName>` in csproj).

## Build Output
```
bin/Release/net9.0-windows7.0/
├── PixelAssetGenerator.exe
├── PixelAssetGenerator.dll
├── Core/Gpu/Shaders.*.cso    # Precompiled shaders (optional)
└── Resources/Nodes/          # Node .node.json files
```

## NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.CodeAnalysis.CSharp | 4.14.0 | Runtime C# script compilation |
| Microsoft.Extensions.DependencyInjection | 10.0.8 | DI container |
| SharpNoise | 0.12.1.1 | Noise generation |
| SharpDX.Direct3D9 | 4.2.0 | D3DImage WPF interop |
| SharpDX.Direct3D11 | 4.2.0 | Direct3D 11 |
| SharpDX.DXGI | 4.2.0 | DXGI interop |
| Vortice.Direct3D11 *(conditional)* | 1.8.3 | GPU compute shaders |
| Vortice.DXGI *(conditional)* | 1.8.3 | GPU DXGI |
| Vortice.D3DCompiler *(conditional)* | 1.8.3 | HLSL compilation |
| Vortice.Mathematics *(conditional)* | 1.8.0 | GPU math library |
