namespace PixelAssetGenerator.Core;

/// <summary>
/// 实时预览模式：静帧预览显示选中节点的输出，动画预览驱动全图实时播放。
/// </summary>
public enum PreviewMode
{
    /// <summary>静帧预览 — 精确显示当前选中节点的输出端口结果。</summary>
    Still,
    /// <summary>动画预览 — 全图实时播放，驱动动画/粒子/物理系统。</summary>
    Animation
}
