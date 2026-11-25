using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using SvgGlyph = PCL.Core.UI.Controls.SvgIcon.SvgIcon;

namespace PCL;

public sealed class SymbioSegment : SymbioRadio
{
    public enum ColorState { White, Highlight }
    public static readonly DependencyProperty ColorTypeProperty = DependencyProperty.Register(
        nameof(ColorType), typeof(ColorState), typeof(SymbioSegment), new PropertyMetadata(ColorState.White));
    public ColorState ColorType { get => (ColorState)GetValue(ColorTypeProperty); set => SetValue(ColorTypeProperty, value); }

    private readonly TextBlock caption;
    public Thickness LabelMargin { get => caption.Margin; set => caption.Margin = value; }

    private readonly ContentPresenter glyph = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed };
    public string Logo { get => field; set { field = value; UpdateGlyph(); } } = "";
    public string SvgIcon { get => field; set { field = value; UpdateGlyph(); } } = "";
    public double LogoScale { get => field; set { field = value; UpdateGlyph(); } } = 1;

    public new event Action<SymbioSegment, bool>? Check;
    public event Action<object, ModBase.RouteEventArgs>? PreviewClick;

    public SymbioSegment()
    {
        caption = (TextBlock)Content;
        Content = null;
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, caption } };
        SetResourceReference(StyleProperty, "SySegment");
        base.Check += (_, args) => Check?.Invoke(this, args.raiseByMouse);
        PreviewCheck += (sender, args) => PreviewClick?.Invoke(sender, args);
    }

    public void SetChecked(bool value, bool raiseByMouse, bool anime) => base.SetChecked(value, raiseByMouse);

    private void UpdateGlyph()
    {
        if (!string.IsNullOrWhiteSpace(SvgIcon))
        {
            var icon = new SvgGlyph { Icon = SvgIcon };
            icon.SetBinding(SvgGlyph.IconBrushProperty, new Binding(nameof(Foreground)) { Source = this });
            glyph.Content = icon;
        }
        else if (!string.IsNullOrWhiteSpace(Logo))
        {
            var path = new Path
            {
                Data = Geometry.Parse(Logo), Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = new ScaleTransform(LogoScale, LogoScale)
            };
            path.SetBinding(Shape.FillProperty, new Binding(nameof(Foreground)) { Source = this });
            glyph.Content = path;
        }
        else glyph.Content = null;
        glyph.Visibility = glyph.Content is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
