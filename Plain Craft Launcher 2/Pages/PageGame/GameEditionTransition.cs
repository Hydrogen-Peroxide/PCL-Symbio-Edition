using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCL;

/// <summary>
///     Java 版 / 基岩版切换动效。
///     <para>
///         原实现对整页做 <c>BitmapCache</c> 并在其上逐帧改 Opacity / TranslateTransform：页面里有滚动列表、
///         SVG 图标与大量文本，切换时既要做一次全页栅格化，又要让两个页面同时参与合成，帧时间会明显抖动。
///         这里改成「快照交叉淡化」：把即将隐藏的页面拍成一张位图，真正的页面瞬间切换，动画只作用在这一张位图上，
///         每帧只需要合成一个图层，复杂度与页面内容无关。
///     </para>
/// </summary>
internal static class GameEditionTransition
{
    private const string SnapshotTag = "GameEditionSnapshot";

    /// <summary>
    ///     执行一次快照交叉淡化。
    /// </summary>
    /// <param name="host">两个页面所在的容器（快照会临时加在它里面）</param>
    /// <param name="outgoing">即将隐藏的页面，可为空</param>
    /// <param name="incoming">即将显示的页面</param>
    /// <param name="snapshotOffsetX">快照朝哪个方向滑走（像素，正数向右）</param>
    /// <param name="durationMs">动画时长</param>
    /// <param name="key">动画分组名，用于中断</param>
    /// <returns>是否成功生成了快照；<see langword="false" /> 时调用方应改用无动画切换</returns>
    public static bool CrossFade(Panel host, FrameworkElement? outgoing, FrameworkElement incoming,
        double snapshotOffsetX, int durationMs, string key)
    {
        RemoveLeftoverSnapshots(host);

        if (outgoing is null || !IsSettled(outgoing))
        {
            ResetFrame(outgoing);
            ResetFrame(incoming);
            incoming.Visibility = Visibility.Visible;
            return true;
        }

        // 先让即将显示的页面完成一次布局：折叠状态的元素没有被 measure/arrange，
        // 如果留到动画第一帧再算，就会在新的流程里出现一次开头卡顿
        incoming.Visibility = Visibility.Visible;
        incoming.UpdateLayout();

        Image? snapshot;
        try
        {
            snapshot = Capture(outgoing, host);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Game] 生成切换快照失败，改为直接切换", ModBase.LogLevel.Debug);
            snapshot = null;
        }

        ModAnimation.AniStop(key);

        // 真正的页面立即切换，动画只作用在快照上
        outgoing.Visibility = Visibility.Collapsed;
        ResetFrame(outgoing);
        incoming.Visibility = Visibility.Visible;
        ResetFrame(incoming);

        if (snapshot is null)
            return true;

        host.Children.Add(snapshot);
        ModAnimation.AniStart(
            new List<ModAnimation.AniData>
            {
                ModAnimation.AaOpacity(snapshot, -1d, durationMs),
                ModAnimation.AaTranslateX(snapshot, snapshotOffsetX, durationMs,
                    ease: new ModAnimation.AniEaseOutFluent()),
                ModAnimation.AaCode(() =>
                {
                    host.Children.Remove(snapshot);
                }, after: true)
            }, key);
        return true;
    }

    /// <summary>
    ///     页面是否处于「静止」状态（没有正在进行中的切换动画，也没有被拖到半透明）。
    /// </summary>
    private static bool IsSettled(FrameworkElement element)
    {
        return element.Visibility == Visibility.Visible &&
               element.Opacity > 0.99d &&
               element.ActualWidth > 1d &&
               element.ActualHeight > 1d;
    }

    private static void ResetFrame(FrameworkElement? element)
    {
        if (element is null)
            return;
        element.Opacity = 1d;
        element.RenderTransform = null;
        element.CacheMode = null;
    }

    private static void RemoveLeftoverSnapshots(Panel host)
    {
        for (var i = host.Children.Count - 1; i >= 0; i--)
            if (host.Children[i] is Image { Tag: SnapshotTag })
                host.Children.RemoveAt(i);
    }

    /// <summary>
    ///     把页面按当前 DPI 栅格化成一张位图，并按它在容器里的位置摆放。
    /// </summary>
    private static Image? Capture(FrameworkElement source, Panel host)
    {
        var dpi = VisualTreeHelper.GetDpi(source);
        var width = (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0)
            return null;

        var bitmap = new RenderTargetBitmap(width, height, 96d * dpi.DpiScaleX, 96d * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(source);
        bitmap.Freeze();

        var offset = source.TranslatePoint(new Point(0d, 0d), host);
        var image = new Image
        {
            Source = bitmap,
            Width = source.ActualWidth,
            Height = source.ActualHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
            Tag = SnapshotTag,
            RenderTransform = new TranslateTransform()
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.LowQuality);
        image.Margin = new Thickness(offset.X, offset.Y, 0d, 0d);
        return image;
    }
}
