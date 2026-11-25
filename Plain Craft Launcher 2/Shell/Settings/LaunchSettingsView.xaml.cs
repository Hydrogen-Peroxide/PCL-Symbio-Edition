using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.Utils.OS;
using PCL.Core.App.Localization;

namespace PCL;

public partial class LaunchSettingsView
{
    private readonly DispatcherTimer memoryTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public LaunchSettingsView()
    {
        InitializeComponent();
        memoryTimer.Tick += (_, _) => RefreshRam();
        RectRamGame.SizeChanged += (_, _) => RefreshRamText();
        Loaded += LaunchSettingsView_Loaded;
        Unloaded += (_, _) =>
        {
            memoryTimer.Stop();
            ModSetup.LaunchMemoryChanged -= RamType;
        };
    }

    private void LaunchSettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();
        RefreshRam(false);
        if (ModInstanceList.McMcInstanceSelected is null)
            BtnSwitch.Visibility = Visibility.Collapsed;
        else
            BtnSwitch.Visibility = Visibility.Visible;

        ModAnimation.AniBeginControl();
        try { Reload(); }
        finally { ModAnimation.AniEndControl(); }
        ModSetup.LaunchMemoryChanged += RamType;
        memoryTimer.Start();
    }

    public void Reload(bool resetInvalid = true)
    {
        try
        {
            // 启动参数
            TextArgumentTitle.Text = Config.Launch.Title;
            TextArgumentInfo.Text = Config.Launch.TypeInfo;
            ComboArgumentIndieV2.SelectedIndex = Config.Launch.IndieSolutionV2;
            ComboArgumentVisibie.SelectedIndex = (int)Config.Launch.LauncherVisibility;
            ComboArgumentPriority.SelectedValue = ((int)Config.Launch.ProcessPriority).ToString();
            ComboArgumentWindowType.SelectedIndex = (int)Config.Launch.GameWindowMode;
            TextArgumentWindowWidth.Text = Config.Launch.GameWindowWidth.ToString();
            TextArgumentWindowHeight.Text = Config.Launch.GameWindowHeight.ToString();
            ComboMsAuthType.SelectedIndex = Config.Launch.LoginMsAuthType;
            ComboPreferredIpStack.SelectedIndex = (int)Config.Launch.PreferredIpStack;
            WindowTypeUIRefresh();

            // 游戏内存
            ((SymbioRadio)FindName("RadioRamType" + Config.Launch.MemoryAllocationMode)).Checked = true;
            SliderRamCustom.StepValue = Config.Launch.CustomMemorySize;
            SliderRamInitialCustom.StepValue = Config.Launch.CustomInitialMemorySize;
            RamType(Config.Launch.MemoryAllocationMode);

            // 高级设置
            ComboAdvanceRenderer.SelectedIndex = Config.Launch.Renderer;
            TextAdvanceJvm.Text = Config.Launch.JvmArgs;
            TextAdvanceGame.Text = Config.Launch.GameArgs;
            TextAdvanceRun.Text = Config.Launch.PreLaunchCommand;
            CheckAdvanceRunWait.Checked = Config.Launch.PreLaunchCommandWait;
            CheckAdvanceDisableLF.Checked = Config.Launch.DisableLF;
            CheckAdvanceGraphicCard.Checked = Config.Launch.SetGpuPreference;
            CheckAdvanceNoJavaw.Checked = Config.Launch.NoJavaw;
            CheckAdvanceDisableLwjglUnsafeAgent.Checked = Config.Launch.DisableLwjglUnsafeAgent;
            CheckAdvanceDisableCrashAnalysis.Checked = Config.Launch.DisableCrashAnalysis;
            CheckAdvanceLockMemory.Checked = Config.Launch.LockMemory;
            if (SystemInfo.IsArm64System)
            {
                CheckAdvanceDisableJLW.Checked = true;
                CheckAdvanceDisableJLW.IsEnabled = false;
                CheckAdvanceDisableJLW.ToolTip = Lang.Text("Setup.Launch.Advanced.DisableJlw.Arm64Notice");
            }
            else
            {
                CheckAdvanceDisableJLW.Checked = Config.Launch.DisableJlw;
            }
        }

        catch (NullReferenceException ex) when (resetInvalid)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Launch.Error.ConfigReset"),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Launch.Error.ConfigReset"));
            ModSetup.ResetLaunchPreferences();
            Reload(resetInvalid: false);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.Launch.Error.LoadFailed"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Setup.Launch.Error.LoadFailed"));
        }
    }

    // 将控件改变路由到设置改变
    private void RadioBoxChange(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (SymbioRadio)senderRaw;
        var gotCfg = sender.Tag?.ToString()?.Split("/") ?? Array.Empty<string>();
        if (!ModAnimation.AniExitsAnyControl() && gotCfg.Length >= 2)
            SetByTag(gotCfg[0], int.Parse(gotCfg[1]));
    }

    private void TextBoxChange(object senderRaw, RoutedEventArgs e)
    {
        var sender = (SymbioTextField)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.Text);
    }

    private void TextArgumentTitle_OnTextChanged(object senderRaw, TextChangedEventArgs e)
    {
        var sender = (SymbioTextField)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.Text);
    }

    private void SliderChange(object senderRaw, bool user)
    {
        var sender = (SymbioSlider)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.StepValue);
    }

    private void ComboChange(object senderRaw, SelectionChangedEventArgs e)
    {
        var sender = (SymbioSelect)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
        {
            var senderTag = sender.Tag?.ToString();
            SetByTag(senderTag,
                senderTag == "LaunchArgumentPriority" ? Convert.ToInt32(sender.SelectedValue) : sender.SelectedIndex);
            if (senderTag == "LaunchArgumentWindowType") WindowTypeUIRefresh();
        }
    }

    private void CheckBoxChange(object senderRaw, bool user)
    {
        var sender = (SymbioCheck)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.Checked);
        // 锁定内存状态变化时同步刷新初始内存滑块的可用状态
        if (ReferenceEquals(sender, CheckAdvanceLockMemory))
            RamType(Config.Launch.MemoryAllocationMode);
    }

    private static void SetByTag(string tag, object value)
        => ConfigService.TrySetValue(tag, value);

    // 切换到实例独立设置
    private void BtnSwitch_Click(object sender, RoutedEventArgs e)
    {
        ModInstanceList.McMcInstanceSelected.Load();
        InstanceWorkspace.Current = ModInstanceList.McMcInstanceSelected;
        ModMain.frmMain.PageChange(FormMain.PageType.InstanceSetup, FormMain.PageSubType.VersionSetup);
    }

    private void ComboAdvanceRenderer_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboChange(sender, e);
        ComboAdvanceRenderer_SelectionChanged((SymbioSelect)sender, e);
    }

    private void ComboArgumentIndie_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboChange(sender, e);
        ComboArgumentIndie_SelectionChanged(sender, e);
    }

    #region 游戏内存

    public void RamType(int type)
    {
        if (SliderRamCustom is null)
            return;
        SliderRamCustom.IsEnabled = type == 1;
        // 锁定内存时 -Xms 恒等于 -Xmx，初始内存设置不生效
        SliderRamInitialCustom.IsEnabled = type == 1 && !Config.Launch.LockMemory;
    }

    /// <summary>
    ///     刷新 UI 上的 RAM 显示。
    /// </summary>
    public void RefreshRam(bool showAnim)
    {
        if (LabRamGame is null || LabRamUsed is null || ModMain.frmMain.pageCurrent != FormMain.PageType.Setup ||
            ModMain.frmSetupLeft.pageID != FormMain.PageSubType.SetupLaunch)
            return;
        // 获取内存情况
        var ramGame = Math.Round(JavaMemoryAllocation.GetRam(ModInstanceList.McMcInstanceSelected, false), 5);
        var phyRam = KernelInterop.GetPhysicalMemoryBytes();
        var ramTotal = Math.Round((double)phyRam.Total / 1024 / 1024 / 1024, 1);
        var ramAvailable = Math.Round((double)phyRam.Available / 1024 / 1024 / 1024, 1);
        var ramGameActual = Math.Round(Math.Min(ramGame, ramAvailable), 5);
        var ramUsed = Math.Round(ramTotal - ramAvailable, 5);
        var ramEmpty = Math.Round(ModBase.MathClamp(ramTotal - ramUsed - ramGame, 0d, 1000d), 1);
        // 设置最大可用内存
        if (ramTotal <= 1.5d)
            SliderRamCustom.MaxValue = (int)Math.Round(Math.Max(Math.Floor((ramTotal - 0.3d) / 0.1d), 1d));
        else if (ramTotal <= 8d)
            SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((ramTotal - 1.5d) / 0.5d) + 12d);
        else if (ramTotal <= 16d)
            SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((ramTotal - 8d) / 1d) + 25d);
        else
            SliderRamCustom.MaxValue = (int)Math.Round(Math.Floor((ramTotal - 16d) / 2d) + 33d);
        // 设置文本
        var ramInitial = JavaMemoryAllocation.GetInitialRam(ModInstanceList.McMcInstanceSelected, false);
        string suffixText;
        if (ramInitial.HasValue && ramGame != ramGameActual)
            suffixText = Lang.Text("Setup.Launch.Memory.SuffixBoth", Lang.Number(ramInitial.Value, "N1"),
                Lang.Number(ramGameActual, "N1"));
        else if (ramInitial.HasValue)
            suffixText = Lang.Text("Setup.Launch.Memory.SuffixInitial", Lang.Number(ramInitial.Value, "N1"));
        else if (ramGame != ramGameActual)
            suffixText = Lang.Text("Setup.Launch.Memory.SuffixAvailable", Lang.Number(ramGameActual, "N1"));
        else
            suffixText = "";
        LabRamGame.Text = $"{Lang.Number(ramGame, "N1")} GiB{(suffixText.Length > 0 ? $" ({suffixText})" : "")}";
        LabRamUsed.Text = $"{Lang.Number(ramUsed, "N1")} GiB";
        LabRamTotal.Text = $" / {Lang.Number(ramTotal, "N1")} GiB";
        LabRamWarn.Visibility =
            ramGame == 1d && !ModJava.IsGameSet64BitJava() && !SystemInfo.Is32BitSystem && ModJava.Javas.ExistAnyJava()
                ? Visibility.Visible
                : Visibility.Collapsed;
        HintRamTooHigh.Visibility = ramGame / ramTotal > 0.75d ? Visibility.Visible : Visibility.Collapsed;
        void SetWidth(ColumnDefinition column, string channel, double value) =>
            SymbioMotion.Spring(this, channel, () => column.Width.Value,
                v => column.Width = new GridLength(Math.Max(0, v), GridUnitType.Star), value,
                SymbioMotion.Exit, immediate: !showAnim);

        SetWidth(ColumnRamUsed, "ramUsed", ramUsed);
        SetWidth(ColumnRamGame, "ramGame", ramGameActual);
        SetWidth(ColumnRamEmpty, "ramFree", ramEmpty);
    }

    private void RefreshRam()
    {
        RefreshRam(true);
    }

    private int ramTextLeft = 2;

    /// <summary>
    ///     刷新 UI 上的文本位置。
    /// </summary>
    private void RefreshRamText()
    {
        // 获取宽度信息
        var rectUsedWidth = RectRamUsed.ActualWidth;
        var totalWidth = PanRamDisplay.ActualWidth;
        var labGameWidth = LabRamGame.ActualWidth;
        var labUsedWidth = LabRamUsed.ActualWidth;
        var labTotalWidth = LabRamTotal.ActualWidth;
        var labGameTitleWidth = LabRamGameTitle.ActualWidth;
        var labUsedTitleWidth = LabRamUsedTitle.ActualWidth;
        // 左侧
        int left;
        if (rectUsedWidth - 30d < labUsedWidth || rectUsedWidth - 30d < labUsedTitleWidth)
            // 全写不下了
            left = 0;
        else if (rectUsedWidth - 25d < labUsedWidth + labTotalWidth)
            // 显示不下完整数据
            left = 1;
        else
            // 正常
            left = 2;
        if (ramTextLeft != left)
        {
            ramTextLeft = left;
            SymbioMotion.Fade(LabRamUsed, left > 0 ? 1 : 0);
            SymbioMotion.Fade(LabRamTotal, left == 2 ? 1 : 0);
            SymbioMotion.Fade(LabRamUsedTitle, left > 0 ? .7 : 0);
        }

        // 右侧
        int right;
        if (totalWidth < labGameWidth + 2d + rectUsedWidth || totalWidth < labGameTitleWidth + 2d + rectUsedWidth)
            // 挤到最右边
            right = 0;
        else
            // 正常情况
            right = 1;
        var gameLeft = right == 0 ? totalWidth - labGameWidth : 2 + rectUsedWidth;
        var titleLeft = right == 0 ? totalWidth - labGameTitleWidth : 2 + rectUsedWidth;
        SymbioMotion.SpringMarginLeft(LabRamGame, gameLeft, SymbioMotion.Exit);
        SymbioMotion.SpringMarginLeft(LabRamGameTitle, titleLeft, SymbioMotion.Exit);
    }

    #endregion

    #region 其他选项

    private void WindowTypeUIRefresh()
    {
        if (ComboArgumentWindowType is null)
            return;
        if (ComboArgumentWindowType.SelectedIndex == 3 && LabArgumentWindowMiddle is not null &&
            LabArgumentWindowMiddle.Visibility == Visibility.Collapsed)
        {
            LabArgumentWindowMiddle.Visibility = Visibility.Visible;
            TextArgumentWindowHeight.Visibility = Visibility.Visible;
            TextArgumentWindowWidth.Visibility = Visibility.Visible;
        }
        else if (ComboArgumentWindowType.SelectedIndex != 3 && LabArgumentWindowMiddle is not null &&
                 LabArgumentWindowMiddle.Visibility == Visibility.Visible)
        {
            LabArgumentWindowMiddle.Visibility = Visibility.Collapsed;
            TextArgumentWindowHeight.Visibility = Visibility.Collapsed;
            TextArgumentWindowWidth.Visibility = Visibility.Collapsed;
        }
    }

    // 可见性选择直接关闭的警告
    private void ComboArgumentVisibie_SelectionChanged(object sender, SelectionChangedEventArgs sizeChangedEventArgs)
    {
        ComboChange(sender, sizeChangedEventArgs);
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (ComboArgumentVisibie.SelectedIndex == 0)
            if (ModMain.MyMsgBox(
                    Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Message"),
                    Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Title"),
                    Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Continue"),
                    Lang.Text("Common.Action.Cancel")) == 2)
                ComboArgumentVisibie.SelectedItem = sizeChangedEventArgs.RemovedItems[0];
    }

    // 实例隔离提示
    private void ComboArgumentIndie_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        ModMain.MyMsgBox(Lang.Text("Setup.Launch.Options.InstanceIsolation.DefaultPolicyHint"));
    }

    #endregion

    #region 高级设置

    private void TextAdvanceRun_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckAdvanceRunWait.Visibility =
            string.IsNullOrEmpty(TextAdvanceRun.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // JVM 参数重设
    private void TextAdvanceJvm_TextChanged(object sender, TextChangedEventArgs e)
    {
        BtnAdvanceJvmReset.Visibility =
            TextAdvanceJvm.Text == Config.Launch.JvmArgsConfig.DefaultValue
                ? Visibility.Hidden
                : Visibility.Visible;
    }

    private void BtnAdvanceJvmReset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Config.Launch.JvmArgsConfig.Reset();
        Reload();
    }

    private void ComboAdvanceRenderer_SelectionChanged(SymbioSelect sender, object e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (!States.Hint.Renderer && ComboAdvanceRenderer.SelectedIndex != 0)
        {
            if (ModMain.MyMsgBox(Lang.Text("Setup.Launch.Advanced.Renderer.Warning.Message"),
                    Lang.Text("Common.Dialog.Warning"),
                    Lang.Text("Setup.Launch.Advanced.Renderer.Warning.Confirm"),
                    Lang.Text("Common.Action.Cancel"), isWarn: true) == 2)
            {
                ComboAdvanceRenderer.SelectedItem = ((SelectionChangedEventArgs)e).RemovedItems[0];
            }
            else
            {
                Config.Launch.Renderer = sender.SelectedIndex;
                States.Hint.Renderer = true;
            }
        }
        else
        {
            Config.Launch.Renderer = sender.SelectedIndex;
        }
    }

    #endregion
}
