using System.IO;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class BedrockLibraryView : IRefreshable
{
    private readonly Dictionary<string, SelectionRow> _items = [];
    private string? _lastSignature;

    public BedrockLibraryView()
    {
        InitializeComponent();
        SearchBox.HintText = Lang.Text("Select.Instance.Search.Hint");
        SearchBox.TextChanged += (_, _) => RefreshInstances();
        Loaded += (_, _) =>
        {
            BedrockLaunchSession.Current.LibraryChanged += RefreshInstances;
            RefreshInstances();
        };
        Unloaded += (_, _) => BedrockLaunchSession.Current.LibraryChanged -= RefreshInstances;
    }

    public void Refresh()
    {
        Folders.RefreshFolders();
        RefreshInstances();
    }

    /// <summary>
    ///     重建实例列表：与 Java 版一致，按通道分组为带数量的卡片，并支持搜索。
    /// </summary>
    private void RefreshInstances()
    {
        if (PanInstances is null) return;

        var versions = ModBedrockInstance.GetLocalVersions();
        // 已经选过的实例优先，避免每次打开都跳回第一个
        var configured = Config.Launch.BedrockSelectedVersion;
        var current = !string.IsNullOrWhiteSpace(configured) && versions.Contains(configured)
            ? configured
            : versions.FirstOrDefault() ?? "";
        ModBedrockInstance.SelectedVersionId = current;

        var keyword = (SearchBox.Text ?? "").Trim();
        // 实例集合与搜索词都没变时只同步勾选状态，避免整列表重建造成闪烁
        var signature = ModBedrockInstance.VersionsDirectory + "|" + string.Join(",", versions) + "|" + keyword;
        if (signature == _lastSignature)
        {
            SyncSelection(current);
            return;
        }

        _lastSignature = signature;
        _items.Clear();
        PanInstances.Children.Clear();

        if (versions.Count == 0)
        {
            PanInstances.Children.Add(CreateEmptyText(Lang.Text("Bedrock.Select.NoInstances"), true));
            return;
        }

        var filtered = string.IsNullOrEmpty(keyword)
            ? versions
            : versions.Where(version => Matches(version, keyword)).ToList();
        if (filtered.Count == 0)
        {
            PanInstances.Children.Add(CreateEmptyText(Lang.Text("Bedrock.Select.NoMatch"), false));
            return;
        }

        foreach (var channel in new[] { "release", "preview", "beta" })
        {
            var grouped = filtered.Where(version => ModBedrockInstance.GetChannel(version) == channel).ToList();
            if (grouped.Count == 0) continue;

            PanInstances.Children.Add(BuildChannelCard(channel, grouped, current));
        }
    }

    /// <summary>只更新选中项，不重建列表。</summary>
    private void SyncSelection(string current)
    {
        foreach (var (version, item) in _items)
            if (item.Checked != (version == current))
                item.SetChecked(version == current, false, false);
    }

    private static bool Matches(string versionId, string keyword)
    {
        var name = ModBedrockInstance.GetDisplayName(ModBedrockInstance.ResolveVersionsDirectory(versionId), versionId);
        return versionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>构建一个通道分组卡片，标题与 Java 版一样带上实例数量。</summary>
    private WorkspaceCard BuildChannelCard(string channel, List<string> versions, string current)
    {
        var content = new StackPanel { Tag = "install", Margin = new Thickness(15, 36, 15, 0) };

        void Install(StackPanel stack)
        {
            foreach (var version in versions)
            {
                var item = BuildInstanceItem(version, channel);
                item.Tag = version;
                stack.Children.Add(item);
                if (version == current) item.SetChecked(true, false, false);
            }
        }

        var card = new WorkspaceCard
        {
            Title = $"{ChannelLabel(channel)}  ({versions.Count})",
            Margin = new Thickness(0, 0, 0, 8),
            SwapControl = content,
            InstallMethod = Install
        };
        card.Children.Add(content);
        card.StackInstall();
        return card;
    }

    private SelectionRow BuildInstanceItem(string version, string channel)
    {
        var item = new SelectionRow
        {
            Title = ModBedrockInstance.GetDisplayName(ModBedrockInstance.ResolveVersionsDirectory(version), version),
            Info = BuildInstanceInfo(version, channel),
            Logo = LogoFor(channel),
            Type = SelectionRow.CheckType.CheckBox,
            Height = 46,
        };
        var target = version;
        item.Click += (_, args) =>
        {
            args.Handled = true;
            SelectVersion(target);
        };
        _items[version] = item;
        return item;
    }

    /// <summary>副标题与 Java 版一致：通道 + 版本信息，方便区分预览版与正式版。</summary>
    private static string BuildInstanceInfo(string versionId, string channel)
    {
        var label = ChannelLabel(channel);
        try
        {
            var folder = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.ResolveVersionsDirectory(versionId), versionId);
            var version = ModBedrockUwp.ReadDirectoryManifest(folder)?.Version;
            if (string.IsNullOrEmpty(version))
                version = ModBedrockUwp.ReadDirectoryManifest(Path.Combine(folder, "inner"))?.Version;
            if (!string.IsNullOrEmpty(version)) return $"{label} · {version}";
            return $"{label} · {(ModBedrockGdk.IsGdkBuild(folder) ? "GDK" : "UWP")}";
        }
        catch
        {
            return label;
        }
    }

    private static UIElement CreateEmptyText(string text, bool withPath)
    {
        var panel = new StackPanel();
        var hint = new TextBlock
        {
            Text = text,
            Margin = new Thickness(6, 12, 6, 0),
            FontSize = 12.5d,
            TextWrapping = TextWrapping.Wrap
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SyTextSecondary");
        panel.Children.Add(hint);

        if (withPath)
        {
            var pathHint = new TextBlock
            {
                Text = ModBedrockInstance.VersionsDirectory,
                Margin = new Thickness(6, 6, 6, 0),
                FontSize = 11.5d,
                TextWrapping = TextWrapping.Wrap
            };
            pathHint.SetResourceReference(TextBlock.ForegroundProperty, "SyTextTertiary");
            panel.Children.Add(pathHint);
        }

        return panel;
    }

    /// <summary>选中实例：同时写入配置，启动按钮与左右页面都跟随它。</summary>
    private void SelectVersion(string versionId)
    {
        ModBedrockInstance.SelectedVersionId = versionId;
        BedrockLaunchSession.Current.SelectVersion(versionId);
        // 点一下就让左侧状态和右侧「当前实例」跟着变，不用等确定
        ModMain.frmMain.RefreshShell();
        // 与 Java 版一致：选完实例直接回主页，不用再手动返回
        ModMain.frmMain.PageBack();
    }

    private static string ChannelLabel(string channel) => channel switch
    {
        "preview" => Lang.Text("Download.Bedrock.Channel.Preview"),
        "beta" => Lang.Text("Download.Bedrock.Channel.Beta"),
        _ => Lang.Text("Download.Bedrock.Channel.Release")
    };

    private static string LogoFor(string channel) => channel switch
    {
        "preview" => ModBase.pathImage + "Blocks/CommandBlock.png",
        "beta" => ModBase.pathImage + "Blocks/GoldBlock.png",
        _ => ModBase.pathImage + "Blocks/Grass.png"
    };
}
