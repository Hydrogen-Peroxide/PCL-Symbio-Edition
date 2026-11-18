using System.IO;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockInstanceSelect : IRefreshable
{
    private readonly Dictionary<string, MyListItem> _items = [];
    private string? _lastSignature;

    public PageBedrockInstanceSelect()
    {
        InitializeComponent();
        SearchBox.HintText = Lang.Text("Select.Instance.Search.Hint");
        SearchBox.TextChanged += (_, _) => RefreshInstances();
        Loaded += (_, _) => RefreshInstances();
    }

    public void Refresh()
    {
        RefreshInstances();
    }

    /// <summary>读取指定目录下的实例（兼容 &lt;root&gt;\versions 与版本目录本身两种布局）。</summary>
    private static List<string> GetVersionsIn(string root)
    {
        var result = new List<string>();
        try
        {
            if (!Directory.Exists(root)) return result;
            var nested = Path.Combine(root, "versions");
            var target = Directory.Exists(nested) ? nested : root;
            foreach (var dir in Directory.GetDirectories(target))
            {
                var name = Path.GetFileName(dir);
                // 这些是启动器的辅助目录，不是实例
                if (name.Equals("extracted", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith('.')) continue;
                result.Add(name);
            }

            result.Sort((a, b) => ModDownload.CompareBedrockVersions(b, a));
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 读取实例列表失败（{root}）：{ex.Message}", ModBase.LogLevel.Debug);
        }

        return result;
    }

    /// <summary>
    ///     重建实例列表：与 Java 版一致，按通道分组为带数量的卡片，并支持搜索。
    /// </summary>
    public void RefreshInstances(string? rootOverride = null)
    {
        if (PanInstances is null) return;

        var versions = rootOverride is null
            ? ModBedrockInstance.GetLocalVersions()
            : GetVersionsIn(rootOverride);
        // 已经选过的实例优先，避免每次打开都跳回第一个
        var configured = Config.Launch.BedrockSelectedVersion;
        var current = !string.IsNullOrWhiteSpace(configured) && versions.Contains(configured)
            ? configured
            : versions.FirstOrDefault() ?? "";
        ModBedrockInstance.SelectedVersionId = current;

        var keyword = (SearchBox.Text ?? "").Trim();
        // 实例集合与搜索词都没变时只同步勾选状态，避免整列表重建造成闪烁
        var signature = (rootOverride ?? "") + "|" + string.Join(",", versions) + "|" + keyword;
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
        var name = ModBedrockInstance.GetDisplayName(ModBedrockInstance.VersionsDirectory, versionId);
        return versionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>构建一个通道分组卡片，标题与 Java 版一样带上实例数量。</summary>
    private MyCard BuildChannelCard(string channel, List<string> versions, string current)
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

        var card = new MyCard
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

    private MyListItem BuildInstanceItem(string version, string channel)
    {
        var item = new MyListItem
        {
            Title = ModBedrockInstance.GetDisplayName(ModBedrockInstance.VersionsDirectory, version),
            Info = BuildInstanceInfo(version, channel),
            Logo = LogoFor(channel),
            Type = MyListItem.CheckType.RadioBox,
            Height = 46,
        };
        var target = version;
        item.Click += (_, _) => SelectVersion(target);
        _items[version] = item;
        return item;
    }

    /// <summary>副标题与 Java 版一致：通道 + 版本信息，方便区分预览版与正式版。</summary>
    private static string BuildInstanceInfo(string versionId, string channel)
    {
        var label = ChannelLabel(channel);
        try
        {
            var folder = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, versionId);
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
        hint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
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
            pathHint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            panel.Children.Add(pathHint);
        }

        return panel;
    }

    /// <summary>选中实例：同时写入配置，启动按钮与左右页面都跟随它。</summary>
    private void SelectVersion(string versionId)
    {
        ModBedrockInstance.SelectedVersionId = versionId;
        Config.Launch.BedrockSelectedVersion = versionId;
        // 点一下就让左侧状态和右侧「当前实例」跟着变，不用等确定
        ModMain.frmBedrockLeft?.RefreshStatus();
        ModMain.frmBedrockRight?.RefreshInstanceCard();
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
