using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        BeginAnimation(OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
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

        var fade = new DoubleAnimation(Opacity, 0d, fadeOut)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);

        // 兜底：淡出动画可能被首次渲染的淡入替换而收不到 Completed，超时后强制关闭
        var timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = fadeOut + TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        timer.Start();
    }
}
