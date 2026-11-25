using System.Windows;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>The selected destination within each workspace, independent of whether its navigation view is mounted.</summary>
internal static class ShellNavigation
{
    private static readonly Dictionary<FormMain.PageType, FormMain.PageSubType> selections = new();
    internal static bool RevealHidden { get; set { field = value; NotifyVisibilityChanged(); } }
    internal static event Action? VisibilityChanged;

    internal static void NotifyVisibilityChanged() => ModBase.RunInUi(() => VisibilityChanged?.Invoke());

    internal static FormMain.PageSubType Selected(FormMain.PageType area) =>
        selections.TryGetValue(area, out var selected) ? selected : area switch
        {
            FormMain.PageType.Download => FormMain.PageSubType.DownloadInstall,
            FormMain.PageType.Tools => FormMain.PageSubType.ToolsTest,
            FormMain.PageType.SkinLibrary => FormMain.PageSubType.SkinLibrarySkins,
            FormMain.PageType.InstanceSetup or FormMain.PageType.BedrockInstance or FormMain.PageType.VersionSaves => FormMain.PageSubType.Default,
            _ => 0
        };

    internal static void Select(FormMain.PageType area, FormMain.PageSubType destination) => selections[area] = destination;

    internal static void RefreshVisibility()
    {
        if (ModMain.frmMain?.IsLoaded != true)
            return;
        try
        {
            // 获取配置组引用以缩短代码
            var conf = Config.Preference.Hide;

            ModMain.frmMain.RefreshShell();

            // 设置子页面 (FrmSetupLeft)
            if (ModMain.frmSetupLeft is not null)
            {
                ModMain.frmSetupLeft.ItemLaunch.Visibility =
                    !RevealHidden && conf.SetupLaunch ? Visibility.Collapsed : Visibility.Visible;
                ModMain.frmSetupLeft.ItemUI.Visibility =
                    !RevealHidden && conf.SetupUi ? Visibility.Collapsed : Visibility.Visible;
                ModMain.frmSetupLeft.ItemLauncherLanguage.Visibility = !RevealHidden && conf.SetupLauncherLanguage
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                ModMain.frmSetupLeft.ItemGameManage.Visibility = !RevealHidden && conf.SetupGameManage
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                ModMain.frmSetupLeft.ItemLauncherMisc.Visibility = !RevealHidden && conf.SetupLauncherMisc
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                ModMain.frmSetupLeft.ItemJava.Visibility =
                    !RevealHidden && conf.SetupJava ? Visibility.Collapsed : Visibility.Visible;
                ModMain.frmSetupLeft.ItemAbout.Visibility =
                    !RevealHidden && conf.SetupAbout ? Visibility.Collapsed : Visibility.Visible;
                ModMain.frmSetupLeft.ItemFeedback.Visibility = !RevealHidden && conf.SetupFeedback
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                ModMain.frmSetupLeft.ItemLog.Visibility =
                    !RevealHidden && conf.SetupLog ? Visibility.Collapsed : Visibility.Visible;

                var categories = new[]
                {
                    (ModMain.frmSetupLeft.TextGameCategory,
                        !(conf.SetupLaunch && conf.SetupJava && conf.SetupGameManage)),
                    // 更新页不属于可隐藏项，始终显示，因此启动器分类也始终显示
                    (ModMain.frmSetupLeft.TextLauncherCategory, true),
                    (ModMain.frmSetupLeft.TextAboutCategory,
                        !(conf.SetupAbout && conf.SetupFeedback && conf.SetupLog))
                };

                foreach (var category in categories)
                {
                    var isVisible = category.Item2 || RevealHidden;
                    category.Item1.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                    if (isVisible)
                        category.Item1.Opacity = 0.6d;
                }

                // 统计设置页可用项数量（更新页始终可用，计为 1 项）
                var setupCount = 1;
                if (!conf.SetupLaunch)
                    setupCount += 1;
                if (!conf.SetupUi)
                    setupCount += 1;
                if (!conf.SetupLauncherLanguage)
                    setupCount += 1;
                if (!conf.SetupGameManage)
                    setupCount += 1;
                if (!conf.SetupLauncherMisc)
                    setupCount += 1;
                if (!conf.SetupJava)
                    setupCount += 1;
                if (!conf.SetupAbout)
                    setupCount += 1;
                if (!conf.SetupFeedback)
                    setupCount += 1;
                if (!conf.SetupLog)
                    setupCount += 1;
                ModMain.frmSetupLeft.PanItem.Visibility =
                    setupCount < 2 && !RevealHidden ? Visibility.Collapsed : Visibility.Visible;
            }

            // 工具子页面 (FrmToolsLeft)
            if (ModMain.frmToolsLeft is not null)
            {
                ModMain.frmToolsLeft.ItemTest.Visibility =
                    !RevealHidden && conf.ToolsTest ? Visibility.Collapsed : Visibility.Visible;

                // 存档入口始终可见，因此工具分类与左侧面板始终显示
                ModMain.frmToolsLeft.TextToolsCategory.Visibility = Visibility.Visible;
                ModMain.frmToolsLeft.TextToolsCategory.Opacity = 0.6;
                ModMain.frmToolsLeft.PanItem.Visibility = Visibility.Visible;
            }

            // 其他入口刷新
            if (ModMain.frmMain.pageCurrent == FormMain.PageType.InstanceSelect)
                ModMain.frmMain.RefreshInstanceLibraryVisibility();
            if (ModMain.frmMain.pageCurrent == FormMain.PageType.InstanceSetup)
                ModMain.frmMain.RefreshResourceAvailability();
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "刷新功能隐藏项目失败",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
        }
    }
}
