using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class BedrockInstanceNavigation : IRefreshable
{
    private BedrockOverviewView? _pageOverall;
    private BedrockPreferencesView? _pageSetup;
    private BedrockWorldsView? _pageSaves;
    private BedrockInstanceActionsView? _pageActions;
    private BedrockLoaderSettingsView? _pageLoader;
    private BedrockInstanceFilesView? _pageFiles;
    private BedrockStatisticsView? _pageStats;

    public BedrockInstanceNavigation()
    {
        InitializeComponent();
        foreach (var entry in new[] { ItemOverall, ItemSetup, ItemLoader, ItemMod, ItemWorld, ItemScreenshot, ItemServer, ItemActions, ItemStats })
            entry.Chosen += PageCheck;
        foreach (var entry in new[] { ItemOverall, ItemMod, ItemWorld, ItemScreenshot, ItemServer, ItemStats })
            entry.ActionRequested += RefreshButton_Click;
    }

    public void Refresh()
    {
        Refresh(ModMain.frmMain.PageCurrentSub);
    }

    public void Refresh(FormMain.PageSubType subType)
    {
        // 基岩版没有 Mod、存档等挂载页，这里只需要让当前页重新读一次数据
        switch (subType)
        {
            case FormMain.PageSubType.VersionOverall:
                _pageOverall?.RefreshData();
                break;
            case FormMain.PageSubType.VersionSetup:
                _pageSetup?.RefreshData();
                break;
            case FormMain.PageSubType.VersionWorld:
                _pageSaves?.RefreshData();
                break;
            case FormMain.PageSubType.BedrockLoader:
                _pageLoader?.RefreshData();
                break;
            case FormMain.PageSubType.BedrockMods:
            case FormMain.PageSubType.BedrockScreenshots:
            case FormMain.PageSubType.BedrockServers:
                _pageFiles?.RefreshData();
                break;
            case FormMain.PageSubType.BedrockStats:
                _pageStats?.Refresh();
                break;
        }
    }

    #region 页面切换

    /// <summary>
    ///     当前页面的编号。从 0 开始计算。
    /// </summary>
    public FormMain.PageSubType pageID
    {
        get => ShellNavigation.Selected(FormMain.PageType.BedrockInstance);
        set => ShellNavigation.Select(FormMain.PageType.BedrockInstance, value);
    }

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object sender, ModBase.RouteEventArgs e)
    {
        if (sender is NavigationEntry item && item.Tag is not null)
            PageChange((FormMain.PageSubType)ModBase.Val(item.Tag));
    }

    /// <summary>导航行右侧的刷新按钮：重新读取该行对应页面的数据。</summary>
    private void RefreshButton_Click(object sender, EventArgs e)
    {
        if (sender is FrameworkElement button && button.Tag is not null)
            Refresh((FormMain.PageSubType)ModBase.Val(button.Tag));
    }

    public object PageGet(FormMain.PageSubType id)
    {
        if ((int)id == -1)
            id = pageID;
        switch (id)
        {
            case FormMain.PageSubType.VersionOverall:
            {
                _pageOverall ??= new BedrockOverviewView();
                return _pageOverall;
            }
            case FormMain.PageSubType.VersionSetup:
            {
                _pageSetup ??= new BedrockPreferencesView();
                return _pageSetup;
            }
            case FormMain.PageSubType.VersionWorld:
            {
                _pageSaves ??= new BedrockWorldsView();
                return _pageSaves;
            }
            case FormMain.PageSubType.BedrockActions:
            {
                _pageActions ??= new BedrockInstanceActionsView();
                return _pageActions;
            }
            case FormMain.PageSubType.BedrockLoader:
            {
                _pageLoader ??= new BedrockLoaderSettingsView();
                return _pageLoader;
            }
            case FormMain.PageSubType.BedrockMods:
            {
                _pageFiles ??= new BedrockInstanceFilesView();
                _pageFiles.Mode = 0;
                return _pageFiles;
            }
            case FormMain.PageSubType.BedrockScreenshots:
            {
                _pageFiles ??= new BedrockInstanceFilesView();
                _pageFiles.Mode = 1;
                return _pageFiles;
            }
            case FormMain.PageSubType.BedrockServers:
            {
                _pageFiles ??= new BedrockInstanceFilesView();
                _pageFiles.Mode = 2;
                return _pageFiles;
            }
            case FormMain.PageSubType.BedrockStats:
            {
                _pageStats ??= new BedrockStatisticsView();
                return _pageStats;
            }

            default:
            {
                throw new Exception("未知的基岩版实例子页面种类：" + (int)id);
            }
        }
    }

    /// <summary>
    ///     切换现有页面。
    /// </summary>
    public void PageChange(FormMain.PageSubType id)
    {
        if (pageID == id && ModMain.frmMain.pageRight is WorkspacePage)
            return;
        ModAnimation.AniBeginControl();
        try
        {
            ModMain.frmMain.SwitchWorkspaceContent((WorkspacePage)PageGet(id));
            pageID = id;
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "切换基岩版实例子页面失败（ID " + (int)id + "）",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Error.OperationFailed"));
        }
        finally
        {
            ModAnimation.AniEndControl();
        }
    }



    #endregion
}
