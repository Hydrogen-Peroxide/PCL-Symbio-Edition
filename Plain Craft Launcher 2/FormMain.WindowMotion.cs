using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PCL;

public partial class FormMain
{
    private readonly ScaleTransform windowMotionScale = new(1, 1);
    private readonly TranslateTransform windowMotionOffset = new();
    private bool windowCloseRequested;

    private static bool WindowMotionEnabled =>
        !SystemParameters.HighContrast && ModAnimation.aniSpeed is > 0 and < 200;

    private void InitializeWindowMotion()
    {
        PanBack.RenderTransformOrigin = new Point(0.5, 0.5);
        PanBack.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection { windowMotionScale, windowMotionOffset }
        };
        // Prepare before the first frame so the full-size window never flashes first.
        if (WindowMotionEnabled)
        {
            PanBack.Opacity = 0;
            windowMotionScale.ScaleX = windowMotionScale.ScaleY = 0.96;
            windowMotionOffset.Y = 12;
        }
        ContentRendered += WindowMotion_ContentRendered;
    }

    private void WindowMotion_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= WindowMotion_ContentRendered;
        if (!windowCloseRequested)
            AnimateWindowSurface(opening: true);
    }

    private void BeginWindowClose(bool isUpdating)
    {
        IsHitTestVisible = false;
        AnimateWindowSurface(opening: false, completed: () =>
        {
            Visibility = Visibility.Collapsed;
            ShowInTaskbar = false;
            EndProgramForce(force: false, isUpdating: isUpdating);
        });
    }

    private void AnimateWindowSurface(bool opening, Action? completed = null)
    {
        var opacity = opening ? 1d : 0d;
        var scale = opening ? 1d : 0.96d;
        var offset = opening ? 0d : 8d;
        if (!WindowMotionEnabled || !IsVisible || WindowState == WindowState.Minimized)
        {
            PanBack.BeginAnimation(OpacityProperty, null);
            windowMotionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            windowMotionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            windowMotionOffset.BeginAnimation(TranslateTransform.YProperty, null);
            PanBack.Opacity = opacity;
            windowMotionScale.ScaleX = windowMotionScale.ScaleY = scale;
            windowMotionOffset.Y = offset;
            completed?.Invoke();
            return;
        }

        var duration = TimeSpan.FromMilliseconds((opening ? 300 : 180) / ModAnimation.aniSpeed);
        var easing = new CubicEase { EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn };
        // Sample the displayed values before replacing clocks: closing during entry stays smooth.
        var fade = CreateAnimation(PanBack.Opacity, opacity);
        var scaleX = CreateAnimation(windowMotionScale.ScaleX, scale);
        var scaleY = CreateAnimation(windowMotionScale.ScaleY, scale);
        var slide = CreateAnimation(windowMotionOffset.Y, offset);
        if (completed is not null)
            fade.Completed += (_, _) => completed();

        // Store final base values so animation clocks can release their properties on completion.
        PanBack.Opacity = opacity;
        windowMotionScale.ScaleX = windowMotionScale.ScaleY = scale;
        windowMotionOffset.Y = offset;
        windowMotionScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        windowMotionScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        windowMotionOffset.BeginAnimation(TranslateTransform.YProperty, slide);
        PanBack.BeginAnimation(OpacityProperty, fade);

        DoubleAnimation CreateAnimation(double from, double to) => new(from, to, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
    }
}
