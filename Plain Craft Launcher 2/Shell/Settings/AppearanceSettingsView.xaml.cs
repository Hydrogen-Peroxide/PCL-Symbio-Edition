using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.UI;
using PCL.Core.UI.Theme;
using PCL.Core.Utils;
using PCL.Core.App.Localization;

namespace PCL;

public partial class AppearanceSettingsView
{
    // 配色下拉框的显示顺序；列表里的下标与枚举值并不总是一致（HMCL 仅在愚人节出现）
    private static readonly ColorTheme[] ThemeValues =
    [
        ColorTheme.SkyBlue, ColorTheme.CatBlue, ColorTheme.DeathBlue, ColorTheme.KleinBlue
    ];

    private static readonly ColorTheme[] AprilFoolThemeValues =
    [
        ColorTheme.SkyBlue, ColorTheme.CatBlue, ColorTheme.DeathBlue, ColorTheme.KleinBlue, ColorTheme.HmclBlue
    ];

    private static ColorTheme[] CurrentThemeValues => Basics.IsAprilFool ? AprilFoolThemeValues : ThemeValues;

    public string[] ThemeColors => CurrentThemeValues.Select(ThemeDisplayName).ToArray();

    /// <summary>
    /// 取得配色主题的显示名称。
    /// </summary>
    public static string ThemeDisplayName(ColorTheme theme) => Lang.Text(theme switch
    {
        ColorTheme.SkyBlue => "Setup.Ui.Theme.Color.SkyBlue",
        ColorTheme.CatBlue => "Setup.Ui.Theme.Color.CatBlue",
        ColorTheme.DeathBlue => "Setup.Ui.Theme.Color.CrashBlue",
        ColorTheme.HmclBlue => "Setup.Ui.Theme.Color.Hmcl",
        ColorTheme.KleinBlue => "Setup.Ui.Theme.Color.Klein",
        _ => "Setup.Ui.Theme.Color.CatBlue"
    });

    public new bool isLoaded;

    public AppearanceSettingsView()
    {
        InitializeComponent();
        Loaded += AppearanceSettingsView_Loaded;
        Loaded += (_, _) =>
        {
            ModSetup.AppearanceOptionsChanged += RefreshAppearanceOptions;
            ShellNavigation.VisibilityChanged += RefreshVisibility;
            LauncherBackground.CatalogChanged += RefreshBackgroundOptions;
            ModVideoBack.GamingStateChanged += RefreshVideoState;
            ModVideoBack.ForcePlayChanged += RefreshVideoState;
            ModMusic.LibraryChanged += RefreshMusicOptions;
            RefreshVisibility();
        };
        Unloaded += (_, _) =>
        {
            ModSetup.AppearanceOptionsChanged -= RefreshAppearanceOptions;
            ShellNavigation.VisibilityChanged -= RefreshVisibility;
            LauncherBackground.CatalogChanged -= RefreshBackgroundOptions;
            ModVideoBack.GamingStateChanged -= RefreshVideoState;
            ModVideoBack.ForcePlayChanged -= RefreshVideoState;
            ModMusic.LibraryChanged -= RefreshMusicOptions;
        };
    }

    private void AppearanceSettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();

        ModAnimation.AniBeginControl();
        Reload(); // #4826，在每次进入页面时都刷新一下
        ModAnimation.AniEndControl();

        // 非重复加载部分
        if (isLoaded)
            return;
        isLoaded = true;

        SliderLoad();

        PanLauncherHide.Visibility = Visibility.Visible;
    }

    public void Reload(bool resetInvalid = true)
    {
        try
        {
            // 启动器
            SliderLauncherOpacity.StepValue = Config.Preference.Theme.WindowOpacity;
            CheckLauncherLogo.Checked = Config.Preference.ShowStartupLogo;
            ComboDarkMode.SelectedIndex = (int)Config.Preference.Theme.ColorMode;
            ComboDarkColor.SelectedIndex = Array.IndexOf(CurrentThemeValues, Config.Preference.Theme.DarkColor);
            ComboLightColor.SelectedIndex = Array.IndexOf(CurrentThemeValues, Config.Preference.Theme.LightColor);
            CheckShowLaunchingHint.Checked = Config.Preference.ShowLaunchingHint;

            // 字体设置
            ComboUiFont.SelectedFontTag = Config.Preference.Font;
            ComboUiMotdFont.SelectedFontTag = Config.Preference.MotdFont;

            CheckBlur.Checked = Config.Preference.Blur.IsEnabled;
            SliderBlurValue.StepValue = Config.Preference.Blur.Radius;
            SliderBlurSamplingRate.StepValue = Config.Preference.Blur.SamplingRate;
            ComboBlurType.SelectedIndex = Config.Preference.Blur.KernelType;
            CheckLockWindowSize.Checked = Config.Preference.LockWindowSize;
            CheckDisableMotion.Checked = Config.Preference.DisableAllMotion;
            ComboDockPlacement.SelectedIndex = Math.Clamp(Config.Preference.DockPlacement, 0, 1);

            // 背景图片
            SliderBackgroundOpacity.StepValue = Config.Preference.Background.WallpaperOpacity;
            SliderBackgroundBlur.StepValue = Config.Preference.Background.WallpaperBlurRadius;
            ComboBackgroundSuit.SelectedIndex = Config.Preference.Background.WallpaperSuitMode;
            CheckBackgroundColorful.Checked = Config.Preference.Background.BackgroundColorful;
            var autoPauseVideo = Config.Preference.Background.AutoPauseVideo;
            CheckAutoPauseVideo.Checked = autoPauseVideo;
            LauncherBackground.Refresh(false, false);
            RefreshBackgroundOptions(LauncherBackground.ItemCount);
            RefreshVideoState(null, null);

            // 标题栏
            ((SymbioRadio)FindName("RadioLogoType" + (int)Config.Preference.WindowTitleType))
                .Checked = true;
            TextLogoText.Text = Config.Preference.WindowTitleCustomText;
            CheckLogoLeft.Checked = Config.Preference.TopBarLeftAlign;

            // 背景音乐
            CheckMusicRandom.Checked = Config.Preference.Music.ShufflePlayback;
            CheckMusicAuto.Checked = Config.Preference.Music.StartOnStartup;
            CheckMusicStop.Checked = Config.Preference.Music.StopInGame;
            CheckMusicStart.Checked = Config.Preference.Music.StartInGame;
            CheckMusicSMTC.Checked = Config.Preference.Music.EnableSMTC;
            SliderMusicVolume.StepValue = Config.Preference.Music.Volume;
            RefreshMusicOptions();

            // 主页
            try
            {
                ComboCustomPreset.SelectedIndex = Config.Preference.Homepage.SelectedPreset;
            }
            catch
            {
                Config.Preference.Homepage.SelectedPresetConfig.Reset();
            }

            ((SymbioRadio)FindName("RadioCustomType" + Config.Preference.Homepage.Type)).Checked = true;
            TextCustomNet.Text = Config.Preference.Homepage.CustomUrl;
            RefreshAppearanceOptions();

            // 功能隐藏
            // 获取配置组引用
            var uiHidden = Config.Preference.Hide;

            // 主页面
            CheckHiddenPageDownload.Checked = uiHidden.PageDownload;
            CheckHiddenPageSetup.Checked = uiHidden.PageSetup;
            CheckHiddenPageTools.Checked = uiHidden.PageTools;

            // 子页面 设置
            CheckHiddenSetupLaunch.Checked = uiHidden.SetupLaunch;
            CheckHiddenSetupUI.Checked = uiHidden.SetupUi;
            CheckHiddenSetupLauncherLanguage.Checked = uiHidden.SetupLauncherLanguage;
            CheckHiddenSetupGameManage.Checked = uiHidden.SetupGameManage;
            CheckHiddenSetupJava.Checked = uiHidden.SetupJava;
            CheckHiddenLauncherMisc.Checked = uiHidden.SetupLauncherMisc;
            CheckHiddenSetupAbout.Checked = uiHidden.SetupAbout;
            CheckHiddenSetupFeedback.Checked = uiHidden.SetupFeedback;
            CheckHiddenSetupLog.Checked = uiHidden.SetupLog;

            // 子页面 工具
            CheckHiddenToolsTest.Checked = uiHidden.ToolsTest;

            // 子页面 实例设置
            CheckHiddenVersionEdit.Checked = uiHidden.InstanceEdit;
            CheckHiddenVersionExport.Checked = uiHidden.InstanceExport;
            CheckHiddenVersionSave.Checked = uiHidden.InstanceSave;
            CheckHiddenVersionScreenshot.Checked = uiHidden.InstanceScreenshot;
            CheckHiddenVersionMod.Checked = uiHidden.InstanceMod;
            CheckHiddenVersionResourcePack.Checked = uiHidden.InstanceResourcePack;
            CheckHiddenVersionShader.Checked = uiHidden.InstanceShader;
            CheckHiddenVersionSchematic.Checked = uiHidden.InstanceSchematic;
            CheckHiddenVersionServer.Checked = uiHidden.InstanceServer;

            // 特定功能
            CheckHiddenFunctionSelect.Checked = uiHidden.FunctionSelect;
            CheckHiddenFunctionModUpdate.Checked = uiHidden.FunctionModUpdate;
            CheckHiddenFunctionHidden.Checked = uiHidden.FunctionHidden;
        }
        catch (NullReferenceException ex) when (resetInvalid)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Ui.Error.ConfigReset"),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Ui.Error.ConfigReset"));
            ModSetup.ResetAppearancePreferences();
            Reload(resetInvalid: false);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Ui.Error.LoadFailed"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Setup.Ui.Error.LoadFailed"));
        }
    }

    private void RefreshAppearanceOptions()
    {
        PanBlurValue.Visibility = Config.Preference.Blur.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        var title = ThemeService.CurrentTheme == ColorTheme.HmclBlue ? 4 : (int)Config.Preference.WindowTitleType;
        CheckLogoLeft.Visibility = title == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanLogoText.Visibility = title == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanLogoChange.Visibility = title == 3 ? Visibility.Visible : Visibility.Collapsed;

        var homepage = Config.Preference.Homepage.Type;
        PanCustomPreset.Visibility = homepage == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanCustomLocal.Visibility = homepage == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanCustomNet.Visibility = homepage == 2 ? Visibility.Visible : Visibility.Collapsed;
        HintCustom.Visibility = homepage is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        HintCustomWarn.Visibility = homepage is 1 or 2 && !States.Hint.UntrustedHomepage
            ? Visibility.Visible : Visibility.Collapsed;
        if (homepage is 1 or 2)
            HintCustom.SetResourceReference(SymbioNotice.TextProperty,
                homepage == 1 ? "Setup.Ui.Homepage.LocalFile.Hint" : "Setup.Ui.Homepage.NetUpdate.Hint");
        CustomEventService.SetEventType(HintCustom, homepage == 2 ? EventType.OpenUrl : EventType.None);
        CustomEventService.SetEventData(HintCustom, homepage == 2
            ? "https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/wiki" : "");
    }

    private void RefreshVisibility()
    {
        var visible = ShellNavigation.RevealHidden || !Config.Preference.Hide.FunctionHidden;
        CardSwitch.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LinkVisibility.Visibility = CardSwitch.Visibility;
        HeaderSwitch.SetResourceReference(TextBlock.TextProperty, ShellNavigation.RevealHidden
            ? "Setup.Ui.FeatureHide.TitleTemporarilyDisabled" : "Setup.Ui.FeatureHide.Title");
    }

    private void RefreshVideoState(object? sender, ModVideoBack.BooleanEventArgs? args) =>
        ModBase.RunInUi(() => BtnBackgroundRefresh.IsEnabled = !ModVideoBack.IsGaming || ModVideoBack.ForcePlay);

    private void JumpToSection(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && FindName(name) is FrameworkElement section)
            section.BringIntoView();
    }

    // 将控件改变路由到设置改变
    private void SliderChange(object senderRaw, bool user)
    {
        var sender = (SymbioSlider)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.StepValue);
    }

    private void ComboDockPlacement_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniExitsAnyControl() || ComboDockPlacement.SelectedIndex < 0) return;
        Config.Preference.DockPlacement = ComboDockPlacement.SelectedIndex;
        ModMain.frmMain?.ApplyDockPlacement(ComboDockPlacement.SelectedIndex);
    }

    private void ComboChange(object senderRaw, SelectionChangedEventArgs e)
    {
        var sender = (SymbioSelect)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.SelectedIndex);
    }

    private void CheckBoxChange(object senderRaw, bool user)
    {
        var sender = (SymbioCheck)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.Checked);
    }

    private void TextBoxChange(object senderRaw, RoutedEventArgs e)
    {
        var sender = (SymbioTextField)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.Text);
    }

    private void RadioBoxChange(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (SymbioRadio)senderRaw;
        var gotCfg = sender.Tag?.ToString()?.Split("/") ?? Array.Empty<string>();
        if (!ModAnimation.AniExitsAnyControl() && gotCfg.Length >= 2)
            SetByTag(gotCfg[0], int.Parse(gotCfg[1]));
    }

    private static void SetByTag(string tag, object value)
    {
        ConfigService.TrySetValue(tag, value);
    }

    private void ComboFontChange(object sender, SelectionChangedEventArgs e)
    {
        if (!ModAnimation.AniExitsAnyControl()) Config.Preference.Font = ComboUiFont.SelectedFontTag;
    }

    private void ComboMotdFontChange(object sender, SelectionChangedEventArgs e)
    {
        if (!ModAnimation.AniExitsAnyControl()) Config.Preference.MotdFont = ComboUiMotdFont.SelectedFontTag;
    }

    // 背景图片
    private void BtnUIBgOpen_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModBase.OpenExplorer(ModBase.exePath + @"PCL\Pictures\");
    }

    private void BtnBackgroundRefresh_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        LauncherBackground.Refresh(true, true);
    }

    private void RefreshBackgroundOptions(int count)
    {
        if (count > 0)
        {
            PanBackgroundOpacity.Visibility = Visibility.Visible;
            PanBackgroundBlur.Visibility = Visibility.Visible;
            PanBackgroundSuit.Visibility = Visibility.Visible;
            BtnBackgroundClear.Visibility = Visibility.Visible;
            CheckAutoPauseVideo.Visibility = Visibility.Visible;
            HeaderBackground.Text = Lang.Text("Setup.Ui.Background.TitleWithCount", count);
        }
        else
        {
            PanBackgroundOpacity.Visibility = Visibility.Collapsed;
            PanBackgroundBlur.Visibility = Visibility.Collapsed;
            PanBackgroundSuit.Visibility = Visibility.Collapsed;
            BtnBackgroundClear.Visibility = Visibility.Collapsed;
            CheckAutoPauseVideo.Visibility = Visibility.Collapsed;
            HeaderBackground.Text = Lang.Text("Setup.Ui.Background.TitleDefault");
        }

    }

    private void BtnBackgroundClear_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ModMain.MyMsgBox(Lang.Text("Setup.Ui.Background.Clear.Confirm.Message"),
                Lang.Text("Common.Dialog.Warning"), button2: Lang.Text("Common.Action.Cancel"),
                isWarn: true) == 1)
        {
            ModBase.DeleteDirectory(ModBase.exePath + @"PCL\Pictures");
            LauncherBackground.Refresh(false, true);
            HintService.Hint(Lang.Text("Setup.Ui.Background.Clear.Success"), HintType.Success);
        }
    }

    // 顶部栏
    private void BtnLogoChange_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var fileName = SystemDialogs.SelectFile(
            Lang.Text("Setup.Ui.ImageFile.Filter"),
            Lang.Text("Setup.Ui.ImageFile.SelectTitle"));
        if (string.IsNullOrEmpty(fileName))
            return;
        try
        {
            // 拷贝文件
            File.Delete(ModBase.exePath + @"PCL\Logo.png");
            ModBase.CopyFile(fileName, ModBase.exePath + @"PCL\Logo.png");
            // 设置当前显示
            ModMain.frmMain.LoadBrandImage(ModBase.exePath + @"PCL\Logo.png");
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("参数无效"))
                ModBase.Log(
                    """
                    改变标题栏图片失败，该图片文件可能并非标准格式。
                    你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。
                    """,
                    ModBase.LogLevel.Msgbox,
                    userSummary: Lang.Text("Setup.Ui.Error.TitleImageInvalidFormat"));
            else
                ModBase.Log(
                    ex,
                    "设置标题栏图片失败",
                    ModBase.LogLevel.Msgbox,
                    userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
            ModMain.frmMain.LoadBrandImage(null);
        }
    }

    private void RadioLogoType3_Check(object sender, ModBase.RouteEventArgs e)
    {
        if (!(!ModAnimation.AniExitsAnyControl() && e.raiseByMouse))
            return;
        Refresh: ;

        // 已有图片则不再选择
        if (File.Exists(ModBase.exePath + @"PCL\Logo.png"))
        {
            try
            {
                ModMain.frmMain.LoadBrandImage(ModBase.exePath + @"PCL\Logo.png");
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("参数无效"))
                    ModBase.Log(
                        """
                        调整标题栏图片失败，该图片文件可能并非标准格式。
                        你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。
                        """,
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Setup.Ui.Error.TitleImageResizeInvalidFormat"));
                else
                    ModBase.Log(
                        ex,
                        "调整标题栏图片失败",
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
                ModMain.frmMain.LoadBrandImage(null);
                e.handled = true;
                try
                {
                    File.Delete(ModBase.exePath + @"PCL\Logo.png");
                }
                catch (Exception exx)
                {
                    ModBase.Log(
                        exx,
                        "清理错误的标题栏图片失败",
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
                }
            }

            return;
        }

        // 没有图片则要求选择
        var fileName = SystemDialogs.SelectFile(Lang.Text("Setup.Ui.ImageFile.Filter"), Lang.Text("Setup.Ui.ImageFile.SelectTitle"));
        if (string.IsNullOrEmpty(fileName))
        {
            ModMain.frmMain.LoadBrandImage(null);
            e.handled = true;
        }
        else
        {
            try
            {
                // 拷贝文件
                File.Delete(ModBase.exePath + @"PCL\Logo.png");
                ModBase.CopyFile(fileName, ModBase.exePath + @"PCL\Logo.png");
                goto Refresh;
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    "复制标题栏图片失败",
                    ModBase.LogLevel.Msgbox,
                    userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
            }
        }
    }

    private void BtnLogoDelete_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            File.Delete(ModBase.exePath + @"PCL\Logo.png");
            RadioLogoType1.SetChecked(true, true);
            HintService.Hint(Lang.Text("Setup.Ui.Logo.Clear.Success"), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "清空标题栏图片失败",
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
        }
    }

    // 背景音乐
    private void BtnMusicOpen_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModBase.OpenExplorer(ModBase.exePath + @"PCL\Musics\");
    }

    private void BtnMusicRefresh_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModMusic.MusicRefreshPlay(true);
    }

    private void RefreshMusicOptions()
    {
        if (PanBackgroundOpacity is null)
            return;
        if (ModMusic.musicAllList.Any())
        {
            PanMusicVolume.Visibility = Visibility.Visible;
            PanMusicDetail.Visibility = Visibility.Visible;
            BtnMusicClear.Visibility = Visibility.Visible;
            HeaderMusic.Text = Lang.Text("Setup.Ui.Music.TitleWithCount", ModBase.EnumerateFiles(ModBase.exePath + @"PCL\Musics\").Count());
        }
        else
        {
            PanMusicVolume.Visibility = Visibility.Collapsed;
            PanMusicDetail.Visibility = Visibility.Collapsed;
            BtnMusicClear.Visibility = Visibility.Collapsed;
            HeaderMusic.Text = Lang.Text("Setup.Ui.Music.Title");
        }

    }

    private void BtnMusicClear_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ModMain.MyMsgBox(Lang.Text("Setup.Ui.Music.Clear.Confirm.Message"),
                Lang.Text("Common.Dialog.Warning"), button2: Lang.Text("Common.Action.Cancel"),
                isWarn: true) == 1)
            ModBase.RunInThread(() =>
            {
                HintService.Hint(Lang.Text("Setup.Ui.Music.Deleting"));
                // 停止播放音乐
                ModMusic.musicNAudio = null;
                ModMusic.musicWaitingList = new List<string>();
                ModMusic.musicAllList = new List<string>();
                Thread.Sleep(200);
                // 删除文件
                try
                {
                    ModBase.DeleteDirectory(ModBase.exePath + @"PCL\Musics");
                    // DisableSMTCSupport()
                    HintService.Hint(Lang.Text("Setup.Ui.Music.Delete.Success"), HintType.Success);
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        "删除背景音乐失败",
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
                }

                try
                {
                    Directory.CreateDirectory(ModBase.exePath + @"PCL\Musics");
                    ModBase.RunInUi(() => ModMusic.MusicRefreshPlay(false));
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        "重建背景音乐文件夹失败",
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
                }
            });
    }

    private void CheckMusicStart_Change(object sender, bool user)
    {
        CheckBoxChange(sender, user);
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (CheckMusicStart.Checked == true)
            CheckMusicStop.Checked = false;
    }

    private void CheckMusicStop_Change()
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (CheckMusicStop.Checked == true)
            CheckMusicStart.Checked = false;
    }

    // 主页

    private void BtnCustomRefresh_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        HomepageContent.RequestRefresh();
        HintService.Hint(Lang.Text("Setup.Ui.Homepage.Refresh.Success"), HintType.Success);
    }

    private void BtnCustomTutorial_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModBase.OpenWebsite("https://docs.pclc.cc/ce/customization/xaml-format");
    }

    // 主题
    private void ThemeColor_Change(object senderRaw, SelectionChangedEventArgs e)
    {
        var sender = (SymbioSelect)senderRaw;
        var values = CurrentThemeValues;
        if (sender.SelectedIndex >= 0 && sender.SelectedIndex < values.Length)
            SetByTag(sender.Tag?.ToString(), (int)values[sender.SelectedIndex]);
        ThemeManager.ThemeRefresh();
    }

    // 赞助
    private void BtnLauncherDonate_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModBase.OpenWebsite("https://afdian.com/a/LTCat");
    }

    // 滑动条
    private void SliderLoad()
    {
        SliderMusicVolume.getHintText = new Func<object, object>(v =>
            Lang.Number(Math.Ceiling(Convert.ToDouble(v) * 0.1d) / 100d, "P0"));
        SliderLauncherOpacity.getHintText = new Func<object, object>(v =>
            Lang.Number(Math.Round(40 + Convert.ToDouble(v) * 0.1d) / 100d, "P0"));
        SliderBackgroundOpacity.getHintText = new Func<object, object>(v =>
            Lang.Number(Math.Round(Convert.ToDouble(v) * 0.1d) / 100d, "P0"));
        SliderBackgroundBlur.getHintText = new Func<object, object>(v => Lang.Text("Setup.Ui.Slider.Pixel", Lang.Number(Convert.ToDouble(v), "N0")));
        SliderBlurValue.getHintText = new Func<object, object>(v => Lang.Text("Setup.Ui.Slider.Pixel", Lang.Number(Convert.ToDouble(v), "N0")));
        SliderBlurSamplingRate.getHintText = new Func<object, object>(v => Lang.Number(Convert.ToDouble(v) / 100d, "P0"));
    }

    private void CheckMusicStart_OnChange(object sender, bool user)
    {
        CheckBoxChange(sender, user);
        CheckMusicStart_Change(sender, user);
    }

    private void CheckMusicStop_OnChange(object sender, bool user)
    {
        CheckBoxChange(sender, user);
        CheckMusicStop_Change();
    }

    #region 功能隐藏

    // ================= 设置页面协同 =================
    private void HiddenSetupMain()
    {
        var isChecked = (bool)CheckHiddenPageSetup.Checked;
        CheckHiddenSetupLaunch.Checked = isChecked;
        CheckHiddenSetupUI.Checked = isChecked;
        CheckHiddenSetupLauncherLanguage.Checked = isChecked;
        CheckHiddenSetupGameManage.Checked = isChecked;
        CheckHiddenLauncherMisc.Checked = isChecked;
        CheckHiddenSetupJava.Checked = isChecked;
        CheckHiddenSetupAbout.Checked = isChecked;
        CheckHiddenSetupFeedback.Checked = isChecked;
        CheckHiddenSetupLog.Checked = isChecked;
    }

    // ================= 设置页面协同 =================
    private void HiddenSetupMain(object sender, bool user)
    {
        if (!user)
            return; // 仅处理用户点击，防止死循环
        var isChecked = (bool)CheckHiddenPageSetup.Checked;
        CheckHiddenSetupLaunch.Checked = isChecked;
        CheckHiddenSetupUI.Checked = isChecked;
        CheckHiddenSetupLauncherLanguage.Checked = isChecked;
        CheckHiddenSetupGameManage.Checked = isChecked;
        CheckHiddenLauncherMisc.Checked = isChecked;
        CheckHiddenSetupJava.Checked = isChecked;
        CheckHiddenSetupAbout.Checked = isChecked;
        CheckHiddenSetupFeedback.Checked = isChecked;
        CheckHiddenSetupLog.Checked = isChecked;
    }

    private void HiddenSetupSub(object sender, bool user)
    {
        if (!user)
            return;
        var conf = Config.Preference.Hide;
        // 判断是否全部勾选
        var allChecked = conf.SetupLaunch && conf.SetupUi && conf.SetupLauncherLanguage && conf.SetupJava &&
                         conf.SetupAbout && conf.SetupFeedback &&
                         conf.SetupLog && conf.SetupLauncherMisc && conf.SetupGameManage;
        CheckHiddenPageSetup.Checked = allChecked;
    }

    // ================= 工具页面协同 =================
    private void HiddenToolsMain(object sender, bool user)
    {
        if (!user)
            return;
        var isChecked = (bool)CheckHiddenPageTools.Checked;
        CheckHiddenToolsTest.Checked = isChecked;
    }

    private void HiddenToolsSub(object sender, bool user)
    {
        if (!user)
            return;
        var conf = Config.Preference.Hide;
        var allChecked = conf.ToolsTest;
        CheckHiddenPageTools.Checked = allChecked;
    }

    // 警告提示
    private void HiddenHint(object sender, bool user)
    {
        if (!ModAnimation.AniExitsAnyControl() && sender is SymbioCheck checkBox && checkBox.Checked == true)
            HintService.Hint(Lang.Text("Setup.Ui.FeatureHide.TemporaryHint"));
    }

    #endregion
}
