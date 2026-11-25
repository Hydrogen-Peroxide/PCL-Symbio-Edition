using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using PCLLF.Wpf.Motion;
using SvgGlyph = PCL.Core.UI.Controls.SvgIcon.SvgIcon;

namespace PCL;

public abstract class IconActionButton : Button
{
    public static readonly DependencyProperty LogoProperty = DependencyProperty.Register(
        nameof(Logo), typeof(string), typeof(IconActionButton),
        new PropertyMetadata("", (d, _) => ((IconActionButton)d).UpdateGlyph()));
    public static readonly DependencyProperty SvgIconProperty = DependencyProperty.Register(
        nameof(SvgIcon), typeof(string), typeof(IconActionButton),
        new PropertyMetadata("", (d, _) => ((IconActionButton)d).UpdateGlyph()));
    public static readonly DependencyProperty LogoScaleProperty = DependencyProperty.Register(
        nameof(LogoScale), typeof(double), typeof(IconActionButton),
        new PropertyMetadata(1d, (d, _) => ((IconActionButton)d).UpdateGlyph()));
    public static readonly DependencyProperty CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner(typeof(IconActionButton));

    protected readonly Grid Glyph = new() { IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
    private readonly SvgGlyph svg = new() { Stretch = Stretch.Uniform };
    private readonly Path path = new() { Stretch = Stretch.Uniform };
    private readonly ScaleTransform glyphScale = new();

    protected IconActionButton()
    {
        Glyph.Children.Add(path);
        Glyph.Children.Add(svg);
        Glyph.RenderTransform = glyphScale;
        svg.SetBinding(SvgGlyph.IconBrushProperty, new Binding(nameof(Foreground)) { Source = this });
        path.SetBinding(Shape.FillProperty, new Binding(nameof(Foreground)) { Source = this });
        UpdateGlyph();
        PressFeedback.SetIsEnabled(this, true);
    }

    public string Logo { get => (string)GetValue(LogoProperty); set => SetValue(LogoProperty, value); }
    public string SvgIcon { get => (string)GetValue(SvgIconProperty); set => SetValue(SvgIconProperty, value); }
    public double LogoScale { get => (double)GetValue(LogoScaleProperty); set => SetValue(LogoScaleProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    private void UpdateGlyph()
    {
        var hasSvg = !string.IsNullOrWhiteSpace(SvgIcon);
        svg.Icon = SvgIcon ?? "";
        svg.Visibility = hasSvg ? Visibility.Visible : Visibility.Collapsed;
        path.Visibility = !hasSvg && !string.IsNullOrWhiteSpace(Logo) ? Visibility.Visible : Visibility.Collapsed;
        path.Data = path.Visibility == Visibility.Visible ? Geometry.Parse(Logo) : null;
        glyphScale.ScaleX = glyphScale.ScaleY = hasSvg ? 1 : LogoScale;
        Glyph.Visibility = hasSvg || path.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed class SymbioIconButton : IconActionButton
{
    public enum Themes { Color, White, Black, Red, Custom }

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(Themes), typeof(SymbioIconButton), new PropertyMetadata(Themes.Color));
    public Themes Theme { get => (Themes)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }

    public SymbioIconButton()
    {
        Glyph.MaxWidth = Glyph.MaxHeight = 18;
        Content = Glyph;
        SetResourceReference(StyleProperty, "SyIconAction");
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var measured = base.MeasureOverride(constraint);
        return double.IsNaN(Width) && double.IsFinite(Height) && Height > 0 ? new Size(Height, Height) : measured;
    }

    protected override void OnClick()
    {
        base.OnClick();
        ModMain.RaiseCustomEvent(this);
    }
}

[ContentProperty(nameof(Inlines))]
public sealed class SymbioIconTextAction : IconActionButton
{
    public enum ColorState { Black, Highlight }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SymbioIconTextAction),
        new PropertyMetadata("", (d, e) => ((SymbioIconTextAction)d).label.Text = e.NewValue as string));
    public static readonly DependencyProperty ColorTypeProperty = DependencyProperty.Register(
        nameof(ColorType), typeof(ColorState), typeof(SymbioIconTextAction), new PropertyMetadata(ColorState.Black));
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ColorState ColorType { get => (ColorState)GetValue(ColorTypeProperty); set => SetValue(ColorTypeProperty, value); }
    public InlineCollection Inlines => label.Inlines;

    public SymbioIconTextAction()
    {
        Glyph.Width = Glyph.Height = 16;
        Glyph.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Glyph);
        row.Children.Add(label);
        Glyph.IsVisibleChanged += (_, _) => label.Margin = new Thickness(Glyph.Visibility == Visibility.Visible ? 7 : 0, 0, 0, 0);
        Content = row;
        SetResourceReference(StyleProperty, "SyIconTextAction");
    }

    protected override void OnClick()
    {
        base.OnClick();
        ModMain.RaiseCustomEvent(this);
    }
}

public static partial class ModAnimation
{
    public static void AniDispose(SymbioIconButton control, bool removeFromChildren, ParameterizedThreadStart? callback = null)
    {
        if (!control.IsHitTestVisible) return;
        control.IsHitTestVisible = false;
        SymbioMotion.ExitDepth(control, -6, .97, () =>
        {
            if (removeFromChildren)
            {
                if (control.Parent is Panel host) host.Children.Remove(control);
            }
            else control.Visibility = Visibility.Collapsed;
            callback?.Invoke(control);
        });
    }
}
