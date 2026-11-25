using System.Windows;
using System.Windows.Media;
using PCLLF.Wpf.Motion;

namespace PCL;

public partial class FormMain
{
    private readonly ScaleTransform windowMotionScale = new(1, 1);
    private readonly TranslateTransform windowMotionOffset = new();
    private bool windowCloseRequested;


    private static bool WindowMotionEnabled => MotionPreferences.AllowsAnimation;

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
        SymbioMotion.Reveal(PanBack, opening, progress =>
        {
            windowMotionScale.ScaleX = windowMotionScale.ScaleY = .96 + .04 * progress;
            windowMotionOffset.Y = 12 * (1 - progress);
        }, opening ? SymbioMotion.Depth : SymbioMotion.Exit,
            immediate: !WindowMotionEnabled || !IsVisible || WindowState == WindowState.Minimized,
            completed: completed);
    }
}
