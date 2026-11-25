using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using PCLLF.Wpf.Motion;

namespace PCL;

[ContentProperty(nameof(Inlines))]
public sealed class SymbioButton : Button
{
    public enum ColorState { Normal, Highlight, Red }
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(SymbioButton), new PropertyMetadata("", (d, e) => ((SymbioButton)d).label.Text = e.NewValue as string));
    public static readonly DependencyProperty ColorTypeProperty = DependencyProperty.Register(nameof(ColorType),
        typeof(ColorState), typeof(SymbioButton), new PropertyMetadata(ColorState.Normal));
    public static readonly DependencyProperty IsTextActionProperty = DependencyProperty.Register(nameof(IsTextAction),
        typeof(bool), typeof(SymbioButton), new PropertyMetadata(false));
    public bool IsTextAction { get => (bool)GetValue(IsTextActionProperty); set => SetValue(IsTextActionProperty, value); }

    public static readonly DependencyProperty CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner(typeof(SymbioButton));

    public SymbioButton()
    {
        Content = label;
        SetResourceReference(StyleProperty, "SyActionButton");
        PressFeedback.SetIsEnabled(this, true);
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ColorState ColorType { get => (ColorState)GetValue(ColorTypeProperty); set => SetValue(ColorTypeProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public Thickness TextPadding { get => label.Padding; set => label.Padding = value; }
    public InlineCollection Inlines => label.Inlines;

    protected override void OnClick()
    {
        base.OnClick();
        ModMain.RaiseCustomEvent(this);
    }
}
