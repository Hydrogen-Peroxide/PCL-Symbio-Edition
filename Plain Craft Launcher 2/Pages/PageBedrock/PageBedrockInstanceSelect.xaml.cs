using System.IO;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockInstanceSelect : IRefreshable
{
    public PageBedrockInstanceSelect()
    {
        InitializeComponent();
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
                if (name.Equals("extracted", StringComparison.OrdinalIgnoreCase)) continue;
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

    public void RefreshInstances(string? rootOverride = null)
    {
        PanInstances.Children.Clear();
        var versions = rootOverride is null
            ? ModBedrockInstance.GetLocalVersions()
            : GetVersionsIn(rootOverride);
        // 已经选过的实例优先，避免每次打开都跳回第一个
        var configured = Config.Launch.BedrockSelectedVersion;
        var current = !string.IsNullOrWhiteSpace(configured) && versions.Contains(configured)
            ? configured
            : versions.FirstOrDefault() ?? "";
        ModBedrockInstance.SelectedVersionId = current;

        if (versions.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Lang.Text("Bedrock.Select.NoInstances"),
                Margin = new Thickness(6, 12, 6, 0),
                FontSize = 12.5d,
                TextWrapping = TextWrapping.Wrap
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
            PanInstances.Children.Add(empty);

            // 顺带把正在看的路径写出来，避免“实例都不见了”时不知道在哪个目录里找
            var pathHint = new TextBlock
            {
                Text = ModBedrockInstance.VersionsDirectory,
                Margin = new Thickness(6, 6, 6, 0),
                FontSize = 11.5d,
                TextWrapping = TextWrapping.Wrap
            };
            pathHint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");
            PanInstances.Children.Add(pathHint);
            return;
        }

        foreach (var channel in new[] { "release", "preview", "beta" })
        {
            var grouped = versions.Where(version => ModBedrockInstance.GetChannel(version) == channel).ToList();
            if (grouped.Count == 0) continue;

            PanInstances.Children.Add(BuildGroupHeader(ChannelLabel(channel)));
            foreach (var version in grouped) AddInstanceItem(version, channel);
        }
    }

    private static TextBlock BuildGroupHeader(string text)
    {
        var header = new TextBlock
        {
            Text = text,
            FontSize = 13.5d,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 16, 0, 6)
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2");
        return header;
    }

    private void AddInstanceItem(string version, string channel)
    {
        var item = new MyListItem
        {
            Title = ModBedrockInstance.GetDisplayName(ModBedrockInstance.VersionsDirectory, version),
            Info = version,
            Logo = LogoFor(channel),
            Type = MyListItem.CheckType.RadioBox,
            Height = 46
        };
        var target = version;
        item.Click += (_, _) => SelectVersion(target);
        PanInstances.Children.Add(item);
        if (version == ModBedrockInstance.SelectedVersionId) item.SetChecked(true, false, false);
    }

    /// <summary>选中实例：同时写入配置，启动按钮与左右页面都跟随它。</summary>
    private void SelectVersion(string versionId)
    {
        ModBedrockInstance.SelectedVersionId = versionId;
        Config.Launch.BedrockSelectedVersion = versionId;
        // 点一下就让左侧状态和右侧「当前实例」跟着变，不用等确定
        ModMain.frmBedrockLeft?.RefreshStatus();
        ModMain.frmBedrockRight?.RefreshInstanceCard();
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