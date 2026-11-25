using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

public partial class DownloadNavigation : IRefreshable
{
    private readonly Dictionary<ModComp.CompType, ResourceBrowserPage> resourcePages = new();

    public void Refresh()
    {
        Refresh(ModMain.frmMain.PageCurrentSub);
    }

    // 强制刷新
    public void RefreshButton_Click(object sender, EventArgs e) // 由边栏按钮匿名调用
    {
        Refresh((FormMain.PageSubType)ModBase.Val(((FrameworkElement)sender).Tag));
    }

    public void Refresh(FormMain.PageSubType subType)
    {
        switch (subType)
        {
            case FormMain.PageSubType.DownloadInstall:
            {
                ModDownload.dlClientListLoader.Start(isForceRestart: true);
                ModDownload.dlOptiFineListLoader.Start(isForceRestart: true);
                ModDownload.dlForgeListLoader.Start(isForceRestart: true);
                ModDownload.dlNeoForgeListLoader.Start(isForceRestart: true);
                ModDownload.dlCleanroomListLoader.Start(isForceRestart: true);
                ModDownload.dlLiteLoaderListLoader.Start(isForceRestart: true);
                ModDownload.dlFabricListLoader.Start(isForceRestart: true);
                ModDownload.dlLegacyFabricListLoader.Start(isForceRestart: true);
                ModDownload.dlFabricApiLoader.Start(isForceRestart: true);
                ModDownload.dlLegacyFabricApiLoader.Start(isForceRestart: true);
                ModDownload.dlOptiFabricLoader.Start(isForceRestart: true);
                ModDownload.dlLabyModListLoader.Start(isForceRestart: true);
                ItemInstall.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadBedrock:
            {
                ModDownload.dlBedrockChannelListLoader.Start(isForceRestart: true);
                ItemBedrock.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadMod:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.Mod, out var browser)) browser.RefreshSearch();

                ItemMod.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadPack:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.ModPack, out var browser)) browser.RefreshSearch();

                ItemPack.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadDataPack:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.DataPack, out var browser)) browser.RefreshSearch();

                ItemDataPack.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadResourcePack:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.ResourcePack, out var browser)) browser.RefreshSearch();

                ItemResourcePack.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadShader:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.Shader, out var browser)) browser.RefreshSearch();

                ItemShader.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadWorld:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.World, out var browser)) browser.RefreshSearch();

                ItemWorld.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadClient:
            {
                ModDownload.dlClientListLoader.Start(isForceRestart: true);
                ItemInstall.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadOptiFine:
            {
                ModDownload.dlOptiFineListLoader.Start(isForceRestart: true);
                ItemOptiFine.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadForge:
            {
                ModDownload.dlForgeListLoader.Start(isForceRestart: true);
                ItemForge.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadNeoForge:
            {
                ModDownload.dlNeoForgeListLoader.Start(isForceRestart: true);
                ItemNeoForge.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadCleanroom:
            {
                ModDownload.dlCleanroomListLoader.Start(isForceRestart: true);
                ItemCleanroom.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadLiteLoader:
            {
                ModDownload.dlLiteLoaderListLoader.Start(isForceRestart: true);
                ItemLiteLoader.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadFabric:
            {
                ModDownload.dlFabricListLoader.Start(isForceRestart: true);
                ItemFabric.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadLabyMod:
            {
                ModDownload.dlLabyModListLoader.Start(isForceRestart: true);
                ItemLabyMod.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadLegacyFabric:
            {
                ModDownload.dlLegacyFabricListLoader.Start(isForceRestart: true);
                ItemLegacyFabric.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadCompFavorites:
            {
                favorites?.PageLoaderRestart();
                ItemFavorites.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadBedrockAddon:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.BedrockAddon, out var browser)) browser.RefreshSearch();

                ItemBedrockAddon.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadBedrockMap:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.BedrockMap, out var browser)) browser.RefreshSearch();

                ItemBedrockMap.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadBedrockMaterial:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.BedrockMaterial, out var browser)) browser.RefreshSearch();

                ItemBedrockMaterial.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadBedrockScript:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.BedrockScript, out var browser)) browser.RefreshSearch();

                ItemBedrockScript.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadBedrockSkin:
            {
                ModComp.compProjectCache.Clear();
                ModComp.compFilesCache.Clear();
                if (resourcePages.TryGetValue(ModComp.CompType.BedrockSkin, out var browser)) browser.RefreshSearch();

                ItemBedrockSkin.Selected = true;
                break;
            }
            case FormMain.PageSubType.DownloadBedrockLip:
            {
                bedrockSources ??= new BedrockSourceCatalogView();
                ItemBedrockLip.Selected = true;
                break;
            }
        }

        HintService.Hint(Lang.Text("Download.Left.Hint.Refreshing"), log: false);
    }

    // 点击返回
    private void ItemInstall_Click(object sender, RoutedEventArgs e)
    {
        if (!ItemInstall.Selected)
            return;
        if (javaInstaller?.IsLoaded == true) javaInstaller.ExitSelectPage();
    }

    #region 页面切换

    /// <summary>
    ///     当前页面的编号。
    /// </summary>
    public FormMain.PageSubType pageID
    {
        get => ShellNavigation.Selected(FormMain.PageType.Download);
        set => ShellNavigation.Select(FormMain.PageType.Download, value);
    }

    private readonly Dictionary<FormMain.PageSubType, VersionCatalogView> versionCatalogs = new();
    private JavaInstallView? javaInstaller;

    private ResourceFavoritesView? favorites;
    private BedrockSourceCatalogView? bedrockSources;
    private BedrockCatalogView? bedrockCatalog;

    public DownloadNavigation()
    {
        InitializeComponent();
        foreach (var entry in new[] { ItemInstall, ItemBedrock, ItemMod, ItemPack, ItemDataPack, ItemResourcePack, ItemShader, ItemWorld, ItemFavorites, ItemBedrockLip, ItemBedrockAddon, ItemBedrockMap, ItemBedrockMaterial, ItemBedrockScript, ItemBedrockSkin, ItemOptiFine, ItemForge, ItemNeoForge, ItemCleanroom, ItemFabric, ItemLegacyFabric, ItemLabyMod, ItemLiteLoader })
            entry.Chosen += PageCheck;
        foreach (var entry in new[] { ItemMod, ItemPack, ItemDataPack, ItemResourcePack, ItemShader, ItemWorld, ItemFavorites, ItemBedrockLip, ItemBedrockAddon, ItemBedrockMap, ItemBedrockMaterial, ItemBedrockScript, ItemBedrockSkin, ItemOptiFine, ItemForge, ItemNeoForge, ItemCleanroom, ItemFabric, ItemLegacyFabric, ItemLabyMod, ItemLiteLoader })
            entry.ActionRequested += RefreshButton_Click;
        AnimatedControl = PanItem;
    }

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object sender, ModBase.RouteEventArgs e)
    {
        if (sender is NavigationEntry { Tag: { } tag })
            PageChange((FormMain.PageSubType)ModBase.Val(tag));
    }

    private ResourceBrowserPage Browser(ModComp.CompType type, bool curseForge, bool modrinth)
    {
        if (!resourcePages.TryGetValue(type, out var page))
            resourcePages[type] = page = new ResourceBrowserPage(type, curseForge, modrinth);
        return page;
    }

    public object PageGet(FormMain.PageSubType id)
    {
        if (id == default)
            id = pageID;
        switch (id)
        {
            case FormMain.PageSubType.DownloadInstall:
            {
                return javaInstaller ??= new JavaInstallView();
            }
            case FormMain.PageSubType.DownloadMod:
            {
                return Browser(ModComp.CompType.Mod, true, true);
            }
            case FormMain.PageSubType.DownloadPack:
            {
                return Browser(ModComp.CompType.ModPack, true, true);
            }
            case FormMain.PageSubType.DownloadDataPack:
            {
                return Browser(ModComp.CompType.DataPack, true, true);
            }
            case FormMain.PageSubType.DownloadResourcePack:
            {
                return Browser(ModComp.CompType.ResourcePack, true, true);
            }
            case FormMain.PageSubType.DownloadShader:
            {
                return Browser(ModComp.CompType.Shader, true, true);
            }
            case FormMain.PageSubType.DownloadWorld:
            {
                return Browser(ModComp.CompType.World, true, false);
            }
            case FormMain.PageSubType.DownloadCompFavorites:
            {
                return favorites ??= new ResourceFavoritesView();
            }
            case FormMain.PageSubType.DownloadClient:
            case FormMain.PageSubType.DownloadOptiFine:
            case FormMain.PageSubType.DownloadForge:
            case FormMain.PageSubType.DownloadNeoForge:
            case FormMain.PageSubType.DownloadCleanroom:
            case FormMain.PageSubType.DownloadLiteLoader:
            case FormMain.PageSubType.DownloadFabric:
            case FormMain.PageSubType.DownloadLabyMod:
            case FormMain.PageSubType.DownloadLegacyFabric:
            {
                if (!versionCatalogs.TryGetValue(id, out var catalog))
                {
                    catalog = new VersionCatalogView(id);
                    versionCatalogs.Add(id, catalog);
                }
                return catalog;
            }
            case FormMain.PageSubType.DownloadBedrock:
            {
                return bedrockCatalog ??= new BedrockCatalogView();
            }
            case FormMain.PageSubType.DownloadBedrockAddon:
            {
                return Browser(ModComp.CompType.BedrockAddon, true, false);
            }
            case FormMain.PageSubType.DownloadBedrockMap:
            {
                return Browser(ModComp.CompType.BedrockMap, true, false);
            }
            case FormMain.PageSubType.DownloadBedrockMaterial:
            {
                return Browser(ModComp.CompType.BedrockMaterial, true, false);
            }
            case FormMain.PageSubType.DownloadBedrockScript:
            {
                return Browser(ModComp.CompType.BedrockScript, true, false);
            }
            case FormMain.PageSubType.DownloadBedrockSkin:
            {
                return Browser(ModComp.CompType.BedrockSkin, true, false);
            }
            case FormMain.PageSubType.DownloadBedrockLip:
            {
                return bedrockSources ??= new BedrockSourceCatalogView();
            }

            default:
            {
                throw new Exception(Lang.Text("Download.Left.Error.UnknownSubPageType", (int)id));
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
            ModMain.frmMain.RefreshShell();
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "切换分页面失败（ID " + (int)id + "）",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Download.Error.OperationFailed"));
        }
        finally
        {
            ModAnimation.AniEndControl();
        }
    }



    #endregion
}
