using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.IO;
using PCL.Core.Minecraft;
using PCL.Core.Minecraft.Java.UserPreference;
using PCL.Core.UI;
using PCL.Core.Utils.OS;
using PCL.Core.App.Localization;
using PCL.Core.Utils;

namespace PCL;

public partial class InstanceSettingsView
{
    private readonly DispatcherTimer memoryTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public InstanceSettingsView()
    {     
        Loaded += PageSetupSystem_Loaded;
        InitializeComponent();
        RectRamGame.SizeChanged += (_, _) => RefreshRamText();
        memoryTimer.Tick += (_, _) => RefreshRam();
        Unloaded += (_, _) =>
        {
            memoryTimer.Stop();
            ModSetup.InstanceMemoryChanged -= InstanceMemoryChanged;
        };

        ComboArgumentIndieV2.SelectionChanged += ComboArgumentIndieV2_SelectionChanged;
        TextArgumentTitle.TextChanged += TextArgumentTitle_TextChanged;
        TextArgumentInfo.TextChanged += TextBoxChange;
        ComboArgumentJava.SelectionChanged += JavaSelectionUpdate;

        RadioRamType2.Check += RadioBoxChange;
        RadioRamType0.Check += RadioBoxChange;
        RadioRamType1.Check += RadioBoxChange;
        SliderRamCustom.Change += SliderChange;
        SliderRamInitialCustom.Change += SliderChange;

        ComboServerLoginRequire.SelectionChanged += ComboServerLogin_Changed;
        TextServerAuthServer.TextChanged += TextBoxChange;
        TextServerAuthServer.LostFocus += TextServerAuthServer_MouseLeave;
        TextServerAuthRegister.TextChanged += TextBoxChange;
        TextServerAuthName.TextChanged += TextBoxChange;
        TextServerEnter.TextChanged += TextBoxChange;
        BtnServerAuthLittle.Click += BtnServerAuthLittle_Click;
        BtnServerAuthLock.Click += BtnServerAuthLock_Click;
        BtnServerNewProfile.Click += BtnServerNewProfile_Click;

        ComboAdvanceRenderer.SelectionChanged += ComboAdvanceRenderer_SelectionChanged;
        TextAdvanceJvm.TextChanged += TextBoxChange;
        TextAdvanceGame.TextChanged += TextBoxChange;
        TextAdvanceClasspathHead.TextChanged += TextBoxChange;
        TextAdvanceRun.TextChanged += TextAdvanceRun_TextChanged;
        CheckAdvanceRunWait.Change += CheckBoxChange;
        CheckAdvanceJava.Change += CheckBoxChange;
        CheckAdvanceAssetsV2.Change += CheckBoxChange;
        CheckAdvanceUseProxyV2.Change += CheckBoxChange;
        CheckAdvanceDisableJLW.Change += CheckBoxChange;
        CheckAdvanceDisableLF.Change += CheckBoxChange;
        CheckUseDebugLog4j2Config.Change += CheckUseDebugLog4j2Config_CheckChanged;
        CheckAdvanceDisableLwjglUnsafeAgent.Change += CheckBoxChange;

        BtnSwitch.Click += BtnSwitch_Click;
        
        TextServerEnter.TextChanged += TextServerEnter_Change;
        ComboArgumentJava.DropDownOpened += ComboArgumentJava_DropDownOpened;
        CheckArgumentTitleEmpty.Change += CheckArgumentTitleEmpty_Change;
    }

    private void PageSetupSystem_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();
        RefreshRam(false);

        // 由于各个实例不同，每次都需要重新加载
        ModAnimation.AniBeginControl();
        Reload();
        ModAnimation.AniEndControl();

        ModSetup.InstanceMemoryChanged += InstanceMemoryChanged;
        memoryTimer.Start();
    }

    private void InstanceMemoryChanged(string path, int type)
    {
        if (string.Equals(path, InstanceWorkspace.Current?.PathInstance, StringComparison.OrdinalIgnoreCase))
            RamType(type);
    }

    public void Reload()
    {
        try
        {
            // 启动参数
            TextArgumentTitle.Text = Config.Instance.Title[InstanceWorkspace.Current.PathInstance];
            CheckArgumentTitleEmpty.Checked = Config.Instance.UseGlobalTitle[InstanceWorkspace.Current.PathInstance];
            TextArgumentInfo.Text = Config.Instance.TypeInfo[InstanceWorkspace.Current.PathInstance];
            var _unused = InstanceWorkspace.Current.PathIndie; // 触发自动判定
            ComboArgumentIndieV2.SelectedIndex = Config.Instance.IndieV2[InstanceWorkspace.Current.PathInstance] ? 0 : 1;
            CheckArgumentTitleEmpty.Visibility = TextArgumentTitle.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            TextArgumentTitle.HintText = CheckArgumentTitleEmpty.Checked == true ? Lang.Text("Common.Option.Default") : Lang.Text("Instance.Setup.FollowGlobal");
            RefreshJavaComboBox();

            // 游戏内存
            var ramType = Config.Instance.MemorySolution[InstanceWorkspace.Current.PathInstance];
            ((SymbioRadio)FindName("RadioRamType" + ramType)).Checked = true;
            SliderRamCustom.StepValue = Config.Instance.CustomMemorySize[InstanceWorkspace.Current.PathInstance];
            SliderRamInitialCustom.StepValue = Config.Instance.CustomInitialMemorySize[InstanceWorkspace.Current.PathInstance];
            RamType(ramType);

            // 服务器
            TextServerEnter.Text = Config.Instance.ServerToEnter[InstanceWorkspace.Current.PathInstance];
            ComboServerLoginRequire.SelectedIndex = Config.InstanceAuth.LoginRequirementSolution[InstanceWorkspace.Current.PathInstance];
            comboServerLoginLast = ComboServerLoginRequire.SelectedIndex;
            TextServerAuthServer.Text = Config.InstanceAuth.AuthServerAddress[InstanceWorkspace.Current.PathInstance];
            TextServerAuthName.Text = Config.InstanceAuth.AuthServerDisplayName[InstanceWorkspace.Current.PathInstance];
            TextServerAuthRegister.Text = Config.InstanceAuth.AuthRegisterAddress[InstanceWorkspace.Current.PathInstance];
            ServerLogin(ComboServerLoginRequire.SelectedIndex);

            // 高级设置
            ComboAdvanceRenderer.SelectedIndex = Config.Instance.Renderer[InstanceWorkspace.Current.PathInstance];
            TextAdvanceClasspathHead.Text = Config.Instance.ClasspathHead[InstanceWorkspace.Current.PathInstance];
            TextAdvanceJvm.Text = Config.Instance.JvmArgs[InstanceWorkspace.Current.PathInstance];
            TextAdvanceGame.Text = Config.Instance.GameArgs[InstanceWorkspace.Current.PathInstance];
            TextAdvanceRun.Text = Config.Instance.PreLaunchCommand[InstanceWorkspace.Current.PathInstance];
            CheckAdvanceRunWait.Checked = Config.Instance.PreLaunchCommandWait[InstanceWorkspace.Current.PathInstance];
            CheckAdvanceDisableLwjglUnsafeAgent.Checked = Config.Instance.DisableLwjglUnsafeAgent[InstanceWorkspace.Current.PathInstance];
            if (Config.Instance.AssetVerifySolutionV1[InstanceWorkspace.Current.PathInstance] == 2)
            {
                ModBase.Log("[Setup] 已迁移老版本的关闭文件校验设置");
                Config.Instance.AssetVerifySolutionV1Config.Reset(InstanceWorkspace.Current.PathInstance);
                Config.Instance.DisableAssetVerifyV2[InstanceWorkspace.Current.PathInstance] = true;
            }

            CheckAdvanceAssetsV2.Checked = Config.Instance.DisableAssetVerifyV2[InstanceWorkspace.Current.PathInstance];
            CheckAdvanceUseProxyV2.Checked = Config.Instance.UseProxy[InstanceWorkspace.Current.PathInstance];
            CheckAdvanceJava.Checked = Config.Instance.IgnoreJavaCompatibility[InstanceWorkspace.Current.PathInstance];
            if (SystemInfo.IsArm64System)
            {
                CheckAdvanceDisableJLW.Checked = true;
                CheckAdvanceDisableJLW.IsEnabled = false;
                CheckAdvanceDisableJLW.ToolTip = Lang.Text("Setup.Launch.Advanced.DisableJlw.Arm64ToolTip");
            }
            else
            {
                CheckAdvanceDisableJLW.Checked = Config.Instance.DisableJlw[InstanceWorkspace.Current.PathInstance];
            }
            CheckUseDebugLog4j2Config.Checked = Config.Instance.UseDebugLof4j2Config[InstanceWorkspace.Current.PathInstance];
            CheckAdvanceDisableLF.Checked = Config.Instance.DisableLF[InstanceWorkspace.Current.PathInstance];
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "重载实例独立设置时出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Setup.Error.OperationFailed"));
        }
    }

    // 将控件改变路由到设置改变
    private static void SetByTag(string tag, object value)
        => ConfigService.TrySetValue(tag, value, InstanceWorkspace.Current.PathInstance);

    private void RadioBoxChange(object o, ModBase.RouteEventArgs routeEventArgs)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (o is not SymbioRadio { Tag: string tag }) return;

        var slash = tag.IndexOf('/');
        if (slash < 0) return;

        SetByTag(tag[..slash], int.Parse(tag[(slash + 1)..]));
    }

    private void TextBoxChange(object o, TextChangedEventArgs textChangedEventArgs)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (o is not SymbioTextField textBox) return;

        SetByTag(textBox.Tag?.ToString(), textBox.Text);
    }

    private void SliderChange(object o, bool user)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (o is not SymbioSlider slider) return;

        SetByTag(slider.Tag?.ToString(), slider.StepValue);
    }

    private void ComboChange(SymbioSelect sender, object e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;

        SetByTag(sender.Tag?.ToString(), sender.SelectedIndex);
    }

    private void CheckBoxChange(object sender, bool user)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (sender is not SymbioCheck checkBox) return;

        SetByTag(checkBox.Tag?.ToString(), checkBox.Checked.GetValueOrDefault());
    }

    // 切换到全局设置
    private void BtnSwitch_Click(object sender, RoutedEventArgs e)
    {
        ModMain.frmMain.OpenLaunchSettings();
    }

    #region 游戏内存
    public void RamType(int type)
    {
        if (SliderRamCustom is null)
            return;
        SliderRamCustom.IsEnabled = type == 1;
        // 锁定内存为全局配置，启用时 -Xms 恒等于 -Xmx，初始内存设置不生效
        SliderRamInitialCustom.IsEnabled = type == 1 && !Config.Launch.LockMemory;
    }

    /// <summary>
    ///     刷新 UI 上的 RAM 显示。
    /// </summary>
    public void RefreshRam(bool showAnim)
    {
        if (LabRamGame is null || LabRamUsed is null ||
            ModMain.frmMain.pageCurrent != FormMain.PageType.InstanceSetup ||
            ModMain.frmInstanceLeft.pageID != FormMain.PageSubType.VersionSetup)
            return;
        // 获取内存情况
        var ramGame = Math.Round(JavaMemoryAllocation.GetInstanceRam(InstanceWorkspace.Current), 5);
        var phyRam = KernelInterop.GetPhysicalMemoryBytes();
        var ramTotal = Math.Round((double)(phyRam.Total / 1024 / 1024 / 1024), 1);
        var ramAvailable = Math.Round((double)(phyRam.Available / 1024 / 1024 / 1024), 1);
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
        var ramInitial = JavaMemoryAllocation.GetInstanceInitialRam(InstanceWorkspace.Current);
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
            ramGame == 1d && !ModJava.IsGameSet64BitJava(InstanceWorkspace.Current) && !SystemInfo.Is32BitSystem &&
            ModJava.Javas.ExistAnyJava()
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

    #region 服务器

    // 全局
    private int comboServerLoginLast;

    private void ComboServerLogin_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        ServerLogin(ComboServerLoginRequire.SelectedIndex);
        if (TextServerAuthServer.IsValidated)
            BtnServerAuthLock.IsEnabled = true;
        else
            BtnServerAuthLock.IsEnabled = false;
        if ((ComboServerLoginRequire.SelectedIndex == 2 || ComboServerLoginRequire.SelectedIndex == 3) &&
            !TextServerAuthServer.IsValidated)
            return;
        if (comboServerLoginLast == ComboServerLoginRequire.SelectedIndex)
            return;
        comboServerLoginLast = ComboServerLoginRequire.SelectedIndex;
        Config.InstanceAuth.LoginRequirementSolution[InstanceWorkspace.Current.PathInstance] = ComboServerLoginRequire.SelectedIndex;
    }

    private void TextServerAuthServer_MouseLeave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextServerAuthServer.Text))
            return;
        if (!(TextServerAuthServer.Text.EndsWithF("/api/yggdrasil/") ||
              TextServerAuthServer.Text.EndsWithF("/api/yggdrasil")))
        {
            if (TextServerAuthServer.Text.EndsWithF("/"))
            {
                TextServerAuthServer.Text = $"{TextServerAuthServer.Text}api/yggdrasil";
                HintService.Hint(Lang.Text("Instance.Setup.Server.AuthServer.AutoFormatted"));
            }
            else
            {
                TextServerAuthServer.Text = $"{TextServerAuthServer.Text}/api/yggdrasil";
                HintService.Hint(Lang.Text("Instance.Setup.Server.AuthServer.AutoFormatted"));
            }
        }

        if (TextServerAuthServer.Text.EndsWithF("/api/yggdrasil/"))
        {
            TextServerAuthServer.Text = TextServerAuthServer.Text.BeforeLast("/");
            HintService.Hint(Lang.Text("Instance.Setup.Server.AuthServer.AutoFormatted"));
        }

        comboServerLoginLast = ComboServerLoginRequire.SelectedIndex;
        ComboChange(ComboServerLoginRequire, null);
    }

    public void ServerLogin(int type)
    {
        LabServerAuthName.Visibility = type == 2 || type == 3 ? Visibility.Visible : Visibility.Collapsed;
        TextServerAuthName.Visibility = type == 2 || type == 3 ? Visibility.Visible : Visibility.Collapsed;
        LabServerAuthRegister.Visibility = type == 2 || type == 3 ? Visibility.Visible : Visibility.Collapsed;
        TextServerAuthRegister.Visibility = type == 2 || type == 3 ? Visibility.Visible : Visibility.Collapsed;
        LabServerAuthServer.Visibility = type == 2 || type == 3 ? Visibility.Visible : Visibility.Collapsed;
        TextServerAuthServer.Visibility = type == 2 || type == 3 ? Visibility.Visible : Visibility.Collapsed;
        BtnServerAuthLittle.Visibility = type == 2 || type == 3 ? Visibility.Visible : Visibility.Collapsed;
        BtnServerNewProfile.Visibility = type == 2 || type == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (type == 0 || type == 1)
            BtnServerAuthLock.Visibility = Visibility.Collapsed;
        else
            BtnServerAuthLock.Visibility = Visibility.Visible;
        if (Config.InstanceAuth.AuthLocked[InstanceWorkspace.Current.PathInstance])
        {
            HintServerLoginLock.Visibility = Visibility.Visible;
            ComboServerLoginRequire.IsEnabled = false;
            TextServerAuthServer.IsEnabled = false;
            TextServerAuthName.IsEnabled = false;
            TextServerAuthRegister.IsEnabled = false;
            BtnServerAuthLittle.IsEnabled = false;
        }
        else
        {
            HintServerLoginLock.Visibility = Visibility.Collapsed;
            ComboServerLoginRequire.IsEnabled = true;
            TextServerAuthServer.IsEnabled = true;
            TextServerAuthName.IsEnabled = true;
            TextServerAuthRegister.IsEnabled = true;
            BtnServerAuthLittle.IsEnabled = true;
        }

        CardServer.TriggerForceResize();
        // 避免正版验证和离线验证出现此提示
        if (type != 2 && type != 3)
        {
            LabServerAuthServerSecurity.Visibility = Visibility.Collapsed;
            LabServerAuthServerSecurityVerify.Visibility = Visibility.Collapsed;
        }
        // 如果开头为 http:// 给予警告
        else if (TextServerAuthServer.Text.StartsWithF("https://"))
        {
            LabServerAuthServerSecurity.Visibility = Visibility.Collapsed;
            LabServerAuthServerSecurityVerify.Visibility = Visibility.Visible;
        }
        else if (TextServerAuthServer.Text.StartsWithF("http://"))
        {
            LabServerAuthServerSecurity.Visibility = Visibility.Visible;
            LabServerAuthServerSecurityVerify.Visibility = Visibility.Collapsed;
        }
        else
        {
            LabServerAuthServerSecurity.Visibility = Visibility.Collapsed;
            LabServerAuthServerSecurityVerify.Visibility = Visibility.Collapsed;
        }
    }

    // LittleSkin
    private void BtnServerAuthLittle_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TextServerAuthServer.Text) &&
        TextServerAuthServer.Text != "https://littleskin.cn/api/yggdrasil" && ModMain.MyMsgBox(
        Lang.Text("Instance.Setup.Server.LittleSkin.Override.Message"),
        Lang.Text("Instance.Setup.Server.LittleSkin.Override.Title"), Lang.Text("Instance.Setup.Server.LittleSkin.Override.Continue"), Lang.Text("Common.Action.Cancel")) == 2)
            return;
        TextServerAuthServer.Text = "https://littleskin.cn/api/yggdrasil";
        TextServerAuthRegister.Text = "https://littleskin.cn/auth/register";
        TextServerAuthName.Text = Lang.Text("Instance.Setup.Server.LittleSkin.Name");
    }

    // 锁定设置
    private void BtnServerAuthLock_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ModMain.MyMsgBox(
                Lang.Text("Instance.Setup.Server.LockLoginMethod.Message"),
                Lang.Text("Instance.Setup.Server.LockLoginMethod.Title"), Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel"), isWarn: true) == 1)
        {
            Config.InstanceAuth.AuthLocked[InstanceWorkspace.Current.PathInstance] = true;
            Reload();
        }
    }

    // 跳转新建档案
    private void BtnServerNewProfile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModMain.frmMain.ShowAccountLogin(ModLaunch.McLoginType.Auth, TextServerAuthServer.Text);
    }

    private static void TextServerEnter_Change(object sender, TextChangedEventArgs e)
    {
        if (sender is SymbioTextField textBox) textBox.Text = textBox.Text.Replace("：", ":");
    }

    #endregion

    #region Java 选择

    // 刷新 Java 下拉框显示
    public void RefreshJavaComboBox()
    {
        if (ComboArgumentJava is null)
            return;

        // 获取实例的 Java 偏好（已兼容新旧格式）
        var preference = ModJava.GetInstanceJavaPreference(InstanceWorkspace.Current);

        // === 1. 初始化固定选项（使用类型安全的 Tag） ===
        ComboArgumentJava.Items.Clear();

        // 选项 0: 跟随全局设置
        ComboArgumentJava.Items.Add(new SelectOption
        {
            Content = Lang.Text("Instance.Setup.FollowGlobal"),
            Tag = new UseGlobalPreference()
        });

        // 选项 1: 自动选择
        ComboArgumentJava.Items.Add(new SelectOption
        {
            Content = Lang.Text("Instance.Setup.Options.Java.AutoSelect"),
            Tag = new AutoSelect() // Nothing 表示自动选择
        });

        // 选项 2: 相对路径选项
        SelectOption relativePathItem;
        if (preference is UseRelativePath)
        {
            var relPref = (UseRelativePath)preference;
            var absPath = Path.GetFullPath(Path.Combine(Basics.ExecutableDirectory, relPref.RelativePath));
            var javaEntry = ModJava.Javas.Get(absPath);

            if (Files.IsPathWithinDirectory(absPath, Basics.ExecutableDirectory) && javaEntry is not null &&
                javaEntry.IsEnabled)
                // 有效路径：显示具体 Java 信息
                relativePathItem = new SelectOption
                {
                    Content = Lang.Text("Instance.Setup.Options.Java.SelectRelative.WithJava", javaEntry.ToString()),
                    Tag = new UseRelativePath(relPref.RelativePath),
                    ToolTip = Lang.Text("Instance.Setup.Options.Java.RelativePathToolTip", relPref.RelativePath, absPath)
                };
            else
                // 无效路径：提示用户重新选择
                relativePathItem = new SelectOption
                {
                    Content = Lang.Text("Instance.Setup.Options.Java.SelectRelative.Invalid"),
                    Tag = new UseRelativePath(relPref.RelativePath),
                    ToolTip = Lang.Text("Instance.Setup.Options.Java.InvalidPathToolTip", absPath)
                };
        }
        else
        {
            // 未配置相对路径：使用默认模板
            relativePathItem = new SelectOption
            {
                Content = Lang.Text("Instance.Setup.Options.Java.SelectRelative"),
                Tag = new UseRelativePath(@"jre\bin\java.exe"),
                ToolTip = Lang.Text("Instance.Setup.Options.Java.SelectRelativeToolTip")
            };
        }

        ComboArgumentJava.Items.Add(relativePathItem);

        // === 2. 添加所有可用 Java 运行时 ===
        SelectOption selectedItem = null;
        try
        {
            foreach (var curJava in ModJava.Javas.GetSortedJavaList())
            {
                var item = new SelectOption
                {
                    Content = curJava.ToString(),
                    ToolTip =
                        Lang.Text("Instance.Setup.Options.Java.Details.ToolTip", curJava.Installation.JavaExePath, curJava.Installation.Version, curJava.Source),
                    Tag = curJava
                };
                ToolTipService.SetInitialShowDelay(item, 300);
                ToolTipService.SetBetweenShowDelay(item, 100);
                ComboArgumentJava.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            Config.Instance.SelectedJava[InstanceWorkspace.Current.PathInstance] = "使用全局设置";
            ModBase.Log(
                ex,
                "更新实例设置 Java 下拉框失败",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Setup.Error.OperationFailed"));
            ComboArgumentJava.Items.Clear();
            ComboArgumentJava.Items.Add(new SelectOption
            {
                Content = Lang.Text("Instance.Setup.Options.Java.LoadFailed"),
                IsEnabled = false
            });
            ComboArgumentJava.SelectedIndex = 0;
            RefreshRam(true);
            return;
        }

        // === 3. 根据当前偏好设置选中项（优先使用新格式 preference） ===
        if (preference is null)
        {
            // 自动选择
            selectedItem = ComboArgumentJava.Items[1] as SelectOption;
        }
        else if (preference is UseGlobalPreference)
        {
            selectedItem = ComboArgumentJava.Items[0] as SelectOption;
        }
        else if (preference is UseRelativePath)
        {
            selectedItem = ComboArgumentJava.Items[2] as SelectOption;
        }
        else if (preference is ExistingJava)
        {
            var existPref = (ExistingJava)preference;
            // 在 Java 列表中查找匹配项（从索引 3 开始）
            for (int i = 3, loopTo = ComboArgumentJava.Items.Count - 1; i <= loopTo; i++)
            {
                var item = ComboArgumentJava.Items[i] as SelectOption;
                if (item is not null && item.Tag is JavaEntry)
                {
                    var javaEntry = (JavaEntry)item.Tag;
                    if (string.Equals(javaEntry.Installation.JavaExePath, existPref.JavaExePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        selectedItem = item;
                        break;
                    }
                }
            }
        }

        // 降级处理：无匹配项时回退到自动选择
        if (selectedItem is null && ComboArgumentJava.Items.Count > 1)
            selectedItem = ComboArgumentJava.Items[1] as SelectOption;

        // 设置选中项
        if (selectedItem is not null) ComboArgumentJava.SelectedItem = selectedItem;

        // === 4. 无可用 Java 时的降级处理 ===
        if (!ModJava.Javas.ExistAnyJava() && ComboArgumentJava.Items.Count <= 3)
        {
            ComboArgumentJava.Items.Clear();
            var noJavaItem = new SelectOption
            {
                Content = Lang.Text("Instance.Setup.Options.Java.NoRuntime"),
                ToolTip = Lang.Text("Instance.Setup.Options.Java.NoRuntime.ToolTip"),
                IsEnabled = false
            };
            ComboArgumentJava.Items.Add(noJavaItem);
            ComboArgumentJava.SelectedItem = noJavaItem;
        }

        // === 5. 刷新关联控件 ===
        RefreshRam(true);
    }

    // 阻止在无效状态下展开下拉框
    private void ComboArgumentJava_DropDownOpened(object? sender, EventArgs e)
    {
        if (ComboArgumentJava.SelectedItem is null)
        {
            ComboArgumentJava.IsDropDownOpen = false;
            return;
        }

        var firstItem = ComboArgumentJava.Items[0] as SelectOption;
        if (firstItem is not null &&
        ((string)firstItem.Content == Lang.Text("Instance.Setup.Options.Java.NoRuntime") ||
        (string)firstItem.Content == Lang.Text("Instance.Setup.Options.Java.LoadFailed")))
            ComboArgumentJava.IsDropDownOpen = false;
    }

    // 下拉框选择更改处理（保存新格式配置）
    private void JavaSelectionUpdate(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (ComboArgumentJava.SelectedItem is null)
            return;

        var selectedItem = ComboArgumentJava.SelectedItem as SelectOption;
        if (selectedItem is null || (selectedItem.Tag is null &&
        (string)selectedItem.Content != Lang.Text("Instance.Setup.Options.Java.AutoSelect")))
            return;

        JavaPreference preference = default;
        var logMessage = "";

        // 根据 Tag 类型生成偏好对象
        if (selectedItem.Tag is null or AutoSelect)
        {
            // 自动选择：存储空字符串
            preference = new AutoSelect();
            logMessage = "[Java] 修改实例 Java 选择设置：自动选择";
        }
        else if (selectedItem.Tag is UseGlobalPreference)
        {
            preference = new UseGlobalPreference();
            logMessage = "[Java] 修改实例 Java 选择设置：跟随全局设置";
        }
        else if (selectedItem.Tag is UseRelativePath)
        {
            // 相对路径：需要用户选择实际文件
            var ret = SystemDialogs.SelectFile(Lang.Text("Setup.Java.SelectFile.Filter"), Lang.Text("Setup.Java.SelectFile.Title"), Basics.ExecutableDirectory);
            if (string.IsNullOrWhiteSpace(ret))
                // 用户取消，不保存配置，保持原选择
                return;

            ret = Path.GetFullPath(ret);
            var relativePath = Path.GetRelativePath(Basics.ExecutableDirectory, ret);

            // 验证路径是否在启动器目录内
            if (!Files.IsPathWithinDirectory(relativePath, Basics.ExecutableDirectory))
            {
                HintService.Hint(Lang.Text("Instance.Setup.Options.Java.PathOutOfRange"), HintType.Error);
                return;
            }

            preference = new UseRelativePath(relativePath);
            logMessage = $"[Java] 修改实例 Java 选择设置：相对路径 | {relativePath}";
        }
        else if (selectedItem.Tag is JavaEntry)
        {
            var javaEntry = (JavaEntry)selectedItem.Tag;
            preference = new ExistingJava(javaEntry.Installation.JavaExePath);
            logMessage = $"[Java] 修改实例 Java 选择设置：{javaEntry}";
        }

        // 保存配置
        var json = JsonSerializer.Serialize(preference, JsonCompat.SerializerOptions);
        Config.Instance.SelectedJava[InstanceWorkspace.Current.PathInstance] = json;


        ModBase.Log(logMessage);
        RefreshRam(true);
    }

    #endregion

    #region 其他设置

    // 版本隔离警告
    private bool isReverting;

    private void ComboArgumentIndieV2_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        if (isReverting)
            return;
        if (ModMain.MyMsgBox(
                Lang.Text("Instance.Setup.Options.InstanceIsolation.Message"),
                Lang.Text("Common.Dialog.Warning"), Lang.Text("Setup.Launch.Advanced.Renderer.Warning.Confirm"), Lang.Text("Common.Action.Cancel"), isWarn: true) == 2)
        {
            isReverting = true;
            ComboArgumentIndieV2.SelectedItem = e.RemovedItems[0];
            isReverting = false;
        }
        else
        {
            bool newValue = ComboArgumentIndieV2.SelectedIndex == 0;
            Config.Instance.IndieV2[InstanceWorkspace.Current.PathInstance] = newValue;
        }
    }

    // 游戏窗口
    private void CheckArgumentTitleEmpty_Change(object sender, bool e)
    {
        TextArgumentTitle.HintText = CheckArgumentTitleEmpty.Checked == true ? Lang.Text("Common.Option.Default") : Lang.Text("Instance.Setup.FollowGlobal");
        CheckBoxChange(sender,e);
    }

    private void TextArgumentTitle_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckArgumentTitleEmpty.Visibility = TextArgumentTitle.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        TextBoxChange(sender,e);
    }

    #endregion

    #region 高级设置

    private void TextAdvanceRun_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckAdvanceRunWait.Visibility = string.IsNullOrEmpty(TextAdvanceRun.Text) ? Visibility.Collapsed : Visibility.Visible;
        TextBoxChange(sender,e);
    }

    private void ComboAdvanceRenderer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;

        var args = e; // 转换事件参数

        if (!States.Hint.Renderer && ComboAdvanceRenderer.SelectedIndex != 0)
        {
            if (ModMain.MyMsgBox(Lang.Text("Setup.Launch.Advanced.Renderer.Warning.Message"),
                    Lang.Text("Common.Dialog.Warning"),
                    Lang.Text("Setup.Launch.Advanced.Renderer.Warning.Confirm"), Lang.Text("Common.Action.Cancel"), isWarn: true) == 2)
            {
                ComboAdvanceRenderer.SelectedItem = args.RemovedItems[0];
            }
            else
            {
                ComboChange(ComboAdvanceRenderer, e);
                States.Hint.Renderer = true;
            }
        }
        else
        {
            ComboChange(ComboAdvanceRenderer, e);
        }
    }

    private void CheckUseDebugLog4j2Config_CheckChanged(object sender, bool e)
    {
        if (ModAnimation.AniExitsAnyControl())
            return;
        var checkBox = sender as SymbioCheck;
        if (checkBox is null) return;
    
        if (checkBox.Checked.GetValueOrDefault() && !States.Hint.DebugLog4j2Config)
        {
            if (ModMain.MyMsgBox(
                    Lang.Text("Instance.Setup.Advanced.UseDebugLog4j.Message"),
                    Lang.Text("Common.Dialog.Warning"), Lang.Text("Setup.Launch.Advanced.Renderer.Warning.Confirm"), Lang.Text("Common.Action.Cancel"), isWarn: true) == 2)
            {
                checkBox.Checked = false;
            }
            else
            {
                CheckBoxChange(sender, e);
                States.Hint.DebugLog4j2Config = true;
            }
        }
        else
        {
            CheckBoxChange(sender, e);
        }
    }

    #endregion
}
