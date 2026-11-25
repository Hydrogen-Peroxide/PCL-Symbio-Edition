using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using PCL.Core.UI.Controls.SvgIcon;

namespace PCL;

public sealed class SymbioMenuAction : MenuItem
{
    public static readonly DependencyProperty SvgIconProperty = DependencyProperty.Register(
        nameof(SvgIcon), typeof(string), typeof(SymbioMenuAction),
        new PropertyMetadata("", (d, _) => ((SymbioMenuAction)d).UpdateGlyph()));

    private ContentPresenter? glyph;

    public string SvgIcon { get => (string)GetValue(SvgIconProperty); set => SetValue(SvgIconProperty, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        glyph = GetTemplateChild("Glyph") as ContentPresenter;
        UpdateGlyph();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IconProperty) UpdateGlyph();
    }

    private void UpdateGlyph()
    {
        if (glyph is null) return;
        if (!string.IsNullOrWhiteSpace(SvgIcon))
        {
            var visual = new SvgIcon { Icon = SvgIcon, Stretch = Stretch.Uniform };
            visual.SetBinding(PCL.Core.UI.Controls.SvgIcon.SvgIcon.IconBrushProperty, new Binding(nameof(Foreground)) { Source = this });
            glyph.Content = visual;
        }
        else if (Icon is Geometry || Icon is string)
        {
            try
            {
                var visual = new Path { Data = Icon as Geometry ?? Geometry.Parse((string)Icon), Stretch = Stretch.Uniform };
                visual.SetBinding(Shape.FillProperty, new Binding(nameof(Foreground)) { Source = this });
                glyph.Content = visual;
            }
            catch (FormatException) { glyph.Content = null; }
        }
        else glyph.Content = Icon;
    }

    protected override void OnClick()
    {
        base.OnClick();
        ModMain.RaiseCustomEvent(this);
    }
}
