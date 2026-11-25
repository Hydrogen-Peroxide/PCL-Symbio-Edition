using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.IO.Net.Http;
using PCL.Core.UI.Theme;
using PCL.Core.Utils.Exts;
using PCL.Network;
using PCLLF.Wpf.Motion;

using PCL.Core.App.Localization;
namespace PCL;

public class ModSetup
{
    internal static event Action? AppearanceOptionsChanged;
    internal static event Action? LaunchLogVisibilityChanged;
    internal static event Action<int>? LaunchMemoryChanged;
    internal static event Action<string, int>? InstanceMemoryChanged;

    public ModSetup()
    {
        // === Hide Group ===
        ConfigService.RegisterObserver(Config.Preference.Hide,
            new ConfigObserver(ConfigEvent.Changed, _ => ShellNavigation.NotifyVisibilityChanged()));

        States.UI.ShowLaunchLogConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            _ => ModBase.RunInUi(() => LaunchLogVisibilityChanged?.Invoke())));

        // === Launch ===
        Config.Launch.MemoryAllocationModeConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => LaunchRamType((int)e.Value!)));
        States.Game.SelectedFolderConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => LaunchFolderSelect((string)(e.Value ?? ""))));
        States.Game.SelectedInstanceConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => LaunchInstanceSelect((string)(e.Value ?? ""))));

        // === Tool ===
        Config.Download.ThreadLimitConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => ToolDownloadThread((int)e.Value!)));
        Config.Download.SpeedLimitConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => ToolDownloadSpeed((int)e.Value!)));

        // === UI - Launcher ===
        Config.Preference.Theme.WindowOpacityConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiLauncherTransparent((int)e.Value!)));
        Config.Preference.Theme.ThemeSelectedConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiLauncherTheme((int)e.Value!)));
        Config.Preference.Background.BackgroundColorfulConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiBackgroundColorful((bool)e.Value!)));
        Config.Preference.LockWindowSizeConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiLockWindowSize((bool)e.Value!)));

        // UI - Video Background
        Config.Preference.Background.AutoPauseVideoConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiAutoPauseVideo((bool)e.Value!)));

        // UI - Background Image
        Config.Preference.Background.WallpaperOpacityConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiBackgroundOpacity((int)e.Value!)));
        Config.Preference.Background.WallpaperBlurRadiusConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiBackgroundBlur((int)e.Value!)));
        Config.Preference.Background.WallpaperSuitModeConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiBackgroundSuit((int)e.Value!)));

        // UI - Font
        Config.Preference.FontConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiFont((string)(e.Value ?? ""))));

        // UI - Homepage
        Config.Preference.Homepage.TypeConfig.Observe(new ConfigObserver(ConfigEvent.Changed, _ => NotifyAppearanceOptions()));
        States.Hint.UntrustedHomepageConfig.Observe(new ConfigObserver(ConfigEvent.Changed, _ => NotifyAppearanceOptions()));

        // UI - Blur
        Config.Preference.Blur.IsEnabledConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiBlur((bool)e.Value!)));
        Config.Preference.Blur.RadiusConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiBlurValue((int)e.Value!)));
        Config.Preference.Blur.SamplingRateConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiBlurSamplingRate((int)e.Value!)));
        Config.Preference.Blur.KernelTypeConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiBlurType((int)e.Value!)));

        // UI - Title Bar
        Config.Preference.WindowTitleTypeConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiLogoType((int)e.Value!)));
        Config.Preference.WindowTitleCustomTextConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiLogoText((string)(e.Value ?? ""))));
        Config.Preference.TopBarLeftAlignConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => UiLogoLeft((bool)e.Value!)));

        // === System ===
        Config.Debug.EnabledConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => SystemDebugMode((bool)e.Value!)));
        Config.Debug.AnimationSpeedConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => SystemDebugAnim((int)e.Value!)));
        Config.System.AnimationFpsLimitConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => MotionPreferences.FrameRateLimit = Math.Clamp((int)e.Value!, 0, 239) + 1));
        Config.Network.HttpProxy.CustomAddressConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => SystemHttpProxy((string)(e.Value ?? ""))));
        Config.Network.HttpProxy.TypeConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => SystemHttpProxyType((int)e.Value!)));
        Config.Network.HttpProxy.CustomUsernameConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => SystemHttpProxyCustomUsername((string)(e.Value ?? ""))));
        Config.Network.HttpProxy.CustomPasswordConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => SystemHttpProxyCustomPassword((string)(e.Value ?? ""))));

        // === Version ===
        Config.Instance.MemorySolutionConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => PublishInstanceMemory(e.Argument as string, (int)e.Value!)));
        Config.InstanceAuth.LoginRequirementSolutionConfig.Observe(new ConfigObserver(ConfigEvent.Changed,
            e => RefreshInstanceLogin(e.Argument as string)));
    }

    /// <summary>
    ///     主动应用所有当前配置值。
    /// </summary>
    public static void ApplyAll()
    {
        // Launch
        LaunchRamType(Config.Launch.MemoryAllocationMode);

        // Tool
        ToolDownloadThread(Config.Download.ThreadLimit);
        ToolDownloadSpeed(Config.Download.SpeedLimit);

        // UI - Launcher
        UiLauncherTransparent(Config.Preference.Theme.WindowOpacity);
        UiLauncherTheme(Config.Preference.Theme.ThemeSelected);
        UiBackgroundColorful(Config.Preference.Background.BackgroundColorful);
        UiLockWindowSize(Config.Preference.LockWindowSize);

        // UI - Video Background
        UiAutoPauseVideo(Config.Preference.Background.AutoPauseVideo);

        // UI - Background Image
        UiBackgroundOpacity(Config.Preference.Background.WallpaperOpacity);
        UiBackgroundBlur(Config.Preference.Background.WallpaperBlurRadius);
        UiBackgroundSuit(Config.Preference.Background.WallpaperSuitMode);

        // UI - Font
        UiFont(Config.Preference.Font);

        // UI - Blur
        if (Config.Preference.Blur.IsEnabled)
        {
            UiBlurValue(Config.Preference.Blur.Radius);
            UiBlurSamplingRate(Config.Preference.Blur.SamplingRate);
            UiBlurType(Config.Preference.Blur.KernelType);
        }
        else
        {
            UiBlurValue(0);
        }

        UiBlur(Config.Preference.Blur.IsEnabled);

        // UI - Title Bar
        UiLogoType((int)Config.Preference.WindowTitleType);
        UiLogoText(Config.Preference.WindowTitleCustomText);
        UiLogoLeft(Config.Preference.TopBarLeftAlign);

        // UI - Hide
        ShellNavigation.NotifyVisibilityChanged();

        // System
        SystemDebugMode(Config.Debug.Enabled);
        SystemDebugAnim(Config.Debug.AnimationSpeed);
        MotionPreferences.FrameRateLimit = Math.Clamp(Config.System.AnimationFpsLimit, 0, 239) + 1;
        SystemHttpProxy(Config.Network.HttpProxy.CustomAddress);
        SystemHttpProxyType(Config.Network.HttpProxy.Type);
        SystemHttpProxyCustomUsername(Config.Network.HttpProxy.CustomUsername);
        SystemHttpProxyCustomPassword(Config.Network.HttpProxy.CustomPassword);
    }

    #region Launch

    // 切换选择
    public static void LaunchInstanceSelect(string value)
    {
        ModBase.Log("[Setup] 当前选择的 Minecraft 版本：" + value);
        ModBase.WriteIni(ModFolder.mcFolderSelected + "PCL.ini", "Version", value);
    }

    public static void LaunchFolderSelect(string value)
    {
        ModBase.Log("[Setup] 当前选择的 Minecraft 文件夹：" + value.Replace("$", ModBase.exePath));
        ModFolder.mcFolderSelected = value.Replace("$", ModBase.exePath);
    }

    // 游戏内存
    private static void LaunchRamType(int type) => ModBase.RunInUi(() => LaunchMemoryChanged?.Invoke(type));

    internal static void ResetLaunchPreferences()
    {
        try
        {
            Config.Launch.Reset();
            ModBase.Log("[Setup] 已初始化启动设置");
            HintService.Hint(Lang.Text("Setup.Launch.Initialized"), HintType.Success, false);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Launch.Error.InitFailed"),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Launch.Error.InitFailed"));
        }

    }

    #endregion

    #region Tool

    public static void ToolDownloadThread(int value)
    {
        ModNet.NetTaskThreadLimit = value + 1;
    }

    public static void ToolDownloadSpeed(int value)
    {
        ModNet.NetTaskSpeedLimitHigh = value switch
        {
            <= 14 => (long)Math.Round((value + 1) * 0.1d * 1024d * 1024d),
            <= 31 => (long)Math.Round((value - 11) * 0.5d * 1024d * 1024d),
            <= 41 => (value - 21) * 1024 * 1024L,
            _ => -1
        };
    }

    #endregion

    #region UI

    // 启动器
    public static void UiLauncherTransparent(int value)
    {
        ModMain.frmMain.Opacity = Math.Clamp(value / 1000d + .4, .4, 1);
    }

    public static void UiLauncherTheme(int value)
    {
        ThemeManager.ThemeRefresh(value);
    }

    public static void UiBackgroundColorful(bool value)
    {
        ThemeManager.ThemeRefresh();
    }

    public static void UiLockWindowSize(bool value)
    {
        if (value)
            ModMain.frmMain.RemoveResizer();
        else
            ModMain.frmMain.AddResizer();
    }

    // 视频背景
    public static void UiAutoPauseVideo(bool value)
    {
        if (!value)
        {
            ModVideoBack.ForcePlay = true;
            ModVideoBack.VideoPlay();
        }
        else
        {
            ModVideoBack.ForcePlay = false;
            if (ModVideoBack.IsGaming)
                ModVideoBack.VideoPause();
        }
    }

    // 背景图片
    public static void UiBackgroundOpacity(int value)
    {
        var window = ModMain.frmMain;
        window.ImgBack.Opacity = window.ImgBack.Background is ImageBrush ? 0 : value / 1000d;
        window.RefreshShell();
    }

    public static void UiBackgroundBlur(int value)
    {
        ModMain.frmMain.ImgBack.Effect = value == 0 ? null : new BlurEffect { Radius = value + 1 };
        ModMain.frmMain.ImgBack.Margin = new Thickness(-(value + 1) / 1.8d);
    }

    public static void UiBackgroundSuit(int value)
    {
        if (ModMain.frmMain.ImgBack.Background is null)
            return;
        var width = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Width;
        var height = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Height;
        if (value == 0)
        {
            // 智能：当图片较小时平铺，较大时适应
            if (width < ModMain.frmMain.PanMain.ActualWidth / 2d && height < ModMain.frmMain.PanMain.ActualHeight / 2d)
                value = 4; // 平铺
            else
                value = 2; // 适应
        }

        ((ImageBrush)ModMain.frmMain.ImgBack.Background).TileMode = TileMode.None;
        ((ImageBrush)ModMain.frmMain.ImgBack.Background).Viewport = new Rect(0d, 0d, 1d, 1d);
        ((ImageBrush)ModMain.frmMain.ImgBack.Background).ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
        switch (value)
        {
            case 1: // 居中
            {
                ModMain.frmMain.ImgBack.HorizontalAlignment = HorizontalAlignment.Center;
                ModMain.frmMain.ImgBack.VerticalAlignment = VerticalAlignment.Center;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Stretch = Stretch.None;
                ModMain.frmMain.ImgBack.Width = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Width;
                ModMain.frmMain.ImgBack.Height = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Height;
                break;
            }
            case 2: // 适应
            {
                ModMain.frmMain.ImgBack.HorizontalAlignment = HorizontalAlignment.Stretch;
                ModMain.frmMain.ImgBack.VerticalAlignment = VerticalAlignment.Stretch;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Stretch = Stretch.UniformToFill;
                ModMain.frmMain.ImgBack.Width = double.NaN;
                ModMain.frmMain.ImgBack.Height = double.NaN;
                break;
            }
            case 3: // 拉伸
            {
                ModMain.frmMain.ImgBack.HorizontalAlignment = HorizontalAlignment.Stretch;
                ModMain.frmMain.ImgBack.VerticalAlignment = VerticalAlignment.Stretch;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Stretch = Stretch.Fill;
                ModMain.frmMain.ImgBack.Width = double.NaN;
                ModMain.frmMain.ImgBack.Height = double.NaN;
                break;
            }
            case 4: // 平铺
            {
                ModMain.frmMain.ImgBack.HorizontalAlignment = HorizontalAlignment.Stretch;
                ModMain.frmMain.ImgBack.VerticalAlignment = VerticalAlignment.Stretch;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Stretch = Stretch.None;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).TileMode = TileMode.Tile;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Viewport = new Rect(0d, 0d,
                    ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Width,
                    ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Height);
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).ViewportUnits = BrushMappingMode.Absolute;
                ModMain.frmMain.ImgBack.Width = double.NaN;
                ModMain.frmMain.ImgBack.Height = double.NaN;
                break;
            }
            case 5: // 左上
            {
                ModMain.frmMain.ImgBack.HorizontalAlignment = HorizontalAlignment.Left;
                ModMain.frmMain.ImgBack.VerticalAlignment = VerticalAlignment.Top;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Stretch = Stretch.None;
                ModMain.frmMain.ImgBack.Width = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Width;
                ModMain.frmMain.ImgBack.Height = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Height;
                break;
            }
            case 6: // 右上
            {
                ModMain.frmMain.ImgBack.HorizontalAlignment = HorizontalAlignment.Right;
                ModMain.frmMain.ImgBack.VerticalAlignment = VerticalAlignment.Top;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Stretch = Stretch.None;
                ModMain.frmMain.ImgBack.Width = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Width;
                ModMain.frmMain.ImgBack.Height = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Height;
                break;
            }
            case 7: // 左下
            {
                ModMain.frmMain.ImgBack.HorizontalAlignment = HorizontalAlignment.Left;
                ModMain.frmMain.ImgBack.VerticalAlignment = VerticalAlignment.Bottom;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Stretch = Stretch.None;
                ModMain.frmMain.ImgBack.Width = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Width;
                ModMain.frmMain.ImgBack.Height = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Height;
                break;
            }
            case 8: // 右下
            {
                ModMain.frmMain.ImgBack.HorizontalAlignment = HorizontalAlignment.Right;
                ModMain.frmMain.ImgBack.VerticalAlignment = VerticalAlignment.Bottom;
                ((ImageBrush)ModMain.frmMain.ImgBack.Background).Stretch = Stretch.None;
                ModMain.frmMain.ImgBack.Width = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Width;
                ModMain.frmMain.ImgBack.Height = ((ImageBrush)ModMain.frmMain.ImgBack.Background).ImageSource.Height;
                break;
            }
        }
    }

    // 字体
    public static void UiFont(string value)
    {
        try
        {
            ModBase.SetLaunchFont(value);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "字体加载失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Setup.Error.OperationFailed"));
        }
    }

    private static void NotifyAppearanceOptions() => ModBase.RunInUi(() => AppearanceOptionsChanged?.Invoke());

    internal static void ResetAppearancePreferences()
    {
        try
        {
            Config.Preference.Reset();
            ModBase.Log("[Setup] 已初始化个性化设置！");
            HintService.Hint(Lang.Text("Setup.Ui.Initialized"), HintType.Success, false);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Setup.Ui.Error.InitFailed"), ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Ui.Error.InitFailed"));
        }
    }

    // 高级材质
    public static void UiBlur(bool value)
    {
        UiBlurValue(value ? Config.Preference.Blur.Radius : 0);
        if (ModMain.frmMain is not null)
            ThemeManager.ThemeRefreshMain();
        NotifyAppearanceOptions();
    }

    public static void UiBlurValue(int value)
    {
        System.Windows.Application.Current.Resources["BlurRadius"] = value * 1.0d;
    }

    public static void UiBlurSamplingRate(int value)
    {
        System.Windows.Application.Current.Resources["BlurSamplingRate"] = value * 0.01d;
    }

    public static void UiBlurType(int value)
    {
        System.Windows.Application.Current.Resources["BlurType"] = (KernelType)value;
    }

    // 顶部栏
    public static void UiLogoType(int value)
    {
        if (ThemeService.CurrentTheme == ColorTheme.HmclBlue) value = 4;
        ModMain.frmMain.RefreshBrand();
        if (value == 3)
        {
            try { ModMain.frmMain.LoadBrandImage(System.IO.Path.Combine(ModBase.exePath, "PCL", "Logo.png")); }
            catch (Exception ex) { ModBase.Log(ex, "显示标题栏图片失败", ModBase.LogLevel.Hint); }
        }
        NotifyAppearanceOptions();
    }

    public static void UiLogoText(string value) => ModMain.frmMain.RefreshBrand();

    public static void UiLogoLeft(bool value) => ModMain.frmMain.RefreshBrand();

    #endregion

    #region System

    // 调试选项
    public static void SystemDebugMode(bool value)
    {
        ModBase.modeDebug = value;
    }

    public static void SystemDebugAnim(int value)
    {
        ModAnimation.aniSpeed = value >= 30
            ? 200d
            : ModBase.MathClamp(value * 0.1d + 0.1d, 0.1d, 3d);
    }

    public static void SystemHttpProxy(string value)
    {
        if (value.IsNullOrWhiteSpace()) return;
        try
        {
            HttpProxyManager.Instance.CustomProxyAddress = new Uri(value);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "HTTP 代理应用出错");
        }
    }

    public static void SystemHttpProxyType(int value)
    {
        var mode = (HttpProxyManager.ProxyMode)value;
        HttpProxyManager.Instance.Mode = Enum.IsDefined(mode)
            ? mode
            : HttpProxyManager.Instance.Mode;
    }

    public static void SystemHttpProxyCustomUsername(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var password = Config.Network.HttpProxy.CustomPassword;
            HttpProxyManager.Instance.Credentials = new NetworkCredential(value, password);
        }
        else
        {
            HttpProxyManager.Instance.Credentials = null;
        }
    }

    public static void SystemHttpProxyCustomPassword(string value)
    {
        var username = Config.Network.HttpProxy.CustomUsername;
        HttpProxyManager.Instance.Credentials = !string.IsNullOrEmpty(username)
            ? new NetworkCredential(username, value)
            : null;
    }

    #endregion

    internal static void ResetGameManagePreferences()
    {
        try
        {
            Config.Download.Reset();
            Config.Tool.Reset();
            ModBase.Log("[Setup] 已初始化其他页设置");
            HintService.Hint(Lang.Text("Setup.GameManage.Initialized"), HintType.Success, false);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.GameManage.Error.InitFailed"),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.GameManage.Error.InitFailed"));
        }

    }

    internal static void ResetLauncherMiscPreferences()
    {
        try
        {
            Config.Network.Reset();
            Config.Debug.Reset();
            Config.System.Reset();
            ModBase.Log("[Setup] 已初始化启动器-杂项页设置");
            HintService.Hint(Lang.Text("Setup.Misc.Initialized"), HintType.Success, false);

        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Misc.Error.InitFailed"),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Misc.Error.InitFailed"));
        }

    }

    private static void PublishInstanceMemory(string? path, int type)
    {
        if (path is not null) ModBase.RunInUi(() => InstanceMemoryChanged?.Invoke(path, type));
    }

    private static void RefreshInstanceLogin(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
        var instancePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var versions = Path.GetDirectoryName(instancePath);
        if (versions is null || !string.Equals(Path.GetFileName(versions), "versions", StringComparison.OrdinalIgnoreCase)) return;
        var directory = Path.GetDirectoryName(versions);
        if (directory is null) return;
        var folder = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        ModBase.RunInUi(() =>
        {
            if (InstanceWorkspace.Current is { } current && string.Equals(
                Path.TrimEndingDirectorySeparator(current.PathInstance), instancePath, StringComparison.OrdinalIgnoreCase))
                InstanceWorkspace.Current = new McInstance(instancePath).Load();
            ModFolder.RefreshInstances(folder, clearCache: true);
        });
    }

    internal static void ResetInstancePreferences(McInstance instance)
    {
        try
        {
            if (!Config.InstanceAuth.AuthLocked[instance.PathInstance])
                Config.InstanceAuth.Reset(instance.PathInstance);

            Config.Instance.Reset(instance.PathInstance);

            ModBase.Log("[Setup] 已初始化实例独立设置");
            HintService.Hint(Lang.Text("Instance.Setup.Initialize.Success"), HintType.Success, false);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "初始化实例独立设置失败",
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Instance.Setup.Error.OperationFailed"));
        }

    }

}
