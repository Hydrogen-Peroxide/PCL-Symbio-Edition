using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCL;

public sealed class AssetImage : Image
{
    public static readonly DependencyProperty AddressProperty = DependencyProperty.Register(nameof(Address), typeof(string),
        typeof(AssetImage), new PropertyMetadata(null, OnAddressChanged));
    public static readonly DependencyProperty FallbackSourceProperty = DependencyProperty.Register(nameof(FallbackSource),
        typeof(string), typeof(AssetImage), new PropertyMetadata(null, OnAddressChanged));
    public static readonly DependencyProperty LoadingSourceProperty = DependencyProperty.Register(nameof(LoadingSource),
        typeof(string), typeof(AssetImage), new PropertyMetadata("pack://application:,,,/images/Icons/NoIcon.png", OnAddressChanged));
    public static readonly DependencyProperty EnableCacheProperty = DependencyProperty.Register(nameof(EnableCache),
        typeof(bool), typeof(AssetImage), new PropertyMetadata(true, OnAddressChanged));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius),
        typeof(CornerRadius), typeof(AssetImage), new PropertyMetadata(new CornerRadius(), (d, _) => ((AssetImage)d).UpdateClip()));

    private static readonly TimeSpan CacheAge = TimeSpan.FromDays(14);
    private int request;

    public AssetImage()
    {
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) => request++;
        SizeChanged += (_, _) => UpdateClip();
    }

    public string? Address { get => (string?)GetValue(AddressProperty); set => SetValue(AddressProperty, value); }
    public string? FallbackSource { get => (string?)GetValue(FallbackSourceProperty); set => SetValue(FallbackSourceProperty, value); }
    public string? LoadingSource { get => (string?)GetValue(LoadingSourceProperty); set => SetValue(LoadingSourceProperty, value); }
    public bool EnableCache { get => (bool)GetValue(EnableCacheProperty); set => SetValue(EnableCacheProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    private static void OnAddressChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) =>
        ((AssetImage)element).Reload();

    private async void Reload()
    {
        if (!IsLoaded) return;
        var number = ++request;
        var address = Address;
        if (string.IsNullOrWhiteSpace(address))
        {
            SetCurrentValue(SourceProperty, null);
            return;
        }

        try
        {
            if (ImageCache.IsRemote(address))
            {
                var cache = ImageCache.GetPath(address);
                var displayedCache = EnableCache && File.Exists(cache) && await Present(cache, number);
                if (number != request) return;
                if (displayedCache && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < CacheAge) return;

                if (!displayedCache)
                {
                    SetCurrentValue(SourceProperty, null);
                    if (!string.IsNullOrEmpty(LoadingSource)) await Present(LoadingSource, number);
                    if (number != request) return;
                }

                var refreshed = await ImageCache.FetchAsync(address);
                if (number != request) return;
                if (refreshed is not null && await Present(refreshed, number)) return;
                if (displayedCache) return;
            }
            else
            {
                SetCurrentValue(SourceProperty, null);
                if (await Present(address, number)) return;
            }

            if (number != request || FallbackSource is not { Length: > 0 } fallback || fallback == address) return;
            var path = ImageCache.IsRemote(fallback) ? await ImageCache.FetchAsync(fallback) : fallback;
            if (path is not null && number == request) await Present(path, number);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"加载图片失败（{address}）", ModBase.LogLevel.Developer);
        }
    }

    private async Task<bool> Present(string path, int number)
    {
        try
        {
            var bitmap = await Task.Run(() => (ImageSource)new MyBitmap(path));
            if (request != number || bitmap is null) return false;
            SetCurrentValue(SourceProperty, bitmap);
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"解码图片失败（{path}）", ModBase.LogLevel.Developer);
            return false;
        }
    }

    private void UpdateClip()
    {
        var radiusX = CornerRadius.TopLeft;
        var radiusY = CornerRadius.TopRight;
        Clip = ActualWidth > 0 && ActualHeight > 0 && radiusX > 0 && radiusY > 0
            ? new RectangleGeometry(new Rect(RenderSize), radiusX, radiusY)
            : null;
    }
}
