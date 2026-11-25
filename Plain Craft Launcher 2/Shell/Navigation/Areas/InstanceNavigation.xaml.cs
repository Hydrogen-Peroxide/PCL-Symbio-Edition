using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class InstanceNavigation : IRefreshable
{
    /// <summary>
    ///     当前显示设置的 MC 实例。
    /// </summary>

    private InstanceLoaderView? installer;
    private InstanceSettingsView? instanceSettings;
    private readonly Dictionary<ModComp.CompType, ResourceManagerView> resourceViews = new();
    private NestedComponentsView? nestedComponents;

    private ResourceManagerView GetResourceView(ModComp.CompType resource)
    {
        if (!resourceViews.TryGetValue(resource, out var view))
        {
            view = new ResourceManagerView(resource);
            resourceViews.Add(resource, view);
        }
        return view;
    }

    private InstanceOverviewView? overview;
    private ResourceUnavailableView? modUnavailable;
    private InstanceCapturesView? captures;
    private WorldLibraryView? worldLibrary;
    private InstanceServersView? servers;
    private InstanceExportView? export;

    public InstanceNavigation()
    {
        InitializeComponent();
        foreach (var entry in new[] { ItemOverall, ItemSetup, ItemInstall, ItemExport, ItemWorld, ItemScreenshot, ItemMod, ItemModDisabled, ItemResourcePack, ItemShader, ItemSchematic, ItemServer })
            entry.Chosen += PageCheck;
        ItemSetup.ActionRequested += Reset;
        foreach (var entry in new[] { ItemInstall, ItemExport, ItemWorld, ItemScreenshot, ItemMod, ItemResourcePack, ItemShader, ItemSchematic, ItemServer })
            entry.ActionRequested += RefreshButton_Click;
        Loaded += (_, _) => RefreshModDisabled();
    }

    public void Refresh()
    {
        Refresh(ModMain.frmMain.PageCurrentSub);
    }

    public void RefreshModDisabled()
    {
        var hide = Config.Preference.Hide;

        if (InstanceWorkspace.Current is not null && InstanceWorkspace.Current.Modable)
        {
            ItemMod.Visibility = !ShellNavigation.RevealHidden && hide.InstanceMod
                ? Visibility.Collapsed
                : Visibility.Visible;
            ItemModDisabled.Visibility = Visibility.Collapsed;
        }
        else
        {
            ItemMod.Visibility = Visibility.Collapsed;
            ItemModDisabled.Visibility = !ShellNavigation.RevealHidden && hide.InstanceMod
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        // 功能隐藏
        if (!ShellNavigation.RevealHidden)
        {
            var disableCount = 0;
            if (hide.InstanceSave)
                disableCount += 1;
            if (hide.InstanceScreenshot)
                disableCount += 1;
            if (hide.InstanceMod)
                disableCount += 1;
            if (hide.InstanceResourcePack)
                disableCount += 1;
            if (hide.InstanceShader)
                disableCount += 1;
            if (hide.InstanceSchematic)
                disableCount += 1;
            if (hide.InstanceServer)
                disableCount += 1;
            if (disableCount == 7)
                TextResource.Visibility = Visibility.Collapsed;
            else
                TextResource.Visibility = Visibility.Visible;
        }
        else
        {
            TextResource.Visibility = Visibility.Visible;
        }

        ItemInstall.Visibility = !ShellNavigation.RevealHidden && hide.InstanceEdit
            ? Visibility.Collapsed
            : Visibility.Visible;
        ItemExport.Visibility = !ShellNavigation.RevealHidden && hide.InstanceExport
            ? Visibility.Collapsed
            : Visibility.Visible;
        ItemWorld.Visibility = !ShellNavigation.RevealHidden && hide.InstanceSave
            ? Visibility.Collapsed
            : Visibility.Visible;
        ItemScreenshot.Visibility = !ShellNavigation.RevealHidden && hide.InstanceScreenshot
            ? Visibility.Collapsed
            : Visibility.Visible;
        ItemResourcePack.Visibility = !ShellNavigation.RevealHidden && hide.InstanceResourcePack
            ? Visibility.Collapsed
            : Visibility.Visible;
        ItemShader.Visibility = !ShellNavigation.RevealHidden && hide.InstanceShader
            ? Visibility.Collapsed
            : Visibility.Visible;
        ItemSchematic.Visibility = !ShellNavigation.RevealHidden && hide.InstanceSchematic
            ? Visibility.Collapsed
            : Visibility.Visible;
        ItemServer.Visibility = !ShellNavigation.RevealHidden && hide.InstanceServer
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RefreshButton_Click(object sender, EventArgs e) // 由边栏按钮匿名调用
    {
        Refresh((FormMain.PageSubType)ModBase.Val(((FrameworkElement)sender).Tag));
    }

    public void Refresh(FormMain.PageSubType subType)
    {
        switch (subType)
        {
            case FormMain.PageSubType.VersionMod:
            {
                ResourceManagerView.Refresh(ModComp.CompType.Mod);
                break;
            }
            case FormMain.PageSubType.VersionScreenshot:
            {
                if (captures is not null) _ = captures.RefreshAsync();
                break;
            }
            case FormMain.PageSubType.VersionWorld:
            {
                worldLibrary?.Refresh();
                break;
            }
            case FormMain.PageSubType.VersionResourcePack:
            {
                ResourceManagerView.Refresh(ModComp.CompType.ResourcePack);
                break;
            }
            case FormMain.PageSubType.VersionShader:
            {
                ResourceManagerView.Refresh(ModComp.CompType.Shader);
                break;
            }
            case FormMain.PageSubType.VersionSchematic:
            {
                ResourceManagerView.Refresh(ModComp.CompType.Schematic);
                break;
            }
            case FormMain.PageSubType.VersionInstall:
            {
                ModDownload.dlClientListLoader.Start(isForceRestart: true);
                ModDownload.dlOptiFineListLoader.Start(isForceRestart: true);
                ModDownload.dlForgeListLoader.Start(isForceRestart: true);
                ModDownload.dlNeoForgeListLoader.Start(isForceRestart: true);
                ModDownload.dlCleanroomListLoader.Start(isForceRestart: true);
                ModDownload.dlLiteLoaderListLoader.Start(isForceRestart: true);
                ModDownload.dlFabricListLoader.Start(isForceRestart: true);
                ModDownload.dlFabricApiLoader.Start(isForceRestart: true);
                ModDownload.dlLegacyFabricListLoader.Start(isForceRestart: true);
                ModDownload.dlLegacyFabricApiLoader.Start(isForceRestart: true);
                ModDownload.dlOptiFabricLoader.Start(isForceRestart: true);
                ModDownload.dlLabyModListLoader.Start(isForceRestart: true);
                ItemInstall.Selected = true;
                if (installer?.IsLoaded == true) installer.GetCurrentInfo();
                break;
            }
            case FormMain.PageSubType.VersionExport:
            {
                export?.RefreshAll();
                ItemExport.Selected = true;
                break;
            }
            case FormMain.PageSubType.VersionServer:
            {
                servers?.RefreshServers();
                ItemServer.Selected = true;
                break;
            }
        }
    }

    public void Reset(object sender, EventArgs e)
    {
        if (ModMain.MyMsgBox(Lang.Text("Instance.Left.InitializeSettings.ConfirmMessage"),
                Lang.Text("Instance.Left.InitializeSettings.ConfirmTitle"),
                button2: Lang.Text("Common.Action.Cancel"),
                isWarn: true)
            == 1)
        {
            if (InstanceWorkspace.Current is not { } instance) return;
            ModSetup.ResetInstancePreferences(instance);
            if (instanceSettings?.IsLoaded == true)
            {
                ModAnimation.AniBeginControl();
                try { instanceSettings.Reload(); }
                finally { ModAnimation.AniEndControl(); }
            }
            ItemSetup.Selected = true;
        }
    }

    #region 页面切换

    /// <summary>
    ///     当前页面的编号。从 0 开始计算。
    /// </summary>
    public FormMain.PageSubType pageID
    {
        get => ShellNavigation.Selected(FormMain.PageType.InstanceSetup);
        set => ShellNavigation.Select(FormMain.PageType.InstanceSetup, value);
    }

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object sender, ModBase.RouteEventArgs e)
    {
        if (sender is NavigationEntry item && item.Tag is not null)
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
                return overview ??= new InstanceOverviewView();
            }
            case FormMain.PageSubType.VersionMod:
            {
                return GetResourceView(ModComp.CompType.Mod);
            }
            case FormMain.PageSubType.VersionModDisabled:
            {
                return modUnavailable ??= new ResourceUnavailableView();
            }
            case FormMain.PageSubType.VersionModJarInJar:
            {
                return nestedComponents ??= new NestedComponentsView();
            }
            case FormMain.PageSubType.VersionSetup:
            {
                return instanceSettings ??= new InstanceSettingsView();
            }
            case FormMain.PageSubType.VersionWorld:
            {
                return worldLibrary ??= new WorldLibraryView();
            }
            case FormMain.PageSubType.VersionScreenshot:
            {
                return captures ??= new InstanceCapturesView();
            }
            case FormMain.PageSubType.VersionResourcePack:
            {
                return GetResourceView(ModComp.CompType.ResourcePack);
            }
            case FormMain.PageSubType.VersionShader:
            {
                return GetResourceView(ModComp.CompType.Shader);
            }
            case FormMain.PageSubType.VersionSchematic:
            {
                return GetResourceView(ModComp.CompType.Schematic);
            }
            case FormMain.PageSubType.VersionInstall:
            {
                return installer ??= new InstanceLoaderView();
            }
            case FormMain.PageSubType.VersionExport:
            {
                return export ??= new InstanceExportView();
            }
            case FormMain.PageSubType.VersionServer:
            {
                return servers ??= new InstanceServersView();
            }

            default:
            {
                throw new Exception("未知的实例设置子页面种类：" + (int)id);
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
                "切换分页面失败（ID " + (int)id + "）",
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
