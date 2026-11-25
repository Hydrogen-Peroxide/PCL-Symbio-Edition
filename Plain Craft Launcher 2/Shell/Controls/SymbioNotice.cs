using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.UI.Theme;

namespace PCL;

[ContentProperty(nameof(Inlines))]
public sealed class SymbioNotice : Control
{
    public enum Themes { Blue, Red, Yellow }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(SymbioNotice), new PropertyMetadata("", (d, e) => ((SymbioNotice)d).Message.Text = (string)e.NewValue));
    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(nameof(Theme), typeof(Themes),
        typeof(SymbioNotice), new PropertyMetadata(Themes.Red, (d, _) => ((SymbioNotice)d).RefreshPalette(ThemeManager.IsDarkMode)));
    public static readonly DependencyProperty IsWarnProperty = DependencyProperty.Register(nameof(IsWarn), typeof(bool),
        typeof(SymbioNotice), new PropertyMetadata(true, (d, e) => ((SymbioNotice)d).Theme = (bool)e.NewValue ? Themes.Red : Themes.Blue));
    public static readonly DependencyProperty CanCloseProperty = DependencyProperty.Register(nameof(CanClose), typeof(bool),
        typeof(SymbioNotice), new PropertyMetadata(false));
    public static readonly DependencyProperty HasBorderProperty = DependencyProperty.Register(nameof(HasBorder), typeof(bool),
        typeof(SymbioNotice), new PropertyMetadata(true));
    public static readonly DependencyProperty CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner(typeof(SymbioNotice));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(Geometry), typeof(SymbioNotice));
    public static readonly DependencyProperty GlyphBackgroundProperty = DependencyProperty.Register(nameof(GlyphBackground),
        typeof(Brush), typeof(SymbioNotice));
    public static readonly DependencyProperty GlyphStrokeProperty = DependencyProperty.Register(nameof(GlyphStroke),
        typeof(Brush), typeof(SymbioNotice));

    private static readonly Geometry InfoGlyph = MakeGlyph("M8,7.2 V11.6 M8,4.6 V4.7");
    private static readonly Geometry WarningGlyph = MakeGlyph("M8,4.4 V8.8 M8,11.4 V11.5");
    private static readonly Geometry ErrorGlyph = MakeGlyph("M5.6,5.6 L10.4,10.4 M10.4,5.6 L5.6,10.4");
    private Button? dismiss;
    private bool pressed;

    public SymbioNotice()
    {
        Message.SetResourceReference(TextBlock.ForegroundProperty, "SyText");
        SetResourceReference(BorderBrushProperty, "SyCardStroke");
        RefreshPalette(ThemeManager.IsDarkMode);
        Loaded += (_, _) =>
        {
            ThemeService.ColorModeChanged += ThemeChanged;
            RefreshPalette(ThemeManager.IsDarkMode);
            if (CanClose && ConfigService.TryGetConfigItemNoType(RelativeSetup, out var item) && item.GetValueNoType() is true)
                SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        };
        Unloaded += (_, _) => ThemeService.ColorModeChanged -= ThemeChanged;
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Themes Theme { get => (Themes)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }
    public bool IsWarn
    {
        get => Theme == Themes.Red;
        set => SetCurrentValue(ThemeProperty, value ? Themes.Red : Themes.Blue);
    }
    public bool CanClose { get => (bool)GetValue(CanCloseProperty); set => SetValue(CanCloseProperty, value); }
    public bool HasBorder { get => (bool)GetValue(HasBorderProperty); set => SetValue(HasBorderProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public Geometry Glyph => (Geometry)GetValue(GlyphProperty);
    public Brush GlyphBackground => (Brush)GetValue(GlyphBackgroundProperty);
    public Brush GlyphStroke => (Brush)GetValue(GlyphStrokeProperty);
    public string RelativeSetup { get; set; } = "";
    public TextBlock Message { get; } = new()
    {
        LineHeight = 18, Padding = new Thickness(10, 9, 12, 9), VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
    public InlineCollection Inlines => Message.Inlines;

    public override void OnApplyTemplate()
    {
        if (dismiss is not null) dismiss.Click -= Dismiss;
        base.OnApplyTemplate();
        dismiss = GetTemplateChild("PART_Dismiss") as Button;
        if (dismiss is not null) dismiss.Click += Dismiss;
    }

    private void Dismiss(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ConfigService.TryGetConfigItemNoType(RelativeSetup, out var item)) item.SetValueNoType(true);
        ModAnimation.AniDispose(this, false);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        pressed = !e.Handled;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        pressed = false;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var invoke = pressed && !e.Handled;
        pressed = false;
        if (!invoke) return;
        ModMain.RaiseCustomEvent(this);
    }

    private void ThemeChanged(bool dark, ColorTheme theme)
    {
        if (Dispatcher.CheckAccess()) RefreshPalette(dark);
        else Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) RefreshPalette(ThemeManager.IsDarkMode); }));
    }

    private static Geometry MakeGlyph(string data)
    {
        var glyph = Geometry.Parse(data);
        glyph.Freeze();
        return glyph;
    }

    private static SolidColorBrush SolidBrush(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void RefreshPalette(bool dark)
    {
        var (background, badge, glyph) = Theme switch
        {
            Themes.Red => (dark ? SolidBrush(0x44, 0x27, 0x26) : SolidBrush(0xFD, 0xE7, 0xE9),
                dark ? SolidBrush(0xFF, 0x99, 0xA4) : SolidBrush(0xC4, 0x2B, 0x1C), ErrorGlyph),
            Themes.Yellow => (dark ? SolidBrush(0x43, 0x35, 0x19) : SolidBrush(0xFF, 0xF4, 0xCE),
                dark ? SolidBrush(0xFC, 0xE1, 0x00) : SolidBrush(0x9D, 0x5D, 0x00), WarningGlyph),
            _ => (dark ? SolidBrush(255, 255, 255, 0x10) : SolidBrush(0xF6, 0xF6, 0xF6, 0xB3), null, InfoGlyph)
        };
        SetCurrentValue(BackgroundProperty, background);
        SetValue(GlyphProperty, glyph);
        if (badge is null)
        {
            SetResourceReference(GlyphBackgroundProperty, "SyAccentFill");
            SetResourceReference(GlyphStrokeProperty, "SyTextOnAccent");
        }
        else
        {
            SetValue(GlyphBackgroundProperty, badge);
            SetValue(GlyphStrokeProperty, dark ? Brushes.Black : Brushes.White);
        }
    }
}

public static partial class ModAnimation
{
    public static void AniDispose(SymbioNotice notice, bool removeFromChildren, ParameterizedThreadStart? callback = null)
    {
        if (!notice.IsHitTestVisible) return;
        notice.IsHitTestVisible = false;
        SymbioMotion.ExitDepth(notice, -6, .97, () =>
        {
            if (removeFromChildren) (notice.Parent as Panel)?.Children.Remove(notice);
            else notice.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            callback?.Invoke(notice);
        });
    }
}
