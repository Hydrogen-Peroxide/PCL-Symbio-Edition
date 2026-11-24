// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PCLLF.Wpf.Glass;

/// <summary>
/// Shares an already-rendered wallpaper blur between surfaces of the same host.
/// Call on the host's Dispatcher, outside OnRender (for example, in a coalesced callback).
/// Only the supplied image is drawn; the host and its UI descendants are never captured.
/// </summary>
internal static class WallpaperBlurCache {
    private const int MaxDimension = 4096;
    private const long MaxPixels = 8_000_000;
    private const int MaxCachedEntries = 6;
    private static readonly ConditionalWeakTable<FrameworkElement, HostCache> Hosts = new();
    private static readonly ConditionalWeakTable<ImageSource, SourceStamp> SourceStamps = new();
    private static readonly ConditionalWeakTable<ImageSource, object> VideoFrames = new();
    private static readonly ImageSource EmptySource = EmptyImage();
    private static long _buildCount;

    internal readonly record struct Frame(ImageSource Source, double Padding);

    /// <summary>Successful raster builds; cache hits and unblurred fallbacks do not increment it.</summary>
    internal static long BuildCount => Interlocked.Read(ref _buildCount);

    internal static void RegisterVideoFrame(ImageSource source) => VideoFrames.Add(source, new object());
    internal static bool IsVideoFrame(ImageSource source) => VideoFrames.TryGetValue(source, out _);

    internal static bool TryGet(FrameworkElement host, ImageSource source, Stretch stretch, double radius, out Frame frame) {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(source);
        host.Dispatcher.VerifyAccess();
        source.Dispatcher?.VerifyAccess();

        if (IsVideoFrame(source)) { frame = new(source, 0); return true; }
        if (!TryBuildKey(host, source, stretch, radius, out Key key)) { frame = Unblurred(source); return true; }
        if (radius == 0) { frame = Unblurred(source); return true; }
        if (Hosts.TryGetValue(host, out HostCache? cache)) {
            for (int index = 0; index < cache.Entries.Count; index++) {
                Entry existing = cache.Entries[index];
                if (!existing.Key.Matches(key)) continue;
                if (index != 0) { cache.Entries.RemoveAt(index); cache.Entries.Insert(0, existing); }
                frame = existing.Frame;
                return true;
            }
        }
        frame = default;
        return false;
    }

    internal static Frame GetOrCreate(FrameworkElement host, ImageSource source, Stretch stretch, double radius) {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(source);
        host.Dispatcher.VerifyAccess();
        source.Dispatcher?.VerifyAccess();

        if (IsVideoFrame(source)) return new(source, 0);
        if (!TryBuildKey(host, source, stretch, radius, out Key key) || radius == 0) return Unblurred(source);
        HostCache cache = Hosts.GetValue(host, static _ => new HostCache());
        for (int index = 0; index < cache.Entries.Count; index++) {
            Entry existing = cache.Entries[index];
            if (!existing.Key.Matches(key)) continue;
            if (index != 0) { cache.Entries.RemoveAt(index); cache.Entries.Insert(0, existing); }
            return existing.Frame;
        }
        // A detached render can raise layout notifications. A reentrant request must
        // not recursively build the same host's texture before the first one is ready.
        if (cache.Building) return Unblurred(source);
        cache.Building = true;
        Frame frame;
        try { frame = Render(key); }
        catch (Exception error) when (Recoverable(error)) { frame = Unblurred(source); }
        finally { cache.Building = false; }
        // Keep only actual blurred frames in the bounded LRU cache so multi-radius hosts never thrash.
        cache.Entries.Insert(0, new Entry(key, frame));
        if (cache.Entries.Count > MaxCachedEntries) cache.Entries.RemoveAt(MaxCachedEntries);
        return frame;
    }

    private static bool TryBuildKey(FrameworkElement host, ImageSource source, Stretch stretch, double radius, out Key key) {
        Size size = host.RenderSize;
        if (!double.IsFinite(radius) || radius < 0 || !Positive(size.Width) || !Positive(size.Height)) { key = default; return false; }
        DpiScale dpi = VisualTreeHelper.GetDpi(host);
        if (!Positive(dpi.DpiScaleX) || !Positive(dpi.DpiScaleY)) { key = default; return false; }
        long revision = source.IsFrozen ? 0 : SourceStamps.GetValue(source, static image => new SourceStamp(image)).Revision;
        key = new Key(source, revision, size, dpi.DpiScaleX, dpi.DpiScaleY, stretch, radius);
        return true;
    }

    private static Frame Render(Key key) {
        double padding = key.Radius * 2;
        double width = key.Size.Width + padding * 2, height = key.Size.Height + padding * 2;
        if (!Positive(width) || !Positive(height)) return Unblurred(key.Source);
        double wantedWidth = Math.Ceiling(width * key.DpiX), wantedHeight = Math.Ceiling(height * key.DpiY);
        if (!Positive(wantedWidth) || !Positive(wantedHeight)) return Unblurred(key.Source);
        double blurScale = key.Radius >= 30 ? 0.28 : key.Radius >= 18 ? 0.35 : key.Radius >= 10 ? 0.5 : 1;
        double minScale = Math.Min(1, 64 / Math.Min(wantedWidth, wantedHeight));
        double reduction = Math.Min(1, Math.Min(MaxDimension / wantedWidth, MaxDimension / wantedHeight));
        reduction = Math.Min(reduction, Math.Sqrt((double)MaxPixels / wantedWidth / wantedHeight));
        reduction = Math.Min(reduction, Math.Max(blurScale, minScale));
        int pixelWidth = Math.Max(1, (int)Math.Floor(wantedWidth * reduction));
        int pixelHeight = Math.Max(1, (int)Math.Floor(wantedHeight * reduction));
        pixelWidth = Math.Min(MaxDimension, pixelWidth); pixelHeight = Math.Min(MaxDimension, pixelHeight);
        if ((long)pixelWidth * pixelHeight > MaxPixels) pixelHeight = Math.Max(1, (int)(MaxPixels / pixelWidth));

        // Derive output DPI from the chosen pixels, so downsampling/rounding never
        // changes the image's DIP size or the caller's exact 2*radius padding.
        double outputDpiX = 96 * pixelWidth / width, outputDpiY = 96 * pixelHeight / height;
        var brush = new ImageBrush(key.Source) {
            Stretch = key.Stretch,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Viewport = new Rect(padding, padding, key.Size.Width, key.Size.Height),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.FlipXY
        };
        if (key.Source.IsFrozen) brush.Freeze();
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.LowQuality);
        using (DrawingContext context = visual.RenderOpen()) {
            context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }
        var raster = new RenderTargetBitmap(pixelWidth, pixelHeight, outputDpiX, outputDpiY, PixelFormats.Pbgra32);
        raster.Render(visual);

        int stride = pixelWidth * 4;
        byte[] buffer = new byte[stride * pixelHeight];
        raster.CopyPixels(buffer, stride, 0);

        double sigmaX = key.Radius / 3d * (pixelWidth / width);
        double sigmaY = key.Radius / 3d * (pixelHeight / height);
        BlurPbgra32(buffer, pixelWidth, pixelHeight, sigmaX, sigmaY);

        BitmapSource bitmap = BitmapSource.Create(pixelWidth, pixelHeight, outputDpiX, outputDpiY, PixelFormats.Pbgra32, null, buffer, stride);
        bitmap.Freeze();
        Interlocked.Increment(ref _buildCount);
        return new Frame(bitmap, padding);
    }

    internal static unsafe void BlurPbgra32(byte[] pixels, int width, int height, double sigmaX, double sigmaY) {
        if (width <= 1 && height <= 1) return;
        ComputeBoxRadii(sigmaX, out int rx0, out int rx1, out int rx2);
        ComputeBoxRadii(sigmaY, out int ry0, out int ry1, out int ry2);
        if (rx0 == 0 && rx1 == 0 && rx2 == 0 && ry0 == 0 && ry1 == 0 && ry2 == 0) return;

        byte[] scratch = System.Buffers.ArrayPool<byte>.Shared.Rent(pixels.Length);
        int[]? verticalAcc = width * 4 > 4096 ? System.Buffers.ArrayPool<int>.Shared.Rent(width * 4) : null;
        Span<int> acc = verticalAcc is not null ? verticalAcc.AsSpan(0, width * 4) : stackalloc int[width * 4];
        try {
            fixed (byte* pA = pixels)
            fixed (byte* pB = scratch)
            fixed (int* pAcc = acc) {
                byte* cur = pA;
                byte* tmp = pB;
                ReadOnlySpan<int> rx = [rx0, rx1, rx2];
                ReadOnlySpan<int> ry = [ry0, ry1, ry2];
                for (int pass = 0; pass < 3; pass++) {
                    int hRadius = rx[pass];
                    if (hRadius > 0 && width > 1) {
                        BoxBlurHorizontal(cur, tmp, width, height, Math.Min(hRadius, width - 1));
                        byte* swap = cur; cur = tmp; tmp = swap;
                    }
                    int vRadius = ry[pass];
                    if (vRadius > 0 && height > 1) {
                        BoxBlurVertical(cur, tmp, pAcc, width, height, Math.Min(vRadius, height - 1));
                        byte* swap = cur; cur = tmp; tmp = swap;
                    }
                }
                if (cur != pA) {
                    long byteCount = (long)width * height * 4;
                    Buffer.MemoryCopy(cur, pA, byteCount, byteCount);
                }
            }
        }
        finally {
            if (verticalAcc is not null) System.Buffers.ArrayPool<int>.Shared.Return(verticalAcc);
            System.Buffers.ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    // W3C SVG feGaussianBlur 3-pass symmetric box filter radii matching Gaussian standard deviation sigma.
    private static void ComputeBoxRadii(double sigma, out int r0, out int r1, out int r2) {
        if (!double.IsFinite(sigma) || sigma <= 0.15) { r0 = r1 = r2 = 0; return; }
        double wIdeal = Math.Sqrt(4d * sigma * sigma + 1d);
        int wl = (int)Math.Floor(wIdeal);
        if ((wl & 1) == 0) wl = Math.Max(1, wl - 1);
        int wu = wl + 2;
        double mIdeal = (12d * sigma * sigma - 3d * wl * wl - 12d * wl - 9d) / (-4d * wl - 4d);
        int m = Math.Clamp((int)Math.Round(mIdeal), 0, 3);
        int rl = (wl - 1) >> 1;
        int ru = (wu - 1) >> 1;
        r0 = m > 0 ? rl : ru;
        r1 = m > 1 ? rl : ru;
        r2 = m > 2 ? rl : ru;
    }

    private static unsafe void BoxBlurHorizontal(byte* src, byte* dst, int width, int height, int radius) {
        unchecked {
            int kernel = radius * 2 + 1;
            ulong inv = ((1UL << 32) + (ulong)kernel - 1) / (ulong)kernel;
            ulong half = (ulong)(kernel >> 1);
            int stride = width * 4;
            int lastX = width - 1;

            for (int y = 0; y < height; y++) {
                byte* rowSrc = src + (long)y * stride;
                byte* rowDst = dst + (long)y * stride;

                int b0 = rowSrc[0], g0 = rowSrc[1], r0 = rowSrc[2], a0 = rowSrc[3];
                int sumB = b0 * (radius + 1), sumG = g0 * (radius + 1), sumR = r0 * (radius + 1), sumA = a0 * (radius + 1);
                for (int k = 1; k <= radius; k++) {
                    int idx = Math.Min(k, lastX) << 2;
                    sumB += rowSrc[idx];
                    sumG += rowSrc[idx + 1];
                    sumR += rowSrc[idx + 2];
                    sumA += rowSrc[idx + 3];
                }

                for (int x = 0; x < width; x++) {
                    int outIdx = x << 2;
                    rowDst[outIdx] = (byte)((((ulong)(uint)sumB + half) * inv) >> 32);
                    rowDst[outIdx + 1] = (byte)((((ulong)(uint)sumG + half) * inv) >> 32);
                    rowDst[outIdx + 2] = (byte)((((ulong)(uint)sumR + half) * inv) >> 32);
                    rowDst[outIdx + 3] = (byte)((((ulong)(uint)sumA + half) * inv) >> 32);

                    int leftIdx = Math.Max(x - radius, 0) << 2;
                    int rightIdx = Math.Min(x + radius + 1, lastX) << 2;
                    sumB += rowSrc[rightIdx] - rowSrc[leftIdx];
                    sumG += rowSrc[rightIdx + 1] - rowSrc[leftIdx + 1];
                    sumR += rowSrc[rightIdx + 2] - rowSrc[leftIdx + 2];
                    sumA += rowSrc[rightIdx + 3] - rowSrc[leftIdx + 3];
                }
            }
        }
    }

    private static unsafe void BoxBlurVertical(byte* src, byte* dst, int* acc, int width, int height, int radius) {
        unchecked {
            int kernel = radius * 2 + 1;
            ulong inv = ((1UL << 32) + (ulong)kernel - 1) / (ulong)kernel;
            ulong half = (ulong)(kernel >> 1);
            int rowBytes = width * 4;
            int lastY = height - 1;

            for (int i = 0; i < rowBytes; i++) {
                acc[i] = src[i] * (radius + 1);
            }
            for (int k = 1; k <= radius; k++) {
                byte* kRow = src + (long)Math.Min(k, lastY) * rowBytes;
                for (int i = 0; i < rowBytes; i++) {
                    acc[i] += kRow[i];
                }
            }

            for (int y = 0; y < height; y++) {
                byte* dstRow = dst + (long)y * rowBytes;
                byte* topRow = src + (long)Math.Max(y - radius, 0) * rowBytes;
                byte* botRow = src + (long)Math.Min(y + radius + 1, lastY) * rowBytes;
                for (int i = 0; i < rowBytes; i++) {
                    int s = acc[i];
                    dstRow[i] = (byte)((((ulong)(uint)s + half) * inv) >> 32);
                    acc[i] = s + botRow[i] - topRow[i];
                }
            }
        }
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Recoverable(Exception error) => error is ArgumentException or InvalidOperationException or NotSupportedException or OutOfMemoryException or ExternalException;

    private static Frame Unblurred(ImageSource source) {
        if (source.IsFrozen) return new Frame(source, 0);
        try {
            var snapshot = (ImageSource)source.CloneCurrentValue();
            if (snapshot.CanFreeze) { snapshot.Freeze(); return new Frame(snapshot, 0); }
        }
        catch (Exception error) when (Recoverable(error)) { }
        // Dynamic/unfreezable sources cannot safely become shared cached state.
        // The surrounding material still supplies its own fallback tint.
        return new Frame(EmptySource, 0);
    }

    private static ImageSource EmptyImage() { var image = new DrawingImage(); image.Freeze(); return image; }

    private sealed class HostCache {
        internal readonly List<Entry> Entries = new(MaxCachedEntries);
        internal bool Building;
    }
    private sealed record Entry(Key Key, Frame Frame);
    private readonly record struct Key(ImageSource Source, long Revision, Size Size, double DpiX, double DpiY, Stretch Stretch, double Radius) {
        internal bool Matches(Key other) => ReferenceEquals(Source, other.Source) && Revision == other.Revision &&
            Size == other.Size && DpiX == other.DpiX && DpiY == other.DpiY && Stretch == other.Stretch && Radius == other.Radius;
    }
    private sealed class SourceStamp {
        private long _revision;
        internal long Revision => Interlocked.Read(ref _revision);
        internal SourceStamp(ImageSource source) => source.Changed += OnChanged;
        private void OnChanged(object? sender, EventArgs args) => Interlocked.Increment(ref _revision);
    }
}
