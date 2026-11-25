using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Network;
using PCL.Network.Loaders;
using SvgIconControl = PCL.Core.UI.Controls.SvgIcon.SvgIcon;

namespace PCL;

public partial class BedrockManagementView
{
    private bool isLoad;
    private readonly HashSet<string> _downloadingVersions = [];
    private readonly HashSet<string> _localVersionIds = [];
    private readonly Dictionary<string, SymbioIconButton> _versionButtons = [];
    private readonly Dictionary<string, bool> _versionAvailable = [];
    private readonly Dictionary<string, string> _versionBuildTypes = [];
    private static JsonArray? Versions => BedrockVersionCatalog.Versions;
    private string _versionFilter = "all";

    // 状态行当前展示「点击复制安装路径」时，保存可复制的路径；其它状态文案下为 null
    private string? _detailCopyPath;

    private static BedrockLaunchSession Session => BedrockLaunchSession.Current;

    private static string BedrockVersionsDir => ModBedrockInstance.VersionsDirectory;

    public BedrockManagementView()
    {
        InitializeComponent();
        FilterAll.Check += (_, _) =>
        {
            _versionFilter = "all";
            ApplyVersionFilter();
        };
        FilterUwp.Check += (_, _) =>
        {
            _versionFilter = "uwp";
            ApplyVersionFilter();
        };
        FilterGdk.Check += (_, _) =>
        {
            _versionFilter = "gdk";
            ApplyVersionFilter();
        };
        SearchBox.TextChanged += (_, _) => ApplyVersionFilter();
        Loaded += (_, _) =>
        {
            Session.StateChanged += RenderLaunchState;
            Session.LibraryChanged += RefreshBedrockLibrary;
            Session.LogLine += AppendLaunchLog;
            ModSetup.LaunchLogVisibilityChanged += RefreshLaunchLogVisibility;
            BedrockVersionCatalog.Changed += RenderVersionList;
            RenderVersionList();
            RenderLaunchState();
        };
        Unloaded += (_, _) =>
        {
            Session.StateChanged -= RenderLaunchState;
            Session.LibraryChanged -= RefreshBedrockLibrary;
            Session.LogLine -= AppendLaunchLog;
            ModSetup.LaunchLogVisibilityChanged -= RefreshLaunchLogVisibility;
            BedrockVersionCatalog.Changed -= RenderVersionList;
        };
        // 状态文案会被启动、安装等流程频繁改写，统一在这里同步“路径可复制”的提示与悬停内容
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(LabDetail, (_, _) => UpdateDetailCopyState());
        Loaded += BedrockManagementView_Loaded;
    }

    /// <summary>状态行是否为「点击复制安装路径」，据此决定鼠标指针与悬停显示的路径。</summary>
    private void RefreshBedrockLibrary()
    {
        RefreshLocalVersions();
        RefreshUpdateHint();
    }

    private void RenderLaunchState()
    {
        BtnImport.IsEnabled = !Session.IsImporting;
        BtnLaunch.IsEnabled = Session.CanExecute;
        if (Session.LaunchText.Length > 0) BtnLaunch.Text = Session.LaunchText;
        if (Session.Status.Length > 0) LabStatus.Text = Session.Status;
        if (Session.Detail.Length > 0) LabDetail.Text = Session.Detail;
        if (Session.InstalledVersionText.Length > 0) LabVersion.Text = Session.InstalledVersionText;
        ShapeStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, Session.IsBedrockInstalled ? "SyAccentText" : "SyTextTertiary");
        PanLaunchProgress.Visibility = Session.IsLaunching ? Visibility.Visible : Visibility.Collapsed;
        LoadLaunching.State.LoadingState = Session.IsLaunching ? LoadingStatus.Run : LoadingStatus.Stop;
        SymbioMotion.Progress(PanLaunchProgress, ProgressBedrockFinished, ProgressBedrockUnfinished, Session.Progress);
    }

    private void UpdateDetailCopyState()
    {
        var isPathHint = LabDetail.Text == Lang.Text("Bedrock.Status.CopyPathHint");
        _detailCopyPath = isPathHint ? BedrockInstallPath : null;
        LabDetail.ToolTip = string.IsNullOrEmpty(_detailCopyPath) ? null : _detailCopyPath;
        LabDetail.Cursor = string.IsNullOrEmpty(_detailCopyPath) ? null : Cursors.Hand;
    }

    private void LabDetail_Click(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_detailCopyPath)) return;

        try
        {
            Clipboard.SetText(_detailCopyPath);
            HintService.Hint(Lang.Text("Bedrock.Status.PathCopied"), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 复制安装路径失败");
            HintService.Hint(Lang.Text("Bedrock.Status.PathCopyFailed"), HintType.Error);
        }
    }

    #region 下载路径

    /// <summary>刷新界面上的基岩版下载路径。</summary>
    private void UpdateBedrockPathDisplay()
    {
        LabBedrockPath.Text = ModBedrockInstance.DataRoot;
        LabBedrockPath.ToolTip = BedrockVersionsDir;
        BtnBedrockPathReset.Visibility = string.IsNullOrWhiteSpace(Config.Launch.BedrockFolder)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ChangeBedrockPath_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (HasActiveBedrockTask())
        {
            HintService.Hint(Lang.Text("Bedrock.Folder.ChangePathBusy"), HintType.Error);
            return;
        }

        var folder = SystemDialogs.SelectFolder(Lang.Text("Bedrock.Folder.ChangePathTitle"), ModBedrockInstance.DataRoot);
        if (string.IsNullOrWhiteSpace(folder)) return;

        ApplyBedrockPath(folder);
    }

    private void ResetBedrockPath_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (HasActiveBedrockTask())
        {
            HintService.Hint(Lang.Text("Bedrock.Folder.ChangePathBusy"), HintType.Error);
            return;
        }

        ApplyBedrockPath("");
    }

    private bool HasActiveBedrockTask() => _downloadingVersions.Count > 0 || Session.InstallingVersions.Count > 0;

    /// <summary>保存新的下载路径；folder 为空表示恢复默认的启动器目录。</summary>
    private void ApplyBedrockPath(string folder)
    {
        try
        {
            var target = Path.TrimEndingDirectorySeparator(folder.Trim());
            // 选中的就是默认目录时按“未自定义”处理，避免多出一个无意义的记录
            var defaultRoot = Path.TrimEndingDirectorySeparator(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "bedrock"));
            if (string.Equals(target, defaultRoot, StringComparison.OrdinalIgnoreCase))
                target = "";

            if (string.IsNullOrEmpty(target))
            {
                Config.Launch.BedrockFolder = "";
            }
            else
            {
                // 提前创建，路径不可写时立刻提示，避免下载开始后才失败
                Directory.CreateDirectory(target);
                Config.Launch.BedrockFolder = target;
            }

            UpdateBedrockPathDisplay();
            Session.DetectBedrockInstallation();
            HintService.Hint(
                string.IsNullOrEmpty(Config.Launch.BedrockFolder)
                    ? Lang.Text("Bedrock.Folder.PathReset")
                    : Lang.Text("Bedrock.Folder.PathChanged", ModBedrockInstance.DataRoot),
                HintType.Success);
            ModBase.Log($"[Bedrock] 基岩版下载路径已切换到 {ModBedrockInstance.DataRoot}", ModBase.LogLevel.Normal);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Bedrock] 更改基岩版下载路径失败（{folder}）", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Bedrock.Folder.PathChangeFailed", ex.Message));
        }
    }

    #endregion

    private void BedrockManagementView_Loaded(object sender, RoutedEventArgs e)
    {
        if (isLoad) return;
        isLoad = true;

        // 上次启动器退出时若隔离未还原，%APPDATA% 下会残留目录联接，启动时先恢复共享数据目录
        _ = Task.Run(() =>
        {
            if (!ModBedrockIsolation.IsAnyGameRunning())
                ModBedrockIsolation.Restore();
        });

        CheckIsolation.Checked = Config.Launch.BedrockVersionIsolation;
        RefreshPage();
    }

    #region 启动检测

    public void RefreshPage()
    {
        RefreshLaunchLogVisibility();
        UpdateBedrockPathDisplay();
        Session.DetectBedrockInstallation();
        RefreshLocalVersions();
        LoadVersionList();
        RefreshCurrentInstanceCard();
        ModMain.frmMain?.RefreshShell();
    }

    /// <summary>刷新右侧「当前实例」卡片，切换实例后立刻能看到变化。</summary>
    private void RefreshCurrentInstanceCard()
    {
        try
        {
            var versions = ModBedrockInstance.GetLocalVersions();
            var versionId = Config.Launch.BedrockSelectedVersion;
            if (string.IsNullOrWhiteSpace(versionId) || !versions.Contains(versionId))
                versionId = versions.Count > 0 ? versions[0] : "";
            if (string.IsNullOrWhiteSpace(versionId))
            {
                LabCurrentInstance.Text = Lang.Text("Bedrock.Right.Current.None");
                LabCurrentVersion.Text = "";
                LabCurrentLoader.Text = "";
                LabCurrentPath.Text = ModBedrockInstance.VersionsDirectory;
                return;
            }

            LabCurrentInstance.Text =
                ModBedrockInstance.GetDisplayName(ModBedrockInstance.VersionsDirectory, versionId);
            LabCurrentVersion.Text = Lang.Text("Bedrock.Status.Version", versionId) + " · " + ChannelLabel(versionId);
            LabCurrentLoader.Text = Lang.Text("Bedrock.Right.Current.Loader", ModBedrockLoader.GetLoader(versionId));
            LabCurrentPath.Text =
                ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, versionId);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 刷新当前实例卡片失败", ModBase.LogLevel.Debug);
        }
    }

    /// <summary>只刷新「当前实例」卡片，供实例选择浮层实时联动，不做检测与联网。</summary>
    public void RefreshInstanceCard()
    {
        RefreshCurrentInstanceCard();
        ModMain.frmMain?.RefreshShell();
    }

    private static string ChannelLabel(string versionId) => ModBedrockInstance.GetChannel(versionId) switch
    {
        "preview" => Lang.Text("Download.Bedrock.Channel.Preview"),
        "beta" => Lang.Text("Download.Bedrock.Channel.Beta"),
        _ => Lang.Text("Download.Bedrock.Channel.Release")
    };

    private string? _lastLaunchLogLine;
    private bool _logStarted;

    /// <summary>「启动日志」卡片是否显示，跟随设置里的启动日志开关（调试模式强制显示）。</summary>
    public void RefreshLaunchLogVisibility()
    {
        if (CardLaunchLog is null) return;
        CardLaunchLog.Visibility = States.UI.ShowLaunchLog || ModBase.modeDebug
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>把启动过程的每一步写进右侧「启动日志」卡片。</summary>
    private void AppendLaunchLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (string.Equals(line, _lastLaunchLogLine, StringComparison.Ordinal)) return;
        _lastLaunchLogLine = line;

        void Apply()
        {
            if (LabLaunchLog is null) return;
            var builder = new System.Text.StringBuilder();
            if (_logStarted && !string.IsNullOrWhiteSpace(LabLaunchLog.Text))
                builder.Append(LabLaunchLog.Text.TrimEnd('\n')).Append('\n');
            builder.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").Append(line);
            _logStarted = true;
            var all = builder.ToString().Split('\n');
            if (all.Length > 200) all = all[(all.Length - 200)..];
            LabLaunchLog.Text = string.Join('\n', all);
        }

        if (Dispatcher.CheckAccess()) Apply();
        else ModBase.RunInUi(Apply);
    }

    public bool IsBedrockInstalled => Session.IsBedrockInstalled;
    public string? BedrockVersion => Session.BedrockVersion;
    public string? BedrockInstallPath => Session.BedrockInstallPath;

    private void CheckIsolation_OnChange(object sender, bool user)
    {
        // 仅处理用户操作，代码里设置 Checked 时不做任何事
        if (!user || sender is not SymbioCheck checkBox) return;

        if (checkBox.Checked != true)
        {
            if (ModBedrockIsolation.IsAnyGameRunning())
            {
                checkBox.SetChecked(true, false);
                ModBase.Log("[Bedrock] 基岩版正在运行，无法关闭版本隔离", ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Bedrock.Isolation.RunningHint"));
                return;
            }

            ModBedrockIsolation.Restore();
            Config.Launch.BedrockVersionIsolation = false;
            ModBase.Log("[Bedrock] 已关闭基岩版版本隔离", ModBase.LogLevel.Normal);
            return;
        }

        if (!Session.IsPortable || string.IsNullOrEmpty(BedrockInstallPath))
        {
            checkBox.SetChecked(false, false);
            ModBase.Log("[Bedrock] 版本隔离仅支持 GDK 便携版", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Bedrock.Isolation.GdkOnly"));
            return;
        }

        if (ModBedrockIsolation.IsAnyGameRunning())
        {
            checkBox.SetChecked(false, false);
            ModBase.Log("[Bedrock] 请先关闭正在运行的基岩版", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Bedrock.Isolation.RunningHint"));
            return;
        }

        try
        {
            ModBedrockIsolation.Apply(BedrockInstallPath, BedrockVersion ?? Path.GetFileName(BedrockInstallPath));
            Config.Launch.BedrockVersionIsolation = true;
            ModBase.Log("[Bedrock] 已启用基岩版版本隔离", ModBase.LogLevel.Normal);
        }
        catch (Exception ex)
        {
            checkBox.SetChecked(false, false);
            ModBase.Log(ex, "启用基岩版版本隔离失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Bedrock.Isolation.Failed", ex.Message));
        }
    }

    /// <summary>
    ///     版本列表加载完成（或重新检测）后，比较已安装包版本与最新正式版。
    ///     商店版包由 Microsoft Store 负责更新，商店安装的包无法由启动器侧载升级。
    /// </summary>
    private void RefreshUpdateHint()
    {
        if (BtnUpdate is null) return;

        if (!IsBedrockInstalled || !Session.IsStorePackage || Session.IsPortable || Versions is null)
        {
            BtnUpdate.Visibility = Visibility.Collapsed;
            return;
        }

        JsonObject? latest = null;
        foreach (var node in Versions)
        {
            if (node is JsonObject version && version["packageVersion"] is not null)
            {
                latest = version;
                break;
            }
        }

        var latestPackageVersion = latest is null ? "" : (string)latest["packageVersion"] ?? "";
        if (string.IsNullOrEmpty(latestPackageVersion) ||
            string.IsNullOrEmpty(BedrockVersion) ||
            !Version.TryParse(latestPackageVersion, out var latestVer) ||
            !Version.TryParse(BedrockVersion, out var currentVer) ||
            currentVer >= latestVer)
        {
            BtnUpdate.Visibility = Visibility.Collapsed;
            return;
        }

        var latestGameVersion = (string)latest["id"] ?? latestPackageVersion;
        BtnUpdate.Text = Lang.Text("Bedrock.Action.UpdateToVersion", latestGameVersion);
        BtnUpdate.Visibility = Visibility.Visible;
        LabVersion.Text = Lang.Text("Bedrock.Status.VersionWithLatest", BedrockVersion, latestGameVersion);
    }

    private void BtnUpdate_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            // 商店版基岩版由 Microsoft Store 分发，只能通过商店更新
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-windows-store://downloadsandupdates",
                UseShellExecute = true
            });
            LabDetail.Text = Lang.Text("Bedrock.Store.Opened");
            ModBase.Log("[Bedrock] 正在打开 Microsoft Store 更新页面");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开 Microsoft Store 失败", ModBase.LogLevel.Hint,
                Lang.Text("Bedrock.Store.CannotOpen"));
        }
    }

    // Keep the existing page-to-page click entry point for instance pages.
    public void BtnLaunch_Click(object sender, System.Windows.RoutedEventArgs e) => Session.RequestLaunch();


    #endregion

    #region 本地版本管理

    /// <summary>
    ///     把本地版本列表滚到可视区域。基岩版没有独立的「实例选择」页，
    ///     外壳上的「选择实例」按钮就是定位到这块列表。
    /// </summary>
    internal void BringLocalVersionsIntoView()
    {
        try
        {
            if (PanLocalVersions.Children.Count == 0) RefreshLocalVersions();
            PanLocalVersions.BringIntoView();
            ModBase.Log("[Bedrock] 已定位到本地版本列表", ModBase.LogLevel.Debug);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 定位本地版本列表失败", ModBase.LogLevel.Debug);
        }
    }

    /// <summary>
    ///     首次使用时在常见位置探测其它启动器的基岩版版本目录（例如 &lt;盘符&gt;:\PCL2CE\bedrock_versions），
    ///     只在确实含基岩版版本时才登记，避免把无关目录写进配置。返回是否新登记了目录。
    /// </summary>
    private static bool EnsureExternalDirectoriesDiscovered()
    {
        if (ModBedrockInstance.ExternalDirectories.Count > 0) return false;

        var candidates = new List<string>();
        foreach (var drive in new[] { "C", "D", "E", "F" })
        {
            foreach (var folder in new[] { "PCL2CE\\bedrock_versions", "PCL2\\bedrock_versions", "PCL\\bedrock_versions" })
                candidates.Add($@"{drive}:\{folder}");
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (!Directory.Exists(candidate)) continue;
                // 至少要有一个子目录带 MicrosoftGame.Config 或 Minecraft.Windows.exe，才算基岩版版本目录
                var looksLikeBedrock = Directory.EnumerateDirectories(candidate).Any(dir =>
                    ModBedrockGdk.IsGdkBuild(dir) ||
                    File.Exists(Path.Combine(dir, "Minecraft.Windows.exe")));
                if (!looksLikeBedrock) continue;

                ModBedrockInstance.AddExternalDirectory(candidate);
                ModBase.Log($"[Bedrock] 已自动识别外部版本目录：{candidate}", ModBase.LogLevel.Normal);
                return true;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[Bedrock] 探测外部版本目录失败（{candidate}）", ModBase.LogLevel.Debug);
            }
        }

        return false;
    }

    internal void RefreshLocalVersions()
    {
        EnsureExternalDirectoriesDiscovered();
        PanLocalVersions.Children.Clear();
        _localVersionIds.Clear();
        Session.UpdateInstalledVersionId();

        var items = new List<(string Id, string FilePath)>();
        // 主版本目录 + 已登记的外部版本目录都要列出
        var roots = new List<string> { BedrockVersionsDir };
        roots.AddRange(ModBedrockInstance.ExternalDirectories);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                // 跳过安装时的临时解压目录与 cache/loaders 等辅助目录
                var name = Path.GetFileName(dir);
                if (name.Equals("extracted", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("cache", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("loaders", StringComparison.OrdinalIgnoreCase)) continue;

                var files = BedrockLaunchSession.GetPackageFiles(dir);
                // 外部目录多为已解包的 GDK 版本（没有 .msixvc），这类也要列出来
                if (files.Count == 0 && !ModBedrockGdk.IsGdkBuild(dir)) continue;

                if (items.Any(item => string.Equals(item.Id, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                items.Add((name, files.Count > 0 ? files[0] : dir));
            }
        }

        // 与在线列表保持一致：按版本号降序
        items.Sort((a, b) => ModDownload.CompareBedrockVersions(b.Id, a.Id));

        foreach (var item in items)
        {
            _localVersionIds.Add(item.Id);
            PanLocalVersions.Children.Add(BuildLocalCard(item.Id, item.FilePath));
        }

        if (items.Count == 0)
            PanLocalVersions.Children.Add(CreateHintText(Lang.Text("Bedrock.Local.Empty")));

        RefreshVersionRowStates();
    }

    /// <summary>
    ///     构建一个版本实例卡片：版本号、构建类型、包版本与注册状态，右侧常显启动与操作按钮。
    /// </summary>
    private FrameworkElement BuildLocalCard(string versionId, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var fileSize = "";
        try
        {
            fileSize = FormatFileSize(new FileInfo(filePath).Length);
        }
        catch
        {
            // 安装包被占用或已删除时忽略大小
        }

        var isInstalling = Session.InstallingVersions.Contains(versionId);
        // GDK 便携版只要解包完成就可以直接运行，不需要注册系统包。
        // 用统一的目录解析，外部登记的版本目录同样能被识别为「已就绪」。
        var isGdk = BedrockLaunchSession.IsGdkVersion(versionId);
        var gdkReady = isGdk && BedrockLaunchSession.FindGdkReadyDirectory(versionId) is not null;
        var isRegistered = string.Equals(Session.RegisteredVersionId, versionId, StringComparison.OrdinalIgnoreCase);
        var isInstalled = !isInstalling && (gdkReady || isRegistered);
        var isSelected = string.Equals(Config.Launch.BedrockSelectedVersion, versionId, StringComparison.OrdinalIgnoreCase);

        var icon = new SvgIconControl
        {
            Icon = isGdk ? "lucide/gamepad-2" : "lucide/box",
            Width = 17,
            Height = 17,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(SvgIconControl.IconBrushProperty,
            isSelected ? "SyTextOnAccent" : isInstalled ? "SyAccentText" : "SyTextSecondary");

        // 圆角图标底：当前选中的版本用实色突出，未解包的版本用灰底弱化
        var iconTile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon
        };
        iconTile.SetResourceReference(Border.BackgroundProperty,
            isSelected ? "SyAccentText" : isInstalled ? "SyControlStroke" : "SyDivider");

        var displayName = ModBedrockInstance.GetDisplayName(BedrockVersionsDir, versionId);
        var title = new TextBlock
        {
            Text = displayName,
            ToolTip = string.Equals(displayName, versionId, StringComparison.Ordinal) ? null : versionId,
            FontSize = 14.5,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "SyText");

        // 构建类型与安装状态都放在标题行，副标题才能独占整行显示版本信息
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                title,
                CreateChip(isGdk ? "GDK" : "UWP",
                    isGdk ? "SyControlStroke" : "SyDivider",
                    isGdk ? "SyAccentText" : "SyTextSecondary",
                    new Thickness(8, 0, 0, 0)),
                isInstalling
                    ? CreateChip(Lang.Text("Bedrock.Local.Tag.Installing"), "SyDivider", "SyTextSecondary",
                        new Thickness(6, 0, 0, 0))
                    : isInstalled
                        ? CreateChip(Lang.Text("Bedrock.Local.Tag.Installed"), "SyControlStroke", "SyAccentText",
                            new Thickness(6, 0, 0, 0))
                        : CreateChip(Lang.Text("Bedrock.Local.Tag.Downloaded"), "SyDivider", "SyTextSecondary",
                            new Thickness(6, 0, 0, 0))
            }
        };
        // 跟随启动按钮的版本需要明确标出，否则只看图标底色难以分辨
        if (isSelected)
            titleRow.Children.Add(CreateChip(Lang.Text("Bedrock.Local.Tag.Selected"), "SyAccentText", "SyTextOnAccent",
                new Thickness(6, 0, 0, 0)));

        var detailParts = new List<string>();
        if (isInstalled)
        {
            var packageVersion = TryReadExtractedIdentityVersion(versionId);
            if (!string.IsNullOrEmpty(packageVersion)) detailParts.Add(packageVersion);
        }

        if (!string.IsNullOrEmpty(fileSize)) detailParts.Add(fileSize);
        // 版本信息始终包含安装包文件名，方便与下载的包对照
        detailParts.Add(fileName);
        if (gdkReady)
            detailParts.Add(ModBedrockLoader.GetDisplayName(ModBedrockLoader.GetLoader(versionId)));

        var detailText = string.Join("  ·  ", detailParts);
        var detail = new TextBlock
        {
            Text = detailText,
            ToolTip = string.IsNullOrEmpty(detailText) ? null : detailText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "SyTextSecondary");

        var info = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Children = { titleRow, detail }
        };

        // 每个已下载的版本都可以直接启动：未注册的版本会先切换注册再启动
        var launchBtn = new SymbioButton
        {
            Text = Lang.Text("Bedrock.Local.LaunchButton"),
            ColorType = SymbioButton.ColorState.Highlight,
            Height = 30,
            MinWidth = 70,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !isInstalling,
            ToolTip = isGdk
                ? Lang.Text("Bedrock.Local.LaunchGdk")
                : isInstalled
                    ? Lang.Text("Bedrock.Local.Launch")
                    : Lang.Text("Bedrock.Local.SwitchLaunch")
        };
        launchBtn.Click += (_, _) => Session.LaunchLocalVersion(versionId);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { launchBtn }
        };

        var openFolderBtn = new SymbioIconButton
        {
            SvgIcon = "lucide/folder-open",
            Tag = versionId,
            Height = 30,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = Lang.Text("Bedrock.Local.OpenFolder")
        };
        openFolderBtn.Click += LocalOpenFolder_Click;
        buttons.Children.Add(openFolderBtn);

        var renameBtn = new SymbioIconButton
        {
            SvgIcon = "lucide/pencil",
            Tag = versionId,
            Height = 30,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = Lang.Text("Bedrock.Local.Rename")
        };
        renameBtn.Click += LocalRename_Click;
        buttons.Children.Add(renameBtn);

        // 只有已解包的 GDK 便携版才值得复制：复制安装包对 UWP 没有意义
        if (gdkReady)
        {
            var copyBtn = new SymbioIconButton
            {
                SvgIcon = "lucide/copy",
                Tag = versionId,
                Height = 30,
                Margin = new Thickness(6, 0, 0, 0),
                IsEnabled = !isInstalling,
                ToolTip = Lang.Text("Bedrock.Local.Copy")
            };
            copyBtn.Click += LocalCopy_Click;
            buttons.Children.Add(copyBtn);
        }

        // GDK 便携版解包完成后可以在版本卡片里选择加载器：原版不注入，LeviLamina 注入 mods 内的 DLL
        if (gdkReady)
        {
            var loader = ModBedrockLoader.GetLoader(versionId);
            var loaderBtn = new SymbioIconButton
            {
                SvgIcon = "lucide/puzzle",
                Tag = versionId,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                IsEnabled = !isInstalling,
                ToolTip = Lang.Text("Bedrock.Loader.Tooltip", ModBedrockLoader.GetDisplayName(loader))
            };
            loaderBtn.Click += LocalLoader_Click;
            buttons.Children.Add(loaderBtn);
        }

        var installBtn = new SymbioIconButton
        {
            SvgIcon = "lucide/package-plus",
            Tag = versionId,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = !isInstalling
        };
        installBtn.ToolTip = isInstalling
            ? Lang.Text("Bedrock.Local.Installing")
            : isInstalled
                ? Lang.Text("Bedrock.Local.Reinstall")
                : Lang.Text("Bedrock.Local.Install");
        installBtn.Click += LocalInstall_Click;
        buttons.Children.Add(installBtn);

        var deleteBtn = new SymbioIconButton
        {
            SvgIcon = "lucide/trash-2",
            Theme = SymbioIconButton.Themes.Red,
            Tag = versionId,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = !isInstalling
        };
        deleteBtn.ToolTip = isInstalled
            ? Lang.Text("Bedrock.Local.DeleteRegistered")
            : Lang.Text("Bedrock.Local.Delete");
        deleteBtn.Click += LocalDelete_Click;
        buttons.Children.Add(deleteBtn);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(iconTile, 0);
        Grid.SetColumn(info, 1);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(iconTile);
        grid.Children.Add(info);
        grid.Children.Add(buttons);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 0, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid,
            Cursor = Cursors.Hand
        };
        card.SetResourceReference(Border.BackgroundProperty, "SyControlAlt");
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "SySubtleHover");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "SyControlAlt");
        // 点卡片的空白处 = 选中这个实例（切换当前使用的基岩版版本）。
        // 以前这里绑的是「安装/重新安装」，导致点卡片既不会切换版本、还容易误触发重装，
        // 表现就是「切换实例按不动」。安装仍由右侧的安装按钮负责。
        card.MouseLeftButtonUp += (_, _) => SelectLocalVersion(versionId);
        return card;
    }

    /// <summary>
    ///     选中一个本地版本作为当前实例：记住选择、刷新卡片高亮，并把版本应用到左侧状态。
    ///     已解包的 GDK 版本会同时把安装路径指向它，这样「启动基岩版」直接跑该实例。
    /// </summary>
    private void SelectLocalVersion(string versionId)
    {
        if (Session.InstallingVersions.Contains(versionId))
        {
            HintService.Hint(Lang.Text("Bedrock.Local.Installing"), HintType.Info);
            return;
        }

        var dir = BedrockLaunchSession.FindPortableGdkDirectory(versionId);
        if (dir is not null)
        {
            // 已解包的 GDK 版本：直接把它设为当前实例，注入与会话都基于该目录
            Session.ActivateGdkVersion(versionId, dir, launchAfterActivate: false);
        }
        else
        {
            // 只有安装包、还没解包：仅记住选择，启动时再解包
            Session.SelectVersion(versionId);
            Session.NotifyLibraryChanged();
            HintService.Hint(Lang.Text("Bedrock.Local.Selected", versionId), HintType.Success);
        }
    }

    /// <summary>
    ///     版本卡片上的小标签。
    /// </summary>
    private static Border CreateChip(string text, string backgroundKey, string foregroundKey,
        Thickness? margin = null)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.Bold
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);

        var chip = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 2),
            Margin = margin ?? new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = label
        };
        chip.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        return chip;
    }

    /// <summary>
    ///     读取解包目录中 AppxManifest 的 Identity 版本号，用于版本卡片的副标题。
    /// </summary>
    private static string? TryReadExtractedIdentityVersion(string versionId)
    {
        var (_, manifestPath) = BedrockLaunchSession.FindExtractedVersion(versionId);
        if (manifestPath is null) return null;

        try
        {
            var identity = XDocument.Load(manifestPath).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Identity");
            return identity?.Attribute("Version")?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     选择该 GDK 版本的模组加载器：原版不做注入，LeviLamina 注入 mods 目录内的 DLL。
    /// </summary>
    private void LocalLoader_Click(object sender, EventArgs e)
    {
        if (sender is not SymbioIconButton btn || btn.Tag is not string versionId)
            return;

        var current = ModBedrockLoader.GetLoader(versionId);
        var currentIndex = current switch
        {
            ModBedrockLoader.LeviLamina => 1,
            ModBedrockLoader.BLoader => 2,
            _ => 0
        };
        List<MyMsgCardOption> options =
        [
            new()
            {
                Title = Lang.Text("Bedrock.Loader.Vanilla"),
                Description = Lang.Text("Bedrock.Loader.Vanilla.Description"),
                SvgIcon = "lucide/box",
                Tag = currentIndex == 0 ? Lang.Text("Bedrock.Loader.CurrentTag") : ""
            },
            new()
            {
                Title = Lang.Text("Bedrock.Loader.LeviLamina"),
                Description = Lang.Text("Bedrock.Loader.LeviLamina.Description"),
                SvgIcon = "lucide/puzzle",
                Tag = currentIndex == 1 ? Lang.Text("Bedrock.Loader.CurrentTag") : ""
            },
            new()
            {
                Title = Lang.Text("Bedrock.Loader.BLoader"),
                Description = Lang.Text("Bedrock.Loader.BLoader.Description"),
                SvgIcon = "lucide/boxes",
                Tag = currentIndex == 2 ? Lang.Text("Bedrock.Loader.CurrentTag") : ""
            }
        ];

        var selected = ModMain.MyMsgBoxCardSelect(options, currentIndex,
            Lang.Text("Bedrock.Loader.SelectTitle"),
            Lang.Text("Bedrock.Loader.SelectCaption", versionId),
            button2: Lang.Text("Common.Action.Cancel"));
        if (selected is null) return;

        ModBedrockLoader.SetLoader(versionId, selected switch
        {
            1 => ModBedrockLoader.LeviLamina,
            2 => ModBedrockLoader.BLoader,
            _ => ModBedrockLoader.Vanilla
        });
        RefreshLocalVersions();
    }

    private void LocalInstall_Click(object sender, EventArgs e)
    {
        if (sender is not SymbioIconButton btn || btn.Tag is not string versionId)
            return;

        if (Session.InstallingVersions.Contains(versionId))
            return;

        var dir = ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId);
        var allFiles = BedrockLaunchSession.GetPackageFiles(dir);
        if (allFiles.Count == 0) return;

        Session.StartInstall(allFiles[0], versionId);
    }

    private void LocalOpenFolder_Click(object sender, EventArgs e)
    {
        if (sender is not SymbioIconButton button || button.Tag is not string versionId)
            return;

        try
        {
            var folder = ModBedrockInstance.GetInstanceFolder(BedrockVersionsDir, versionId);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException(folder);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开基岩版实例目录失败", ModBase.LogLevel.Hint);
            HintService.Hint(Lang.Text("Bedrock.Local.OpenFolderFailed", ex.Message), HintType.Error);
        }
    }

    private void LocalRename_Click(object sender, EventArgs e)
    {
        if (sender is not SymbioIconButton button || button.Tag is not string versionId)
            return;

        var current = ModBedrockInstance.GetDisplayName(BedrockVersionsDir, versionId);
        var input = ModMain.MyMsgBoxInput(
            Lang.Text("Bedrock.Local.RenameTitle"),
            Lang.Text("Bedrock.Local.RenameMessage"),
            current,
            hintText: Lang.Text("Bedrock.Local.RenameHint"),
            button1: Lang.Text("Common.Action.Confirm"),
            button2: Lang.Text("Common.Action.Cancel"));
        if (input is null) return;

        var name = input.Trim();
        if (name.Length == 0 || name.Length > 32 || name.Any(char.IsControl))
        {
            HintService.Hint(Lang.Text("Bedrock.Local.RenameInvalid"), HintType.Error);
            return;
        }

        try
        {
            ModBedrockInstance.SetDisplayName(BedrockVersionsDir, versionId, name);
            RefreshLocalVersions();
            ModBase.Log($"[Bedrock] 实例 {versionId} 显示名改为 {name}", ModBase.LogLevel.Normal,
                userSummary: Lang.Text("Bedrock.Local.Renamed", name));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "重命名基岩版实例失败", ModBase.LogLevel.Hint);
            HintService.Hint(Lang.Text("Bedrock.Local.RenameFailed", ex.Message), HintType.Error);
        }
    }

    private void LocalCopy_Click(object sender, EventArgs e)
    {
        if (sender is not SymbioIconButton button || button.Tag is not string versionId)
            return;
        if (Session.InstallingVersions.Contains(versionId)) return;

        if (ModBedrockIsolation.IsAnyGameRunning())
        {
            HintService.Hint(Lang.Text("Bedrock.Local.RunningHint"), HintType.Error);
            return;
        }

        var displayName = ModBedrockInstance.GetDisplayName(BedrockVersionsDir, versionId);
        if (ModMain.MyMsgBox(
                Lang.Text("Bedrock.Local.CopyConfirmMessage", displayName),
                Lang.Text("Bedrock.Local.CopyConfirmTitle"),
                Lang.Text("Bedrock.Local.CopyConfirmButton"),
                Lang.Text("Common.Action.Cancel")) != 1)
            return;

        var newVersionId = ModBedrockInstance.CreateCopyId(BedrockVersionsDir, versionId);
        Session.CopyVersion(versionId, newVersionId);
    }

    private void LocalDelete_Click(object sender, EventArgs e)
    {
        if (sender is not SymbioIconButton btn || btn.Tag is not string versionId)
            return;

        var packageDir = ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId);
        var extractDir = ModBedrockUwp.GetVersionDirectory(
            Path.Combine(BedrockVersionsDir, "extracted"), versionId);
        var isRegistered = string.Equals(Session.RegisteredVersionId, versionId, StringComparison.OrdinalIgnoreCase);
        var isGdk = BedrockLaunchSession.IsGdkVersion(versionId);

        var displayName = ModBedrockInstance.GetDisplayName(BedrockVersionsDir, versionId);
        if (ModMain.MyMsgBox(
                Lang.Text("Bedrock.Local.DeleteConfirmMessage", displayName),
                Lang.Text("Bedrock.Local.DeleteConfirmTitle"),
                Lang.Text("Bedrock.Local.DeleteConfirmButton"),
                Lang.Text("Common.Action.Cancel"), isWarn: true) != 1)
            return;

        Task.Run(() =>
        {
            try
            {
                if (ModBedrockIsolation.IsAnyGameRunning())
                    throw new InvalidOperationException(Lang.Text("Bedrock.Local.RunningHint"));

                // GDK 便携版可能以开发模式注册过；只注销身份与目录都匹配的注册，避免误删商店版或其它版本
                if (isGdk)
                    ModBedrockRegistration.UnregisterPortable(extractDir);
                // 正在使用的 UWP 版本要先卸载，否则删除目录后会留下失效的包注册
                else if (isRegistered)
                    ModBedrockUwp.UninstallPreservingData();

                if (Directory.Exists(packageDir))
                    Directory.Delete(packageDir, true);
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);

                ModBase.Log($"[Bedrock] 已删除本地版本: {versionId}",
                    ModBase.LogLevel.Normal,
                    userSummary: Lang.Text("Bedrock.Local.Deleted", versionId));
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "删除本地版本失败",
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Bedrock.Local.DeleteFailed", ex.Message));
                return;
            }

            ModBase.RunInUi(() =>
            {
                if (isRegistered)
                {
                    Session.ClearInstallation();
                    Session.DetectBedrockInstallation();
                }
                else
                {
                    RefreshLocalVersions();
                }
            });
        });
    }

    /// <summary>
    ///     导入手动下载的旧版本安装包：读取包内 Identity 版本号 → 复制到版本目录 → 直接开始安装。
    /// </summary>
    private void ImportPackage_Click(object sender, System.Windows.RoutedEventArgs e) => ModMain.frmMain.ImportBedrockPackage();

    private static TextBlock CreateHintText(string text)
    {
        var hint = new TextBlock
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("SyTextSecondary"),
            Margin = new Thickness(5d, 8d, 5d, 8d)
        };
        return hint;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    #endregion

    #region 在线版本下载

    private void LoadVersionList()
    {
        BedrockVersionCatalog.Load();
    }

    private void RenderVersionList()
    {
        try
        {
            if (Versions is null) return;
            ApplyVersionFilter();
            RefreshUpdateHint();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "构建基岩版版本列表失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Bedrock.List.BuildFailed"));
        }
    }

    /// <summary>
    ///     按搜索框关键词与 UWP/GDK 过滤器重建版本列表，并按系列分组为可折叠卡片。
    /// </summary>
    private void ApplyVersionFilter()
    {
        if (PanVersionList is null) return;

        PanVersionList.Children.Clear();
        _versionButtons.Clear();
        _versionAvailable.Clear();
        _versionBuildTypes.Clear();

        if (Versions is null || Versions.Count == 0)
        {
            LabVersionSummary.Text = Lang.Text("Bedrock.List.Empty");
            PanVersionList.Children.Add(CreateHintText(Lang.Text("Bedrock.List.EmptyHint")));
            return;
        }

        var keyword = (SearchBox.Text ?? "").Trim();

        // 数据已按版本号降序排列，这里顺序分组即可
        var grouped = new List<(string Series, List<JsonObject> Items)>();
        var total = 0;

        foreach (var node in Versions)
        {
            if (node is not JsonObject version) continue;

            var versionId = (string)version["id"] ?? "";
            var buildType = (string)version["buildType"] ?? "UWP";

            if (keyword.Length > 0 && !versionId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_versionFilter == "uwp" && !buildType.Equals("UWP", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_versionFilter == "gdk" && !buildType.Equals("GDK", StringComparison.OrdinalIgnoreCase))
                continue;

            var series = (string)version["series"] ?? Lang.Text("Bedrock.List.OtherSeries");
            if (grouped.Count == 0 || grouped[^1].Series != series)
                grouped.Add((series, new List<JsonObject>()));
            grouped[^1].Items.Add(version);
            total++;
        }

        LabVersionSummary.Text = keyword.Length > 0
            ? Lang.Text("Bedrock.List.Found", total)
            : Lang.Text("Bedrock.List.Total", total);

        if (total == 0)
        {
            LabVersionSummary.Text = Lang.Text("Bedrock.List.NoMatch");
            PanVersionList.Children.Add(CreateHintText(Lang.Text("Bedrock.List.NoMatchHint")));
            return;
        }

        // 搜索时展开全部结果，否则只展开最新的一组
        var expandAll = keyword.Length > 0;
        for (var i = 0; i < grouped.Count; i++)
            PanVersionList.Children.Add(BuildSeriesCard(grouped[i].Series, grouped[i].Items, expandAll || i == 0));
    }

    /// <summary>
    ///     构建一个版本系列的折叠卡片。展开状态的卡片需要手动安装内容。
    /// </summary>
    private WorkspaceCard BuildSeriesCard(string series, List<JsonObject> items, bool expand)
    {
        var content = new StackPanel
        {
            Tag = "install",
            Margin = new Thickness(15, 36, 15, 0)
        };

        void Install(StackPanel stack)
        {
            foreach (var version in items)
                stack.Children.Add(BuildVersionRow(version));
        }

        var card = new WorkspaceCard
        {
            Title = $"{series}  ({items.Count})",
            Margin = new Thickness(0, 0, 0, 8),
            SwapControl = content,
            InstallMethod = Install
        };
        card.Children.Add(content);

        if (expand)
            card.StackInstall();
        else
            card.IsSwapped = true;

        return card;
    }

    private SelectionRow BuildVersionRow(JsonObject version)
    {
        var versionId = (string)version["id"] ?? Lang.Text("Bedrock.List.UnknownVersion");
        var releaseDate = (string)version["releaseDate"] ?? "";
        var buildType = (string)version["buildType"] ?? "UWP";
        var available = (bool?)version["available"] ?? true;
        var webAvailable = (bool?)version["webAvailable"] ?? available;

        _versionAvailable[versionId] = available;
        _versionBuildTypes[versionId] = buildType;

        SymbioIconButton? downloadBtn = null;
        if (available)
        {
            downloadBtn = new SymbioIconButton
            {
                SvgIcon = "lucide/download",
                Tag = versionId
            };
            downloadBtn.Click += BedrockVersionDownload_Click;
            _versionButtons[versionId] = downloadBtn;
        }

        var tags = new List<string> { buildType };
        // GDK 版为 .msixvc 便携包，解包后直接运行，可与已注册的 UWP 版本共存
        if (buildType.Equals("GDK", StringComparison.OrdinalIgnoreCase)) tags.Add(Lang.Text("Bedrock.List.GdkPortable"));

        var row = new SelectionRow
        {
            Title = versionId,
            Info = releaseDate,
            Tags = tags,
            Type = available ? SelectionRow.CheckType.Clickable : SelectionRow.CheckType.None,
            Height = 42,
            SvgIcon = "lucide/package",
            Tag = versionId
        };

        if (available)
        {
            row.Buttons = new[] { downloadBtn! };
            row.Click += (_, _) => BedrockVersionDownload_Click(downloadBtn!, EventArgs.Empty);
        }
        else if (webAvailable)
        {
            // 应用内没有下载源，但 mcappx 站上有该版本：改为常显的「网站下载」文字入口，点击打开对应系列页
            var webBtn = new SymbioButton
            { IsTextAction = true, Text = Lang.Text("Bedrock.Version.WebDownload"),
                FontSize = 12,
                Height = 18,
                Padding = new Thickness(2, 0, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
                ToolTip = Lang.Text("Bedrock.Version.WebDownloadTip"),
                Tag = versionId
            };
            webBtn.Click += BedrockVersionWebDownload_Click;
            SelectionRow.SetColumnSpan(webBtn, 10);
            SelectionRow.SetRowSpan(webBtn, 10);
            row.MinPaddingRight = 78;
            row.Children.Add(webBtn);
        }
        else
        {
            // 应用内没有下载源，mcappx 站上也没有这个版本：只标注无下载途径，不提供任何入口
            var noWayText = new TextBlock
            {
                Text = Lang.Text("Bedrock.List.NoSource"),
                FontSize = 12,
                Foreground = (Brush)FindResource("SyTextSecondary"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
                IsHitTestVisible = false
            };
            SelectionRow.SetColumnSpan(noWayText, 10);
            SelectionRow.SetRowSpan(noWayText, 10);
            row.MinPaddingRight = 78;
            row.Children.Add(noWayText);
        }

        UpdateVersionButtonState(versionId);
        return row;
    }

    private void BedrockVersionWebDownload_Click(object sender, EventArgs e)
    {
        if (sender is not SymbioButton btn || btn.Tag is not string versionId) return;

        var pageUrl = GetMcAppxPageUrl(versionId);
        ModBase.OpenWebsite(pageUrl);
        LabDetail.Text = Lang.Text("Bedrock.Version.PageOpened", versionId);
    }

    /// <summary>
    ///     刷新单个版本行右侧按钮的图标、可用性与提示。
    /// </summary>
    private void UpdateVersionButtonState(string versionId)
    {
        if (!_versionButtons.TryGetValue(versionId, out var btn)) return;

        var buildType = _versionBuildTypes.TryGetValue(versionId, out var type) ? type : "UWP";
        var isGdk = buildType.Equals("GDK", StringComparison.OrdinalIgnoreCase);

        if (_downloadingVersions.Contains(versionId))
        {
            btn.SvgIcon = "lucide/refresh-cw";
            btn.IsEnabled = false;
            btn.ToolTip = Lang.Text("Bedrock.Version.Downloading");
        }
        else if (Session.InstallingVersions.Contains(versionId))
        {
            btn.SvgIcon = "lucide/refresh-cw";
            btn.IsEnabled = false;
            btn.ToolTip = Lang.Text("Bedrock.Local.Installing");
        }
        else if (_localVersionIds.Contains(versionId))
        {
            btn.SvgIcon = "lucide/circle-check";
            btn.IsEnabled = false;
            btn.ToolTip = isGdk
                ? Lang.Text("Bedrock.Version.DownloadedGdk")
                : Lang.Text("Bedrock.Version.Downloaded");
        }
        else
        {
            btn.SvgIcon = "lucide/download";
            btn.IsEnabled = true;
            btn.ToolTip = Lang.Text("Bedrock.Version.DownloadTooltip");
        }
    }

    private void RefreshVersionRowStates()
    {
        foreach (var versionId in _versionButtons.Keys.ToList())
            UpdateVersionButtonState(versionId);
    }

    /// <summary>
    ///     生成 mcappx 上对应版本的页面地址。
    ///     mcappx 的路径是「基岩版 / 系列」，而不是完整版本号：直接拼完整版本号会打开 404 页面。
    ///     26.x 起版本号第二段是补丁号，系列按十位归并（26.45 属于 26.40）；
    ///     更早的版本第二段本身就是系列（0.12.0 属于 0.12，1.21.120.21 属于 1.21）。
    /// </summary>
    private static string GetMcAppxPageUrl(string versionId)
    {
        var parts = versionId.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor))
        {
            var seriesMinor = major >= 20 ? minor / 10 * 10 : minor;
            return $"https://www.mcappx.com/bedrock/{major}.{seriesMinor}/";
        }
        return "https://www.mcappx.com/bedrock/";
    }

    private async void BedrockVersionDownload_Click(object sender, EventArgs e)
    {
        if (sender is not SymbioIconButton btn || btn.Tag is not string versionId)
            return;

        // 没有 Appx 下载源的版本不提供任何下载入口
        if (_versionAvailable.TryGetValue(versionId, out var available) && !available)
            return;

        if (_downloadingVersions.Contains(versionId))
            return;

        var buildType = ModDownload.GetBedrockBuildType(versionId);

        _downloadingVersions.Add(versionId);
        UpdateVersionButtonState(versionId);

        try
        {
            var downloadInfo = await ModDownload.GetBedrockDownloadUrlsAsync(versionId);
            if (downloadInfo is not null)
            {
                var (urls, md5, resolvedBuildType) = downloadInfo.Value;
                if (urls.Count > 0)
                {
                    StartBedrockDownload(urls, md5, versionId, resolvedBuildType);
                    return;
                }
            }

            _downloadingVersions.Remove(versionId);
            UpdateVersionButtonState(versionId);

            var webUrl = GetMcAppxPageUrl(versionId);
            ModBase.OpenWebsite(webUrl);
            LabDetail.Text = Lang.Text("Bedrock.Version.PageOpened", versionId);
            ModBase.Log(
                $"[Bedrock] 无法解析 {versionId} 的下载地址（{buildType}），打开网页: {webUrl}",
                ModBase.LogLevel.Normal,
                userSummary: Lang.Text("Bedrock.Download.OpeningPage", versionId));
        }
        catch (Exception ex)
        {
            _downloadingVersions.Remove(versionId);
            UpdateVersionButtonState(versionId);
            ModBase.Log(ex, "解析基岩版下载地址失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Bedrock.Download.ResolveFailedSummary", versionId, ex.Message));
        }
    }

    private void StartBedrockDownload(List<string> urls, string? md5, string versionId, string buildType)
    {
        try
        {
            var saveDir = ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId);
            Directory.CreateDirectory(saveDir);

            // GDK 版是 .msixvc，UWP 版是 .appx
            var extension = buildType.Equals("GDK", StringComparison.OrdinalIgnoreCase) ? ".msixvc" : ".appx";
            var fileName = "Minecraft_Bedrock_" + versionId + extension;

            // FE3 解析出的地址不带扩展名，此时保留默认文件名
            try
            {
                var uri = new Uri(urls[0]);
                var seg = uri.Segments[^1].TrimEnd('/');
                if (BedrockLaunchSession.IsPackageFileName(seg))
                    fileName = seg;
            }
            catch { }

            var savePath = Path.Combine(saveDir, fileName);

            var checker = md5 is not null
                ? new ModBase.FileChecker(hash: md5, canUseExistsFile: true)
                : new ModBase.FileChecker(canUseExistsFile: true);

            var downloadFile = new DownloadFile(urls, savePath, checker);

            var loaderName = Lang.Text("Bedrock.Download.Title") + " " + versionId;
            var downloadLoader = new LoaderDownload(
                loaderName,
                new List<DownloadFile> { downloadFile }
            );

            var combo = new ModLoader.LoaderCombo<string>(
                loaderName,
                new List<ModLoader.LoaderBase> { downloadLoader }
            );

            _downloadingVersions.Add(versionId);
            UpdateVersionButtonState(versionId);

            // 下载完成后刷新本地版本列表
            combo.OnStateChangedUi += (_, newState, _) =>
            {
                if (newState is ModBase.LoadState.Finished)
                {
                    _downloadingVersions.Remove(versionId);
                    ModBase.RunInUi(() =>
                    {
                        RefreshLocalVersions();
                        LabStatus.Text = Lang.Text("Bedrock.Version.DownloadComplete");
                        LabDetail.Text = buildType.Equals("GDK", StringComparison.OrdinalIgnoreCase)
                            ? Lang.Text("Bedrock.Version.DownloadSavedGdk", versionId)
                            : Lang.Text("Bedrock.Version.DownloadSaved", versionId);
                    });
                }
                else if (newState is ModBase.LoadState.Failed or ModBase.LoadState.Aborted)
                {
                    _downloadingVersions.Remove(versionId);
                    ModBase.RunInUi(() => UpdateVersionButtonState(versionId));
                }
            };

            ModLoader.LoaderTaskbarAdd(combo);
            combo.Start();

            ModBase.Log(
                $"[Bedrock] 下载任务已添加: {versionId} ({urls.Count} 个 CDN 源)",
                ModBase.LogLevel.Normal,
                userSummary: Lang.Text("Bedrock.Download.Started", versionId, urls.Count));
        }
        catch (Exception ex)
        {
            _downloadingVersions.Remove(versionId);
            UpdateVersionButtonState(versionId);
            ModBase.Log(ex, "基岩版下载失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Bedrock.Download.FailedSummary", ex.Message));
        }
    }

    #endregion

    #region 安装


    #endregion
}
