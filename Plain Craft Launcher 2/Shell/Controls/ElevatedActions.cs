using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;

namespace PCL;

public abstract class ElevatedActionBase : IconActionButton
{
    private FrameworkElement? pose;
    private double reveal;

    protected ElevatedActionBase()
    {
        IsHitTestVisible = false;
        IsTabStop = false;
    }

    public bool Show
    {
        get;
        set
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => Show = value);
                return;
            }
            if (field == value) return;
            field = value;
            IsHitTestVisible = IsTabStop = value;
            if (value) OnRevealStarted();
            // 未 Loaded 时不能 Begin：先归零姿态，等 Loaded 后再补播一次，否则会直接跳到终态、看不出动画
            if (!IsLoaded && value)
            {
                ApplyReveal(0);
                UiAnim.RunOnce(this, () => SymbioMotion.Spring(this, "elevatedActionReveal", () => reveal, ApplyReveal,
                    1, SymbioMotion.Pop, completed: () => { if (!Show) OnRevealFinished(); }));
                return;
            }

            SymbioMotion.Spring(this, "elevatedActionReveal", () => reveal, ApplyReveal,
                value ? 1 : 0, SymbioMotion.Pop, immediate: !IsLoaded,
                completed: () => { if (!Show) OnRevealFinished(); });
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        pose = GetTemplateChild("PART_Pose") as FrameworkElement;
        ApplyReveal(reveal);
    }

    private void ApplyReveal(double value)
    {
        reveal = value;
        if (pose is not null)
        {
            var scale = SymbioMotion.Scale(pose);
            scale.ScaleX = scale.ScaleY = .6 + .4 * Math.Max(0, value);
            pose.Opacity = Math.Clamp(value * 1.5, 0, 1);
        }
        OnRevealProgress(Math.Clamp(value, 0, 1));
    }

    protected virtual void OnRevealStarted() { }
    protected virtual void OnRevealProgress(double value) { }
    protected virtual void OnRevealFinished() { }

    protected override void OnClick()
    {
        base.OnClick();
        ModMain.RaiseCustomEvent(this);
    }
}

public sealed class FloatingIconAction : ElevatedActionBase
{
    private const double FaceSize = 52;
    private const double SlotWidth = 60;
    private FrameworkElement? progressFill;
    private Panel? pulses;
    private bool rightPressed;

    public FloatingIconAction()
    {
        Visibility = Visibility.Collapsed;
        Width = 0;
        Glyph.Width = Glyph.Height = 20;
        Content = Glyph;
        SetResourceReference(StyleProperty, "SyFloatingIconAction");
        SetBinding(AutomationProperties.NameProperty, new Binding(nameof(ToolTip)) { Source = this });
    }

    public Func<bool>? ShowWhen { get; set; }
    public bool CanRightClick { get; set; }
    public event MouseButtonEventHandler? RightClick;

    public double Progress
    {
        get;
        set
        {
            var progress = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (field == progress) return;
            field = progress;
            ApplyProgress();
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        progressFill = GetTemplateChild("PART_Progress") as FrameworkElement;
        pulses = GetTemplateChild("PART_Pulses") as Panel;
        ApplyProgress();
    }

    public void ShowRefresh()
    {
        if (ShowWhen is { } query) Show = query();
    }

    private void ApplyProgress()
    {
        if (progressFill is null) return;
        progressFill.Height = FaceSize * Progress;
        progressFill.Visibility = Progress < .0001 ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnRevealStarted() => Visibility = Visibility.Visible;
    protected override void OnRevealProgress(double value) => Width = SlotWidth * value;
    protected override void OnRevealFinished() => Visibility = Visibility.Collapsed;

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (!CanRightClick) return;
        rightPressed = true;
        Focus();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var invoke = rightPressed && CanRightClick;
        rightPressed = false;
        if (!invoke) return;
        e.Handled = true;
        RightClick?.Invoke(this, e);
        ModMain.RaiseCustomEvent(this);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        rightPressed = false;
        base.OnMouseLeave(e);
    }

    public void Pulse() => ModBase.RunInUi(() =>
    {
        if (!Show || pulses is not { } host) return;
        var pulse = new Border { CornerRadius = new CornerRadius(8), IsHitTestVisible = false };
        pulse.SetResourceReference(Border.BackgroundProperty, "SyAccentHover");
        host.Children.Add(pulse);
        SymbioMotion.Spring(pulse, "dockPulse", () => 0, value =>
        {
            var scale = SymbioMotion.Scale(pulse);
            scale.ScaleX = scale.ScaleY = 1 + .25 * value;
            pulse.Opacity = .5 * (1 - Math.Clamp(value, 0, 1));
        }, 1, SymbioMotion.Pop, completed: () => host.Children.Remove(pulse));
    });
}

[ContentProperty(nameof(Inlines))]
public sealed class ProminentAction : ElevatedActionBase
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(ProminentAction), new PropertyMetadata("", (d, e) => ((ProminentAction)d).label.Text = (string)e.NewValue));

    private readonly TextBlock label = new()
    {
        VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = FontWeights.SemiBold
    };

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public InlineCollection Inlines => label.Inlines;

    public ProminentAction()
    {
        Glyph.Width = Glyph.Height = 16;
        Glyph.VerticalAlignment = VerticalAlignment.Center;
        Glyph.IsVisibleChanged += (_, _) => label.Margin = new Thickness(Glyph.Visibility == Visibility.Visible ? 10 : 0, 0, 0, 0);
        label.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Foreground)) { Source = this });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph);
        row.Children.Add(label);
        Content = row;
        SetResourceReference(StyleProperty, "SyProminentAction");
        SetBinding(AutomationProperties.NameProperty, new Binding(nameof(Text)) { Source = this });
    }
}
