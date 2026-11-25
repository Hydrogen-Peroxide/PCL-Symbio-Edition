using System.Collections;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App.Localization;
using PCL.Network;
using Destination = PCL.FormMain.PageSubType;

namespace PCL;

public partial class VersionCatalogView
{
    private sealed record Catalog(ModLoader.LoaderBase Loader, Action Present, string? ResourcePrefix = null, string? Website = null);

    private static readonly McVersionCategory[] VanillaCategories =
        [McVersionCategory.Release, McVersionCategory.Snapshot, McVersionCategory.BeforeRelease, McVersionCategory.AprilFools];

    public VersionCatalogView(Destination destination)
    {
        InitializeComponent();
        CatalogDestination = destination;
        var catalog = destination switch
        {
            Destination.DownloadClient => new Catalog(ModDownload.dlClientListLoader, ShowVanilla),
            Destination.DownloadForge => new Catalog(ModDownload.dlForgeListLoader, ShowForge, "Forge", "https://files.minecraftforge.net"),
            Destination.DownloadNeoForge => new Catalog(ModDownload.dlNeoForgeListLoader, ShowNeoForge, "NeoForge", "https://neoforged.net/"),
            Destination.DownloadCleanroom => new Catalog(ModDownload.dlCleanroomListLoader, ShowCleanroom, "Cleanroom", "https://cleanroommc.com/zh/"),
            Destination.DownloadOptiFine => new Catalog(ModDownload.dlOptiFineListLoader, ShowOptiFine, "Optifine", "https://www.optifine.net/"),
            Destination.DownloadLiteLoader => new Catalog(ModDownload.dlLiteLoaderListLoader, ShowLiteLoader, "LiteLoader", "https://www.liteloader.com"),
            Destination.DownloadFabric => new Catalog(ModDownload.dlFabricListLoader, ShowFabric, "Fabric", "https://www.fabricmc.net"),
            Destination.DownloadLegacyFabric => new Catalog(ModDownload.dlLegacyFabricListLoader, ShowLegacyFabric, "LegacyFabric", "https://legacyfabric.net/"),
            Destination.DownloadLabyMod => new Catalog(ModDownload.dlLabyModListLoader, ShowLabyMod, "LabyMod", "https://labymod.net"),
            _ => throw new ArgumentOutOfRangeException(nameof(destination))
        };

        WorkspaceCard? header = null;
        if (catalog.ResourcePrefix is { } prefix)
        {
            header = Introduction;
            var stem = "Download.Version." + prefix;
            Introduction.SetResourceReference(WorkspaceCard.TitleProperty, stem + ".Intro.Title");
            Description.SetResourceReference(TextBlock.TextProperty, stem + ".Intro.Description");
            Progress.SetResourceReference(LoadingIndicator.TextProperty, stem + ".LoadingList");
            Website.Click += (_, _) => ModBase.OpenWebsite(catalog.Website!);
        }
        else
        {
            Introduction.Visibility = Visibility.Collapsed;
            Progress.SetResourceReference(LoadingIndicator.TextProperty, "Download.Version.LoadingList");
        }

        Loaded += (_, _) => CatalogScroll.ScrollToHome();
        PageLoaderInit(Progress, Waiting, Results, header, catalog.Loader, _ => RenderCatalog(catalog));
    }

    public Destination CatalogDestination { get; }

    private void RenderCatalog(Catalog catalog)
    {
        try
        {
            Results.Children.Clear();
            catalog.Present();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"可视化 {CatalogDestination} 版本列表失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Download.Error.OperationFailed"));
        }
    }

    private void AddGroup(string title, Action<StackPanel> populate, bool expanded = false)
    {
        var body = new StackPanel
        {
            Margin = new Thickness(16, WorkspaceCard.SwapedHeight, 16, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Tag = true
        };
        var card = new WorkspaceCard
        {
            Title = title, Margin = new Thickness(0, 0, 0, 12), CanSwap = true,
            SwapControl = body, IsSwapped = !expanded, InstallMethod = populate
        };
        card.Children.Add(body);
        Results.Children.Add(card);
        if (expanded) card.StackInstall();
    }

    private void AddRows<T>(string title, IReadOnlyList<T> entries, Func<T, FrameworkElement> create, bool expanded = false)
    {
        if (entries.Count == 0) return;
        AddGroup(title, body =>
        {
            foreach (var entry in entries) body.Children.Add(create(entry));
        }, expanded);
    }

    private void AddBuildGroup<T>(string version, IReadOnlyList<T> entries, Func<T, FrameworkElement> create) =>
        AddRows($"{version} ({entries.Count})", entries, create);

    private void ShowVanilla()
    {
        var versions = (JsonArray)ModDownload.dlClientListLoader.output.Value["versions"];
        var grouped = versions.OfType<JsonObject>().GroupBy(McVersionClassifier.ClassifyVersion)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(McVersionClassifier.GetReleaseTime).ToArray());
        var latest = new List<JsonObject>();
        var release = grouped.GetValueOrDefault(McVersionCategory.Release)?.FirstOrDefault();
        var snapshot = grouped.GetValueOrDefault(McVersionCategory.Snapshot)?.FirstOrDefault();
        if (release is not null) latest.Add(WithDate(release, "Download.Version.Latest.Release"));
        if (snapshot is not null && (release is null ||
            McVersionClassifier.GetReleaseTime(snapshot) > McVersionClassifier.GetReleaseTime(release)))
            latest.Add(WithDate(snapshot, "Download.Version.Latest.Development"));

        static SelectionRow Row(JsonObject version) => ModDownloadLib.McDownloadListItem(version, ModDownloadLib.McDownloadMenuSave, true);
        static JsonObject WithDate(JsonObject version, string key)
        {
            var marked = (JsonObject)version.DeepClone();
            marked["lore"] = Lang.Text(key, Lang.Date(McVersionClassifier.GetReleaseTime(version), "g"));
            return marked;
        }

        AddRows(Lang.Text("Download.Version.Latest.Title"), latest, Row, expanded: true);
        foreach (var category in VanillaCategories)
            if (grouped.TryGetValue(category, out var entries))
                AddBuildGroup(McVersionClassifier.GetCategoryDisplayName(category), entries, Row);
    }

    private void ShowForge()
    {
        foreach (var version in ModDownload.dlForgeListLoader.output.Value.Sort(McVersionComparer.CompareVersionGe))
            AddGroup(version.Replace("_p", " P"), body => LoadForgeBuilds(version, body));
    }

    private static void LoadForgeBuilds(string version, StackPanel body)
    {
        var loader = new ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>(
            "DlForgeVersion Main", ModDownload.DlForgeVersionMain);
        var progress = new LoadingIndicator { State = loader, Margin = new Thickness(5) };
        progress.SetResourceReference(LoadingIndicator.TextProperty, "Download.Version.Forge.LoadingList");
        progress.Click += (_, _) =>
        {
            if (loader.State == ModBase.LoadState.Failed) loader.Start(version, isForceRestart: true);
        };
        progress.StateChanged += (_, state, _) =>
        {
            if (state != LoadingStatus.Stop || loader.State is ModBase.LoadState.Waiting or ModBase.LoadState.Aborted) return;
            var entries = loader.output.Sort((a, b) => a.version > b.version).ToList();
            body.Children.Clear();
            body.Tag = entries;
            ModDownloadLib.ForgeDownloadListItemPreload(body, entries, ModDownloadLib.ForgeSave_Click, true);
            foreach (var entry in (IEnumerable)body.Tag)
                body.Children.Add(ModDownloadLib.ForgeDownloadListItem(
                    (ModDownload.DlForgeVersionEntry)entry, ModDownloadLib.ForgeSave_Click, true));
            body.Children.Add(new Border { Height = 18 });
            body.Tag = null;
        };
        body.Children.Add(progress);
        loader.Start(version);
    }

    private void ShowNeoForge()
    {
        foreach (var group in ModDownload.dlNeoForgeListLoader.output.Value.GroupBy(version => version.Inherit).OrderByDescending(g => g.Key))
            AddBuildGroup(group.Key, group.ToArray(), item => ModDownloadLib.NeoForgeDownloadListItem(item, ModDownloadLib.NeoForgeSave_Click, true));
    }

    private void ShowCleanroom()
    {
        foreach (var group in ModDownload.dlCleanroomListLoader.output.Value.GroupBy(version => version.Inherit).OrderByDescending(g => g.Key))
            AddBuildGroup(group.Key, group.ToArray(), item => ModDownloadLib.CleanroomDownloadListItem(item, ModDownloadLib.CleanroomSave_Click, true));
    }

    private static int MinorVersion(string version, int maximum)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var minor) && minor >= 0 && minor <= maximum
            ? minor : -1;
    }

    private void ShowOptiFine()
    {
        var groups = ModDownload.dlOptiFineListLoader.output.Value.GroupBy(version => MinorVersion(version.Inherit, 50))
            .OrderByDescending(group => group.Key == -1 ? int.MaxValue : group.Key);
        foreach (var group in groups)
        {
            var title = group.Key == -1 ? Lang.Text("Download.Version.Optifine.Snapshot") : $"1.{group.Key}";
            var entries = group.ToList().Sort((a, b) => McVersionComparer.CompareVersion(a.DisplayName, b.DisplayName) == 1).ToArray();
            AddBuildGroup(title, entries, item => ModDownloadLib.OptiFineDownloadListItem(item, ModDownloadLib.OptiFineSave_Click, true));
        }
    }

    private void ShowLiteLoader()
    {
        var groups = ModDownload.dlLiteLoaderListLoader.output.Value.GroupBy(version => MinorVersion(version.Inherit, 30))
            .OrderByDescending(group => group.Key);
        foreach (var group in groups)
        {
            var title = group.Key == -1 ? Lang.Text("Minecraft.Version.Unknown") : $"1.{group.Key}";
            var entries = group.ToList().Sort((a, b) => McVersionComparer.CompareVersion(a.Inherit, b.Inherit) == 1).ToArray();
            AddBuildGroup(title, entries, item => ModDownloadLib.LiteLoaderDownloadListItem(item, ModDownloadLib.LiteLoaderSave_Click, true));
        }
    }

    private void ShowFabric() => ShowInstallers(
        (JsonArray)ModDownload.dlFabricListLoader.output.Value["installer"],
        ModDownloadLib.FabricDownloadListItem, ModDownloadLib.McDownloadFabricLoaderSave);

    private void ShowLegacyFabric() => ShowInstallers(
        (JsonArray)ModDownload.dlLegacyFabricListLoader.output.Value["installer"],
        ModDownloadLib.LegacyFabricDownloadListItem, ModDownloadLib.McDownloadLegacyFabricLoaderSave);

    private void ShowInstallers(JsonArray manifest, Func<JsonObject, RoutedEventHandler, SelectionRow> create, Action<JsonObject> save)
    {
        var entries = manifest.OfType<JsonObject>().ToArray();
        AddRows(Lang.Text("Download.Version.VersionListCount", entries.Length), entries,
            row => create(row, (_, _) => save(row)), expanded: true);
    }

    private void ShowLabyMod()
    {
        var versions = ModDownload.dlLabyModListLoader.output.Value;
        if (versions is null) return;
        AddGroup(Lang.Text("Download.Version.VersionListCount", versions.Count), body =>
        {
            var production = new JsonObject { ["channel"] = "production", ["version"] = versions["production"]["labyModVersion"].ToString() };
            var snapshot = new JsonObject { ["channel"] = "snapshot", ["version"] = versions["snapshot"]["labyModVersion"].ToString() };
            body.Children.Add(ModDownloadLib.LabyModDownloadListItem(production, (_, _) => ModDownloadLib.McDownloadLabyModProductionLoaderSave()));
            body.Children.Add(ModDownloadLib.LabyModDownloadListItem(snapshot, (_, _) => ModDownloadLib.McDownloadLabyModSnapshotLoaderSave()));
        }, expanded: true);
    }
}
