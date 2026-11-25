using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using PCLLF.Wpf.Glass;
using PCLLF.Wpf.Motion;

namespace PCL;

public sealed class SoftShadow : Border
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color),
        typeof(SoftShadow), new PropertyMetadata(Colors.Black, (d, e) => ((SoftShadow)d).shadow.Color = (Color)e.NewValue));
    public static readonly DependencyProperty ShadowRadiusProperty = DependencyProperty.Register(nameof(ShadowRadius), typeof(double),
        typeof(SoftShadow), new PropertyMetadata(16d, (d, e) => ((SoftShadow)d).shadow.BlurRadius = (double)e.NewValue),
        value => value is double radius && double.IsFinite(radius) && radius >= 0);

    private readonly DropShadowEffect shadow = new() { BlurRadius = 16, ShadowDepth = 4, Direction = 270 };

    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double ShadowRadius { get => (double)GetValue(ShadowRadiusProperty); set => SetValue(ShadowRadiusProperty, value); }

    public SoftShadow()
    {
        Background = Brushes.Black;
        Effect = shadow;
        IsHitTestVisible = false;
    }
}

public class WorkspaceCard : Grid
{
    public const int SwapedHeight = 40;
    private const int SurfaceCount = 4;

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string),
        typeof(WorkspaceCard), new PropertyMetadata(""));
    public static readonly DependencyProperty BackgroundBrushProperty = DependencyProperty.Register(nameof(BackgroundBrush),
        typeof(SolidColorBrush), typeof(WorkspaceCard), new PropertyMetadata(Brushes.Transparent,
            (d, e) => ((WorkspaceCard)d).edge.Background = (Brush)e.NewValue));
    public static readonly DependencyProperty CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner(typeof(WorkspaceCard),
        new PropertyMetadata(new CornerRadius(8), (d, e) => ((WorkspaceCard)d).ApplyRadius((CornerRadius)e.NewValue)));

    private readonly SoftShadow shadow = new() { Opacity = .06 };
    private readonly GlassSurface glass = new()
    {
        Background = Brushes.Transparent, BorderThickness = new Thickness(0), BlurRadius = 24,
        TintOpacity = 1, HighlightOpacity = 0, IsHitTestVisible = false
    };
    private readonly Border edge = new() { IsHitTestVisible = false, BorderThickness = new Thickness(1) };
    private readonly Button fold = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 4, 6, 0), Width = 32, Height = 32, Visibility = Visibility.Collapsed
    };
    private bool resizing;
    private bool headerPressed;

    public WorkspaceCard()
    {
        Background = Brushes.Transparent;
        shadow.SetResourceReference(SoftShadow.ColorProperty, "SyShadowColor");
        edge.SetResourceReference(Border.BorderBrushProperty, "SyFrostEdge");
        ApplyRadius(CornerRadius);
        SetResourceReference(BackgroundBrushProperty, "SyFrostFill");

        MainTextBlock = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(15, 12, 32, 0), FontWeight = FontWeights.SemiBold, FontSize = 14,
            IsHitTestVisible = false
        };
        MainTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "SyText");
        MainTextBlock.SetBinding(TextBlock.TextProperty, new Binding(nameof(Title)) { Source = this, Mode = BindingMode.OneWay });
        MainSwap = new Path
        {
            Data = Geometry.Parse("M0 0 L4 4 L8 0"), Stretch = Stretch.Uniform, Width = 10, Height = 6,
            StrokeThickness = 1.5, RenderTransformOrigin = new Point(.5, .5)
        };
        MainSwap.SetResourceReference(Shape.StrokeProperty, "SyTextSecondary");
        fold.Content = MainSwap;
        // Fluent subtle icon button; SyButton's 12 px padding squeezed the chevron into a dot.
        fold.SetResourceReference(StyleProperty, "SyIconButton");
        fold.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(Title)) { Source = this });
        fold.Click += (_, _) => Toggle();
        PressFeedback.SetIsEnabled(fold, true);

        var header = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = SwapedHeight };
        header.Children.Add(MainTextBlock);
        header.Children.Add(fold);
        Children.Add(shadow);
        Children.Add(glass);
        Children.Add(edge);
        Children.Add(header);

        Loaded += (_, _) => TriggerForceResize();
        MouseEnter += (_, _) => { if (HasMouseAnimation) SymbioMotion.Fade(shadow, .12); };
        MouseLeave += (_, _) =>
        {
            headerPressed = false;
            if (HasMouseAnimation) SymbioMotion.Fade(shadow, .06);
        };
        SizeChanged += ResizeContent;
    }

    public TextBlock MainTextBlock { get; }
    public Path MainSwap { get; }
    public InlineCollection Inlines => MainTextBlock.Inlines;

    public UIElement? BorderChild { get => edge.Child; set => edge.Child = value; }
    public SolidColorBrush BackgroundBrush { get => (SolidColorBrush)GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool CanSwap { get; set; }
    public bool SwapLogoRight { get; set; }
    public bool HasMouseAnimation { get; set; } = true;
    public bool UseAnimation { get; set; } = true;
    public UIElement? SwapControl;
    public Action<StackPanel>? InstallMethod { get; set; }

    public event Action<object, ModBase.RouteEventArgs>? PreviewSwap;
    public event Action<object, ModBase.RouteEventArgs>? Swap;

    public bool IsSwapped
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            if (!IsLoaded) return;
            EnsureFoldContent();
            if (SwapControl is null) return;
            if (!value) StackInstall();
            if (value)
                ResizeTo(ActualHeight, SwapedHeight, SwapedHeight, collapse: true);
            else TriggerForceResize();
            UpdateArrow();
        }
    }

    // Older saved homepages can still use this spelling on the replacement card.
    public bool IsSwaped { get => IsSwapped; set => IsSwapped = value; }

    private void ApplyRadius(CornerRadius radius)
    {
        shadow.CornerRadius = radius;
        glass.CornerRadius = radius;
        edge.CornerRadius = radius;
    }

    private void EnsureFoldContent()
    {
        if (CanSwap && SwapControl is null && Children.Count > SurfaceCount) SwapControl = Children[SurfaceCount];
        fold.Visibility = SwapControl is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateArrow()
    {
        if (fold.Visibility == Visibility.Visible)
            SymbioMotion.SpringRotate(MainSwap, IsSwapped && SwapLogoRight ? -90 : IsSwapped ? 0 : 180);
    }

    public void TriggerForceResize()
    {
        EnsureFoldContent();
        SymbioMotion.Stop(this, "workspaceCardHeight");
        resizing = false;
        if (SwapControl is not null)
        {
            SwapControl.Visibility = IsSwapped ? Visibility.Collapsed : Visibility.Visible;
            SetCurrentValue(HeightProperty, IsSwapped ? (double)SwapedHeight : double.NaN);
        }
        UpdateArrow();
    }

    private void ResizeContent(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged || resizing || !UseAnimation || !IsLoaded || IsSwapped ||
            e.PreviousSize.Height <= 0 || Math.Abs(e.NewSize.Height - e.PreviousSize.Height) < 1) return;
        ResizeTo(e.PreviousSize.Height, e.NewSize.Height, Height, collapse: false);
    }

    private void ResizeTo(double from, double target, double finish, bool collapse)
    {
        var animate = UseAnimation && IsLoaded && from > 0 && Math.Abs(from - target) >= 1;
        resizing = true;
        SetCurrentValue(HeightProperty, from);
        SymbioMotion.Spring(this, "workspaceCardHeight", () => Height, value => SetCurrentValue(HeightProperty, Math.Max(value, 0)),
            target, SymbioMotion.Depth, immediate: !animate, completed: () =>
            {
                SetCurrentValue(HeightProperty, finish);
                if (collapse && IsSwapped && SwapControl is not null) SwapControl.Visibility = Visibility.Collapsed;
                resizing = false;
            });
    }

    public void StackInstall()
    {
        EnsureFoldContent();
        if (SwapControl is not StackPanel stack) return;
        StackInstall(ref stack, InstallMethod);
        SwapControl = stack;
    }

    public static void StackInstall(ref StackPanel stack, Action<StackPanel>? installMethod)
    {
        if (stack.Tag is null || installMethod is null) return;
        try { installMethod(stack); }
        catch (Exception ex) { ModBase.Log(ex, "加载卡片内容失败"); }
        stack.Children.Add(new Border { Height = 18 });
        stack.Tag = null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        headerPressed = !e.Handled && e.GetPosition(this).Y < SwapedHeight - 6;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasPressed = headerPressed;
        headerPressed = false;
        if (wasPressed && e.GetPosition(this).Y < SwapedHeight - 6) Toggle();
    }

    private void Toggle()
    {
        ModMain.RaiseCustomEvent(this);
        if (SwapControl is null) return;
        var args = new ModBase.RouteEventArgs(true);
        PreviewSwap?.Invoke(this, args);
        if (args.handled) return;
        IsSwapped = !IsSwapped;
        Swap?.Invoke(this, args);
    }
}

public static partial class ModAnimation
{
    public static void AniDispose(WorkspaceCard control, bool removeFromChildren, ParameterizedThreadStart? callBack = null)
    {
        void Remove()
        {
            if (removeFromChildren)
            {
                if (control.Parent is not Panel host) return;
                host.Children.Remove(control);
            }
            else control.Visibility = Visibility.Collapsed;
            callBack?.Invoke(control);
        }

        if (!control.IsHitTestVisible) { Remove(); return; }
        control.IsHitTestVisible = false;
        SymbioMotion.ExitDepth(control, -6, .97, Remove);
    }
}
