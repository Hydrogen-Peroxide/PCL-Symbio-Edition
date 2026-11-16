using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockInstanceLeft : IRefreshable
{
    /// <summary>
    ///     当前正在查看设置的基岩版实例的版本 ID。
    /// </summary>
    public static string? CurrentVersion;

    private static PageBedrockInstanceOverall? _pageOverall;
    private static PageBedrockInstanceSetupPage? _pageSetup;
    private static PageBedrockInstanceSaves? _pageSaves;
    private static PageBedrockInstanceActions? _pageActions;
    private static PageBedrockInstanceLoader? _pageLoader;
    private static PageBedrockInstanceFiles? _pageFiles;
    private static PageBedrockInstanceStats? _pageStats;

    /// <summary>把过长的路径折叠成「头…尾」，保持行高不变。</summary>
    public static string FoldPath(string? path, int max = 46)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (path.Length <= max) return path;
        var head = max / 2 - 2;
        var tail = max - head - 1;
        return path[..head] + "…" + path[^tail..];
    }

    public PageBedrockInstanceLeft()
    {
        InitializeComponent();
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
                _pageStats?.RefreshData();
                break;
        }
    }

    #region 页面切换

    /// <summary>
    ///     当前页面的编号。从 0 开始计算。
    /// </summary>
    public FormMain.PageSubType pageID = FormMain.PageSubType.Default;

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object sender, ModBase.RouteEventArgs e)
    {
        if (sender is MyListItem item && item.Tag is not null)
            PageChange((FormMain.PageSubType)ModBase.Val(item.Tag));
    }

    public object PageGet(FormMain.PageSubType id)
    {
        if ((int)id == -1)
            id = pageID;
        switch (id)
        {
            case FormMain.PageSubType.VersionOverall:
            {
                _pageOverall ??= new PageBedrockInstanceOverall();
                return _pageOverall;
            }
            case FormMain.PageSubType.VersionSetup:
            {
                _pageSetup ??= new PageBedrockInstanceSetupPage();
                return _pageSetup;
            }
            case FormMain.PageSubType.VersionWorld:
            {
                _pageSaves ??= new PageBedrockInstanceSaves();
                return _pageSaves;
            }
            case FormMain.PageSubType.BedrockActions:
            {
                _pageActions ??= new PageBedrockInstanceActions();
                return _pageActions;
            }
            case FormMain.PageSubType.BedrockLoader:
            {
                _pageLoader ??= new PageBedrockInstanceLoader();
                return _pageLoader;
            }
            case FormMain.PageSubType.BedrockMods:
            {
                _pageFiles ??= new PageBedrockInstanceFiles();
                _pageFiles.Mode = 0;
                return _pageFiles;
            }
            case FormMain.PageSubType.BedrockScreenshots:
            {
                _pageFiles ??= new PageBedrockInstanceFiles();
                _pageFiles.Mode = 1;
                return _pageFiles;
            }
            case FormMain.PageSubType.BedrockServers:
            {
                _pageFiles ??= new PageBedrockInstanceFiles();
                _pageFiles.Mode = 2;
                return _pageFiles;
            }
            case FormMain.PageSubType.BedrockStats:
            {
                _pageStats ??= new PageBedrockInstanceStats();
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
        if (pageID == id && ModMain.frmMain.pageRight is MyPageRight)
            return;
        ModAnimation.AniBeginControl();
        try
        {
            PageChangeRun((MyPageRight)PageGet(id));
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

    private static void PageChangeRun(MyPageRight target)
    {
        ModAnimation.AniStop("FrmMain PageChangeRight"); // 停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        if (target.Parent is not null)
            target.SetValue(ContentPresenter.ContentProperty, null);
        ModMain.frmMain.pageRight = target;
        ((MyPageRight)ModMain.frmMain.PanMainRight.Child).PageOnExit();
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaCode(() =>
            {
                ((MyPageRight)ModMain.frmMain.PanMainRight.Child).PageOnForceExit();
                ModMain.frmMain.PanMainRight.Child = ModMain.frmMain.pageRight;
                ModMain.frmMain.pageRight.Opacity = 0d;
            }, 130),
            ModAnimation.AaCode(() =>
            {
                // 延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                ModMain.frmMain.pageRight.Opacity = 1d;
                ModMain.frmMain.pageRight.PageOnEnter();
            }, 30, true)
        }, "PageLeft PageChange");
    }

    #endregion
}