// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PCLLF.Wpf.Glass;

/// <summary>Inherited material context shared by a launcher shell and its nested glass surfaces.</summary>
public static class GlassMaterial {
    public static readonly DependencyProperty WallpaperSourceProperty = DependencyProperty.RegisterAttached(
        "WallpaperSource", typeof(ImageSource), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));

    public static readonly DependencyProperty WallpaperHostProperty = DependencyProperty.RegisterAttached(
        "WallpaperHost", typeof(FrameworkElement), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));
    public static readonly DependencyProperty WallpaperStretchProperty = DependencyProperty.RegisterAttached(
        "WallpaperStretch", typeof(Stretch), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(Stretch.UniformToFill, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));

    public static readonly DependencyProperty OpacityMultiplierProperty = DependencyProperty.RegisterAttached(
        "OpacityMultiplier", typeof(double), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged),
        value => value is double number && double.IsFinite(number) && number >= 0 && number <= 1);
    public static readonly DependencyProperty FallbackColorProperty = DependencyProperty.RegisterAttached(
        "FallbackColor", typeof(Color), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(Color.FromRgb(242, 245, 248), FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));
    public static readonly DependencyProperty FallbackBrushProperty = DependencyProperty.RegisterAttached(
        "FallbackBrush", typeof(SolidColorBrush), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));
    // Ordinary layered-window alpha is available even without a native blur backend.
    public static readonly DependencyProperty AlphaFallbackProperty = DependencyProperty.RegisterAttached(
        "AlphaFallback", typeof(bool), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));
    public static bool GetAlphaFallback(DependencyObject element) => (bool)element.GetValue(AlphaFallbackProperty);
    public static void SetAlphaFallback(DependencyObject element, bool value) => element.SetValue(AlphaFallbackProperty, value);

    public static readonly DependencyProperty DesktopBackdropEnabledProperty = DependencyProperty.RegisterAttached(
        "DesktopBackdropEnabled", typeof(bool), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));
    public static readonly DependencyProperty IgnoreWallpaperHostEffectProperty = DependencyProperty.RegisterAttached(
        "IgnoreWallpaperHostEffect", typeof(bool), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));
    public static readonly DependencyProperty DeferWallpaperBlurProperty = DependencyProperty.RegisterAttached(
        "DeferWallpaperBlur", typeof(bool), typeof(GlassMaterial),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));

    public static ImageSource? GetWallpaperSource(DependencyObject element) => (ImageSource?)element.GetValue(WallpaperSourceProperty);
    public static void SetWallpaperSource(DependencyObject element, ImageSource? value) => element.SetValue(WallpaperSourceProperty, value);
    public static FrameworkElement? GetWallpaperHost(DependencyObject element) => (FrameworkElement?)element.GetValue(WallpaperHostProperty);
    public static void SetWallpaperHost(DependencyObject element, FrameworkElement? value) => element.SetValue(WallpaperHostProperty, value);
    public static Stretch GetWallpaperStretch(DependencyObject element) => (Stretch)element.GetValue(WallpaperStretchProperty);
    public static void SetWallpaperStretch(DependencyObject element, Stretch value) => element.SetValue(WallpaperStretchProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool AllowsTransparency(DependencyObject element) =>
        GetIsEnabled(element) && !SystemParameters.HighContrast && GlassSystemPreferences.TransparencyEnabled;
    public static double GetOpacityMultiplier(DependencyObject element) => (double)element.GetValue(OpacityMultiplierProperty);
    public static void SetOpacityMultiplier(DependencyObject element, double value) => element.SetValue(OpacityMultiplierProperty, value);
    public static Color GetFallbackColor(DependencyObject element) => (Color)element.GetValue(FallbackColorProperty);
    public static void SetFallbackColor(DependencyObject element, Color value) {
        element.SetValue(FallbackColorProperty, value);
        // A local color overrides an inherited live workspace brush (for example a floating dock).
        element.SetValue(FallbackBrushProperty, null);
    }
    public static SolidColorBrush? GetFallbackBrush(DependencyObject element) => (SolidColorBrush?)element.GetValue(FallbackBrushProperty);
    public static void SetFallbackBrush(DependencyObject element, SolidColorBrush? brush) {
        if (!ReferenceEquals(element.GetValue(FallbackBrushProperty), brush))
            element.SetValue(FallbackBrushProperty, brush);
    }
    public static bool GetDesktopBackdropEnabled(DependencyObject element) => (bool)element.GetValue(DesktopBackdropEnabledProperty);
    public static void SetDesktopBackdropEnabled(DependencyObject element, bool value) => element.SetValue(DesktopBackdropEnabledProperty, value);
    public static bool GetIgnoreWallpaperHostEffect(DependencyObject element) => (bool)element.GetValue(IgnoreWallpaperHostEffectProperty);
    public static void SetIgnoreWallpaperHostEffect(DependencyObject element, bool value) => element.SetValue(IgnoreWallpaperHostEffectProperty, value);
    public static bool GetDeferWallpaperBlur(DependencyObject element) => (bool)element.GetValue(DeferWallpaperBlurProperty);
    public static void SetDeferWallpaperBlur(DependencyObject element, bool value) => element.SetValue(DeferWallpaperBlurProperty, value);

    private static void OnContextChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) {
        // WriteableBitmap updates reach existing image brushes on the render thread. Only a different frame buffer
        // needs to rebuild the surface's sampling state.
        if (args.Property == WallpaperSourceProperty && args.NewValue is ImageSource source &&
            ReferenceEquals(args.OldValue, source) && WallpaperBlurCache.IsVideoFrame(source)) return;
        if (element is not GlassSurface surface) return;
        if (args.Property == FallbackBrushProperty || args.Property == FallbackColorProperty) {
            // Paint keeps a reference to the animated brush. Its frames never invalidate the blur sample.
            if (!Equals(args.OldValue, args.NewValue)) surface.InvalidateMaterialPaint(includeBackdrop: true);
            return;
        }
        surface.InvalidateMaterial();
    }
}

internal static class GlassSystemPreferences {
    internal static bool TransparencyEnabled { get; private set; } = ReadTransparencyEnabled();

    internal static void Refresh() => TransparencyEnabled = ReadTransparencyEnabled();

    private static bool ReadTransparencyEnabled() {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int enabled || enabled != 0;
        } catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) {
            return true;
        }
    }
}
