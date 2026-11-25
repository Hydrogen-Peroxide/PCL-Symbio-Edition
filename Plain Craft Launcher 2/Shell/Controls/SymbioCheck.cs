using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using PCLLF.Wpf.Motion;

namespace PCL;

[ContentProperty(nameof(Inlines))]
public sealed class SymbioCheck : CheckBox
{
    private readonly TextBlock label = new();
    private bool userChange;
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(SymbioCheck), new PropertyMetadata("", (d, e) => ((SymbioCheck)d).label.Text = e.NewValue as string));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public InlineCollection Inlines => label.Inlines;

    public SymbioCheck()
    {
        Content = label;
        SetResourceReference(StyleProperty, "SyCheck");
        PressFeedback.SetIsEnabled(this, true);
    }

    public new bool? Checked { get => IsChecked; set => SetChecked(value, false); }
    public event Action<object, bool>? Change;
    public event Action<object, ModBase.RouteEventArgs>? PreviewChange;

    protected override void OnToggle() => SetChecked(IsThreeState ? IsChecked switch
        { true => null, false => true, null => false } : IsChecked != true, true);

    public void SetChecked(bool? value, bool user)
    {
        var next = IsThreeState ? value : value == true;
        if (IsChecked == next) return;
        if (user && next == true)
        {
            var args = new ModBase.RouteEventArgs(true);
            PreviewChange?.Invoke(this, args);
            if (args.handled) return;
        }
        userChange = user;
        try { SetCurrentValue(IsCheckedProperty, next); }
        finally { userChange = false; }
    }

    protected override void OnChecked(RoutedEventArgs e) { base.OnChecked(e); StateChanged(); }
    protected override void OnUnchecked(RoutedEventArgs e) { base.OnUnchecked(e); StateChanged(); }
    protected override void OnIndeterminate(RoutedEventArgs e) { base.OnIndeterminate(e); StateChanged(); }
    private void StateChanged()
    {
        UpdateGlyph();
        if (!IsLoaded) return;
        Change?.Invoke(this, userChange);
        ModMain.RaiseCustomEvent(this);
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateGlyph(); }
    private void UpdateGlyph()
    {
        if (GetTemplateChild("CheckGlyph") is FrameworkElement check)
            SymbioMotion.SpringScale(check, IsChecked == true ? 1 : 0, MotionTokens.Toggle);
        if (GetTemplateChild("MixedGlyph") is FrameworkElement mixed)
            SymbioMotion.SpringScale(mixed, IsChecked is null ? 1 : 0, MotionTokens.Toggle);
        // ToggleSwitch template: the knob slides to the "on" end on a slightly under-damped spring.
        if (GetTemplateChild("SwitchKnob") is FrameworkElement knob)
            SymbioMotion.SpringTranslateX(knob, IsChecked == true ? 20 : 0, new SpringParameters(.3, .72));
    }
}
