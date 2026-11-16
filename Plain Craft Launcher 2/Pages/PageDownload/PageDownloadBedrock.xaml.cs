using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.App.Localization;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>
///     下载页的 Bedrock 分类，排版对齐 Java 组的下载页：
///     顶部「最新版本」卡片（最新正式版 + 最新预览版），下面按通道分组的可折叠卡片。
/// </summary>
public partial class PageDownloadBedrock
{
    private static readonly (string Channel, string TitleKey)[] Sections =
    [
        ("release", "Download.Bedrock.Channel.Release"),
        ("preview", "Download.Bedrock.Channel.Preview"),
        ("beta", "Download.Bedrock.Channel.Beta")
    ];

    private readonly HashSet<string> _downloading = new(StringComparer.OrdinalIgnoreCase);
    private JsonArray? _versions;
    private bool _hooked;

    public PageDownloadBedrock()
    {
        InitializeComponent();
        Loaded += (_, _) => Init();
    }

    private void Init()
    {
        if (!_hooked)
        {
            _hooked = true;
            ModDownload.dlBedrockChannelListLoader.PreviewFinish += _ =>
                ModBase.RunInUiWait(() => Apply(ModDownload.dlBedrockChannelListLoader.output));
            ModDownload.dlBedrockChannelListLoader.OnStateChangedUi += (_, state, _) =>
            {
                if (state is ModBase.LoadState.Finished)
                    ModBase.RunInUi(() => Apply(ModDownload.dlBedrockChannelListLoader.output));
                else if (state is ModBase.LoadState.Failed or ModBase.LoadState.Aborted)
                    ModBase.RunInUi(() => ShowHint(Lang.Text("Download.Bedrock.LoadFailed"), true));
            };
        }

        if (ModDownload.dlBedrockChannelListLoader.output is not null)
        {
            Apply(ModDownload.dlBedrockChannelListLoader.output);
            return;
        }

        PanLoad.Visibility = Visibility.Visible;
        ModDownload.dlBedrockChannelListLoader.Start();
    }

    private void Apply(JsonObject? data)
    {
        PanLoad.Visibility = Visibility.Collapsed;
        _versions = data?["versions"] as JsonArray;
        PanMain.Children.Clear();

        if (_versions is null || _versions.Count == 0)
        {
            ShowHint(Lang.Text("Download.Bedrock.Empty"), true);
            return;
        }

        var all = _versions.OfType<JsonObject>().ToList();
        PanMain.Children.Add(BuildLatestCard(all));
        foreach (var (channel, titleKey) in Sections)
        {
            var items = all.Where(v => ((string?)v["channel"] ?? "release") == channel).ToList();
            if (items.Count == 0) continue;
            PanMain.Children.Add(BuildSection(Lang.Text(titleKey), items));
        }
    }

    /// <summary>顶部「最新版本」卡片：沿用 Java 下载页的 MyCard + StackInstall 写法。</summary>
    private FrameworkElement BuildLatestCard(List<JsonObject> all)
    {
        var card = new MyCard { Title = Lang.Text("Download.Bedrock.LatestTitle"), Margin = new Thickness(0d, 0d, 0d, 15d) };
        var latest = new List<JsonObject>();
        var release = all.FirstOrDefault(v => ((string?)v["channel"] ?? "") == "release");
        if (release is not null)
        {
            var item = (JsonObject)release.DeepClone();
            item["lore"] = Lang.Text("Download.Bedrock.Latest.Release") + "，" +
                           Lang.Text("Download.Bedrock.PublishedAt", (string?)release["releaseDate"] ?? "");
            latest.Add(item);
        }

        var preview = all.FirstOrDefault(v => ((string?)v["channel"] ?? "") == "preview");
        if (preview is not null)
        {
            var item = (JsonObject)preview.DeepClone();
            item["lore"] = Lang.Text("Download.Bedrock.Latest.Preview") + "，" +
                           Lang.Text("Download.Bedrock.PublishedAt", (string?)preview["releaseDate"] ?? "");
            latest.Add(item);
        }

        var panInfo = new StackPanel
        {
            Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d), VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform(0d, 0d), Tag = latest
        };

        void PutMethod(StackPanel stack)
        {
            foreach (var item in (IEnumerable)stack.Tag)
                stack.Children.Add(BuildRow((JsonObject)item));
        }

        MyCard.StackInstall(ref panInfo, PutMethod);
        card.Children.Add(panInfo);
        return card;
    }

    /// <summary>按通道分组的折叠卡片，折叠机制与 Java 下载页一致（SwapControl + IsSwapped）。</summary>
    private FrameworkElement BuildSection(string title, List<JsonObject> items)
    {
        var card = new MyCard { Title = $"{title} ({items.Count})", Margin = new Thickness(0d, 0d, 0d, 15d) };
        var stack = new StackPanel
        {
            Margin = new Thickness(20d, MyCard.SwapedHeight, 18d, 0d), VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform(0d, 0d), Tag = items
        };

        void PutMethod(StackPanel target)
        {
            foreach (var item in (IEnumerable)target.Tag)
                target.Children.Add(BuildRow((JsonObject)item));
        }

        card.Children.Add(stack);
        card.SwapControl = stack;
        card.InstallMethod = PutMethod;
        card.IsSwapped = true;
        return card;
    }
    private FrameworkElement BuildRow(JsonObject version)
    {
        var id = (string?)version["id"] ?? "";
        var channel = (string?)version["channel"] ?? "release";
        var buildType = (string?)version["buildType"] ?? "UWP";
        var date = (string?)version["releaseDate"] ?? "";
        var lore = (string?)version["lore"];

        // 与 Java 下载页一致：正式版草方块、预览版命令方块、Beta 金块
        var logo = channel switch
        {
            "preview" => ModBase.pathImage + "Blocks/CommandBlock.png",
            "beta" => ModBase.pathImage + "Blocks/GoldBlock.png",
            _ => ModBase.pathImage + "Blocks/Grass.png"
        };

        var info = lore is not null
            ? lore
            : (buildType + (date.Length > 0 ? " | " + date : ""));

        var item = new MyListItem
        {
            Logo = logo,
            SnapsToDevicePixels = true,
            Title = id,
            Info = info,
            Height = 42d,
            Type = MyListItem.CheckType.Clickable,
            Tag = id
        };
        item.Click += (_, _) => StartDownload(id);
        return item;
    }
    private void ShowHint(string text, bool isError = false)
    {
        PanLoad.Visibility = Visibility.Collapsed;
        PanMain.Children.Clear();
        PanMain.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(4, 8, 4, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(isError ? Color.FromRgb(0xD9, 0x53, 0x4F) : Color.FromRgb(0x8A, 0x8A, 0x8A))
        });
    }

    /// <summary>下载指定版本到本地版本目录；安装仍在「启动 → 基岩版」中完成。</summary>
    private async void StartDownload(string versionId)
    {
        if (_downloading.Contains(versionId)) return;
        _downloading.Add(versionId);
        Refresh();

        try
        {
            var info = await ModDownload.GetBedrockDownloadUrlsAsync(versionId);
            if (info is null || info.Value.Urls.Count == 0)
            {
                ModBase.Log($"[Download] 无法解析基岩版 {versionId} 的下载地址", ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Download.Bedrock.ResolveFailed", versionId));
                return;
            }

            var (urls, md5, buildType) = info.Value;
            var saveDir = ModBedrockUwp.GetVersionDirectory(ModBedrockInstance.VersionsDirectory, versionId);
            Directory.CreateDirectory(saveDir);
            var extension = buildType.Equals("GDK", StringComparison.OrdinalIgnoreCase) ? ".msixvc" : ".appx";
            var savePath = Path.Combine(saveDir, "Minecraft_Bedrock_" + versionId + extension);

            var checker = md5 is not null
                ? new ModBase.FileChecker(hash: md5, canUseExistsFile: true)
                : new ModBase.FileChecker(canUseExistsFile: true);
            var downloadFile = new DownloadFile(urls, savePath, checker);

            var name = Lang.Text("Download.Bedrock.TaskName") + " " + versionId;
            var combo = new ModLoader.LoaderCombo<string>(name,
                new List<ModLoader.LoaderBase> { new LoaderDownload(name, new List<DownloadFile> { downloadFile }) });
            combo.OnStateChangedUi += (_, state, _) =>
            {
                if (state is not (ModBase.LoadState.Finished or ModBase.LoadState.Failed or ModBase.LoadState.Aborted))
                    return;
                _downloading.Remove(versionId);
                ModBase.RunInUi(Refresh);
                if (state is ModBase.LoadState.Finished)
                    HintService.Hint(Lang.Text("Download.Bedrock.Finished", versionId), HintType.Success);
            };

            ModLoader.LoaderTaskbarAdd(combo);
            combo.Start();
            HintService.Hint(Lang.Text("Download.Bedrock.Started", versionId));
            ModBase.Log($"[Download] 基岩版下载任务已添加: {versionId}（{urls.Count} 个源）", ModBase.LogLevel.Normal);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"基岩版 {versionId} 下载失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Download.Bedrock.Failed", ex.Message));
        }
        finally
        {
            if (!_downloading.Contains(versionId)) Refresh();
        }
    }

    private void Refresh() => Apply(ModDownload.dlBedrockChannelListLoader.output);
}
