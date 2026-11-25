using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

public partial class SaveNavigation : IRefreshable
{

    // 初始化
    private bool isLoad;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (isLoad)
            return;
        isLoad = true;
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ModBase.OpenExplorer($@"{InstanceWorkspace.SaveDirectory}\");
    }

    #region 龙猫牌 页面管理

    /// <summary>
    ///     当前页面的编号。从 0 开始计算。
    /// </summary>
    public FormMain.PageSubType pageID
    {
        get => ShellNavigation.Selected(FormMain.PageType.VersionSaves);
        set => ShellNavigation.Select(FormMain.PageType.VersionSaves, value);
    }

    private WorldDetailsView? details;
    private WorldDatapacksView? datapacks;

    public SaveNavigation()
    {
        InitializeComponent();
        ItemDatapack.ActionRequested += RefreshButton_Click;
        Loaded += Page_Loaded;
        ItemInfo.Chosen += PageCheck;
        ItemDatapack.Chosen += PageCheck;
        BtnOpenFolder.Invoked += BtnOpenFolder_Click;
    }

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object sender, ModBase.RouteEventArgs e)
    {
        if (sender is NavigationEntry item && item.Tag is not null)
            PageChange((FormMain.PageSubType)ModBase.Val(item.Tag));
    }

    public object PageGet(FormMain.PageSubType id = FormMain.PageSubType.Default)
    {
        if ((int)id == -1)
            id = pageID;
        switch (id)
        {
            case FormMain.PageSubType.VersionSavesInfo:
            {
                return details ??= new WorldDetailsView();
            }
            case FormMain.PageSubType.VersionSavesDatapack:
            {
                return datapacks ??= new WorldDatapacksView();
            }

            default:
            {
                throw new Exception(Lang.Text("Instance.Saves.Left.UnknownSubPage", (int)id));
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
        try
        {
            ModMain.frmMain.SwitchWorkspaceContent((WorkspacePage)PageGet(id));
            pageID = id;
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Instance.Saves.Left.SwitchFailed", (int)id),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Saves.Left.SwitchFailed", (int)id));
        }
        finally
        {
            ModAnimation.AniEndControl();
        }
    }



    public void RefreshButton_Click(object sender, EventArgs e) // 由边栏按钮匿名调用
    {
        Refresh((FormMain.PageSubType)ModBase.Val(((FrameworkElement)sender).Tag));
    }

    public void Refresh()
    {
        Refresh(ModMain.frmMain.PageCurrentSub);
    }

    public void Refresh(FormMain.PageSubType subType)
    {
        switch (subType)
        {
            case FormMain.PageSubType.VersionSavesDatapack:
            {
                datapacks ??= new WorldDatapacksView();
                if (ItemDatapack.Selected)
                    datapacks.Refresh();
                else
                    ItemDatapack.Selected = true;

                break;
            }
        }

        HintService.Hint(Lang.Text("Instance.Saves.Left.Refreshing"));
    }

    #endregion
}
