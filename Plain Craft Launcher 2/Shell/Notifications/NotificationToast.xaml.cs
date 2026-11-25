using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.UI.Theme;
using PCLLF.Wpf.Motion;

namespace PCL;

public partial class NotificationToast
{
    private const double DragDeadzone = 4;
    private const double DragOpacityFloor = .35;
    private const double DismissThresholdRatio = .12;
    private const double DismissThresholdMin = 24;
    private const double MinRemainingMs = 300;

    private readonly DispatcherTimer countdown = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly Stopwatch elapsed = new();
    private readonly TranslateTransform _offset = new();
    private double durationMs;
    private double remainingMs;
    private double targetHeight;

    private bool _dragPending;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartTranslateX;
    private FrameworkElement? _dragReference;

    public NotificationToast()
    {
        InitializeComponent();
        RenderTransform = _offset;
        countdown.Tick += UpdateCountdown;
        DismissAction.Click += (_, _) => Dismiss();
        PreviewMouseLeftButtonDown += DragPressed;
        PreviewMouseMove += DragMoved;
        PreviewMouseLeftButtonUp += DragReleased;
        LostMouseCapture += LoseDragCapture;
        Loaded += (_, _) =>
        {
            ApplyPalette();
            ThemeService.ColorModeChanged += OnThemeChanged;
            ThemeService.ColorThemeChanged += OnColorThemeChanged;
        };
        Unloaded += (_, _) =>
        {
            PauseCountdown();
            ThemeService.ColorModeChanged -= OnThemeChanged;
            ThemeService.ColorThemeChanged -= OnColorThemeChanged;
        };
    }

    public string Context { get => Description.Text; set => Description.Text = value; }
    public string Icon { get; set; } = "lucide/info";
    public HintType ToastType { get; set; } = HintType.Info;
    public double DisplayDuration { get; set; } = 5000;
    public bool IsDismissing { get; private set; }

    public void Show()
    {
        if (Parent is not Panel) return;
        if (Window.GetWindow(this) is { ActualWidth: > 0 } window) MaxWidth = window.ActualWidth * .9;
        Margin = new Thickness(0, 0, 0, 6);
        Opacity = 0;
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Arrange(new Rect(0, 0, DesiredSize.Width, DesiredSize.Height));
        targetHeight = Math.Max(ActualHeight, 45);
        Height = 0;

        SymbioMotion.Reveal(this, true, value =>
        {
            _offset.Y = -16 * (1 - value);
            Height = targetHeight * Math.Clamp(value, 0, 1);
        }, SymbioMotion.Pop);
        RestartCountdown();
    }

    public void Emphasize()
    {
        if (IsDismissing) return;
        _isDragging = _dragPending = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        SymbioMotion.Fade(this, Opacity, immediate: true);
        FinishEntrance();
        if (MotionPreferences.AllowsAnimation) MoveX(-6, immediate: true);
        MoveX(0);
        RestartCountdown();
    }

    private void FinishEntrance() => SymbioMotion.Reveal(this, true, _ =>
    {
        _offset.Y = 0;
        Height = targetHeight;
    }, SymbioMotion.Pop, immediate: true);

    private void MoveX(double target, bool immediate = false) =>
        SymbioMotion.Spring(this, "toastX", () => _offset.X, value => _offset.X = value,
            target, SymbioMotion.Pop, immediate);

    public void Dismiss() => StartDismiss(slideRight: true);

    private void StartDismiss(bool slideRight)
    {
        if (IsDismissing) return;
        IsDismissing = true;
        IsHitTestVisible = false;
        _isDragging = _dragPending = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        PauseCountdown();
        SymbioMotion.Fade(this, Opacity, immediate: true);
        if (slideRight) MoveX(_offset.X + 60);
        var startOpacity = Math.Max(.001, Opacity);
        var startHeight = double.IsFinite(Height) ? Height : ActualHeight;
        var startY = _offset.Y;
        SymbioMotion.Reveal(this, false, value =>
        {
            var fraction = Math.Clamp(value / startOpacity, 0, 1);
            Height = startHeight * fraction;
            if (!slideRight) _offset.Y = startY - 10 * (1 - fraction);
        }, SymbioMotion.Exit, completed: () => (Parent as Panel)?.Children.Remove(this));
    }

    private void RestartCountdown()
    {
        durationMs = double.IsFinite(DisplayDuration) ? Math.Max(0, DisplayDuration) : 0;
        remainingMs = durationMs;
        SetCountdownRatio(remainingMs);
        ResumeCountdown();
    }

    private void UpdateCountdown(object? sender, EventArgs e)
    {
        var left = Math.Max(0, remainingMs - elapsed.Elapsed.TotalMilliseconds);
        SetCountdownRatio(left);
        if (left == 0) StartDismiss(slideRight: false);
    }

    private void PauseCountdown()
    {
        countdown.Stop();
        if (!elapsed.IsRunning) return;
        remainingMs = Math.Max(0, remainingMs - elapsed.Elapsed.TotalMilliseconds);
        elapsed.Reset();
        SetCountdownRatio(remainingMs);
    }

    private void ResumeCountdown()
    {
        elapsed.Restart();
        countdown.Start();
    }

    private void SetCountdownRatio(double timeLeft) =>
        CountdownScale.ScaleX = durationMs == 0 ? 0 : Math.Clamp(timeLeft / durationMs, 0, 1);

    private void ClipToSurface(object sender, SizeChangedEventArgs e) =>
        Frame.Clip = new RectangleGeometry(new Rect(Frame.RenderSize), 8, 8);

    private void OnThemeChanged(bool dark, ColorTheme theme) => RefreshPalette();
    private void OnColorThemeChanged(ColorTheme theme) => RefreshPalette();

    private void RefreshPalette()
    {
        if (Dispatcher.CheckAccess()) ApplyPalette();
        else Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) ApplyPalette(); }));
    }

    private void ApplyPalette()
    {
        var dark = ThemeService.IsDarkMode;
        var color = ToastType switch
        {
            HintType.Success => dark ? Color.FromRgb(0x6C, 0xCB, 0x5F) : Color.FromRgb(0x0F, 0x7B, 0x0F),
            HintType.Error => dark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C),
            HintType.Warning => dark ? Color.FromRgb(0xFC, 0xE1, 0x00) : Color.FromRgb(0x9D, 0x5D, 0x00),
            _ => TryFindResource("SyAccentFill") is SolidColorBrush accent ? accent.Color : Color.FromRgb(0, 0x5F, 0xB8)
        };
        var severity = new SolidColorBrush(color);
        severity.Freeze();
        SeverityIcon.Icon = Icon;
        SeverityIcon.IconBrush = severity;
        SeverityIcon.StrokeThickness = 0;
        TimeRemaining.Background = severity;
    }

    private void DragPressed(object sender, MouseButtonEventArgs e)
    {
        _dragPending = false;
        if (IsDismissing)
            return;
        if (IsDescendantOf(e.OriginalSource as DependencyObject, DismissAction))
            return;
        _dragReference = Parent as FrameworkElement;
        if (_dragReference is null)
            return;
        _dragPending = true;
        _isDragging = false;
        _dragStartPoint = e.GetPosition(_dragReference);
    }

    private void DragMoved(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed || _dragReference is null)
            {
                _isDragging = false;
                _dragPending = false;
                if (IsMouseCaptured) ReleaseMouseCapture();
                ReturnFromDrag();
                return;
            }
            var dragCurrent = e.GetPosition(_dragReference);
            UpdateDragPosition(dragCurrent.X - _dragStartPoint.X);
            e.Handled = true;
            return;
        }

        if (!_dragPending)
            return;
        if (Mouse.LeftButton != MouseButtonState.Pressed || _dragReference is null)
        {
            _dragPending = false;
            return;
        }

        var current = e.GetPosition(_dragReference);
        var delta = current.X - _dragStartPoint.X;

        if (delta < DragDeadzone)
            return;

        BeginDrag(delta);
    }

    private void DragReleased(object sender, MouseButtonEventArgs e)
    {
        if (_dragPending && !_isDragging)
        {
            _dragPending = false;
            return;
        }

        if (!_isDragging)
            return;

        _isDragging = false;
        _dragPending = false;
        e.Handled = true;

        if (IsMouseCaptured)
            ReleaseMouseCapture();

        var currentX = _offset.X;
        if (currentX - _dragStartTranslateX >= GetDismissThreshold())
        {
            Dismiss();
            return;
        }

        ReturnFromDrag();
    }

    private void LoseDragCapture(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
            return;
        _isDragging = false;
        _dragPending = false;
        ReturnFromDrag();
    }

    private void BeginDrag(double initialDelta)
    {
        _isDragging = true;
        _dragPending = false;

        PauseCountdown();
        var x = _offset.X;
        FinishEntrance();
        MoveX(x, immediate: true);
        SymbioMotion.Fade(this, GetDragOpacity(x), immediate: true);
        _dragStartTranslateX = x;

        CaptureMouse();

        UpdateDragPosition(initialDelta);
    }

    private void UpdateDragPosition(double delta)
    {
        var newX = _dragStartTranslateX + ApplyDragResistance(delta);
        _offset.X = newX;
        Opacity = GetDragOpacity(newX);
    }

    private void ReturnFromDrag()
    {
        if (Parent is null || IsDismissing)
            return;
        var remaining = remainingMs;
        if (remaining < MinRemainingMs)
        {
            Dismiss();
            return;
        }

        MoveX(0);
        SymbioMotion.Fade(this, 1);
        ResumeCountdown();
    }

    private static double ApplyDragResistance(double delta)
    {
        return Math.Max(0d, delta);
    }

    private double GetDragOpacity(double translateX)
    {
        if (translateX <= 0d)
            return 1d;
        var width = ActualWidth > 0 ? ActualWidth : 1d;
        return Math.Max(DragOpacityFloor, 1d - (translateX / width) * (1d - DragOpacityFloor));
    }

    private double GetDismissThreshold()
    {
        return Math.Max(DismissThresholdMin, ActualWidth * DismissThresholdRatio);
    }

    private static bool IsDescendantOf(DependencyObject? descendant, DependencyObject ancestor)
    {
        while (descendant is not null)
        {
            if (ReferenceEquals(descendant, ancestor))
                return true;
            descendant = descendant is Visual ? VisualTreeHelper.GetParent(descendant) :
                LogicalTreeHelper.GetParent(descendant);
        }
        return false;
    }
}
