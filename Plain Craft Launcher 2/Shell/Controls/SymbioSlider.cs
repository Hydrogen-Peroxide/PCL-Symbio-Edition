using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using PCLLF.Wpf.Motion;

namespace PCL;

public sealed class SymbioSlider : Slider
{
    public static readonly DependencyProperty ValueHintProperty = DependencyProperty.Register(
        nameof(ValueHint), typeof(string), typeof(SymbioSlider), new PropertyMetadata(""));
    public string ValueHint { get => (string)GetValue(ValueHintProperty); private set => SetValue(ValueHintProperty, value); }

    private readonly DispatcherTimer hideHint = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private Popup? hint;
    private bool reverting;
    private bool dragging;
    public Delegate? getHintText;
    public event Action<object, bool>? Change;
    public event Action<object, ModBase.RouteEventArgs>? PreviewChange;

    public SymbioSlider()
    {
        Minimum = 0;
        Maximum = 100;
        IsSnapToTickEnabled = true;
        TickFrequency = 1;
        SmallChange = LargeChange = 1;
        IsMoveToPointEnabled = true;
        SetResourceReference(StyleProperty, "SySlider");
        hideHint.Tick += (_, _) => HideHint();
        Unloaded += (_, _) => HideHint();
        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(BeginHint));
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(EndHint));
    }

    public int MaxValue { get => (int)Maximum; set => SetCurrentValue(MaximumProperty, (double)Math.Max(0, value)); }
    public uint ValueByKey
    {
        get => (uint)SmallChange;
        set
        {
            SetCurrentValue(SmallChangeProperty, (double)value);
            SetCurrentValue(LargeChangeProperty, (double)value);
        }
    }

    public int StepValue
    {
        get => (int)Math.Round(base.Value);
        set => SetCurrentValue(ValueProperty, Math.Clamp((double)value, Minimum, Maximum));
    }

    public override void OnApplyTemplate()
    {
        HideHint();
        if (hint is not null) hint.Opened -= Hint_Opened;
        base.OnApplyTemplate();
        hint = GetTemplateChild("PART_ValueHint") as Popup;
        if (hint is not null) hint.Opened += Hint_Opened;
    }

    /// <summary>值提示气泡以前是瞬时出现，接上与下拉框一致的进入动效。</summary>
    private void Hint_Opened(object? sender, EventArgs e)
    {
        if (hint?.Child is not FrameworkElement child) return;
        child.RenderTransformOrigin = new Point(0.5, 1);
        SymbioMotion.EnterDepth(child, 4, .97, SymbioMotion.Pop);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        if (reverting) return;
        var notify = !ModAnimation.AniExitsAnyControl();
        if (notify)
        {
            var e = new ModBase.RouteEventArgs();
            PreviewChange?.Invoke(this, e);
            if (e.handled)
            {
                reverting = true;
                try { SetCurrentValue(ValueProperty, oldValue); }
                finally { reverting = false; }
                if (dragging && IsMouseCaptureWithin) Mouse.Capture(null);
                HideHint();
                return;
            }
        }
        base.OnValueChanged(oldValue, newValue);
        if (notify) Change?.Invoke(this, false);
        if (hint?.IsOpen == true) RefreshPopup();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (dragging && e.Key is Key.Left or Key.Right) return;
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right) ShowHintBriefly();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!dragging) ShowHintBriefly();
    }

    public void RefreshPopup()
    {
        if (hint is null || getHintText is null) return;
        ValueHint = getHintText.DynamicInvoke(StepValue)?.ToString() ?? "";
        hint.IsOpen = true;
    }

    private void ShowHintBriefly()
    {
        if (getHintText is null) return;
        RefreshPopup();
        hideHint.Stop();
        hideHint.Start();
    }

    private void BeginHint(object sender, DragStartedEventArgs e)
    {
        dragging = true;
        hideHint.Stop();
        RefreshPopup();
        if (e.OriginalSource is Thumb thumb) SymbioMotion.SpringScale(thumb, .9, MotionTokens.Press);
    }

    private void EndHint(object sender, DragCompletedEventArgs e)
    {
        dragging = false;
        HideHint();
        if (e.OriginalSource is Thumb thumb) SymbioMotion.SpringScale(thumb, 1, MotionTokens.Press);
    }

    private void HideHint()
    {
        hideHint.Stop();
        if (hint is not null) hint.IsOpen = false;
    }
}
