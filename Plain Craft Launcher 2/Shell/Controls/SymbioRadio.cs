using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using PCLLF.Wpf.Motion;

namespace PCL;

[ContentProperty(nameof(Inlines))]
public class SymbioRadio : RadioButton, IMyRadio
{
    private readonly TextBlock label = new();
    private bool userChange;
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(SymbioRadio), new PropertyMetadata("", (d, e) => ((SymbioRadio)d).label.Text = e.NewValue as string));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public InlineCollection Inlines => label.Inlines;

    public SymbioRadio()
    {
        Content = label;
        SetResourceReference(StyleProperty, "SyRadio");
        PressFeedback.SetIsEnabled(this, true);
    }

    public new bool Checked { get => IsChecked == true; set => SetChecked(value, false); }
    public event IMyRadio.CheckEventHandler? Check;
    public event IMyRadio.ChangedEventHandler? Changed;
    public event Action<object, ModBase.RouteEventArgs>? PreviewCheck;
    public event Action<object, ModBase.RouteEventArgs>? PreviewChange;

    protected override void OnToggle() => SetChecked(true, true);

    public void SetChecked(bool value, bool user)
    {
        if (value && user)
        {
            var args = new ModBase.RouteEventArgs(true);
            PreviewCheck?.Invoke(this, args);
            if (args.handled) return;
        }
        if (Checked == value) return;
        if (IsLoaded) PreviewChange?.Invoke(this, new ModBase.RouteEventArgs(user));
        userChange = user;
        try
        {
            SetCurrentValue(IsCheckedProperty, value);
            if (!value && Parent is Panel panel)
            {
                var siblings = panel.Children.OfType<SymbioRadio>().Where(option => option.GroupName == GroupName).ToList();
                if (siblings.Count != 0 && !siblings.Any(option => option.Checked)) siblings[0].SetChecked(true, false);
            }
        }
        finally { userChange = false; }
    }

    protected override void OnChecked(RoutedEventArgs e) { base.OnChecked(e); StateChanged(); }
    protected override void OnUnchecked(RoutedEventArgs e) { base.OnUnchecked(e); StateChanged(); }
    private void StateChanged()
    {
        UpdateGlyph();
        if (Parent is null) return;
        if (Checked) Check?.Invoke(this, new ModBase.RouteEventArgs(userChange));
        Changed?.Invoke(this, new ModBase.RouteEventArgs(userChange));
        ModMain.RaiseCustomEvent(this);
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateGlyph(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsPressedProperty || e.Property == IsMouseOverProperty) UpdateGlyph();
    }
    private void UpdateGlyph()
    {
        if (GetTemplateChild("Dot") is FrameworkElement dot)
        {
            double scale = Checked ? IsPressed ? .82 : IsMouseOver ? 1.12 : 1 : IsPressed ? .8 : 0;
            SymbioMotion.SpringScale(dot, scale, MotionTokens.Toggle);
        }
    }
}
