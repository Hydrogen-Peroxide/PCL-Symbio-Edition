using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PCL;

/// <summary>
///     Java 版 / 基岩版切换动效。
///     <para>
///         动效只作用在即将显示的那一页上：旧页面立即收起，新页面按原来的方向滑入并淡入。
///         这样每帧只有一页参与合成，且 Opacity / TranslateTransform 都是渲染属性，不会触发重新布局与重绘，
///         既保留原来的观感，又不会像「整页位图缓存 + 两页同时动」那样抖动。
///     </para>
/// </summary>
internal static class GameEditionTransition
{
    /// <summary>
    ///     让 <paramref name="incoming" /> 从 <paramref name="offsetX" /> 处滑入并淡入。
    /// </summary>
    /// <param name="outgoing">即将隐藏的页面，可为空</param>
    /// <param name="incoming">即将显示的页面</param>
    /// <param name="offsetX">滑入起点相对最终位置的横向偏移（像素），正数表示从右侧滑入</param>
    /// <param name="durationMs">位移动画时长</param>
    /// <param name="key">动画分组名，用于中断上一轮切换</param>
    public static void SlideIn(FrameworkElement? outgoing, FrameworkElement incoming, double offsetX,
        int durationMs, string key)
    {
        ModAnimation.AniStop(key);

        if (outgoing is not null)
        {
            outgoing.Visibility = Visibility.Collapsed;
            outgoing.Opacity = 1d;
            outgoing.RenderTransform = null;
            outgoing.CacheMode = null;
        }

        incoming.Visibility = Visibility.Visible;
        incoming.CacheMode = null;
        // 折叠状态的页面没有被 measure/arrange，先付掉这份开销，避免落到动画第一帧
        incoming.UpdateLayout();

        incoming.Opacity = 0d;
        incoming.RenderTransform = new TranslateTransform(offsetX, 0d);

        ModAnimation.AniStart(
            new List<ModAnimation.AniData>
            {
                ModAnimation.AaOpacity(incoming, 1d, Math.Max(120, durationMs - 70), 20),
                ModAnimation.AaTranslateX(incoming, -offsetX, durationMs, 20, new ModAnimation.AniEaseOutFluent()),
                ModAnimation.AaCode(() =>
                {
                    incoming.Opacity = 1d;
                    incoming.RenderTransform = null;
                }, after: true)
            }, key);
    }
}
