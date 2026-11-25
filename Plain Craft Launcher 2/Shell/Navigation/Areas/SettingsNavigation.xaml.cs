using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class SettingsNavigation
{
    private bool isLoad;
    private bool isPageSwitched; // 如果在 Loaded 前切换到其他页面，会导致触发 Loaded 时再次切换一次

    private void SettingsNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        // 是否处于隐藏的子页面
        var isHiddenPage = false;
        var hide = Config.Preference.Hide;

        if (ItemLaunch.Selected && hide.SetupLaunch) isHiddenPage = true;
        if (ItemJava.Selected && hide.SetupJava) isHiddenPage = true;
        if (ItemGameManage.Selected && hide.SetupGameManage)  isHiddenPage = true;
        if (ItemUI.Selected && hide.SetupUi) isHiddenPage = true;
        if (ItemLauncherLanguage.Selected && hide.SetupLauncherLanguage) isHiddenPage = true;
        if (ItemLauncherMisc.Selected && hide.SetupLauncherMisc) isHiddenPage = true;
        if (ItemAbout.Selected && hide.SetupAbout) isHiddenPage = true;
        if (ItemFeedback.Selected && hide.SetupFeedback) isHiddenPage = true;
        if (ItemLog.Selected && hide.SetupLog) isHiddenPage = true;
        if (ShellNavigation.RevealHidden)
            isHiddenPage = false;
        // 若页面错误，或尚未加载，则继续
        if (isLoad && !isHiddenPage)
            return;
        isLoad = true;
        // 刷新子页面隐藏情况
        ShellNavigation.RefreshVisibility();
        // 选择第一个未被禁用的子页面
        if (isPageSwitched)
            return;
        var hideCfg = Config.Preference.Hide;
        if (!hideCfg.SetupLaunch) 
            ItemLaunch.Select(true);
        else if (!hideCfg.SetupJava) 
            ItemJava.Select(true);    
        else if (!hideCfg.SetupGameManage) 
            ItemGameManage.Select(true);
        else if (!hideCfg.SetupUi) 
            ItemUI.Select(true);
        else if (!hideCfg.SetupLauncherLanguage)
            ItemLauncherLanguage.Select(true);
        else if (!hideCfg.SetupLauncherMisc) 
            ItemLauncherMisc.Select(true);
        else if (!hideCfg.SetupAbout) 
            ItemAbout.Select(true);   
        else if (!hideCfg.SetupFeedback) 
            ItemFeedback.Select(true);
        else if (!hideCfg.SetupLog) 
            ItemLog.Select(true);
        else 
            ItemLaunch.Select(true);
    }

    private void PageOtherLeft_Unloaded(object sender, RoutedEventArgs e)
    {
        isPageSwitched = false;
    }

    public void Reset(object sender, EventArgs e)
    {
        switch (ModBase.Val(((FrameworkElement)sender).Tag))
        {
            case (double)FormMain.PageSubType.SetupLaunch:
            {
                if (ModMain.MyMsgBox(Lang.Text("Setup.Left.Reset.Launch.Message"), Lang.Text("Setup.Left.Reset.Title"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) == 1)
                {
                    ModSetup.ResetLaunchPreferences();
                    if (launchSettings?.IsLoaded == true)
                    {
                        ModAnimation.AniBeginControl();
                        try { launchSettings.Reload(); }
                        finally { ModAnimation.AniEndControl(); }
                    }
                    ItemLaunch.Selected = true;
                }

                break;
            }
            case (double)FormMain.PageSubType.SetupUI:
            {
                if (ModMain.MyMsgBox(Lang.Text("Setup.Left.Reset.Ui.Message"),
                        Lang.Text("Setup.Left.Reset.Title"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) == 1)
                {
                    ModSetup.ResetAppearancePreferences();
                    if (appearanceSettings?.IsLoaded == true)
                    {
                        ModAnimation.AniBeginControl();
                        try { appearanceSettings.Reload(); }
                        finally { ModAnimation.AniEndControl(); }
                    }
                    ItemUI.Selected = true;
                }

                break;
            }
            case (double)FormMain.PageSubType.SetupGameManage:
            {
                if (ModMain.MyMsgBox(Lang.Text("Setup.Left.Reset.GameManage.Message"), Lang.Text("Setup.Left.Reset.Title"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) == 1)
                {
                    ModSetup.ResetGameManagePreferences();
                    if (gameManagement?.IsLoaded == true)
                    {
                        ModAnimation.AniBeginControl();
                        try { gameManagement.Reload(); }
                        finally { ModAnimation.AniEndControl(); }
                    }
                    ItemGameManage.Selected = true;
                }

                break;
            }
            case (double)FormMain.PageSubType.SetupLauncherLanguage:
            {
                if (ModMain.MyMsgBox(Lang.Text("Setup.Left.Reset.Language.Message"), Lang.Text("Setup.Left.Reset.Title"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) == 1)
                {
                    LanguageSettings.Reset();
                    ModMain.frmSetupLauncherLanguage?.Reload();
                    HintService.Hint(Lang.Text("Setup.Language.Reset.Success"), HintType.Success, false);
                    ItemLauncherLanguage.Selected = true;
                }

                break;
            }
            case (double)FormMain.PageSubType.SetupLauncherMisc:
            {
                if (ModMain.MyMsgBox(Lang.Text("Setup.Left.Reset.Misc.Message"), Lang.Text("Setup.Left.Reset.Title"), button2: Lang.Text("Common.Action.Cancel"), isWarn: true) == 1)
                {
                    ModSetup.ResetLauncherMiscPreferences();
                    if (launcherSettings?.IsLoaded == true)
                    {
                        ModAnimation.AniBeginControl();
                        try { launcherSettings.Reload(); }
                        finally { ModAnimation.AniEndControl(); }
                    }
                    ItemLauncherMisc.Selected = true;
                }

                break;
            }
        }
    }

    public static void TryFeedback() // Handles ItemFeedback.Click
    {
        ModBase.RunInNewThread(() =>
        {
            if (!ModBase.CanFeedback(true))
                return;
            switch (ModMain.MyMsgBox(Lang.Text("Setup.Left.Feedback.Message"), Lang.Text("Setup.Left.Feedback.Title"),
                        Lang.Text("Setup.Left.Feedback.SubmitNew"), Lang.Text("Setup.Left.Feedback.ViewList"), Lang.Text("Common.Action.Cancel")))
            {
                case 1:
                {
                    ModBase.Feedback();
                    break;
                }
                case 2:
                {
                    ModBase.OpenWebsite("https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition");
                    break;
                }
            }
        });
    }

    public void Refresh(object sender, EventArgs e) // 由边栏按钮匿名调用
    {
        switch (ModBase.Val(((FrameworkElement)sender).Tag))
        {
            case (double)FormMain.PageSubType.SetupFeedback:
            {
                feedbackSettings?.Loader.Start(isForceRestart: true);
                ItemFeedback.Selected = true;
                break;
            }
            case (double)FormMain.PageSubType.SetupJava:
            {
                javaSettings?.loader.Start(isForceRestart: true);
                ItemJava.Selected = true;
                break;
            }
        }

        HintService.Hint(Lang.Text("Setup.Left.Refreshing"), log: false);
    }

    #region 页面切换

    /// <summary>
    ///     当前页面的编号。从左往右从 0 开始计算。
    /// </summary>
    public FormMain.PageSubType pageID
    {
        get => ShellNavigation.Selected(FormMain.PageType.Setup);
        set => ShellNavigation.Select(FormMain.PageType.Setup, value);
    }

    private AppearanceSettingsView? appearanceSettings;
    private LaunchSettingsView? launchSettings;

    private GameManagementSettingsView? gameManagement;
    private JavaSettingsView? javaSettings;
    private AboutSettingsView? aboutSettings;
    private FeedbackSettingsView? feedbackSettings;
    private UpdateSettingsView? updateSettings;
    private LauncherSettingsView? launcherSettings;

    public SettingsNavigation()
    {
        InitializeComponent();
        foreach (var entry in new[] { ItemLaunch, ItemJava, ItemGameManage, ItemUI, ItemLauncherLanguage, ItemLauncherMisc, ItemAbout, ItemUpdate, ItemFeedback, ItemLog })
            entry.Chosen += PageCheck;
        foreach (var entry in new[] { ItemLaunch, ItemGameManage, ItemUI, ItemLauncherLanguage, ItemLauncherMisc })
            entry.ActionRequested += Reset;
        foreach (var entry in new[] { ItemJava, ItemFeedback })
            entry.ActionRequested += Refresh;
        // 选择第一个未被禁用的子页面
        var hideCfg = Config.Preference.Hide;
        if (!hideCfg.SetupLaunch)
            pageID = FormMain.PageSubType.SetupLaunch;
        else if (!hideCfg.SetupJava)
            pageID = FormMain.PageSubType.SetupJava;
        else if (!hideCfg.SetupGameManage)
            pageID = FormMain.PageSubType.SetupGameManage;
        else if (!hideCfg.SetupUi)
            pageID = FormMain.PageSubType.SetupUI;
        else if (!hideCfg.SetupLauncherLanguage)
            pageID = FormMain.PageSubType.SetupLauncherLanguage;
        else if (!hideCfg.SetupLauncherMisc)
            pageID = FormMain.PageSubType.SetupLauncherMisc;
        else if (!hideCfg.SetupAbout)
            pageID = FormMain.PageSubType.SetupAbout;        
        else if (!hideCfg.SetupFeedback)
            pageID = FormMain.PageSubType.SetupFeedback;
        else if (!hideCfg.SetupLog)
            pageID = FormMain.PageSubType.SetupLog;
        else
            pageID = FormMain.PageSubType.SetupLaunch;
        AnimatedControl = PanItem;
        Loaded += SettingsNavigation_Loaded;
        Unloaded += PageOtherLeft_Unloaded;
    }

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (NavigationEntry)senderRaw;
        // 尚未初始化控件属性时，sender.Tag 为 Nothing，会跳过切换，且由于 PageID 默认为 0 而切换到第一个页面
        // 若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
        if (sender.Tag is not null)
            PageChange((FormMain.PageSubType)ModBase.Val(sender.Tag));
    }

    /// <summary>
    ///     获取当前导航指定的右页面。
    /// </summary>
    public object PageGet(FormMain.PageSubType? id = null)
    {
        var targetID = id ?? pageID;
        switch (id)
        {
            case FormMain.PageSubType.SetupLaunch:
            {
                return launchSettings ??= new LaunchSettingsView();
            }
            case FormMain.PageSubType.SetupUI:
            {
                return appearanceSettings ??= new AppearanceSettingsView();
            }
            case FormMain.PageSubType.SetupGameManage:
            {
                return gameManagement ??= new GameManagementSettingsView();
            }
            case FormMain.PageSubType.SetupAbout:
            {
                return aboutSettings ??= new AboutSettingsView();
            }
            case FormMain.PageSubType.SetupLog:
            {
                if (ModMain.frmSetupLog is null)
                    ModMain.frmSetupLog = new LogSettingsView();
                return ModMain.frmSetupLog;
            }
            case FormMain.PageSubType.SetupFeedback:
            {
                return feedbackSettings ??= new FeedbackSettingsView();
            }
            case FormMain.PageSubType.SetupLauncherLanguage:
            {
                if (ModMain.frmSetupLauncherLanguage is null)
                    ModMain.frmSetupLauncherLanguage = new LanguageSettingsView();
                return ModMain.frmSetupLauncherLanguage;
            }
            case FormMain.PageSubType.SetupLauncherMisc:
            {
                return launcherSettings ??= new LauncherSettingsView();
            }
            case FormMain.PageSubType.SetupJava:
            {
                return javaSettings ??= new JavaSettingsView();
            }
            case FormMain.PageSubType.SetupUpdate:
            {
                return updateSettings ??= new UpdateSettingsView();
            }

            default:
            {
                throw new Exception("未知的设置子页面种类：" + (int)id);
            }
        }
    }

    /// <summary>
    ///     切换现有页面。
    /// </summary>
    public void PageChange(FormMain.PageSubType id)
    {
        if (pageID == id)
            return;
        ModAnimation.AniBeginControl();
        isPageSwitched = true;
        try
        {
            ModMain.frmMain.SwitchWorkspaceContent((WorkspacePage)PageGet(id));
            pageID = id;
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                $"切换分页面失败（ID {(int)id}）",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Setup.Error.OperationFailed"));
        }
        finally
        {
            ModAnimation.AniEndControl();
        }
    }



    #endregion
}
