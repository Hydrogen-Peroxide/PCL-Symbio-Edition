using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI.Controls;

namespace PCL;

/// <summary>
///     基岩版实例选择浮层：左侧切换数据目录，右侧按通道列出该目录下的版本实例。
/// </summary>
public partial class PageBedrockInstanceSelect
{
    private readonly List<ModBedrockFolders.Folder> _folders = new();
    private readonly int _uuid = ModBase.GetUuid();
    private Action? _onApplied;

    public PageBedrockInstanceSelect()
    {
        InitializeComponent();
        LabTitle.Text = Lang.Text("Bedrock.Select.Title");
        LabFolderHeader.Text = Lang.Text("Bedrock.Select.Folders");
        LabInstanceHeader.Text = Lang.Text("Bedrock.Select.Instances");
        BtnAddFolder.Text = Lang.Text("Bedrock.Select.AddFolder");
        BtnConfirm.Text = Lang.Text("Common.Action.Confirm");
        BtnCancel.Text = Lang.Text("Common.Action.Cancel");
        ShapeLine.StrokeThickness = ModBase.GetWPFSize(1d);

        BtnAddFolder.Click += BtnAddFolder_Click;
        BtnConfirm.Click += BtnConfirm_Click;
        BtnCancel.Click += BtnCancel_Click;
        Loaded += OnLoaded;

        RefreshFolders();
    }

    /// <summary>在启动器窗口中央显示实例选择浮层；onApplied 会在确认后调用。</summary>
    public static void Show(Action? onApplied = null)
    {
        var host = ModMain.frmMain?.PanMain;
        if (host is null) return;

        // 同一时间只保留一个实例选择页面
        foreach (var existing in host.Children.OfType<PageBedrockInstanceSelect>().ToList())
            host.Children.Remove(existing);

        var page = new PageBedrockInstanceSelect { _onApplied = onApplied };
        Panel.SetZIndex(page, 99);
        host.Children.Add(page);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Opacity = 0d;
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaOpacity(this, 1d, 120, 40),
            ModAnimation.AaDouble(i => TransformPos.Y += (double)i, -TransformPos.Y, 280, 40,
                new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak))
        }, "BedrockInstanceSelect " + _uuid);
    }

    #region 数据目录

    private void RefreshFolders()
    {
        _folders.Clear();
        _folders.AddRange(ModBedrockFolders.Enumerate());
        PanFolders.Children.Clear();

        foreach (var folder in _folders)
        {
            var isCurrent = ModBedrockFolders.IsCurrent(folder);
            var item = new MyListItem
            {
                Title = folder.Title,
                Info = folder.Path,
                SvgIcon = isCurrent ? "lucide/folder-open" : "lucide/folder-input",
                Type = MyListItem.CheckType.RadioBox,
                Height = 48
            };
            var target = folder;
            item.Click += (_, _) => SwitchFolder(target);
            PanFolders.Children.Add(item);
            if (isCurrent) item.SetChecked(true, false, false);
        }

        RefreshInstances();
    }

    private void SwitchFolder(ModBedrockFolders.Folder folder)
    {
        if (!Directory.Exists(folder.Path))
        {
            HintService.Hint(Lang.Text("Bedrock.Select.FolderInvalid"), HintType.Error);
            return;
        }

        if (ModBedrockFolders.IsCurrent(folder)) return;

        ModBedrockFolders.Switch(folder);
        // 换了目录就重新挑实例，避免残留上一个目录里的选择
        ModBedrockInstance.SelectedVersionId = "";
        RefreshFolders();
        HintService.Hint(Lang.Text("Bedrock.Select.Applied", folder.Title), HintType.Success);
    }

    private void BtnAddFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Lang.Text("Bedrock.Select.AddFolder"),
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            HintService.Hint(Lang.Text("Bedrock.Select.FolderMissing"), HintType.Error);
            return;
        }

        if (!ModBedrockFolders.LooksValid(path))
        {
            HintService.Hint(Lang.Text("Bedrock.Select.FolderInvalid"), HintType.Error);
            return;
        }

        if (ModBedrockFolders.Add(path) is null)
        {
            HintService.Hint(Lang.Text("Bedrock.Select.FolderExists"), HintType.Warning);
            return;
        }

        HintService.Hint(Lang.Text("Bedrock.Select.FolderAdded", path), HintType.Success);
        RefreshFolders();
    }

    #endregion

    #region 版本实例

    private void RefreshInstances()
    {
        PanInstances.Children.Clear();
        var versions = ModBedrockInstance.GetLocalVersions();
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
            FontSize = 12d,
            Margin = new Thickness(6, 12, 0, 4)
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
        // 点一下就让左侧版本号和右侧「当前实例」跟着变，不用等确定
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

    #endregion

    #region 关闭

    private void BtnConfirm_Click(object sender, MouseButtonEventArgs e)
    {
        var version = ModBedrockInstance.SelectedVersionId;
        if (!string.IsNullOrWhiteSpace(version)) Config.Launch.BedrockSelectedVersion = version;
        if (!string.IsNullOrWhiteSpace(version))
            HintService.Hint(
                Lang.Text("Bedrock.Instance.Selected",
                    ModBedrockInstance.GetDisplayName(ModBedrockInstance.VersionsDirectory, version)),
                HintType.Success);

        Close();
        _onApplied?.Invoke();
    }

    private void BtnCancel_Click(object sender, MouseButtonEventArgs e) => Close();

    private void Close()
    {
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaOpacity(this, -Opacity, 90, 10),
            ModAnimation.AaDouble(i => TransformPos.Y += (double)i, 20d - TransformPos.Y, 130, 0,
                new ModAnimation.AniEaseOutFluent()),
            ModAnimation.AaCode(() => (Parent as Panel)?.Children.Remove(this), after: true)
        }, "BedrockInstanceSelect " + _uuid);
    }

    #endregion
}