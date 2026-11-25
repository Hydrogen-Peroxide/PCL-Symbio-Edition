using System.Windows;
using System.Windows.Media;
using PCL.Core.UI.Theme;

namespace PCL;

/// <summary>
///     Writes the Symbio (Fluent 2) palette into application resources for the current colour mode and accent.
///     Neutral values follow the WinUI 3 theme resources; accent brushes come from the PCL theme ramp.
///     The shell, the account panel and the few pages written for the former shell all read these keys.
/// </summary>
internal static class SymbioPalette
{
    private static ResourceDictionary Resources => System.Windows.Application.Current.Resources;

    public static bool IsDark { get; private set; }

    private static (bool Dark, bool HighContrast, Color Accent)? _applied;

    public static void Apply(bool isDark)
    {
        // Writing a resource re-notifies every consumer, so only rewrite when the inputs changed.
        var key = (isDark, SystemParameters.HighContrast, Ramp("ColorBrush2", "ColorBrush3"));
        // ThemeService rewrites the PCL ramp on every theme refresh, so the neutral text override is re-asserted.
        ApplyNeutralText(isDark);
        if (_applied == key) return;
        _applied = key;

        IsDark = isDark;
        if (SystemParameters.HighContrast)
        {
            ApplyHighContrast();
            return;
        }

        if (isDark) ApplyDarkNeutrals();
        else ApplyLightNeutrals();
        ApplyAccent(isDark);
        ApplyLegacyAliases();
    }

    private static void ApplyLightNeutrals()
    {
        Set("SyText", 0xE4000000);
        Set("SyTextSecondary", 0x9E000000);
        Set("SyTextTertiary", 0x72000000);
        Set("SyTextDisabled", 0x5C000000);
        Set("SyControl", 0xB3FFFFFF);
        Set("SyControlHover", 0x80F9F9F9);
        Set("SyControlPressed", 0x4DF9F9F9);
        Set("SySubtleHover", 0x09000000);
        Set("SySubtlePressed", 0x06000000);
        Set("SyControlStroke", 0x0F000000);
        SetGradient("SyControlElevation", (0x0F000000, 0), (0x0F000000, .9), (0x29000000, 1));
        Set("SyCardFill", 0xB3FFFFFF);
        Set("SyCardStroke", 0x0F000000);
        Set("SyLayerFill", 0x80FFFFFF);
        Set("SyAcrylicTint", 0xD9F9F9F9);
        Set("SyFrostFill", 0xA6FAFAFB);
        Set("SyFrostHover", 0xC7FDFDFD);
        Set("SyControlStrongStroke", 0x72000000);
        SetTextElevation(0x72000000, 0x0F000000);
        Set("SyControlAlt", 0x06000000);
        Set("SyControlInputActive", 0xFFFFFFFF);
        Set("SyCritical", 0xFFC42B1C);
        Set("SyFlyoutFill", 0xFFF9F9F9);
        Set("SyFrostEdge", 0x2635475B);
        Set("SyVeil", 0x80F3F3F3);
        Set("SyTitleBarFill", 0xB8F3F3F3);
        Set("SySurfaceStroke", 0x24000000);
        Set("SyDivider", 0x0F000000);
        Set("SySelected", 0x09000000);
        Set("SyFocusOuter", 0xE4000000);
        Set("SyFocusInner", 0xB3FFFFFF);
        Set("SyAccentDisabled", 0x37000000);
        Set("SyTextOnAccentDisabled", 0xFFFFFFFF);
        Set("SyWindowFallback", 0xFFF3F3F3);
        Resources["SyShadowColor"] = Color.FromRgb(0, 0, 0);
    }

    private static void ApplyDarkNeutrals()
    {
        Set("SyText", 0xFFFFFFFF);
        Set("SyTextSecondary", 0xC5FFFFFF);
        Set("SyTextTertiary", 0x87FFFFFF);
        Set("SyTextDisabled", 0x5DFFFFFF);
        Set("SyControl", 0x0FFFFFFF);
        Set("SyControlHover", 0x15FFFFFF);
        Set("SyControlPressed", 0x08FFFFFF);
        Set("SySubtleHover", 0x0FFFFFFF);
        Set("SySubtlePressed", 0x0AFFFFFF);
        Set("SyControlStroke", 0x12FFFFFF);
        SetGradient("SyControlElevation", (0x18FFFFFF, 0), (0x12FFFFFF, .1), (0x12FFFFFF, 1));
        Set("SyCardFill", 0x0DFFFFFF);
        Set("SyCardStroke", 0x19000000);
        Set("SyLayerFill", 0x4C3A3A3A);
        Set("SyAcrylicTint", 0xD92C2C2C);
        Set("SyFrostFill", 0x8C2B2B2B);
        Set("SyFrostHover", 0xA6363636);
        Set("SyControlStrongStroke", 0x8BFFFFFF);
        SetTextElevation(0x5CFFFFFF, 0x14FFFFFF);
        Set("SyControlAlt", 0x19000000);
        Set("SyControlInputActive", 0xB31E1E1E);
        Set("SyCritical", 0xFFFF99A4);
        Set("SyFlyoutFill", 0xFF2C2C2C);
        Set("SyFrostEdge", 0x1FFFFFFF);
        Set("SyVeil", 0x731C1C1C);
        Set("SyTitleBarFill", 0xA6202020);
        Set("SySurfaceStroke", 0x33000000);
        Set("SyDivider", 0x15FFFFFF);
        Set("SySelected", 0x0FFFFFFF);
        Set("SyFocusOuter", 0xFFFFFFFF);
        Set("SyFocusInner", 0xB3000000);
        Set("SyAccentDisabled", 0x28FFFFFF);
        Set("SyTextOnAccentDisabled", 0x87FFFFFF);
        Set("SyWindowFallback", 0xFF202020);
        Resources["SyShadowColor"] = Color.FromRgb(0, 0, 0);
    }

    private static void ApplyAccent(bool isDark)
    {
        // Fluent fills accent controls with a darker accent on light and a lighter accent on dark.
        // The PCL ramp mirrors in dark mode, so step 2 is the darker tone on light and the lighter one on dark.
        var accent = Ramp("ColorBrush2", "ColorBrush3");
        var text = accent;
        SetBrush("SyAccentFill", accent);
        Resources["SyAccentColor"] = accent; // flat accent for former PCL gradient stops
        SetBrush("SyAccentHover", WithAlpha(accent, 0xE6));
        SetBrush("SyAccentPressed", WithAlpha(accent, 0xCC));
        SetBrush("SyAccentText", text);
        SetBrush("SyTextOnAccent", RelativeLuminance(accent) > .45 ? Color.FromArgb(0xFF, 0, 0, 0) : Colors.White);
        if (isDark) SetGradient("SyAccentElevation", (0x14000000, 0), (0x14000000, .9), (0x23000000, 1));
        else SetGradient("SyAccentElevation", (0x14FFFFFF, 0), (0x14FFFFFF, .9), (0x66000000, 1));
    }

    private static void ApplyHighContrast()
    {
        foreach (var key in new[] { "SyText", "SyTextSecondary", "SyTextTertiary", "SyFocusOuter" })
            SetBrush(key, SystemColors.WindowTextColor);
        SetBrush("SyTextDisabled", SystemColors.GrayTextColor);
        SetBrush("SyTextOnAccent", SystemColors.HighlightTextColor);
        SetBrush("SyTextOnAccentDisabled", SystemColors.GrayTextColor);
        foreach (var key in new[] { "SyControl", "SyControlHover", "SyControlPressed", "SyCardFill", "SyLayerFill", "SyAcrylicTint", "SyFrostFill", "SyFrostHover", "SyFlyoutFill", "SyControlInputActive", "SyControlAlt", "SyVeil", "SyTitleBarFill", "SyWindowFallback", "SyFocusInner" })
            SetBrush(key, SystemColors.WindowColor);
        foreach (var key in new[] { "SySubtleHover", "SySubtlePressed", "SySelected" })
            SetBrush(key, SystemColors.HighlightColor);
        foreach (var key in new[] { "SyControlStroke", "SyControlElevation", "SyTextControlElevation", "SyCardStroke", "SyFrostEdge", "SyControlStrongStroke", "SyCritical", "SySurfaceStroke", "SyDivider", "SyAccentElevation" })
            SetBrush(key, SystemColors.WindowTextColor);
        foreach (var key in new[] { "SyAccentFill", "SyAccentHover", "SyAccentPressed", "SyAccentText" })
            SetBrush(key, SystemColors.HighlightColor);
        SetBrush("SyAccentDisabled", SystemColors.GrayTextColor);
        Resources["SyAccentColor"] = SystemColors.HighlightColor;
        Resources["SyShadowColor"] = Colors.Transparent;
        ApplyLegacyAliases();
    }

    /// <summary>
    ///     PCL draws most text with ColorBrush1, an accent-tinted dark blue. Fluent text is neutral,
    ///     so step 1 of the ramp is pinned to the Fluent primary text colour.
    /// </summary>
    private static void ApplyNeutralText(bool isDark)
    {
        if (SystemParameters.HighContrast) return;
        var text = isDark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1B, 0x1B, 0x1B);
        if (Resources["ColorObject1"] is Color current && current == text) return;
        Resources["ColorObject1"] = text;
        SetBrush("ColorBrush1", text);
    }

    /// <summary>Keys that pages written for the previous shell still reference.</summary>
    private static void ApplyLegacyAliases()
    {
        Alias("GlassTextBrush", "SyText");
        Alias("GlassSecondaryTextBrush", "SyTextSecondary");
        Alias("HomeTextBrush", "SyText");
        Alias("HomeSecondaryTextBrush", "SyTextSecondary");
        Alias("GlassControlBrush", "SyControl");
        Alias("GlassHoverBrush", "SyControlHover");
        Alias("GlassOutlineBrush", "SyControlStroke");
        Alias("GlassSelectedBrush", "SySubtleHover");
        Alias("GlassListSelectedBrush", "SySubtleHover");
        Alias("GlassCardBrush", "SyCardFill");
        Alias("GlassChromeBrush", "SyLayerFill");
        Alias("GlassMainBrush", "SyLayerFill");
        Alias("GlassWindowBrush", "SyWindowFallback");
        Alias("GlassAccentBrush", "SyAccentFill");
        Alias("GlassAccentTextBrush", "SyTextOnAccent");
    }

    /// <summary>Theme hue used for the generated default backdrop.</summary>
    public static double AccentHue()
    {
        try
        {
            return ThemeService.GetCurrentThemeArgs().Hue;
        }
        catch (Exception)
        {
            return 250;
        }
    }

    private static Color Ramp(string key, string fallbackKey) =>
        Resources[key] is SolidColorBrush brush ? brush.Color
        : Resources[fallbackKey] is SolidColorBrush fallback ? fallback.Color
        : Color.FromRgb(0x00, 0x5F, 0xB8);

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= .03928 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        }

        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color FromArgb(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static void Alias(string legacyKey, string key) => Resources[legacyKey] = Resources[key];

    private static void Set(string key, uint argb) => SetBrush(key, FromArgb(argb));

    private static void SetBrush(string key, Color color)
    {
        if (Resources[key] is SolidColorBrush existing && existing.Color == color) return;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Resources[key] = brush;
    }

    /// <summary>
    ///     WinUI TextControlElevationBorderBrush: the bottom 1 px of the border is the strong stroke, the rest the
    ///     normal stroke. Absolute mapping flipped about the centre keeps the line 1 px tall at any height and lets it
    ///     follow the rounded bottom corners, unlike a separate underline element.
    /// </summary>
    private static void SetTextElevation(uint strong, uint normal)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, 2),
            RelativeTransform = new ScaleTransform(1, -1, .5, .5)
        };
        brush.GradientStops.Add(new GradientStop(FromArgb(strong), .5));
        brush.GradientStops.Add(new GradientStop(FromArgb(normal), .5));
        brush.Freeze();
        Resources["SyTextControlElevation"] = brush;
    }

    private static void SetGradient(string key, params (uint Argb, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (argb, offset) in stops) brush.GradientStops.Add(new GradientStop(FromArgb(argb), offset));
        brush.Freeze();
        Resources[key] = brush;
    }
}
