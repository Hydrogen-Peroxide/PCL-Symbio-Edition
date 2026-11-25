using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;

using PCL.Core.App.Localization;
namespace PCL;

public partial class ToolsNavigation
{
    private bool isLoad;
    private bool isPageSwitched; // 如果在 Loaded 前切换到其他页面，会导致触发 Loaded 时再次切换一次
    private WorldLibraryView? frmSaves;
    private DeviceInfoView? frmDeviceInfo;
    private LauncherToolsView? launcherTools;
    private SchematicLibraryView? frmSchematic;

    public ToolsNavigation()
    {
        InitializeComponent();
        foreach (var entry in new[] { ItemTest, ItemSaves, ItemSchematic, ItemDeviceInfo })
            entry.Chosen += PageCheck;
        AnimatedControl = PanItem;
        Loaded += PageLinkLeft_Loaded;
        Unloaded += PageOtherLeft_Unloaded;
    }

    private void PageLinkLeft_Loaded(object sender, RoutedEventArgs e)
    {
        var isHiddenPage = false;
        var hide = Config.Preference.Hide;

        if (ItemTest.Selected && hide.ToolsTest) isHiddenPage = true;
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
        if (pageID == FormMain.PageSubType.ToolsSaves)
        {
            ItemSaves.Select(true);
            return;
        }

        if (pageID == FormMain.PageSubType.ToolsDeviceInfo)
        {
            ItemDeviceInfo.Select(true);
            return;
        }

        if (pageID == FormMain.PageSubType.ToolsSchematic)
        {
            ItemSchematic.Select(true);
            return;
        }

        if (hide.ToolsTest && !ShellNavigation.RevealHidden)
            ItemSaves.Select(true);
        else
            ItemTest.Select(true);
    }

    private void PageOtherLeft_Unloaded(object sender, RoutedEventArgs e)
    {
        isPageSwitched = false;
    }

    #region 页面切换

    /// <summary>
    ///     当前页面的编号。
    /// </summary>
    public FormMain.PageSubType pageID
    {
        get => ShellNavigation.Selected(FormMain.PageType.Tools);
        set => ShellNavigation.Select(FormMain.PageType.Tools, value);
    }

    private bool _suppressPageCheck;

    /// <summary>
    ///     从外部入口（如启动页的「皮肤库」）直接切到指定子页面，并同步左侧勾选。
    /// </summary>
    public void SelectPage(FormMain.PageSubType id)
    {
        pageID = id;
        if (!IsLoaded)
            return; // 尚未加载时由 Loaded 根据 pageID 勾选对应项

        _suppressPageCheck = true;
        try
        {
            foreach (var child in PanItem.Children)
                if (child is NavigationEntry item && ModBase.Val(item.Tag) == (double)id)
                {
                    item.Select(true);
                    break;
                }
        }
        finally
        {
            _suppressPageCheck = false;
        }
    }

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        if (_suppressPageCheck)
            return;

        var sender = (NavigationEntry)senderRaw;
        // 尚未初始化控件属性时，sender.Tag 为 Nothing，会导致切换到页面 0
        // 若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
        if (sender.Tag is null)
            return;

        var id = (FormMain.PageSubType)ModBase.Val(sender.Tag);
        if ((id == FormMain.PageSubType.ToolsSaves || id == FormMain.PageSubType.ToolsSchematic) &&
            !EnsureInstance())
        {
            HintService.Hint(Lang.Text("Tools.Saves.NoInstance"), HintType.Error);
            return;
        }

        PageChange(id);
    }

    /// <summary>存档页依赖当前实例，未选择时回退到启动页选中的实例。</summary>
    private static bool EnsureInstance()
    {
        if (InstanceWorkspace.Current is not null) return true;
        var selected = ModInstanceList.McMcInstanceSelected;
        if (selected is null) return false;
        selected.Load();
        InstanceWorkspace.Current = selected;
        return true;
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var targetID = id ?? pageID;
        switch (targetID)
        {
            case FormMain.PageSubType.ToolsTest:
            {
                return launcherTools ??= new LauncherToolsView();
            }
            case FormMain.PageSubType.ToolsSaves:
            {
                frmSaves ??= new WorldLibraryView();
                frmSaves.EnableStandaloneMode();
                return frmSaves;
            }
            case FormMain.PageSubType.ToolsDeviceInfo:
            {
                frmDeviceInfo ??= new DeviceInfoView();
                return frmDeviceInfo;
            }
            case FormMain.PageSubType.ToolsSchematic:
            {
                frmSchematic ??= new SchematicLibraryView();
                return frmSchematic;
            }
            default:
            {
                throw new Exception("未知的更多子页面种类：" + (int)targetID);
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
                userSummary: Lang.Text("Tools.Error.OperationFailed"));
        }
        finally
        {
            ModAnimation.AniEndControl();
        }
    }



    #endregion
}
