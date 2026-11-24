using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PCL;

internal static class FluentAppearance
{
    private const int WindowCompositionAccentPolicy = 19;
    private const int AcrylicBlurBehind = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State;
        public int Flags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionData
    {
        public int Attribute;
        public IntPtr Data;
        public nuint Size;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionData data);

    internal static bool ApplyAcrylic(Window window, bool enabled)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
            return false;

        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero)
            return false;

        var policy = new AccentPolicy
        {
            State = enabled ? AcrylicBlurBehind : 0,
            GradientColor = 0xBF281E1E
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, memory, false);
            var data = new CompositionData
            {
                Attribute = WindowCompositionAccentPolicy,
                Data = memory,
                Size = (nuint)size
            };
            return SetWindowCompositionAttribute(windowHandle, ref data) != 0 && enabled;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    internal static void UpdateWallpaperAccent(Brush? wallpaper)
    {
        var resources = System.Windows.Application.Current.Resources;
        resources.Remove("SeWallpaperAccent");
        if (wallpaper is not ImageBrush { ImageSource: BitmapSource source })
            return;

        var scale = Math.Min(1d, 48d / Math.Max(source.PixelWidth, source.PixelHeight));
        var sample = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var bitmap = new FormatConvertedBitmap(sample, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var weights = new int[512];
        var red = new int[512];
        var green = new int[512];
        var blue = new int[512];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var r = pixels[offset + 2];
            var g = pixels[offset + 1];
            var b = pixels[offset];
            var max = Math.Max(r, Math.Max(g, b));
            var spread = max - Math.Min(r, Math.Min(g, b));
            if (pixels[offset + 3] < 192 || max < 48 || spread < 24)
                continue;

            var bucket = (r >> 5) * 64 + (g >> 5) * 8 + (b >> 5);
            weights[bucket] += spread;
            red[bucket] += r * spread;
            green[bucket] += g * spread;
            blue[bucket] += b * spread;
        }

        var winner = Array.IndexOf(weights, weights.Max());
        var weight = weights[winner];
        if (weight == 0)
            return;

        var accent = new SolidColorBrush(Color.FromRgb(
            (byte)(red[winner] / weight), (byte)(green[winner] / weight), (byte)(blue[winner] / weight)));
        accent.Freeze();
        resources["SeWallpaperAccent"] = accent;
    }

    internal static void AnimatePage(FrameworkElement surface)
    {
        if (!SystemParameters.ClientAreaAnimation || ModAnimation.aniSpeed <= 0)
            return;

        surface.RenderTransformOrigin = new Point(0.5, 0.5);
        surface.RenderTransform = new ScaleTransform();
        var storyboard = new Storyboard { FillBehavior = FillBehavior.Stop };
        AddAnimation(new PropertyPath(UIElement.OpacityProperty), 0, 1);
        AddAnimation(new PropertyPath("RenderTransform.ScaleX"), 0.97, 1);
        AddAnimation(new PropertyPath("RenderTransform.ScaleY"), 0.97, 1);
        storyboard.Begin(surface, HandoffBehavior.SnapshotAndReplace);

        void AddAnimation(PropertyPath path, double from, double to)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetProperty(animation, path);
            storyboard.Children.Add(animation);
        }
    }
}
