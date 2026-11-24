// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PCLLF.Wpf.Glass;

/// <summary>
/// A Border-compatible application-wallpaper surface. Optional blur affects only its image layer.
/// A missing image uses the inherited opaque fallback; desktop material is disabled by default.
/// </summary>
public sealed class GlassSurface : Border {
    public static readonly DependencyProperty WallpaperSourceProperty = GlassMaterial.WallpaperSourceProperty;
    public static readonly DependencyProperty WallpaperHostProperty = GlassMaterial.WallpaperHostProperty;
    public static readonly DependencyProperty IsMaterialEnabledProperty = GlassMaterial.IsEnabledProperty;
    public static readonly DependencyProperty BlurRadiusProperty = DependencyProperty.Register(
        nameof(BlurRadius), typeof(double), typeof(GlassSurface), new FrameworkPropertyMetadata(0d, OnMaterialPropertyChanged),
        value => value is double number && double.IsFinite(number) && number >= 0 && number <= 100);
    public static readonly DependencyProperty TintOpacityProperty = DependencyProperty.Register(
        nameof(TintOpacity), typeof(double), typeof(GlassSurface), new FrameworkPropertyMetadata(1d, OnMaterialPropertyChanged),
        value => value is double number && double.IsFinite(number) && number >= 0 && number <= 1);
    /// <summary>
    /// When false the surface paints only its tint over whatever lies beneath it: no wallpaper copy (and no blur of one)
    /// is drawn inside, so the content below stays visible through the tint.
    /// </summary>
    public static readonly DependencyProperty PaintsWallpaperProperty = DependencyProperty.Register(
        nameof(PaintsWallpaper), typeof(bool), typeof(GlassSurface), new FrameworkPropertyMetadata(true, OnMaterialPropertyChanged));
    public static readonly DependencyProperty ExtendWallpaperProperty = DependencyProperty.Register(
        nameof(ExtendWallpaper), typeof(bool), typeof(GlassSurface), new FrameworkPropertyMetadata(false, OnMaterialPropertyChanged));
    public static readonly DependencyProperty HighlightOpacityProperty = DependencyProperty.Register(
        nameof(HighlightOpacity), typeof(double), typeof(GlassSurface), new FrameworkPropertyMetadata(0d, OnMaterialPropertyChanged),
        value => value is double number && double.IsFinite(number) && number >= 0 && number <= 1);

    private readonly MaterialLayer _material;
    private Rect _lastWallpaperBounds = Rect.Empty;
    private FrameworkElement? _watchedHost;
    private Effect? _watchedEffect;
    private ImageSource? _watchedImage;
    private bool _layoutSubscribed;
    private readonly Dictionary<UIElement, TransformWatch> _transformWatches = new();
    private readonly List<DependencyObject> _surfacePath = new(), _hostPath = new();
    private static readonly DependencyPropertyDescriptor? HostOpacity = DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty, typeof(UIElement));
    private static readonly DependencyPropertyDescriptor? HostEffect = DependencyPropertyDescriptor.FromProperty(UIElement.EffectProperty, typeof(UIElement));

    static GlassSurface() {
        // Native composition already supplies the frosted sample. A thin neutral tint and
        // small highlight preserve its colors instead of stacking a gray veil on every card.
        var tint = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        tint.Freeze();
        var edge = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
        edge.Freeze();
        BackgroundProperty.OverrideMetadata(typeof(GlassSurface), new FrameworkPropertyMetadata(tint, OnMaterialPropertyChanged));
        BorderBrushProperty.OverrideMetadata(typeof(GlassSurface), new FrameworkPropertyMetadata(edge, OnMaterialPropertyChanged));
        BorderThicknessProperty.OverrideMetadata(typeof(GlassSurface), new FrameworkPropertyMetadata(new Thickness(1), OnMaterialPropertyChanged));
        CornerRadiusProperty.OverrideMetadata(typeof(GlassSurface), new FrameworkPropertyMetadata(new CornerRadius(16), OnMaterialPropertyChanged));
    }

    public GlassSurface() {
        _material = new MaterialLayer(this);
        AddVisualChild(_material);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public ImageSource? WallpaperSource { get => GlassMaterial.GetWallpaperSource(this); set => GlassMaterial.SetWallpaperSource(this, value); }
    public FrameworkElement? WallpaperHost { get => GlassMaterial.GetWallpaperHost(this); set => GlassMaterial.SetWallpaperHost(this, value); }
    public bool IsMaterialEnabled { get => GlassMaterial.GetIsEnabled(this); set => GlassMaterial.SetIsEnabled(this, value); }
    public double BlurRadius { get => (double)GetValue(BlurRadiusProperty); set => SetValue(BlurRadiusProperty, value); }
    public double TintOpacity { get => (double)GetValue(TintOpacityProperty); set => SetValue(TintOpacityProperty, value); }
    public bool PaintsWallpaper { get => (bool)GetValue(PaintsWallpaperProperty); set => SetValue(PaintsWallpaperProperty, value); }
    public bool ExtendWallpaper { get => (bool)GetValue(ExtendWallpaperProperty); set => SetValue(ExtendWallpaperProperty, value); }
    public double HighlightOpacity { get => (double)GetValue(HighlightOpacityProperty); set => SetValue(HighlightOpacityProperty, value); }
    public bool IsTransparencyActive => CanUseTransparency;

    internal bool CanUseTransparency => IsMaterialEnabled && !SystemParameters.HighContrast && GlassSystemPreferences.TransparencyEnabled &&
        (WallpaperSource is not null || GlassMaterial.GetDesktopBackdropEnabled(this));
    private double ImageOpacity => Math.Clamp(WallpaperHost?.Opacity ?? 1, 0, 1);
    private Stretch ImageStretch => WallpaperHost is Image image ? image.Stretch : GlassMaterial.GetWallpaperStretch(WallpaperHost ?? this);
    private double EffectiveBlurRadius {
        get {
            double global = !GlassMaterial.GetIgnoreWallpaperHostEffect(this) &&
                WallpaperHost?.Effect is BlurEffect { KernelType: KernelType.Gaussian } blur ? blur.Radius : 0;
            return Math.Sqrt(BlurRadius * BlurRadius + global * global);
        }
    }

    // The material is a separate visual behind Border.Child, so no effect ever reaches content.
    protected override int VisualChildrenCount => base.VisualChildrenCount + (_material is null ? 0 : 1);
    protected override Visual GetVisualChild(int index) {
        if (_material is not null) {
            if (index == 0) return _material;
            index--;
        }
        return base.GetVisualChild(index);
    }

    protected override Size MeasureOverride(Size constraint) {
        var result = base.MeasureOverride(constraint);
        _material.Measure(result);
        return result;
    }

    protected override Size ArrangeOverride(Size arrangeSize) {
        var result = base.ArrangeOverride(arrangeSize);
        _material.Arrange(new Rect(arrangeSize));
        return result;
    }

    // MaterialLayer owns background/border rendering, including rounded clipping of its blur.
    protected override void OnRender(DrawingContext drawingContext) { }

    internal void InvalidateMaterial() {
        if (_material is null) return;
        WatchWallpaperHost();
        UpdateLayoutSubscription();
        _lastWallpaperBounds = Rect.Empty;
        _material.Update();
    }

    private static void OnMaterialPropertyChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) =>
        ((GlassSurface)element).InvalidateMaterial();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args) {
        if (IsLoaded && IsVisible) InvalidateMaterial();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        InvalidateMaterial();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) {
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SetLayoutSubscription(false);
        UnwatchWallpaperHost();
        _material.ReleaseWallpaper();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent) {
        base.OnVisualParentChanged(oldParent);
        if (IsLoaded && PaintsWallpaper && WallpaperSource is not null && CanUseTransparency)
            WatchRelativeTransforms(WallpaperCoordinateHost);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMaterial();
    }

    private void UpdateLayoutSubscription() =>
        SetLayoutSubscription(IsLoaded && IsVisible && PaintsWallpaper && WallpaperSource is not null && CanUseTransparency);

    private void SetLayoutSubscription(bool subscribe) {
        if (_layoutSubscribed == subscribe) return;
        _layoutSubscribed = subscribe;
        if (subscribe) LayoutUpdated += OnLayoutUpdated;
        else LayoutUpdated -= OnLayoutUpdated;
    }

    private void WatchWallpaperHost() {
        var host = IsLoaded && PaintsWallpaper && WallpaperSource is not null ? WallpaperCoordinateHost : null;
        bool watchEffect = !GlassMaterial.GetIgnoreWallpaperHostEffect(this);
        if (!ReferenceEquals(host, _watchedHost)) {
            UnwatchWallpaperHost(); _watchedHost = host;
            if (host is not null) {
                HostOpacity?.AddValueChanged(host, HostPresentationChanged);
                HostEffect?.AddValueChanged(host, HostEffectPropertyChanged);
                host.SizeChanged += HostSizeChanged;
            }
        }
        var effect = watchEffect ? host?.Effect : null;
        if (!ReferenceEquals(effect, _watchedEffect)) {
            if (_watchedEffect is { IsFrozen: false }) _watchedEffect.Changed -= HostPresentationChanged;
            _watchedEffect = effect;
            if (effect is { IsFrozen: false }) effect.Changed += HostPresentationChanged;
        }
        ImageSource? image = host is null ? null : WallpaperSource;
        if (!ReferenceEquals(image, _watchedImage)) {
            if (_watchedImage is { IsFrozen: false }) _watchedImage.Changed -= WallpaperSourceChanged;
            _watchedImage = image;
            if (image is { IsFrozen: false }) image.Changed += WallpaperSourceChanged;
        }
        WatchRelativeTransforms(host);
    }
    private void UnwatchWallpaperHost() {
        if (_watchedHost is not null) {
            HostOpacity?.RemoveValueChanged(_watchedHost, HostPresentationChanged);
            HostEffect?.RemoveValueChanged(_watchedHost, HostEffectPropertyChanged);
            _watchedHost.SizeChanged -= HostSizeChanged;
        }
        if (_watchedEffect is { IsFrozen: false }) _watchedEffect.Changed -= HostPresentationChanged;
        if (_watchedImage is { IsFrozen: false }) _watchedImage.Changed -= WallpaperSourceChanged;
        _watchedImage = null;
        _watchedHost = null; _watchedEffect = null;
        foreach (TransformWatch watch in _transformWatches.Values) watch.Dispose();
        _transformWatches.Clear(); _surfacePath.Clear(); _hostPath.Clear();
    }
    private void HostEffectPropertyChanged(object? sender, EventArgs args) {
        if (!GlassMaterial.GetIgnoreWallpaperHostEffect(this)) InvalidateMaterial();
    }
    private void HostSizeChanged(object sender, SizeChangedEventArgs args) {
        _lastWallpaperBounds = Rect.Empty;
        _material.PrepareWallpaper();
        _material.InvalidateWallpaper();
    }
    private void HostPresentationChanged(object? sender, EventArgs args) => InvalidateMaterial();
    private void WallpaperSourceChanged(object? sender, EventArgs args) { _material.ReleaseWallpaper(); InvalidateMaterial(); }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs args) => QueuePreferenceRefresh();
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args) => QueuePreferenceRefresh();
    private void QueuePreferenceRefresh() {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => {
            if (!IsLoaded) return;
            GlassSystemPreferences.Refresh();
            InvalidateMaterial();
        }));
    }

    private static DependencyObject? MaterialParent(DependencyObject element) => element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
    private static bool SamePath(DependencyObject? first, List<DependencyObject> path) {
        int index = 0;
        for (DependencyObject? node = first; node is not null; node = MaterialParent(node))
            if (index >= path.Count || !ReferenceEquals(path[index++], node)) return false;
        return index == path.Count;
    }
    private void WatchRelativeTransforms(FrameworkElement? host) {
        if (host is not null && SamePath(this, _surfacePath) && SamePath(host, _hostPath)) return;
        foreach (TransformWatch watch in _transformWatches.Values) watch.Dispose();
        _transformWatches.Clear(); _surfacePath.Clear(); _hostPath.Clear();
        if (host is null) return;
        for (DependencyObject? node = this; node is not null; node = MaterialParent(node)) _surfacePath.Add(node);
        for (DependencyObject? node = host; node is not null; node = MaterialParent(node)) _hostPath.Add(node);
        var relative = new HashSet<DependencyObject>(_surfacePath); relative.SymmetricExceptWith(_hostPath);
        foreach (DependencyObject node in relative)
            if (node is UIElement element) _transformWatches.Add(element, new TransformWatch(element, RelativeTransformChanged));
    }
    private sealed class TransformWatch : IDisposable {
        private static readonly DependencyPropertyDescriptor? TransformProperty = DependencyPropertyDescriptor.FromProperty(UIElement.RenderTransformProperty, typeof(UIElement));
        private static readonly DependencyPropertyDescriptor? OriginProperty = DependencyPropertyDescriptor.FromProperty(UIElement.RenderTransformOriginProperty, typeof(UIElement));
        private readonly UIElement _element;
        private readonly Action _changed;
        private Transform? _transform;
        internal TransformWatch(UIElement element, Action changed) {
            _element = element; _changed = changed;
            TransformProperty?.AddValueChanged(element, Changed); OriginProperty?.AddValueChanged(element, Changed); AttachTransform();
        }
        private void AttachTransform() {
            if (ReferenceEquals(_transform, _element.RenderTransform)) return;
            if (_transform is { IsFrozen: false }) _transform.Changed -= Changed;
            _transform = _element.RenderTransform;
            if (_transform is { IsFrozen: false }) _transform.Changed += Changed;
        }
        private void Changed(object? sender, EventArgs args) { AttachTransform(); _changed(); }
        public void Dispose() {
            TransformProperty?.RemoveValueChanged(_element, Changed); OriginProperty?.RemoveValueChanged(_element, Changed);
            if (_transform is { IsFrozen: false }) _transform.Changed -= Changed;
            _transform = null;
        }
    }
    private void RelativeTransformChanged() {
        if (!IsLoaded || !PaintsWallpaper || WallpaperSource is null || !CanUseTransparency) return;
        // WPF may not have applied RenderTransformOrigin to VisualTransform yet.
        // Invalidate once now; OnRender samples the final matrix after the event batch.
        _lastWallpaperBounds = Rect.Empty;
        _material.InvalidateWallpaper();
    }
    private void RefreshWallpaperPosition() {
        if (!IsLoaded || !PaintsWallpaper || WallpaperSource is null || !CanUseTransparency) return;
        var bounds = GetWallpaperBounds(this);
        if (bounds == _lastWallpaperBounds) return;
        _lastWallpaperBounds = bounds;
        _material.InvalidateWallpaper();
    }
    private void OnLayoutUpdated(object? sender, EventArgs args) {
        if (WallpaperSource is null || !PaintsWallpaper || !CanUseTransparency) return;
        RefreshWallpaperPosition();
    }

    private FrameworkElement WallpaperCoordinateHost => WallpaperHost ?? Window.GetWindow(this)?.Content as FrameworkElement ?? this;
    private Rect GetWallpaperBounds(Visual target, double padding = 0) {
        var host = WallpaperCoordinateHost;
        try {
            if (host.RenderSize.Width > 0 && host.RenderSize.Height > 0)
                return host.TransformToVisual(target).TransformBounds(new Rect(-padding, -padding, host.RenderSize.Width + padding * 2, host.RenderSize.Height + padding * 2));
        } catch (InvalidOperationException) {
            // Disconnected designer trees use a local preview until the shared host is loaded.
        }
        return new Rect(RenderSize);
    }

    private static bool IsZeroRadius(CornerRadius corners) =>
        corners.TopLeft <= 0 && corners.TopRight <= 0 && corners.BottomRight <= 0 && corners.BottomLeft <= 0;

    private sealed class MaterialLayer : FrameworkElement {
        private readonly GlassSurface _owner;
        private readonly WallpaperPlane _wallpaper;
        private readonly FinishPlane _finish;
        private DispatcherOperation? _prepare;
        private (FrameworkElement Host, ImageSource Source, Size Size, double Radius, Stretch Stretch, DpiScale Dpi)? _prepared;
        private Size _outerSize;
        private Size _arrangedSize;
        private CornerRadius _outerCorners;
        private Geometry? _outerGeometry;
        private bool _hasWallpaperClip;

        internal MaterialLayer(GlassSurface owner) {
            _owner = owner;
            IsHitTestVisible = false;
            Focusable = false;
            _wallpaper = new WallpaperPlane(owner) { IsHitTestVisible = false };
            _finish = new FinishPlane(owner, this) { IsHitTestVisible = false };
            AddVisualChild(_wallpaper);
            AddVisualChild(_finish);
        }

        protected override int VisualChildrenCount => 2;
        protected override Visual GetVisualChild(int index) => index switch {
            0 => _wallpaper,
            1 => _finish,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        internal Geometry GetOuterGeometry(Size size) {
            if (size.Width <= 0 || size.Height <= 0) return Geometry.Empty;
            CornerRadius corners = _owner.CornerRadius;
            if (_outerGeometry is not null && _outerSize == size && _outerCorners == corners) return _outerGeometry;
            _outerSize = size;
            _outerCorners = corners;
            _outerGeometry = RoundedGeometry(new Rect(size), corners);
            return _outerGeometry;
        }

        internal void Update() {
            PrepareWallpaper();
            bool showWallpaper = _owner.PaintsWallpaper && _owner.WallpaperSource is not null && _owner.CanUseTransparency;
            _wallpaper.Visibility = showWallpaper ? Visibility.Visible : Visibility.Hidden;
            UpdateMask(RenderSize, showWallpaper);
            _wallpaper.InvalidateVisual();
            _finish.InvalidateVisual();
        }

        internal void InvalidateWallpaper() => _wallpaper.InvalidateVisual();

        internal void PrepareWallpaper() {
            if (!_owner.IsLoaded || !_owner.PaintsWallpaper || !_owner.CanUseTransparency || _owner.WallpaperSource is not { } source) { ReleaseWallpaper(); return; }
            if (!_owner.IsVisible) return;
            // Keep sampling the last blurred frame while the wallpaper viewport is moving every frame.
            if (_prepared.HasValue && GlassMaterial.GetDeferWallpaperBlur(_owner)) {
                if (_prepare?.Status == DispatcherOperationStatus.Pending) _prepare.Abort();
                _prepare = null;
                return;
            }
            FrameworkElement host = _owner.WallpaperCoordinateHost;
            if (host.RenderSize.Width <= 0 || host.RenderSize.Height <= 0) return;
            double radius = _owner.EffectiveBlurRadius;
            Stretch stretch = _owner.ImageStretch;
            var key = (Host: host, Source: source, Size: host.RenderSize, Radius: radius, Stretch: stretch, Dpi: VisualTreeHelper.GetDpi(host));
            if (_prepared is { } previous && previous.Equals(key)) return;
            if (WallpaperBlurCache.TryGet(host, source, stretch, radius, out WallpaperBlurCache.Frame cached)) {
                if (_prepare?.Status == DispatcherOperationStatus.Pending) _prepare.Abort();
                _prepare = null;
                _prepared = key;
                if (!Equals(_wallpaper.Frame, cached)) {
                    _wallpaper.Frame = cached;
                    _wallpaper.InvalidateVisual();
                }
                return;
            }
            if (_prepare?.Status == DispatcherOperationStatus.Pending) return;
            // Never nest a RenderTargetBitmap.Render in a material's OnRender or a layout callback.
            // Use DataBind (> Render) on initial preparation to avoid a 1-frame sharp->blurred flash,
            // and coalesce resize rebuilds at Background while retaining the previous blurred frame.
            bool resizing = _prepared is { } prior && ReferenceEquals(prior.Host, host) &&
                prior.Radius == key.Radius && prior.Stretch == key.Stretch && prior.Size != key.Size;
            _prepare = Dispatcher.BeginInvoke(resizing ? DispatcherPriority.Background : DispatcherPriority.DataBind, new Action(() => {
                _prepare = null;
                if (!_owner.IsLoaded || !_owner.IsVisible || !_owner.PaintsWallpaper || !_owner.CanUseTransparency || _owner.WallpaperSource is not { } current) return;
                FrameworkElement currentHost = _owner.WallpaperCoordinateHost;
                if (currentHost.RenderSize.Width <= 0 || currentHost.RenderSize.Height <= 0) return;
                double currentRadius = _owner.EffectiveBlurRadius;
                Stretch currentStretch = _owner.ImageStretch;
                _wallpaper.Frame = WallpaperBlurCache.GetOrCreate(currentHost, current, currentStretch, currentRadius);
                _prepared = (currentHost, current, currentHost.RenderSize, currentRadius, currentStretch, VisualTreeHelper.GetDpi(currentHost));
                _wallpaper.InvalidateVisual();
            }));
        }
        internal void ReleaseWallpaper() {
            if (_prepare?.Status == DispatcherOperationStatus.Pending) _prepare.Abort();
            _prepare = null; _prepared = null; _wallpaper.Release();
        }

        protected override Size MeasureOverride(Size availableSize) {
            _wallpaper.Measure(availableSize);
            _finish.Measure(availableSize);
            return new Size();
        }

        protected override Size ArrangeOverride(Size finalSize) {
            // The host-level texture already includes the blur halo. Moving a card only changes sampling.
            _wallpaper.Arrange(new Rect(finalSize));
            _finish.Arrange(new Rect(finalSize));
            UpdateMask(finalSize, _wallpaper.Visibility == Visibility.Visible);
            if (_arrangedSize != finalSize) {
                _arrangedSize = finalSize;
                _wallpaper.InvalidateVisual();
                _finish.InvalidateVisual();
            }
            return finalSize;
        }

        private void UpdateMask(Size size, bool showWallpaper) {
            if (!showWallpaper || size.Width <= 0 || size.Height <= 0 || IsZeroRadius(_owner.CornerRadius)) {
                if (_hasWallpaperClip) { _wallpaper.Clip = null; _hasWallpaperClip = false; }
                return;
            }
            Geometry clip = GetOuterGeometry(size);
            if (!ReferenceEquals(_wallpaper.Clip, clip)) _wallpaper.Clip = clip;
            _hasWallpaperClip = true;
        }
    }

    private sealed class WallpaperPlane : FrameworkElement {
        private readonly GlassSurface _owner;
        internal WallpaperBlurCache.Frame? Frame;
        private ImageBrush? _brush;
        private SolidColorBrush? _fallback;
        internal WallpaperPlane(GlassSurface owner) {
            _owner = owner;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        }
        internal void Release() { Frame = null; if (_brush is not null) _brush.ImageSource = null; }
        protected override void OnRender(DrawingContext drawingContext) {
            if (_owner.WallpaperSource is not { } source || !_owner.CanUseTransparency) return;
            var prepared = Frame;
            var wallpaperBounds = _owner.GetWallpaperBounds(this, prepared?.Padding ?? 0);
            if (wallpaperBounds.IsEmpty || wallpaperBounds.Width <= 0 || wallpaperBounds.Height <= 0) return;
            _brush ??= new ImageBrush {
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                ViewportUnits = BrushMappingMode.Absolute
            };
            _brush.ImageSource = prepared?.Source ?? source;
            // Popups can straddle the launcher edge. Reflect the sample through that edge so the
            // fade into the rest of the wallpaper does not become a straight opaque seam.
            _brush.TileMode = _owner.ExtendWallpaper ? TileMode.FlipXY : TileMode.None;
            _brush.Stretch = prepared is { Padding: > 0 } ? Stretch.Fill : _owner.ImageStretch;
            _brush.Viewport = wallpaperBounds;
            Color color = GlassMaterial.GetFallbackColor(_owner);
            Color opaque = Color.FromRgb(color.R, color.G, color.B);
            if (_fallback?.Color != opaque) { _fallback = new SolidColorBrush(opaque); _fallback.Freeze(); }
            var rect = new Rect(RenderSize);
            drawingContext.DrawRectangle(_fallback, null, rect);
            double opacity = _owner.ImageOpacity;
            if (opacity < 0.999) drawingContext.PushOpacity(opacity);
            drawingContext.DrawRectangle(_brush, null, rect);
            if (opacity < 0.999) drawingContext.Pop();
        }
    }

    private sealed class FinishPlane(GlassSurface owner, MaterialLayer layer) : FrameworkElement {
        private static readonly LinearGradientBrush HighlightBrush = CreateHighlightBrush();
        private SolidColorBrush? _opaqueFallback;
        private Size _borderSize;
        private CornerRadius _borderCorners;
        private Thickness _borderThickness;
        private Geometry? _borderGeometry;

        private static LinearGradientBrush CreateHighlightBrush() {
            var brush = new LinearGradientBrush(Colors.White, Colors.Transparent, new Point(0, 0), new Point(0, 1));
            brush.Freeze();
            return brush;
        }

        private Geometry GetBorderGeometry(Size size, CornerRadius corners, Thickness thickness, Geometry outer) {
            if (_borderGeometry is not null && _borderSize == size && _borderCorners == corners && _borderThickness == thickness)
                return _borderGeometry;
            _borderSize = size;
            _borderCorners = corners;
            _borderThickness = thickness;
            var innerBounds = new Rect(thickness.Left, thickness.Top,
                Math.Max(0, size.Width - thickness.Left - thickness.Right),
                Math.Max(0, size.Height - thickness.Top - thickness.Bottom));
            var innerRadius = new CornerRadius(
                Math.Max(0, corners.TopLeft - Math.Max(thickness.Left, thickness.Top)),
                Math.Max(0, corners.TopRight - Math.Max(thickness.Right, thickness.Top)),
                Math.Max(0, corners.BottomRight - Math.Max(thickness.Right, thickness.Bottom)),
                Math.Max(0, corners.BottomLeft - Math.Max(thickness.Left, thickness.Bottom)));
            var border = new CombinedGeometry(GeometryCombineMode.Exclude, outer, RoundedGeometry(innerBounds, innerRadius));
            border.Freeze();
            _borderGeometry = border;
            return border;
        }

        protected override void OnRender(DrawingContext drawingContext) {
            var size = RenderSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            var outer = layer.GetOuterGeometry(size);
            var transparent = owner.CanUseTransparency;
            var background = owner.Background;
            if (!transparent) {
                background = SystemParameters.HighContrast ? SystemColors.WindowBrush : OpaqueBrush(GlassMaterial.GetFallbackColor(owner));
            }
            double opacity = transparent ? owner.TintOpacity * GlassMaterial.GetOpacityMultiplier(owner) : 1;
            if (opacity < 0.999) drawingContext.PushOpacity(opacity);
            drawingContext.DrawGeometry(background, null, outer);
            if (opacity < 0.999) drawingContext.Pop();

            if (transparent && owner.HighlightOpacity > 0) {
                drawingContext.PushOpacity(owner.HighlightOpacity);
                drawingContext.DrawGeometry(HighlightBrush, null, outer);
                drawingContext.Pop();
            }

            var thickness = owner.BorderThickness;
            if (thickness.Left + thickness.Top + thickness.Right + thickness.Bottom <= 0) return;
            var border = GetBorderGeometry(size, owner.CornerRadius, thickness, outer);
            drawingContext.DrawGeometry(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : owner.BorderBrush, null, border);
        }

        private Brush OpaqueBrush(Color color) {
            Color opaque = Color.FromRgb(color.R, color.G, color.B);
            if (_opaqueFallback?.Color == opaque) return _opaqueFallback;
            _opaqueFallback = new SolidColorBrush(opaque);
            _opaqueFallback.Freeze();
            return _opaqueFallback;
        }
    }

    private static Geometry RoundedGeometry(Rect bounds, CornerRadius corners) {
        if (bounds.Width <= 0 || bounds.Height <= 0) return Geometry.Empty;
        var tl = Math.Min(corners.TopLeft, Math.Min(bounds.Width, bounds.Height) / 2);
        var tr = Math.Min(corners.TopRight, Math.Min(bounds.Width, bounds.Height) / 2);
        var br = Math.Min(corners.BottomRight, Math.Min(bounds.Width, bounds.Height) / 2);
        var bl = Math.Min(corners.BottomLeft, Math.Min(bounds.Width, bounds.Height) / 2);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            context.BeginFigure(new Point(bounds.Left + tl, bounds.Top), true, true);
            context.LineTo(new Point(bounds.Right - tr, bounds.Top), true, false);
            context.ArcTo(new Point(bounds.Right, bounds.Top + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(bounds.Right, bounds.Bottom - br), true, false);
            context.ArcTo(new Point(bounds.Right - br, bounds.Bottom), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(bounds.Left + bl, bounds.Bottom), true, false);
            context.ArcTo(new Point(bounds.Left, bounds.Bottom - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(bounds.Left, bounds.Top + tl), true, false);
            context.ArcTo(new Point(bounds.Left + tl, bounds.Top), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
