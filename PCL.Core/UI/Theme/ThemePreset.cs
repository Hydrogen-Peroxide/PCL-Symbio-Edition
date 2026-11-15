using System.Collections.Generic;
using PCL.Core.App;

namespace PCL.Core.UI.Theme;

/// <summary>
/// 以 OKLCH 描述的颜色（L 明度、C 彩度、H 色相）。
/// </summary>
public readonly record struct LchColor(double L, double C, double H);

/// <summary>
/// 灰度色阶的着色参数，用于给纸面、卡片与次要文字带上极轻微的色偏。
/// </summary>
public readonly record struct GrayTint(int Hue, double Chroma);

/// <summary>
/// 主题预设：描述一个配色主题的强调色色阶，以及可选的界面基色覆盖。
/// </summary>
/// <remarks>
/// 未提供 <see cref="LightRamp"/> / <see cref="DarkRamp"/> 时，强调色由 <see cref="Hue"/> 结合灰度基调动态推导，
/// 与旧版主题（天空蓝、龙猫蓝、死机蓝）的行为一致。
/// </remarks>
public sealed record ThemePreset
{
    /// <summary>
    /// 对应的主题枚举值。
    /// </summary>
    public required ColorTheme Theme { get; init; }

    /// <summary>
    /// 强调色色相。
    /// </summary>
    public int Hue { get; init; }

    /// <summary>
    /// 强调色明度偏移，取值范围 -1 ~ 1。
    /// </summary>
    public double LightAdjust { get; init; }

    /// <summary>
    /// 强调色彩度偏移，取值范围 -1 ~ 1。
    /// </summary>
    public double ChromaAdjust { get; init; }

    /// <summary>
    /// 浅色模式的强调色色阶，依次对应 Color1 ~ Color8。
    /// </summary>
    public IReadOnlyList<LchColor>? LightRamp { get; init; }

    /// <summary>
    /// 深色模式的强调色色阶，依次对应 Color1 ~ Color8。
    /// </summary>
    public IReadOnlyList<LchColor>? DarkRamp { get; init; }

    /// <summary>
    /// 浅色模式下覆盖默认值的界面基色。
    /// </summary>
    public ToneProfile? LightTone { get; init; }

    /// <summary>
    /// 深色模式下覆盖默认值的界面基色。
    /// </summary>
    public ToneProfile? DarkTone { get; init; }

    /// <summary>
    /// 浅色模式灰度色阶的着色。
    /// </summary>
    public GrayTint? LightGrayTint { get; init; }

    /// <summary>
    /// 深色模式灰度色阶的着色。
    /// </summary>
    public GrayTint? DarkGrayTint { get; init; }

    /// <summary>
    /// 彩色背景渐变相对默认配色的彩度缩放，默认 1。
    /// </summary>
    public double BackdropChromaScale { get; init; } = 1;
}

/// <summary>
/// 内置主题预设表。
/// </summary>
public static class ThemePresets
{
    private static readonly ThemePreset[] Items =
    [
        new()
        {
            Theme = ColorTheme.SkyBlue,
            Hue = 235,
            LightAdjust = 0.36,
            ChromaAdjust = 0.2
        },
        new()
        {
            Theme = ColorTheme.CatBlue,
            Hue = 255,
            ChromaAdjust = -0.2
        },
        new()
        {
            Theme = ColorTheme.DeathBlue,
            Hue = 268,
            LightAdjust = -0.05,
            ChromaAdjust = -0.1
        },
        new()
        {
            Theme = ColorTheme.HmclBlue,
            Hue = 275,
            LightAdjust = -0.03,
            ChromaAdjust = -0.35
        },
        new()
        {
            // 归藏设计 · 克莱因：纸白界面、墨黑文字，只留一个国际克莱因蓝作强调色
            Theme = ColorTheme.KleinBlue,
            Hue = 263,
            BackdropChromaScale = 0.35,
            // 色阶（Color1 ~ Color8）：墨 → 深克莱因 → IKB #002FA7 → 悬停蓝 → 浅描边 → 纸面填充
            LightRamp =
            [
                new LchColor(0.145, 0.000, 263),
                new LchColor(0.330, 0.160, 263),
                new LchColor(0.379, 0.195, 263),
                new LchColor(0.500, 0.180, 264),
                new LchColor(0.780, 0.060, 264),
                new LchColor(0.900, 0.014, 264),
                new LchColor(0.945, 0.008, 264),
                new LchColor(0.975, 0.004, 264)
            ],
            DarkRamp =
            [
                new LchColor(0.930, 0.010, 90),
                new LchColor(0.720, 0.150, 264),
                new LchColor(0.550, 0.210, 265),
                new LchColor(0.660, 0.170, 265),
                new LchColor(0.420, 0.100, 265),
                new LchColor(0.300, 0.035, 265),
                new LchColor(0.260, 0.020, 265),
                new LchColor(0.230, 0.012, 265)
            ],
            // 纸面：比纯白略暗一点的暖米白；深色模式压到近黑，让 IKB 成为唯一的亮色
            LightTone = new ToneProfile(
                L6: 0.920, L7: 0.945, L8: 0.965, LBackground: 0.985),
            DarkTone = new ToneProfile(
                L1: 0.960, L2: 0.760, L3: 0.620, L4: 0.660, L5: 0.460,
                L6: 0.210, L7: 0.190, L8: 0.175,
                LWhite: 0.240, LBackground: 0.235),
            LightGrayTint = new GrayTint(95, 0.008),
            DarkGrayTint = new GrayTint(265, 0.004)
        }
    ];

    private static readonly Dictionary<ColorTheme, ThemePreset> Lookup = BuildLookup(Items);

    private static Dictionary<ColorTheme, ThemePreset> BuildLookup(ThemePreset[] presets)
    {
        var lookup = new Dictionary<ColorTheme, ThemePreset>(presets.Length);
        foreach (var preset in presets) lookup[preset.Theme] = preset;
        return lookup;
    }

    /// <summary>
    /// 获取指定主题的预设，未登记的取值退回龙猫蓝。
    /// </summary>
    public static ThemePreset Get(ColorTheme theme)
    {
        if (Lookup.TryGetValue(theme, out var preset)) return preset;
        return Lookup[ColorTheme.CatBlue];
    }
}
