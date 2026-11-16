using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App.Localization;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>
///     下载页的 Bedrock 分类：按 正式版 / 预览版 / Beta 通道列出基岩版版本，并负责下载。
///     排版与 Java 版的 <see cref="PageDownloadClient" /> 保持一致（标题 + 通道选择 + 卡片列表）。
/// </summary>
public partial class PageDownloadBedrock
{
    /// <summary>通道顺序与下拉框下标一致。</summary>
    private static readonly string[] Channels = ["release", "preview", "beta"];

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
                if (state is ModBase.LoadState.Failed or ModBase.LoadState.Aborted)
                    ShowHint(Lang.Text("Download.Bedrock.LoadFailed"), true);
            };
        }

        ComboChannel.SelectedIndex = 0;
        if (_versions is null)
        {
            ShowHint(Lang.Text("Download.Bedrock.Loading"));
            ModDownload.dlBedrockChannelListLoader.Start();
        }
        else
        {
            Apply(ModDownload.dlBedrockChannelListLoader.output);
        }
    }

    private void ComboChannel_SelectionChanged(object sender, SelectionChangedEventArgs e) => Render();

    private void Apply(JsonObject? data)
    {
        _versions = data?["versions"] as JsonArray;
        Render();
    }

    private void Render()
    {
        if (PanList is null) return;
        PanList.Children.Clear();
        if (_versions is null || _versions.Count == 0)
        {
            ShowHint(Lang.Text("Download.Bedrock.Empty"), true);
            return;
        }

        var channel = Channels[Math.Clamp(ComboChannel.SelectedIndex, 0, Channels.Length - 1)];
        var items = _versions.OfType<JsonObject>()
            .Where(v => ((string?)v["channel"] ?? "release") == channel)
            .ToList();

        if (items.Count == 0)
        {
            ShowHint(Lang.Text("Download.Bedrock.Empty"), true);
            return;
        }

        // 按系列分组（与基岩版页面一致），系列内保持数据源的降序
        foreach (var group in items.GroupBy(v => (string?)v["series"] ?? ""))
        {
            var card = new MyCard
            {
                Title = $"{group.Key}  ({group.Count()})",
                Margin = new Thickness(0, 0, 0, 15)
            };
            var stack = new StackPanel { Margin = new Thickness(15, 36, 15, 0) };
            foreach (var version in group)
                stack.Children.Add(BuildRow(version));
            card.Children.Add(stack);
            PanList.Children.Add(card);
        }
    }

    private FrameworkElement BuildRow(JsonObject version)
    {
        var id = (string?)version["id"] ?? "";
        var buildType = (string?)version["buildType"] ?? "UWP";
        var available = (bool?)version["available"] ?? true;
        var date = (string?)version["releaseDate"] ?? "";

        var grid = new Grid { Height = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Orientation = Orientation.Horizontal };
        info.Children.Add(new TextBlock
        {
            Text = id, FontSize = 13, VerticalAlignment = VerticalAlignment.Center
        });
        info.Children.Add(new TextBlock
        {
            Text = $"  {buildType}", FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("ColorBrushGray4")
        });
        if (date.Length > 0)
            info.Children.Add(new TextBlock
            {
                Text = $"  {date}", FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("ColorBrushGray4")
            });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var button = new MyButton
        {
            Height = 26, Width = 74, Text = Lang.Text("Download.Bedrock.Download"), Tag = id,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => StartDownload(id);
        if (_downloading.Contains(id)) button.Text = Lang.Text("Download.Bedrock.Downloading");
        if (!available)
        {
            button.IsEnabled = false;
            button.Text = Lang.Text("Download.Bedrock.Unavailable");
        }

        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        return grid;
    }

    private void ShowHint(string text, bool isError = false)
    {
        PanList.Children.Clear();
        PanList.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(4, 8, 4, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource(isError ? "ColorBrushRedLight" : "ColorBrushGray4")
        });
    }

    /// <summary>下载指定基岩版版本到本地版本目录；安装仍在「启动 → 基岩版」中完成。</summary>
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

    private void Refresh() => Render();
}
