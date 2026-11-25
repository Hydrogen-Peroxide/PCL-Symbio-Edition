using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PCL.Core.UI.Theme;

namespace PCL;

/// <summary>
///     Generates the default wallpaper shown when the user has not chosen one: a soft field of three
///     colour washes around the theme hue. It is a real bitmap so glass surfaces can blur and sample it
///     exactly like a user wallpaper.
/// </summary>
internal static class SymbioBackdrop
{
    private const int Width = 960;
    private const int Height = 600;

    private static (double Hue, bool Dark)? _cachedKey;
    private static BitmapSource? _cached;

    public static BitmapSource Create(double hue, bool dark)
    {
        if (_cached is not null && _cachedKey == (hue, dark)) return _cached;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, Width, Height);
            var baseColor = dark ? Lch(0.19, 0.012, hue) : Lch(0.965, 0.010, hue);
            context.DrawRectangle(new SolidColorBrush(baseColor), null, rect);

            // Three washes: theme hue top-left, a cooler neighbour bottom-right, a warmer neighbour mid-right.
            DrawWash(context, new Point(Width * 0.12, Height * 0.10), Width * 0.70,
                dark ? Lch(0.34, 0.075, hue) : Lch(0.86, 0.070, hue));
            DrawWash(context, new Point(Width * 0.92, Height * 0.95), Width * 0.75,
                dark ? Lch(0.30, 0.065, hue + 48) : Lch(0.88, 0.060, hue + 48));
            DrawWash(context, new Point(Width * 0.78, Height * 0.22), Width * 0.45,
                dark ? Lch(0.27, 0.050, hue - 42) : Lch(0.91, 0.050, hue - 42));
        }

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        _cachedKey = (hue, dark);
        _cached = bitmap;
        return bitmap;
    }

    private static void DrawWash(DrawingContext context, Point center, double radius, Color color)
    {
        var transparent = Color.FromArgb(0, color.R, color.G, color.B);
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            {
                new GradientStop(color, 0),
                new GradientStop(Color.FromArgb(150, color.R, color.G, color.B), 0.45),
                new GradientStop(transparent, 1)
            }
        };
        brush.Freeze();
        context.DrawEllipse(brush, null, center, radius, radius * 0.8);
    }

    private static Color Lch(double l, double c, double h) => LabColor.FromLch(l, c, ((h % 360) + 360) % 360);
}
