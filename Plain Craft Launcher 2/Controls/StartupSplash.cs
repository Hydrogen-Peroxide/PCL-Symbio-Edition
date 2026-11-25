using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PCL;

/// <summary>
///     启动 Logo 窗口。
///     WPF 内置的 SplashScreen 会按图片原始像素显示，并把半透明边缘合成到白色底上（表现为白边），
///     因此改为自绘透明窗口：固定 128 DIP 大小、按像素透明度合成、带柔和阴影。
/// </summary>
public class StartupSplash : Window
{
    private const double LogoSize = 128d;

    private bool _closing;

    public StartupSplash()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;

        var logo = new Image
        {
            Width = LogoSize,
            Height = LogoSize,
            Stretch = Stretch.Uniform,
            Source = new BitmapImage(new Uri("pack://application:,,,/Images/icon.png")),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18d,
                ShadowDepth = 3d,
                Direction = 270d,
                Opacity = 0.28d,
                Color = Colors.Black
            }
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        // 外层留白用于容纳阴影，不至于被窗口边缘裁掉
        Content = new Grid { Margin = new Thickness(24d), Children = { logo } };
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // 若窗口在首次渲染前就被要求关闭，不要再用淡入动画覆盖关闭时的淡出，
        // 否则淡出的 Completed 永远不触发，Logo 会一直留在屏幕上
        if (_closing) return;
        SymbioMotion.Fade(this, 0, immediate: true);
        SymbioMotion.Fade(this, 1);
    }

    /// <summary>以淡出动画关闭窗口。</summary>
    public void Close(TimeSpan fadeOut)
    {
        if (_closing) return;
        _closing = true;
        if (fadeOut <= TimeSpan.Zero)
        {
            Close();
            return;
        }

        // A busy startup dispatcher may not render another frame before shutdown.
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = fadeOut };
        var closed = false;
        Closed += (_, _) => { closed = true; timer.Stop(); };
        void Finish()
        {
            if (closed) return;
            closed = true;
            timer.Stop();
            Close();
        }
        timer.Tick += (_, _) => Finish();
        timer.Start();
        SymbioMotion.Fade(this, 0, completed: Finish);
    }
}
