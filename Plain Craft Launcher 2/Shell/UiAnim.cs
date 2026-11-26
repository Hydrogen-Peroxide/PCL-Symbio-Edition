using System;
using System.Windows;

namespace PCL;

/// <summary>统一动画种类。新增动效只能从这里加，禁止在页面里直接调 SymbioMotion 拼参数。</summary>
public enum AnimKind
{
    /// <summary>从下方弹入（页面切换）。</summary>
    EnterDepth,

    /// <summary>向上退场。</summary>
    ExitDepth,

    /// <summary>淡入。</summary>
    FadeIn,

    /// <summary>淡出。</summary>
    FadeOut,

    /// <summary>缩放显形（卡片、悬浮按钮）。</summary>
    ScaleReveal
}

/// <summary>
///     统一动画入口：<c>UiAnim.Run(this, AnimKind.EnterDepth)</c>。
///     负责三件历史遗留问题：
///     1) 元素还没 Loaded 时不能 Begin，延后到 Loaded 回调里再播；
///     2) 同一元素连续触发时不会叠一堆弹簧（由 SymbioMotion 的 channel 去重）；
///     3) 元素在动画中途被卸载时不会留下悬挂回调。
/// </summary>
public static class UiAnim
{
    /// <summary>元素首次进入可视树后执行一次。已加载则立刻执行。</summary>
    public static void RunOnce(FrameworkElement element, Action action)
    {
        if (element is null || action is null) return;
        if (element.IsLoaded)
        {
            action();
            return;
        }

        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            element.Loaded -= handler;
            action();
        };
        element.Loaded += handler;
    }

    /// <summary>播放一个统一动效；未 Loaded 的元素会在 Loaded 后补播。</summary>
    public static void Run(FrameworkElement element, AnimKind kind) =>
        RunOnce(element, () => Play(element, kind));

    private static void Play(FrameworkElement element, AnimKind kind)
    {
        // 元素已经离开可视树就不播了，避免给即将回收的容器启动弹簧
        if (!element.IsVisible && element.Parent is null) return;

        switch (kind)
        {
            case AnimKind.EnterDepth:
                SymbioMotion.EnterDepth(element);
                break;
            case AnimKind.ExitDepth:
                SymbioMotion.ExitDepth(element);
                break;
            case AnimKind.FadeIn:
                SymbioMotion.Fade(element, 1);
                break;
            case AnimKind.FadeOut:
                SymbioMotion.Fade(element, 0);
                break;
            case AnimKind.ScaleReveal:
                SymbioMotion.ScaleReveal(element, true);
                break;
        }
    }
}
