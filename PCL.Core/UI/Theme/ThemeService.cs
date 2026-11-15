using System;
using System.Collections.Generic;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.IoC;

namespace PCL.Core.UI.Theme;

/// <summary>
/// 配色模式更改事件。
/// </summary>
/// <param name="isDarkMode">当前是否为深色模式</param>
/// <param name="theme">当前配色主题</param>
public delegate void ColorModeChangedEvent(bool isDarkMode, ColorTheme theme);

/// <summary>
/// 配色主题更改事件。
/// </summary>
/// <param name="theme">当前配色主题</param>
public delegate void ColorThemeChangedEvent(ColorTheme theme);

[LifecycleScope("theme", "主题", false)]
[LifecycleService(LifecycleState.WindowCreating)]
public sealed partial class ThemeService
{
    [AnyConfigItem<ToneProfileConfig>("UiToneProfiles", ConfigSource.Local)]
    public static partial ToneProfileConfig ToneProfiles { get; set; }

    [RegisterConfigEvent]
    public static ConfigEventRegistry OnColorModeConfigChanged => new(
        scope: Config.Preference.Theme.ColorModeConfig,
        trigger: ConfigEvent.Update,
        handler: _ => RefreshColorMode()
    );

    [RegisterConfigEvent]
    public static ConfigEventRegistry OnColorThemeConfigChanged => new(
        scope: [Config.Preference.Theme.DarkColorConfig, Config.Preference.Theme.LightColorConfig],
        trigger: ConfigEvent.Update,
        handler: e =>
        {
            // ignore no change or non-current color theme change
            if (e.OldValue == e.Value) return;
            if (IsDarkMode) { if (e.Item == Config.Preference.Theme.LightColorConfig) return; }
            else { if (e.Item == Config.Preference.Theme.DarkColorConfig) return; }
            // trigger color refresh
            if (Lifecycle.CurrentState > LifecycleState.Loading)
            {
                Lifecycle.CurrentApplication.Dispatcher.BeginInvoke(() =>
                {
                    ApplyGrayResources();
                    ApplyColorResources();
                    ColorThemeChanged?.Invoke(CurrentTheme);
                    _AprilFoolLogic();
                });
            }
        }
    );

    [LifecycleStart]
    private static void _Start()
    {
        IsDarkMode = _IsDarkMode();
        _LogStatus();
        _RefreshAll();
    }

    private static void _LogStatus()
    {
        Context.Debug($"当前状态: {(IsDarkMode ? "Dark" : "Light")}, {CurrentTheme}");
    }

    private static bool _IsDarkMode() => Config.Preference.Theme.ColorMode switch
    {
        ColorMode.Light => false,
        ColorMode.Dark => true,
        ColorMode.System => SystemThemeHelper.IsSystemInDarkMode(),
        _ => false
    };

    /// <summary>
    /// 当前是否为深色主题。
    /// </summary>
    public static bool IsDarkMode { get; private set; }

    /// <summary>
    /// 配色模式更改事件。
    /// </summary>
    public static event ColorModeChangedEvent? ColorModeChanged;

    /// <summary>
    /// 配色主题更改事件。
    /// </summary>
    public static event ColorThemeChangedEvent? ColorThemeChanged;

    private static void _RefreshAll()
    {
        ApplyGrayResources();
        ApplyColorResources();
        ColorModeChanged?.Invoke(IsDarkMode, CurrentTheme);
        _AprilFoolLogic();
    }

    private static void _AprilFoolLogic()
    {
        // for HMCL theme on April Fools' Day
        if (Basics.IsAprilFool) Config.Preference.WindowTitleTypeConfig.TriggerEvent(ConfigEvent.Changed, null);
        else if (CurrentTheme == ColorTheme.HmclBlue) CurrentTheme = ColorTheme.CatBlue;
    }

    /// <summary>
    /// 刷新配色模式，若检测到当前配色模式有实际更改，则会触发主题刷新。
    /// </summary>
    public static void RefreshColorMode()
    {
        var isDarkMode = _IsDarkMode();
        if (IsDarkMode == isDarkMode) return;
        Context.Info("正在更改配色模式");
        IsDarkMode = isDarkMode;
        _LogStatus();
        if (Lifecycle.CurrentState > LifecycleState.Loading)
        {
            Lifecycle.CurrentApplication.Dispatcher.BeginInvoke(_RefreshAll);
        }
    }

    /// <summary>
    /// 当前使用的主题预设。
    /// </summary>
    public static ThemePreset CurrentPreset => ThemePresets.Get(CurrentTheme);

    /// <summary>
    /// 当前使用的色彩属性，主题未覆盖时回退到全局色调配置。
    /// </summary>
    public static ToneProfile CurrentTone
    {
        get
        {
            var preset = CurrentPreset;
            var presetTone = IsDarkMode ? preset.DarkTone : preset.LightTone;
            return presetTone ?? (IsDarkMode ? ToneProfiles.Dark : ToneProfiles.Light);
        }
    }

    /// <summary>
    /// 当前使用的主题色。
    /// </summary>
    public static ColorTheme CurrentTheme
    {
        get
        {
            var theme = Config.Preference.Theme;
            return IsDarkMode ? theme.DarkColor : theme.LightColor;
        }
        set
        {
            var theme = Config.Preference.Theme;
            var config = IsDarkMode ? theme.DarkColorConfig : theme.LightColorConfig;
            config.SetValue(value);
        }
    }

    /// <summary>
    /// 获取当前色彩主题对应的各种参数。
    /// </summary>
    public static (int Hue, double LightAdjust, double ChromaAdjust) GetCurrentThemeArgs()
    {
        var preset = CurrentPreset;
        return (preset.Hue, preset.LightAdjust, preset.ChromaAdjust);
    }

    private static double _AdjustLinear(double value, double adjustment)
    {
        if (adjustment == 0) return value;
        // 确保输入在合理范围内
        value = Math.Clamp(value, 0.0, 1.0);
        adjustment = Math.Clamp(adjustment, -1.0, 1.0);
        // 非对称线性插值
        return adjustment switch
        {
            > 0 => value + (1.0 - value) * adjustment,
            _ => value + value * adjustment
        };
    }

    private static CatColorResource[] _CalculateGrays(ToneProfile tone, GrayTint? tint) => [
        _Gray(tone.L1, tint).ToCatColor("Gray1"),
        _Gray(tone.L2, tint).ToCatColor("Gray2"),
        _Gray(tone.L3, tint).ToCatColor("Gray3"),
        _Gray(tone.L4, tint).ToCatColor("Gray4"),
        _Gray(tone.L5, tint).ToCatColor("Gray5"),
        _Gray(tone.L6, tint).ToCatColor("Gray6"),
        _Gray(tone.L7, tint).ToCatColor("Gray7"),
        _Gray(tone.L8, tint).ToCatColor("Gray8"),
        _Gray(tone.LWhite, tint, tone.AHalfWhite).ToCatColor("HalfWhite", false),
        _Gray(tone.LWhite, tint, tone.ASemiWhite).ToCatColor("SemiWhite", false),
        _Gray(tone.LWhite, tint).ToCatColor("White", false),
        _Gray(tone.LWhite, tint, tone.ATransparent).ToCatColor("Transparent", false),
        _Gray(tone.LBackground, tint, tone.ABackground).ToCatColor("TransparentBackground", false),
        _Gray(tone.LBackground, tint).ToCatColor("Background", false),
        _Gray(tone.LBackground, tint, tone.AToolTip).ToCatColor("ToolTip", false),
        LabColor.FromLch(tone.L7, 0.25, 30, tone.AHalfTransparent).ToCatColor("RedBack", false),
        _Gray(tone.LForeground, tint).ToCatColor("Memory", false),
    ];

    /// <summary>
    /// 按灰度基调生成灰度色，必要时带上主题的轻微色偏。
    /// </summary>
    private static LabColor _Gray(double light, GrayTint? tint, double alpha = 1.0)
    {
        var tintValue = tint.GetValueOrDefault();
        return tintValue.Chroma > 0
            ? LabColor.FromLch(light, tintValue.Chroma, tintValue.Hue, alpha)
            : LabColor.FromLch(light, alpha: alpha);
    }

    private static CatColorResource[] _CalculateColors(ToneProfile tone, ThemePreset preset, bool dark)
    {
        var ramp = dark ? preset.DarkRamp : preset.LightRamp;
        if (ramp is { Count: >= 8 }) return _CalculateColorsFromRamp(tone, ramp);

        var (hue, lightAdj, chromaAdj) = (preset.Hue, preset.LightAdjust, preset.ChromaAdjust);
        return [
            LabColor.FromLch(_AdjustLinear(tone.L1, lightAdj * 0.1), _AdjustLinear(tone.C1, chromaAdj * 0.25), hue).ToCatColor("1"),
            LabColor.FromLch(_AdjustLinear(tone.L2, lightAdj), _AdjustLinear(tone.C2, chromaAdj), hue).ToCatColor("2"),
            LabColor.FromLch(_AdjustLinear(tone.L3, lightAdj), _AdjustLinear(tone.C3, chromaAdj), hue).ToCatColor("3"),
            LabColor.FromLch(_AdjustLinear(tone.L4, lightAdj), _AdjustLinear(tone.C4, chromaAdj), hue).ToCatColor("4"),
            LabColor.FromLch(_AdjustLinear(tone.L5, lightAdj), _AdjustLinear(tone.C5, chromaAdj), hue).ToCatColor("5"),
            LabColor.FromLch(_AdjustLinear(tone.L6, lightAdj), _AdjustLinear(tone.C6, chromaAdj), hue).ToCatColor("6"),
            LabColor.FromLch(_AdjustLinear(tone.L7, lightAdj), _AdjustLinear(tone.C7, chromaAdj), hue).ToCatColor("7"),
            LabColor.FromLch(_AdjustLinear(tone.L8, lightAdj), _AdjustLinear(tone.C8, chromaAdj), hue).ToCatColor("8"),
            LabColor.FromLch(_AdjustLinear(tone.L8, lightAdj), _AdjustLinear(tone.C8, chromaAdj), hue, tone.ASemiTransparent).ToCatColor("SemiTransparent", false),
            LabColor.FromLch(_AdjustLinear(tone.L5, lightAdj), _AdjustLinear(tone.C5, chromaAdj), hue).ToCatColor("Bg0"),
            LabColor.FromLch(_AdjustLinear(tone.L7, lightAdj), _AdjustLinear(tone.C7, chromaAdj), hue, tone.ASemiWhite).ToCatColor("Bg1"),
        ];
    }

    /// <summary>
    /// 按主题预设直接给定的色阶生成强调色，用于需要精确控制每一个色阶的主题。
    /// </summary>
    private static CatColorResource[] _CalculateColorsFromRamp(ToneProfile tone, IReadOnlyList<LchColor> ramp)
    {
        LabColor accent(int index, double alpha = 1.0)
        {
            var color = ramp[index];
            return LabColor.FromLch(color.L, color.C, color.H, alpha);
        }

        return [
            accent(0).ToCatColor("1"),
            accent(1).ToCatColor("2"),
            accent(2).ToCatColor("3"),
            accent(3).ToCatColor("4"),
            accent(4).ToCatColor("5"),
            accent(5).ToCatColor("6"),
            accent(6).ToCatColor("7"),
            accent(7).ToCatColor("8"),
            accent(7, tone.ASemiTransparent).ToCatColor("SemiTransparent", false),
            accent(4).ToCatColor("Bg0"),
            accent(6, tone.ASemiWhite).ToCatColor("Bg1"),
        ];
    }

    private static readonly Dictionary<(ColorTheme Theme, ToneProfile Tone), CatColorResource[]> LightGrayCache = [];

    private static readonly Dictionary<(ColorTheme Theme, ToneProfile Tone), CatColorResource[]> DarkGrayCache = [];

    /// <summary>
    /// 清除灰度配色的计算缓存。
    /// </summary>
    public static void InvalidateGrayCache()
    {
        LightGrayCache.Clear();
        DarkGrayCache.Clear();
    }

    /// <summary>
    /// 应用灰度配色到 WPF 资源字典。
    /// </summary>
    public static void ApplyGrayResources()
    {
        var cache = IsDarkMode ? DarkGrayCache : LightGrayCache;
        var preset = CurrentPreset;
        var tone = CurrentTone;
        var key = (preset.Theme, tone);
        if (!cache.TryGetValue(key, out var resources))
        {
            cache[key] = resources = _CalculateGrays(
                tone, IsDarkMode ? preset.DarkGrayTint : preset.LightGrayTint);
        }

        foreach (var c in resources) c.Apply();
    }

    /// <summary>
    /// 应用彩色配色到 WPF 资源字典。
    /// </summary>
    public static void ApplyColorResources()
    {
        var colors = _CalculateColors(CurrentTone, CurrentPreset, IsDarkMode);
        foreach (var c in colors) c.Apply();
    }
}
