using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;

using PCL.Core.App.Localization;
namespace PCL;

public partial class PageToolsLeft
{
    private bool isLoad;
    private bool isPageSwitched; // 如果在 Loaded 前切换到其他页面，会导致触发 Loaded 时再次切换一次
    private PageInstanceSaves? frmSaves;
    private PageToolsSkinLibrary? frmSkins;
    private PageToolsDeviceInfo? frmDeviceInfo;
    private PageToolsSchematic? frmSchematic;

    public PageToolsLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        Loaded += PageLinkLeft_Loaded;
        Unloaded += PageOtherLeft_Unloaded;
    }

    private void PageLinkLeft_Loaded(object sender, RoutedEventArgs e)
    {
        var isHiddenPage = false;
        var hide = Config.Preference.Hide;

        if (ItemTest.Checked && hide.ToolsTest) isHiddenPage = true;
        if (PageSetupUI.HiddenForceShow)
            isHiddenPage = false;
        // 若页面错误，或尚未加载，则继续
        if (isLoad && !isHiddenPage)
            return;
        isLoad = true;
        // 刷新子页面隐藏情况
        PageSetupUI.HiddenRefresh();
        // 选择第一个未被禁用的子页面
        if (isPageSwitched) 
            return;
        if (pageID == FormMain.PageSubType.ToolsSaves)
        {
            ItemSaves.SetChecked(true, false, false);
            return;
        }

        if (pageID == FormMain.PageSubType.ToolsSkins)
        {
            ItemSkins.SetChecked(true, false, false);
            return;
        }

        if (pageID == FormMain.PageSubType.ToolsDeviceInfo)
        {
            ItemDeviceInfo.SetChecked(true, false, false);
            return;
        }

        if (pageID == FormMain.PageSubType.ToolsSchematic)
        {
            ItemSchematic.SetChecked(true, false, false);
            return;
        }

        if (hide.ToolsTest && !PageSetupUI.HiddenForceShow)
            ItemSaves.SetChecked(true, false, false);
        else
            ItemTest.SetChecked(true, false, false);
    }

    private void PageOtherLeft_Unloaded(object sender, RoutedEventArgs e)
    {
        isPageSwitched = false;
    }

    #region 页面切换

    /// <summary>
    ///     当前页面的编号。
    /// </summary>
    public FormMain.PageSubType pageID = FormMain.PageSubType.ToolsTest;

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object senderRaw, ModBase.RouteEventArgs e)
    {
        var sender = (MyListItem)senderRaw;
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
        if (PageInstanceLeft.McInstance is not null) return true;
        var selected = ModInstanceList.McMcInstanceSelected;
        if (selected is null) return false;
        selected.Load();
        PageInstanceLeft.McInstance = selected;
        return true;
    }

    public object PageGet(FormMain.PageSubType? id = null)
    {
        var targetID = id ?? pageID;
        switch (id)
        {
            case FormMain.PageSubType.ToolsTest:
            {
                if (ModMain.frmToolsTest is null)
                    ModMain.frmToolsTest = new PageToolsTest();
                return ModMain.frmToolsTest;
            }
            case FormMain.PageSubType.ToolsSaves:
            {
                frmSaves ??= new PageInstanceSaves();
                frmSaves.EnableStandaloneMode();
                return frmSaves;
            }
            case FormMain.PageSubType.ToolsSkins:
            {
                frmSkins ??= new PageToolsSkinLibrary();
                return frmSkins;
            }
            case FormMain.PageSubType.ToolsDeviceInfo:
            {
                frmDeviceInfo ??= new PageToolsDeviceInfo();
                return frmDeviceInfo;
            }
            case FormMain.PageSubType.ToolsSchematic:
            {
                frmSchematic ??= new PageToolsSchematic();
                return frmSchematic;
            }
            default:
            {
                throw new Exception("未知的更多子页面种类：" + (int)id);
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
            PageChangeRun((MyPageRight)PageGet(id));
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
