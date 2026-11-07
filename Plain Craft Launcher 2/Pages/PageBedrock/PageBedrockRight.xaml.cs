using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

public partial class PageBedrockRight
{
    private bool isLoad;
    private readonly HashSet<string> _downloadingVersions = [];
    private readonly HashSet<string> _installingVersions = [];
    private readonly HashSet<string> _localVersionIds = [];
    private readonly Dictionary<string, MyIconButton> _versionButtons = [];
    private readonly Dictionary<string, bool> _versionAvailable = [];
    private readonly Dictionary<string, string> _versionBuildTypes = [];
    private JsonArray? _versionCache;
    private string _versionFilter = "all";

    // 当前已注册（正在使用）的本地版本。UWP 包的 PFN 固定，同一时刻只能注册一个版本，
    // 因此“共存”体现为每个版本各占一个解压目录，启动时按目录切换注册。
    private string? _registeredVersionId;
    private bool _switchingVersion;

    // 状态行当前展示「点击复制安装路径」时，保存可复制的路径；其它状态文案下为 null
    private string? _detailCopyPath;

    // 启动进度：实际阶段进度 + 100ms 平滑显示（与 Java 版启动页一致）
    private readonly DispatcherTimer _launchProgressTimer;
    private double _launchActualProgress;
    private double _launchShownProgress;
    private bool _launching;
    private int _launchGeneration;

    private static string BedrockVersionsDir => ModBedrockInstance.VersionsDirectory;

    public PageBedrockRight()
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
        _launchProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _launchProgressTimer.Tick += LaunchProgressTimer_Tick;
        // 状态文案会被启动、安装等流程频繁改写，统一在这里同步“路径可复制”的提示与悬停内容
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(LabDetail, (_, _) => UpdateDetailCopyState());
        Loaded += PageBedrockRight_Loaded;
    }

    /// <summary>状态行是否为「点击复制安装路径」，据此决定鼠标指针与悬停显示的路径。</summary>
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

    private void ChangeBedrockPath_Click(object sender, MouseButtonEventArgs e)
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

    private void ResetBedrockPath_Click(object sender, EventArgs e)
    {
        if (HasActiveBedrockTask())
        {
            HintService.Hint(Lang.Text("Bedrock.Folder.ChangePathBusy"), HintType.Error);
            return;
        }

        ApplyBedrockPath("");
    }

    private bool HasActiveBedrockTask() => _downloadingVersions.Count > 0 || _installingVersions.Count > 0;

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
            DetectBedrockInstallation();
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

    private void PageBedrockRight_Loaded(object sender, RoutedEventArgs e)
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
        UpdateBedrockPathDisplay();
        DetectBedrockInstallation();
        RefreshLocalVersions();
        LoadVersionList();
    }

    #region 启动检测

    public void RefreshPage()
    {
        UpdateBedrockPathDisplay();
        DetectBedrockInstallation();
        RefreshLocalVersions();
        LoadVersionList();
    }

    public bool IsBedrockInstalled { get; private set; }
    public string? BedrockVersion { get; private set; }
    public string? BedrockInstallPath { get; private set; }

    private bool _detecting;
    private bool _isStorePackage;
    // GDK 便携版：解包后直接运行 Minecraft.Windows.exe，不注册系统包
    private bool _isGdkPortable;

    public void DetectBedrockInstallation()
    {
        if (_detecting) return;
        _detecting = true;

        // 检测需要调用 PowerShell 并遍历目录，放到后台线程执行，避免界面卡住导致按钮看起来点不动
        _ = Task.Run(() =>
        {
            // 已注册的 UWP 包优先；没有注册包时回退到启动器解包的 GDK 便携版
            var portable = FindPortableGdkInstallation();
            var bedrockPath = FindBedrockInstallation() ?? portable?.Path;
            var (bedrockVersion, signatureKind) = GetBedrockPackageInfo();
            if (portable is not null && string.Equals(bedrockPath, portable.Value.Path, StringComparison.OrdinalIgnoreCase))
                bedrockVersion = portable.Value.Version;
            Dispatcher.Invoke(() =>
            {
                _detecting = false;
                ApplyInstallationState(bedrockPath, bedrockVersion, signatureKind);
            });
        });
    }

    private void ApplyInstallationState(string? bedrockPath, string? bedrockVersion, string? signatureKind)
    {
        IsBedrockInstalled = !string.IsNullOrEmpty(bedrockPath);
        BedrockVersion = bedrockVersion;
        BedrockInstallPath = bedrockPath;
        _isStorePackage = string.Equals(signatureKind, "Store", StringComparison.OrdinalIgnoreCase);
        _isGdkPortable = !string.IsNullOrEmpty(bedrockPath) && IsPortableGdkPath(bedrockPath);
        if (!string.IsNullOrEmpty(bedrockPath))
        {
            ShapeStatusDot.Fill = (Brush)FindResource("ColorBrush3");
            LabStatus.Text = Lang.Text("Bedrock.Status.Detected");
            LabDetail.Text = Lang.Text("Bedrock.Status.CopyPathHint");
            LabVersion.Text = string.IsNullOrEmpty(bedrockVersion)
                ? "Minecraft Bedrock Edition"
                : Lang.Text("Bedrock.Status.Version", bedrockVersion);
            BtnLaunch.IsEnabled = true;
            BtnLaunch.Text = Lang.Text("Bedrock.Action.Launch");
        }
        else
        {
            ShapeStatusDot.Fill = (Brush)FindResource("ColorBrushGray4");
            LabStatus.Text = Lang.Text("Bedrock.Status.NotFound");
            LabDetail.Text = Lang.Text("Bedrock.Status.DownloadOrImportHint");
            LabVersion.Text = Lang.Text("Bedrock.Status.NotInstalled");
            BtnLaunch.IsEnabled = true;
            BtnLaunch.Text = Lang.Text("Bedrock.Action.Download");
        }

        // 检测结果会影响「已安装」标记，重新渲染本地版本行
        RefreshLocalVersions();
        RefreshUpdateHint();
        // 同步刷新左侧页面的状态，否则左侧会一直停留在检测前的“未检测到”
        ModMain.frmBedrockLeft?.RefreshStatus();
    }

    /// <summary>
    ///     版本隔离开关：GDK 便携版可以在启动前把共享数据目录指向版本自己的目录。
    /// </summary>
    private void CheckIsolation_OnChange(object sender, bool user)
    {
        // 仅处理用户操作，代码里设置 Checked 时不做任何事
        if (!user || sender is not MyCheckBox checkBox) return;

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

        if (!_isGdkPortable || string.IsNullOrEmpty(BedrockInstallPath))
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

        if (!IsBedrockInstalled || !_isStorePackage || _isGdkPortable || _versionCache is null)
        {
            BtnUpdate.Visibility = Visibility.Collapsed;
            return;
        }

        JsonObject? latest = null;
        foreach (var node in _versionCache)
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

    private void BtnUpdate_Click(object sender, MouseButtonEventArgs e)
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

    private static (string? Version, string? SignatureKind) GetBedrockPackageInfo()
    {
        try
        {
            var output = ModBase.ShellAndGetOutput("powershell.exe",
                "-NoProfile -Command \"$p = Get-AppxPackage -Name 'Microsoft.Minecraft*' | Select-Object -First 1; if ($p) { $p.Version.ToString() + '|' + $p.SignatureKind }\"",
                timeout: 10000);
            if (!string.IsNullOrEmpty(output) && !output.Contains("Exception"))
            {
                var trimmed = output.Trim();
                if (trimmed.Length == 0) return (null, null);

                var parts = trimmed.Split('|');
                var version = parts[0].Trim();
                var signatureKind = parts.Length > 1 ? parts[1].Trim() : "";
                return (version.Length == 0 ? null : version,
                    signatureKind.Length == 0 ? null : signatureKind);
            }
        }
        catch { }
        return (null, null);
    }

    private static string? FindBedrockInstallation()
    {
        try
        {
            var output = ModBase.ShellAndGetOutput("powershell.exe",
                "-NoProfile -Command \"Get-AppxPackage -Name Microsoft.Minecraft* | Select-Object -ExpandProperty InstallLocation\"",
                timeout: 10000);
            if (!string.IsNullOrEmpty(output) && !output.Contains("Exception"))
            {
                var trimmed = output.Trim();
                if (Directory.Exists(trimmed))
                    return trimmed;
            }
        }
        catch { }

        try
        {
            // 新版为 Minecraft.Windows.exe，1.16 及更早的旧版为 Minecraft.Win10.DX11.exe
            foreach (var exeName in new[] { "Minecraft.Windows.exe", "Minecraft.Win10.DX11.exe" })
            {
                var path = SearchForFile(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), exeName);
                if (!string.IsNullOrEmpty(path)) return path;

                path = SearchForFile(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), exeName);
                if (!string.IsNullOrEmpty(path)) return path;
            }
        }
        catch { }

        try
        {
            var path = SearchForDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "com.mojang");
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch { }

        return null;
    }

    private static string? SearchForFile(string rootDir, string fileName)
    {
        try
        {
            foreach (var file in Directory.GetFiles(rootDir, fileName, SearchOption.AllDirectories))
                return file;
        }
        catch { }
        return null;
    }

    private static string? SearchForDirectory(string rootDir, string dirName)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(rootDir, dirName, SearchOption.AllDirectories))
                return dir;
        }
        catch { }
        return null;
    }

    /// <summary>
    ///     该版本是否为 GDK 版（版本目录内保存的是 .msixvc 而不是 .appx）。
    /// </summary>
    private static bool IsGdkVersion(string versionId)
    {
        var dir = ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId);
        return GetPackageFiles(dir).Any(ModBedrockGdk.IsGdkPackage);
    }

    /// <summary>
    ///     路径是否为启动器解包出来的 GDK 便携版目录（extracted\&lt;版本号&gt;）。
    /// </summary>
    private static bool IsPortableGdkPath(string path)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(BedrockVersionsDir, "extracted"))
                .TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            var relative = full[(root.Length + 1)..];
            var versionId = relative.Split(Path.DirectorySeparatorChar)[0];
            return !string.IsNullOrEmpty(versionId) &&
                   IsGdkVersion(versionId) &&
                   ModBedrockGdk.IsInstalled(full);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     在已解包目录中寻找可用的 GDK 便携版，取版本号最高的一个。
    /// </summary>
    private static (string Path, string Version)? FindPortableGdkInstallation()
    {
        try
        {
            var extractedRoot = Path.Combine(BedrockVersionsDir, "extracted");
            if (!Directory.Exists(extractedRoot)) return null;

            (string Path, string Version)? best = null;
            foreach (var dir in Directory.GetDirectories(extractedRoot))
            {
                var versionId = Path.GetFileName(dir);
                if (!IsGdkVersion(versionId) || !ModBedrockGdk.IsInstalled(dir)) continue;
                if (best is null || ModDownload.CompareBedrockVersions(versionId, best.Value.Version) > 0)
                    best = (dir, versionId);
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    public void BtnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        if (!IsBedrockInstalled)
        {
            // 尚未检测到安装（或检测仍在后台进行），刷新一次检测并引导到下载列表
            DetectBedrockInstallation();
            PanVersionList.BringIntoView();
            return;
        }

        // 记住的选中版本优先：当前注册的不是它时，先切换到该版本
        var selected = Config.Launch.BedrockSelectedVersion;
        if (!string.IsNullOrEmpty(selected) && _localVersionIds.Contains(selected) &&
            !string.Equals(selected, _registeredVersionId, StringComparison.OrdinalIgnoreCase))
        {
            LaunchLocalVersion(selected);
            return;
        }

        LaunchBedrock();
    }

    #region 启动进度

    /// <summary>开始显示启动进度条，并禁用启动按钮直到本次启动结束。</summary>
    private void BeginLaunchProgress(string detail)
    {
        _launching = true;
        _launchGeneration++;
        _launchActualProgress = 0.02d;
        _launchShownProgress = 0d;
        ProgressBedrockFinished.Width = new GridLength(0d, GridUnitType.Star);
        ProgressBedrockUnfinished.Width = new GridLength(1d, GridUnitType.Star);
        PanLaunchProgress.Visibility = Visibility.Visible;
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Run;
        BtnLaunch.IsEnabled = false;
        LabStatus.Text = Lang.Text("Bedrock.Status.Launching");
        LabDetail.Text = detail;
        _launchProgressTimer.Start();
    }

    /// <summary>推进到实际阶段进度（只会前进），可在后台线程调用。</summary>
    private void SetLaunchStage(double progress, string? detail = null)
    {
        void Apply()
        {
            if (!_launching) return;
            if (progress > _launchActualProgress) _launchActualProgress = progress;
            if (detail is not null) LabDetail.Text = detail;
        }

        if (Dispatcher.CheckAccess()) Apply();
        else ModBase.RunInUi(Apply);
    }

    /// <summary>结束本次启动：成功时先补满进度条再收起，失败时立即收起并显示错误。</summary>
    private void EndLaunchProgress(bool success, string status, string detail)
    {
        void Apply()
        {
            LabStatus.Text = status;
            LabDetail.Text = detail;
            LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Stop;

            if (!success)
            {
                _launching = false;
                _launchProgressTimer.Stop();
                PanLaunchProgress.Visibility = Visibility.Collapsed;
                BtnLaunch.IsEnabled = true;
                return;
            }

            // 启动成功计入总启动次数，百宝箱的启动计数同时统计 Java 版与基岩版
            States.System.LaunchCount += 1;

            _launchActualProgress = 1d;
            var generation = ++_launchGeneration;
            _ = Task.Run(async () =>
            {
                await Task.Delay(700);
                ModBase.RunInUi(() =>
                {
                    // 期间用户可能已经开始下一次启动，只有仍是本次启动才收起
                    if (generation != _launchGeneration) return;
                    _launching = false;
                    _launchProgressTimer.Stop();
                    PanLaunchProgress.Visibility = Visibility.Collapsed;
                    BtnLaunch.IsEnabled = true;
                });
            });
        }

        if (Dispatcher.CheckAccess()) Apply();
        else ModBase.RunInUi(Apply);
    }

    private void LaunchProgressTimer_Tick(object? sender, EventArgs e)
    {
        if (!_launching) return;
        // 向实际进度靠一点，避免进度条看起来一直在跳
        if (_launchActualProgress >= _launchShownProgress)
            _launchShownProgress += (_launchActualProgress - _launchShownProgress) * 0.2d + 0.005d;
        if (_launchActualProgress <= _launchShownProgress)
            _launchShownProgress = _launchActualProgress;
        if (_launchShownProgress > 1d) _launchShownProgress = 1d;

        // 每次 Tick 用同名动画覆盖上一帧，让两列宽度平滑跟随
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaGridLengthWidth(ProgressBedrockFinished,
                _launchShownProgress - ProgressBedrockFinished.Width.Value, 260,
                ease: new ModAnimation.AniEaseOutFluent()),
            ModAnimation.AaGridLengthWidth(ProgressBedrockUnfinished,
                1d - _launchShownProgress - ProgressBedrockUnfinished.Width.Value, 260,
                ease: new ModAnimation.AniEaseOutFluent())
        }, "Bedrock Launch Progress");
    }

    /// <summary>等待游戏窗口出现；拿不到进程句柄时（注册式启动）按进程名查找。</summary>
    private static bool WaitForGameWindow(Process? process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (process is not null)
                {
                    if (process.HasExited) return false;
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero) return true;
                }
                else
                {
                    foreach (var name in new[] { "Minecraft.Windows", "Minecraft.Win10.DX11" })
                    foreach (var running in Process.GetProcessesByName(name))
                        using (running)
                        {
                            running.Refresh();
                            if (running.MainWindowHandle != IntPtr.Zero) return true;
                        }
                }
            }
            catch { }

            Thread.Sleep(200);
        }

        return false;
    }

    #endregion

    public void LaunchBedrock()
    {
        if (_launching) return;
        if (string.IsNullOrEmpty(BedrockInstallPath))
        {
            DetectBedrockInstallation();
            return;
        }

        BeginLaunchProgress(Lang.Text("Bedrock.Status.CallingSystem"));

        // 启动同样需要调用 PowerShell 与注册包，放到后台线程执行
        _ = Task.Run(() =>
        {
            Process? process = null;
            BedrockIsolationSession? isolationSession = null;
            var watchingExit = false;
            try
            {
                // GDK 便携版不经过系统包注册，直接运行解包目录里的主程序，可与其他版本同时运行
                if (_isGdkPortable)
                {
                    var installPath = BedrockInstallPath!;

                    // 版本隔离：启动前把共享数据目录指向该版本自己的目录，游戏退出后还原
                    if (Config.Launch.BedrockVersionIsolation)
                    {
                        SetLaunchStage(0.08, Lang.Text("Bedrock.Launch.Stage.Isolation"));
                        try
                        {
                            // 使用带启动租约的会话：游戏尚未出现时不会被其他还原逻辑误删隔离目录
                            isolationSession = ModBedrockIsolation.BeginSession(installPath,
                                BedrockVersion ?? Path.GetFileName(installPath));
                        }
                        catch (Exception ex)
                        {
                            ModBase.Log(ex, "启用基岩版版本隔离失败，本次使用共享数据目录启动",
                                ModBase.LogLevel.Hint);
                        }
                    }

                    SetLaunchStage(0.25, Lang.Text("Bedrock.Launch.Stage.Starting"));

                    // 原版加载器优先尝试注册式启动：注册后的包具备包身份与完整授权，
                    // 不会像裸跑那样被当作试用版而要求「解锁完整游戏」；
                    // LeviLamina / BLoader 需要注入 DLL，只能直接运行解包目录里的主程序。
                    // preload 目录内有 DLL 时同样必须裸跑，注册式启动由 explorer 拉起进程，无法注入。
                    var loader = ModBedrockLoader.GetLoader(BedrockVersion);

                    // LeviLamina 需要按游戏版本通过 lip 安装（会给 Minecraft.Windows.exe 打补丁）
                    if (loader == ModBedrockLoader.LeviLamina)
                    {
                        SetLaunchStage(0.12, Lang.Text("Bedrock.Loader.LeviLamina.Preparing"));
                        try
                        {
                            ModBedrockLeviLamina.EnsureInstalled(installPath, BedrockVersion ?? "");
                        }
                        catch (Exception ex)
                        {
                            ModBase.Log(ex, "LeviLamina 安装失败，本次按原版启动",
                                ModBase.LogLevel.Hint,
                                userSummary: Lang.Text("Bedrock.Loader.LeviLamina.Failed", ex.Message));
                        }
                    }

                    // BLoader 按需释放并校验 DLL；失败时退回原版启动，不阻断游戏
                    if (loader == ModBedrockLoader.BLoader)
                    {
                        SetLaunchStage(0.18, Lang.Text("Bedrock.Loader.BLoader.Preparing"));
                        try
                        {
                            ModBedrockBLoader.EnsureInstalled();
                        }
                        catch (Exception ex)
                        {
                            ModBase.Log(ex, "BLoader 准备失败，本次按原版启动",
                                ModBase.LogLevel.Hint,
                                userSummary: Lang.Text("Bedrock.Loader.BLoader.Failed", ex.Message));
                        }
                    }

                    var hasPreload = ModBedrockMods.GetPreloadFiles(installPath).Length > 0;
                    if (!hasPreload && loader == ModBedrockLoader.Vanilla &&
                        TryLaunchGdkRegistered(installPath, BedrockVersion))
                    {
                        SetLaunchStage(0.6, Lang.Text("Bedrock.Launch.Stage.WaitingWindow"));
                    }
                    else
                    {
                        process = ModBedrockGdk.Launch(installPath, null, BedrockVersion)
                                  ?? throw new FileNotFoundException(Lang.Text("Bedrock.Gdk.ExecutableMissing"));
                        SetLaunchStage(0.55, Lang.Text("Bedrock.Launch.Stage.WaitingWindow"));
                    }

                    // 版本隔离的还原交给后端：登记本次启动，等游戏进程退出后再恢复共享目录；
                    // 注册式启动拿不到进程句柄时，后端会先等待进程出现，避免游戏还没启动就还原
                    if (isolationSession is not null)
                    {
                        ModBedrockIsolation.RestoreAfterGameExit(isolationSession, process);
                        watchingExit = true;
                    }

                    if (!WaitForGameWindow(process, TimeSpan.FromSeconds(60)))
                    {
                        // 进程在出窗口前就退出了，说明启动确实失败，不能报成功
                        if (process is { HasExited: true })
                            throw new InvalidOperationException(Lang.Text("Bedrock.Launch.Exited", process.ExitCode));
                        ModBase.Log("[Bedrock] 60 秒内未检测到游戏窗口，游戏可能仍在启动",
                            ModBase.LogLevel.Hint);
                    }

                    // 进程句柄统一在 finally 中回收，隔离还原由后端负责

                    ModBase.Log($"[Bedrock] 正在启动 GDK 版本: {installPath}", ModBase.LogLevel.Normal);
                    SetLaunchStage(0.9);
                    EndLaunchProgress(true, Lang.Text("Bedrock.Status.Launched"), installPath);
                    return;
                }

                // 优先用 shell:AppsFolder 启动已注册的包：旧版 Manifest 被设为 AppListEntry=none，
                // minecraft:// 协议可能被商店版或其他版本抢占
                SetLaunchStage(0.4, Lang.Text("Bedrock.Launch.Stage.Starting"));
                if (ModBedrockUwp.LaunchPackage())
                {
                    ModBase.Log("[Bedrock] 正在启动 Minecraft Bedrock Edition...");
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "minecraft://",
                        UseShellExecute = true
                    });
                    ModBase.Log("[Bedrock] 正在通过 minecraft:// 启动 Minecraft Bedrock Edition...");
                }

                SetLaunchStage(0.6, Lang.Text("Bedrock.Launch.Stage.WaitingWindow"));
                if (!WaitForGameWindow(null, TimeSpan.FromSeconds(60)))
                    ModBase.Log("[Bedrock] 60 秒内未检测到游戏窗口，游戏可能仍在启动",
                        ModBase.LogLevel.Hint);

                SetLaunchStage(0.9);
                EndLaunchProgress(true, Lang.Text("Bedrock.Status.Launched"),
                    BedrockInstallPath ?? "Minecraft Bedrock Edition");
            }
            catch (Exception ex)
            {
                ModBase.Log($"[Bedrock] 启动失败: {ex.Message}", ModBase.LogLevel.Hint,
                    Lang.Text("Bedrock.Launch.Error"));
                EndLaunchProgress(false, Lang.Text("Bedrock.Status.LaunchFailed"), ex.Message);
            }
            finally
            {
                process?.Dispose();
                // 启动失败（尚未登记退出监视）时立即还原本次隔离，避免留下目录联接
                if (isolationSession is not null && !watchingExit)
                    ModBedrockIsolation.Restore(isolationSession);
            }
        });
    }

    #region 注册式启动（GDK）

    /// <summary>
    ///     以注册方式启动 GDK 版本，等价于 BedrockBoot 的 RegisterPackageAsync + 开发部署。
    ///     直接运行 Minecraft.Windows.exe 没有包身份与商店授权，游戏会按试用版处理并要求解锁完整游戏；
    ///     注册后的包以开发部署身份运行，可直接进入完整游戏。无法注册时返回 false，由调用方退回直接启动。
    /// </summary>
    private bool TryLaunchGdkRegistered(string extractDir, string? versionId)
    {
        try
        {
            var manifestPath = Path.Combine(extractDir, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
            {
                ModBase.Log("[Bedrock] 解包目录内没有 AppxManifest.xml，改用直接启动", ModBase.LogLevel.Hint);
                return false;
            }

            if (!IsDeveloperModeEnabled())
            {
                ModBase.Log("[Bedrock] 未开启开发者模式，无法以注册方式启动，本次直接运行（可能被当作试用版）",
                    ModBase.LogLevel.Hint, userSummary: Lang.Text("Bedrock.Register.NoDeveloperMode"));
                return false;
            }

            var identityName = ReadManifestIdentity(manifestPath);
            if (string.IsNullOrEmpty(identityName))
            {
                ModBase.Log("[Bedrock] 无法从清单读取包名，改用直接启动", ModBase.LogLevel.Hint);
                return false;
            }

            // 已按该目录注册过就直接启动，避免每次启动都跑一次耗时的 PowerShell 注册
            if (!IsRegisteredFromDirectory(identityName, extractDir))
            {
                SetLaunchStage(0.35, Lang.Text("Bedrock.Launch.Stage.Register"));

                // 改写过的清单与旧签名不匹配，注册前按 BedrockBoot 的方式删除签名文件
                var signaturePath = Path.Combine(extractDir, "AppxSignature.p7x");
                if (File.Exists(signaturePath)) File.Delete(signaturePath);

                if (!IsManifestEdited(manifestPath))
                {
                    ModBedrockUwp.EditManifest(extractDir, $"Minecraft {versionId}");
                    ModBase.Log("[Bedrock] 已改写 GDK 清单以支持注册启动", ModBase.LogLevel.Normal);
                }

                var result = ModBase.ShellAndGetOutput("powershell.exe",
                    ModBedrockUwp.GetAppxInstallArguments(manifestPath, register: true),
                    timeout: 300000);
                // 不仅要命令成功，还要确认注册确实落在当前目录，而不是被商店版或其它目录抢占
                if (IsPowerShellFailure(result) || !IsRegisteredFromDirectory(identityName, extractDir))
                {
                    ModBase.Log($"[Bedrock] 注册 GDK 版本失败或未能确认注册位置，改用直接启动: {result.Trim()}",
                        ModBase.LogLevel.Hint, userSummary: Lang.Text("Bedrock.Register.Failed"));
                    return false;
                }

                ModBase.Log($"[Bedrock] 已注册版本 {versionId}，以开发部署身份启动可避免「解锁完整游戏」提示",
                    ModBase.LogLevel.Normal);
            }

            if (!ModBedrockUwp.LaunchPackage(identityName))
            {
                ModBase.Log("[Bedrock] 已注册但无法通过 AppsFolder 启动，改用直接启动", ModBase.LogLevel.Hint);
                return false;
            }

            ModBase.Log($"[Bedrock] 正在以注册方式启动 GDK 版本 {versionId}", ModBase.LogLevel.Normal);
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "以注册方式启动 GDK 版本失败，改用直接启动", ModBase.LogLevel.Hint);
            return false;
        }
    }

    /// <summary>该目录是否正是当前注册的包，是的话无需重复注册。</summary>
    private static bool IsRegisteredFromDirectory(string packageName, string directory)
    {
        try
        {
            // 后端会同时校验包标识与 InstallLocation，避免把商店版或其它目录的注册误判成本版本
            return ModBedrockRegistration.IsRegisteredFromDirectory(packageName, directory);
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadManifestIdentity(string manifestPath)
    {
        try
        {
            var match = Regex.Match(File.ReadAllText(manifestPath),
                "<Identity\\s[^>]*Name=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>清单是否已被改写（改写后 AppListEntry 会被设为 none，版本号也会变化）。</summary>
    private static bool IsManifestEdited(string manifestPath)
    {
        try
        {
            return Regex.IsMatch(File.ReadAllText(manifestPath),
                "AppListEntry\\s*=\\s*\"none\"", RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPowerShellFailure(string output)
    {
        return output.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("错误") ||
               output.Contains("0x8", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #endregion

    #region 本地版本管理

    private void RefreshLocalVersions()
    {
        PanLocalVersions.Children.Clear();
        _localVersionIds.Clear();
        UpdateInstalledVersionId();

        var items = new List<(string Id, string FilePath)>();
        if (Directory.Exists(BedrockVersionsDir))
        {
            foreach (var dir in Directory.GetDirectories(BedrockVersionsDir))
            {
                // 跳过安装时的临时解压目录
                if (Path.GetFileName(dir).Equals("extracted", StringComparison.OrdinalIgnoreCase)) continue;

                var files = GetPackageFiles(dir);
                if (files.Count == 0) continue;

                items.Add((Path.GetFileName(dir), files[0]));
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
    ///     判断当前安装的基岩版是否由启动器从某个本地版本注册而来。
    ///     注册时解压目录为 extracted\&lt;版本号&gt;（包束会多一层 inner）。
    /// </summary>
    private void UpdateInstalledVersionId()
    {
        _registeredVersionId = null;
        if (string.IsNullOrEmpty(BedrockInstallPath)) return;

        try
        {
            var root = Path.GetFullPath(Path.Combine(BedrockVersionsDir, "extracted"))
                .TrimEnd(Path.DirectorySeparatorChar);
            var installPath = Path.GetFullPath(BedrockInstallPath).TrimEnd(Path.DirectorySeparatorChar);
            if (!installPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;

            var relative = installPath[(root.Length + 1)..];
            var segment = relative.Split(Path.DirectorySeparatorChar)[0];
            if (!string.IsNullOrEmpty(segment))
                _registeredVersionId = segment;
        }
        catch
        {
            // 路径异常时忽略，仅影响「已安装」标记
        }
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

        var isInstalling = _installingVersions.Contains(versionId);
        // GDK 便携版只要解包完成就可以直接运行，不需要注册系统包
        var isGdk = IsGdkVersion(versionId);
        var gdkReady = isGdk && ModBedrockGdk.IsInstalled(
            ModBedrockUwp.GetVersionDirectory(Path.Combine(BedrockVersionsDir, "extracted"), versionId));
        var isRegistered = string.Equals(_registeredVersionId, versionId, StringComparison.OrdinalIgnoreCase);
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
            isSelected ? "ColorBrushWhite" : isInstalled ? "ColorBrush3" : "ColorBrushGray2");

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
            isSelected ? "ColorBrush3" : isInstalled ? "ColorBrush6" : "ColorBrushGray7");

        var displayName = ModBedrockInstance.GetDisplayName(BedrockVersionsDir, versionId);
        var title = new TextBlock
        {
            Text = displayName,
            ToolTip = string.Equals(displayName, versionId, StringComparison.Ordinal) ? null : versionId,
            FontSize = 14.5,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");

        // 构建类型与安装状态都放在标题行，副标题才能独占整行显示版本信息
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                title,
                CreateChip(isGdk ? "GDK" : "UWP",
                    isGdk ? "ColorBrush6" : "ColorBrushGray7",
                    isGdk ? "ColorBrush2" : "ColorBrushGray2",
                    new Thickness(8, 0, 0, 0)),
                isInstalling
                    ? CreateChip(Lang.Text("Bedrock.Local.Tag.Installing"), "ColorBrushGray7", "ColorBrushGray2",
                        new Thickness(6, 0, 0, 0))
                    : isInstalled
                        ? CreateChip(Lang.Text("Bedrock.Local.Tag.Installed"), "ColorBrush6", "ColorBrush2",
                            new Thickness(6, 0, 0, 0))
                        : CreateChip(Lang.Text("Bedrock.Local.Tag.Downloaded"), "ColorBrushGray7", "ColorBrushGray2",
                            new Thickness(6, 0, 0, 0))
            }
        };
        // 跟随启动按钮的版本需要明确标出，否则只看图标底色难以分辨
        if (isSelected)
            titleRow.Children.Add(CreateChip(Lang.Text("Bedrock.Local.Tag.Selected"), "ColorBrush3", "ColorBrushWhite",
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
        detail.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");

        var info = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Children = { titleRow, detail }
        };

        // 每个已下载的版本都可以直接启动：未注册的版本会先切换注册再启动
        var launchBtn = new MyButton
        {
            Text = Lang.Text("Bedrock.Local.LaunchButton"),
            ColorType = MyButton.ColorState.Highlight,
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
        launchBtn.Click += (_, _) => LaunchLocalVersion(versionId);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { launchBtn }
        };

        var openFolderBtn = new MyIconButton
        {
            SvgIcon = "lucide/folder-open",
            Tag = versionId,
            Height = 30,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = Lang.Text("Bedrock.Local.OpenFolder")
        };
        openFolderBtn.Click += LocalOpenFolder_Click;
        buttons.Children.Add(openFolderBtn);

        var renameBtn = new MyIconButton
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
            var copyBtn = new MyIconButton
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
            var loaderBtn = new MyIconButton
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

        var installBtn = new MyIconButton
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

        var deleteBtn = new MyIconButton
        {
            SvgIcon = "lucide/trash-2",
            Theme = MyIconButton.Themes.Red,
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
        card.SetResourceReference(Border.BackgroundProperty, "ColorBrush8");
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "ColorBrush7");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "ColorBrush8");
        // 与旧版一致：点击版本实例的空白处直接安装（已安装时为重新安装）
        card.MouseLeftButtonUp += (_, _) => LocalInstall_Click(installBtn, EventArgs.Empty);
        return card;
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
        var (_, manifestPath) = FindExtractedVersion(versionId);
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
    ///     查找版本解压后的可注册目录。包束会多解一层 inner。
    /// </summary>
    private static (string? Directory, string? Manifest) FindExtractedVersion(string versionId)
    {
        var extractDir = ModBedrockUwp.GetVersionDirectory(Path.Combine(BedrockVersionsDir, "extracted"), versionId);
        var innerDir = Path.Combine(extractDir, "inner");

        var innerManifest = Path.Combine(innerDir, "AppxManifest.xml");
        if (File.Exists(innerManifest)) return (innerDir, innerManifest);

        var manifest = Path.Combine(extractDir, "AppxManifest.xml");
        return File.Exists(manifest) ? (extractDir, manifest) : (null, null);
    }

    /// <summary>
    ///     选择该 GDK 版本的模组加载器：原版不做注入，LeviLamina 注入 mods 目录内的 DLL。
    /// </summary>
    private void LocalLoader_Click(object sender, EventArgs e)
    {
        if (sender is not MyIconButton btn || btn.Tag is not string versionId)
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

    /// <summary>
    ///     启动指定本地版本，并把它记为当前选中版本（之后「启动基岩版」会跟随）。
    /// </summary>
    private void LaunchLocalVersion(string versionId)
    {
        if (_switchingVersion)
            return;

        // GDK 便携版：解包目录里有主程序就能直接启动，不需要改动系统包注册
        if (IsGdkVersion(versionId))
        {
            var gdkDir = ModBedrockUwp.GetVersionDirectory(
                Path.Combine(BedrockVersionsDir, "extracted"), versionId);
            if (ModBedrockGdk.IsInstalled(gdkDir))
            {
                ActivateGdkVersion(versionId, gdkDir);
                return;
            }

            var files = GetPackageFiles(ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId));
            if (files.Count == 0)
            {
                ModBase.Log($"[Bedrock] 版本 {versionId} 的安装包已被删除，无法解包启动",
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Bedrock.Local.PackageMissing"));
                return;
            }

            // 尚未解包：先解包，安装完成后不会注册系统包
            StartInstall(files[0], versionId);
            return;
        }

        // 已经是当前注册的版本，直接启动
        if (string.Equals(_registeredVersionId, versionId, StringComparison.OrdinalIgnoreCase))
        {
            SelectVersion(versionId);
            LaunchBedrock();
            return;
        }

        _switchingVersion = true;
        LabStatus.Text = Lang.Text("Bedrock.Status.Switching");
        LabDetail.Text = Lang.Text("Bedrock.Status.SwitchingDetail", versionId);
        BtnLaunch.IsEnabled = false;

        Task.Run(() =>
        {
            string? installedPath = null;
            Exception? error = null;

            try
            {
                ModBase.Log($"[Bedrock] 正在切换到版本 {versionId}", ModBase.LogLevel.Normal);

                // 切换注册前先结束正在运行的基岩版
                ModBedrockUwp.StopMinecraftProcesses();

                var (targetDir, manifestPath) = FindExtractedVersion(versionId);
                if (manifestPath is not null)
                {
                    var result = ModBase.ShellAndGetOutput("powershell.exe",
                        ModBedrockUwp.GetAppxInstallArguments(manifestPath, register: true),
                        timeout: 300000);

                    if (result.Contains("Exception") || result.Contains("错误"))
                    {
                        ModBase.Log($"[Bedrock] 重新注册 {versionId} 失败，改为重新安装: {result.Trim()}",
                            ModBase.LogLevel.Normal);
                        targetDir = null;
                    }
                }

                if (targetDir is null)
                {
                    // 解压目录缺失或被清理：用已下载的安装包重新解压并注册
                    var files = GetPackageFiles(ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId));
                    if (files.Count == 0)
                        throw new FileNotFoundException(Lang.Text("Bedrock.Local.PackageMissing"));
                    InstallWithRegister(files[0], versionId);
                    (targetDir, _) = FindExtractedVersion(versionId);
                }

                installedPath = targetDir;
                ModBase.Log($"[Bedrock] 已切换到版本 {versionId}", ModBase.LogLevel.Normal);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            ModBase.RunInUi(() =>
            {
                _switchingVersion = false;
                BtnLaunch.IsEnabled = true;

                if (error is not null)
                {
                    ModBase.Log(error, $"切换到基岩版 {versionId} 失败",
                        ModBase.LogLevel.Feedback,
                        userSummary: Lang.Text("Bedrock.Launch.Error"));
                    LabStatus.Text = Lang.Text("Bedrock.Status.LaunchFailed");
                    LabDetail.Text = error.Message;
                    RefreshLocalVersions();
                    return;
                }

                IsBedrockInstalled = true;
                BedrockInstallPath = installedPath;
                BedrockVersion = versionId;
                _isStorePackage = false;
                BtnUpdate.Visibility = Visibility.Collapsed;
                SelectVersion(versionId);
                RefreshLocalVersions();
                // 左侧状态栏也同步显示切换后的版本
                ModMain.frmBedrockLeft?.RefreshStatus();
                LaunchBedrock();
            });
        });
    }

    /// <summary>
    ///     将启动目标切换为 GDK 便携版并直接启动。不触碰系统包注册，因此其他版本仍可继续运行。
    /// </summary>
    private void ActivateGdkVersion(string versionId, string extractDir)
    {
        IsBedrockInstalled = true;
        BedrockInstallPath = extractDir;
        BedrockVersion = versionId;
        _isStorePackage = false;
        _isGdkPortable = true;
        BtnUpdate.Visibility = Visibility.Collapsed;
        ShapeStatusDot.Fill = (Brush)FindResource("ColorBrush3");
        LabVersion.Text = Lang.Text("Bedrock.Status.Version", versionId);
        SelectVersion(versionId);
        RefreshLocalVersions();
        ModMain.frmBedrockLeft?.RefreshStatus();
        LaunchBedrock();
    }

    /// <summary>记住选中的版本，重启启动器后「启动基岩版」仍会跟随它。</summary>
    private void SelectVersion(string versionId)
    {
        Config.Launch.BedrockSelectedVersion = versionId;
    }

    private void LocalInstall_Click(object sender, EventArgs e)
    {
        if (sender is not MyIconButton btn || btn.Tag is not string versionId)
            return;

        if (_installingVersions.Contains(versionId))
            return;

        var dir = ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId);
        var allFiles = GetPackageFiles(dir);
        if (allFiles.Count == 0) return;

        StartInstall(allFiles[0], versionId);
    }

    private void StartInstall(string filePath, string versionId)
    {
        if (_installingVersions.Contains(versionId))
            return;

        // GDK 版是加密的 .msixvc，不能侧载注册，改为解密解包成便携版
        if (ModBedrockGdk.IsGdkPackage(filePath))
        {
            StartInstallGdk(filePath, versionId);
            return;
        }

        // 商店版是「已打包」版本，未打包的旧版本无法覆盖注册（0x80073CFB）。
        // 先备份应用数据再完整卸载，安装完成后恢复；开发者注册版则可以直接覆盖注册。
        var migrateStore = false;
        if (ModBedrockUwp.GetInstalledPackage() is not null &&
            string.Equals(ModBedrockUwp.GetInstalledSignatureKind(), "Store", StringComparison.OrdinalIgnoreCase))
        {
            if (ModMain.MyMsgBox(Lang.Text("Bedrock.Install.StoreMigrate.Message"),
                    Lang.Text("Bedrock.Install.StoreMigrate.Title"),
                    Lang.Text("Bedrock.Install.StoreMigrate.Continue"),
                    Lang.Text("Common.Action.Cancel"), isWarn: true) != 1)
                return;
            migrateStore = true;
        }

        _installingVersions.Add(versionId);
        RefreshLocalVersions();

        LabStatus.Text = Lang.Text("Bedrock.Install.Installing");
        LabDetail.Text = Lang.Text("Bedrock.Install.InstallingVersion", versionId);
        BtnLaunch.IsEnabled = false;

        InstallBedrockPackage(filePath, versionId, migrateStore);
    }

    /// <summary>
    ///     GDK 版安装：解包 .msixvc 到独立目录后直接可运行，不做系统包注册。
    /// </summary>
    private void StartInstallGdk(string filePath, string versionId)
    {
        if (_installingVersions.Contains(versionId))
            return;

        // 构建未内置 CIK 时直接提示，避免先结束进程、清空目录再抛出异常
        if (!ModBedrockGdk.HasContentKey(filePath))
        {
            ModBase.Log($"[Bedrock] 当前构建未内置 GDK 内容密钥（CIK），无法解包: {filePath}",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Bedrock.Gdk.CikMissing"));
            LabStatus.Text = Lang.Text("Bedrock.Install.Failed");
            LabDetail.Text = Lang.Text("Bedrock.Gdk.CikMissing");
            return;
        }

        _installingVersions.Add(versionId);
        RefreshLocalVersions();

        LabStatus.Text = Lang.Text("Bedrock.Gdk.Extracting");
        LabDetail.Text = Lang.Text("Bedrock.Gdk.ExtractingDetail", versionId);
        BtnLaunch.IsEnabled = false;

        InstallGdkPackage(filePath, versionId);
    }

    private void InstallGdkPackage(string filePath, string versionId)
    {
        Task.Run(() =>
        {
            try
            {
                ModBase.Log($"[Bedrock] 开始解包 GDK 版本: {filePath}", ModBase.LogLevel.Normal);

                // 事务式安装：先解包到临时目录并校验主程序，再整体替换目标目录；
                // 旧版本会备份保留，mods/preload 等用户数据会迁移过去，失败时回滚，不会留下半成品
                var lastPercent = -1;
                var extractDir = ModBedrockInstall.InstallGdk(filePath, BedrockVersionsDir, versionId,
                    (_, index, total) =>
                    {
                        if (total <= 0) return;
                        var percent = index * 100 / total;
                        if (percent == lastPercent) return;
                        lastPercent = percent;
                        ModBase.RunInUi(() => LabDetail.Text =
                            Lang.Text("Bedrock.Gdk.ExtractingFile", versionId, percent));
                    });

                ModBase.Log($"[Bedrock] GDK 版本已解包: {versionId}",
                    ModBase.LogLevel.Normal,
                    userSummary: Lang.Text("Bedrock.Gdk.ExtractSuccessSummary", versionId));

                ModBase.RunInUi(() =>
                {
                    IsBedrockInstalled = true;
                    BedrockInstallPath = extractDir;
                    BedrockVersion = versionId;
                    _isStorePackage = false;
                    _isGdkPortable = true;
                    BtnUpdate.Visibility = Visibility.Collapsed;
                    ShapeStatusDot.Fill = (Brush)FindResource("ColorBrush3");
                    LabStatus.Text = Lang.Text("Bedrock.Gdk.Extracted");
                    LabDetail.Text = Lang.Text("Bedrock.Status.CopyPathHint");
                    LabVersion.Text = Lang.Text("Bedrock.Status.Version", versionId);
                    BtnLaunch.IsEnabled = true;
                    BtnLaunch.Text = Lang.Text("Bedrock.Action.Launch");
                    RefreshLocalVersions();
                    ModMain.frmBedrockLeft?.RefreshStatus();
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "GDK 版本解包失败",
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Bedrock.Gdk.ExtractFailed", versionId, ex.Message));

                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Install.Failed");
                    LabDetail.Text = Lang.Text("Bedrock.Install.FailedHint", filePath);
                    BtnLaunch.IsEnabled = true;
                });
            }
            finally
            {
                ModBase.RunInUi(() =>
                {
                    _installingVersions.Remove(versionId);
                    RefreshLocalVersions();
                });
            }
        });
    }

    private void LocalOpenFolder_Click(object sender, EventArgs e)
    {
        if (sender is not MyIconButton button || button.Tag is not string versionId)
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
        if (sender is not MyIconButton button || button.Tag is not string versionId)
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
        if (sender is not MyIconButton button || button.Tag is not string versionId)
            return;
        if (_installingVersions.Contains(versionId)) return;

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
        _installingVersions.Add(newVersionId);
        LabStatus.Text = Lang.Text("Bedrock.Local.Copying");
        LabDetail.Text = Lang.Text("Bedrock.Local.CopyingDetail", displayName);

        Task.Run(() =>
        {
            try
            {
                ModBedrockInstance.CopyPortableInstance(BedrockVersionsDir, versionId, newVersionId,
                    text => ModBase.RunInUi(() => LabDetail.Text = text));
                ModBedrockLoader.SetLoader(newVersionId, ModBedrockLoader.GetLoader(versionId));

                ModBase.Log($"[Bedrock] 已复制实例: {versionId} -> {newVersionId}",
                    ModBase.LogLevel.Normal,
                    userSummary: Lang.Text("Bedrock.Local.Copied", newVersionId));
                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Local.Copied", newVersionId);
                    LabDetail.Text = Lang.Text("Bedrock.Local.CopiedDetail", newVersionId);
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "复制基岩版实例失败",
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Bedrock.Local.CopyFailed", ex.Message));
                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Local.CopyFailed", ex.Message);
                    LabDetail.Text = "";
                });
            }
            finally
            {
                _installingVersions.Remove(newVersionId);
                ModBase.RunInUi(RefreshLocalVersions);
            }
        });
    }

    private void LocalDelete_Click(object sender, EventArgs e)
    {
        if (sender is not MyIconButton btn || btn.Tag is not string versionId)
            return;

        var packageDir = ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId);
        var extractDir = ModBedrockUwp.GetVersionDirectory(
            Path.Combine(BedrockVersionsDir, "extracted"), versionId);
        var isRegistered = string.Equals(_registeredVersionId, versionId, StringComparison.OrdinalIgnoreCase);
        var isGdk = IsGdkVersion(versionId);

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
                    IsBedrockInstalled = false;
                    BedrockInstallPath = null;
                    BedrockVersion = null;
                    _isGdkPortable = false;
                    DetectBedrockInstallation();
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
    private void ImportPackage_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Lang.Text("Bedrock.Import.DialogTitle"),
            Filter = Lang.Text("Bedrock.Import.Filter"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        var sourcePath = dialog.FileName;

        LabStatus.Text = Lang.Text("Bedrock.Import.Importing");
        LabDetail.Text = Path.GetFileName(sourcePath);
        BtnImport.IsEnabled = false;

        Task.Run(() =>
        {
            try
            {
                var identityVersion = TryReadPackageIdentityVersion(sourcePath);
                var versionId = ResolveImportedVersionId(identityVersion, sourcePath);

                var destDir = ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId);
                Directory.CreateDirectory(destDir);
                var destPath = Path.Combine(destDir, Path.GetFileName(sourcePath));
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath),
                        StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourcePath, destPath, true);

                ModBase.Log($"[Bedrock] 已导入安装包: {versionId} -> {destPath}",
                    ModBase.LogLevel.Normal,
                    userSummary: Lang.Text("Bedrock.Import.Success", versionId));

                ModBase.RunInUi(() =>
                {
                    RefreshLocalVersions();
                    StartInstall(destPath, versionId);
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "导入基岩版安装包失败",
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Bedrock.Import.FailedWithMessage", ex.Message));
                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Import.Failed");
                    LabDetail.Text = ex.Message;
                });
            }
            finally
            {
                ModBase.RunInUi(() => BtnImport.IsEnabled = true);
            }
        });
    }

    /// <summary>
    ///     从安装包（或包束）的清单中读取 Identity 的 Version。
    /// </summary>
    private static string? TryReadPackageIdentityVersion(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var manifestName in new[] { "AppxManifest.xml", "AppxBundleManifest.xml" })
            {
                var entry = archive.GetEntry(manifestName);
                if (entry is null) continue;

                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                var identity = doc.Root?.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "Identity");
                var version = identity?.Attribute("Version")?.Value;
                if (!string.IsNullOrEmpty(version)) return version;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 读取安装包版本信息失败: {ex.Message}", ModBase.LogLevel.Normal);
        }

        return null;
    }

    /// <summary>
    ///     用安装包的 Identity 版本号匹配 mcappx 的版本数据，得到可读的版本名。
    ///     匹配失败时退回 Identity 版本号或文件名。
    /// </summary>
    private string ResolveImportedVersionId(string? identityVersion, string sourcePath)
    {
        if (!string.IsNullOrEmpty(identityVersion))
        {
            var normalized = identityVersion.Trim();
            // Identity 固定为四段，mcappx 的 ID 省略了末尾的 .0
            if (normalized.Split('.').Length == 4 && normalized.EndsWith(".0", StringComparison.Ordinal))
                normalized = normalized[..^2];

            if (_versionCache is not null)
            {
                foreach (var node in _versionCache)
                {
                    if (node is not JsonObject version) continue;
                    if (string.Equals((string)version["packageVersion"], normalized,
                            StringComparison.OrdinalIgnoreCase))
                        return (string)version["id"] ?? normalized;
                }
            }

            return normalized;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileNameWithoutExtension(sourcePath), @"\d+\.\d+(?:\.\d+)?");
        return match.Success ? match.Value : Path.GetFileNameWithoutExtension(sourcePath);
    }

    private static TextBlock CreateHintText(string text)
    {
        var hint = new TextBlock
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("ColorBrushGray3"),
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

    private static readonly string[] BedrockPackageExtensions = [".appx", ".msix", ".appxbundle", ".msixbundle", ".msixvc"];

    /// <summary>
    ///     获取目录内的基岩版安装包文件（按扩展名过滤）。
    /// </summary>
    private static List<string> GetPackageFiles(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory)
            .Where(f => BedrockPackageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();
    }

    /// <summary>
    ///     判断 URL 末段是否为安装包文件名。FE3 解析出的地址无扩展名，不能覆盖默认文件名。
    /// </summary>
    private static bool IsPackageFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return BedrockPackageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());
    }

    #endregion

    #region 在线版本下载

    private void LoadVersionList()
    {
        try
        {
            var versionListLoader = new ModLoader.LoaderTask<string, JsonObject>(
                "BedrockVersionList", ModDownload.DlBedrockListMain);
            versionListLoader.PreviewFinish += _ =>
            {
                ModBase.RunInUiWait(() => BuildVersionList(versionListLoader.output));
            };
            versionListLoader.Start();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "加载基岩版版本列表失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Bedrock.List.LoadFailed"));
        }
    }

    private void BuildVersionList(JsonObject versionData)
    {
        try
        {
            _versionCache = versionData["versions"] as JsonArray;
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

        if (_versionCache is null || _versionCache.Count == 0)
        {
            LabVersionSummary.Text = Lang.Text("Bedrock.List.Empty");
            PanVersionList.Children.Add(CreateHintText(Lang.Text("Bedrock.List.EmptyHint")));
            return;
        }

        var keyword = (SearchBox.Text ?? "").Trim();

        // 数据已按版本号降序排列，这里顺序分组即可
        var grouped = new List<(string Series, List<JsonObject> Items)>();
        var total = 0;

        foreach (var node in _versionCache)
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
    private MyCard BuildSeriesCard(string series, List<JsonObject> items, bool expand)
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

        var card = new MyCard
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

    private MyListItem BuildVersionRow(JsonObject version)
    {
        var versionId = (string)version["id"] ?? Lang.Text("Bedrock.List.UnknownVersion");
        var releaseDate = (string)version["releaseDate"] ?? "";
        var buildType = (string)version["buildType"] ?? "UWP";
        var available = (bool?)version["available"] ?? true;
        var webAvailable = (bool?)version["webAvailable"] ?? available;

        _versionAvailable[versionId] = available;
        _versionBuildTypes[versionId] = buildType;

        MyIconButton? downloadBtn = null;
        if (available)
        {
            downloadBtn = new MyIconButton
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

        var row = new MyListItem
        {
            Title = versionId,
            Info = releaseDate,
            Tags = tags,
            Type = available ? MyListItem.CheckType.Clickable : MyListItem.CheckType.None,
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
            var webBtn = new MyTextButton
            {
                Text = Lang.Text("Bedrock.Version.WebDownload"),
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
            MyListItem.SetColumnSpan(webBtn, 10);
            MyListItem.SetRowSpan(webBtn, 10);
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
                Foreground = (Brush)FindResource("ColorBrushGray3"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
                IsHitTestVisible = false
            };
            MyListItem.SetColumnSpan(noWayText, 10);
            MyListItem.SetRowSpan(noWayText, 10);
            row.MinPaddingRight = 78;
            row.Children.Add(noWayText);
        }

        UpdateVersionButtonState(versionId);
        return row;
    }

    private void BedrockVersionWebDownload_Click(object sender, EventArgs e)
    {
        if (sender is not MyTextButton btn || btn.Tag is not string versionId) return;

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
        else if (_installingVersions.Contains(versionId))
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
        if (sender is not MyIconButton btn || btn.Tag is not string versionId)
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
                if (IsPackageFileName(seg))
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

    private void InstallBedrockPackage(string filePath, string versionId, bool migrateStore = false)
    {
        Task.Run(async () =>
        {
            string? dataBackup = null;
            try
            {
                ModBase.Log($"[Bedrock] 开始安装: {filePath}", ModBase.LogLevel.Normal);

                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Install.CheckingEnvironment");
                    LabDetail.Text = Lang.Text("Bedrock.Install.CheckingDependencies");
                });

                // 结束正在运行的基岩版，避免文件被占用
                ModBedrockUwp.StopMinecraftProcesses();

                // 参考 BedrockBoot：安装前补齐安装包 Manifest 声明的 VCLibs / .NET Native 等 UWP 依赖
                await ModBedrockUwp.EnsureDependenciesAsync(filePath).ConfigureAwait(false);

                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Install.Installing");
                    LabDetail.Text = Lang.Text("Bedrock.Install.InstallingVersion", versionId);
                });

                // 商店版是「已打包」版本，未打包的旧版本无法覆盖注册（0x80073CFB）。
                // 先备份应用数据再完整卸载，安装完成后恢复；开发者注册版则可以直接覆盖注册。
                if (migrateStore)
                {
                    ModBase.Log("[Bedrock] 检测到商店版基岩版，先备份应用数据并完整卸载", ModBase.LogLevel.Normal);
                    ModBase.RunInUi(() =>
                    {
                        LabStatus.Text = Lang.Text("Bedrock.Install.SavingData");
                        LabDetail.Text = Lang.Text("Bedrock.Install.SavingDataDetail");
                    });
                    dataBackup = ModBedrockUwp.BackupPackageData(versionId,
                        text => ModBase.RunInUi(() => LabDetail.Text = text));
                    ModBase.RunInUi(() =>
                    {
                        LabStatus.Text = Lang.Text("Bedrock.Install.Replacing");
                        LabDetail.Text = Lang.Text("Bedrock.Install.PreservingData");
                    });
                    ModBedrockUwp.UninstallCompletely();
                }

                try
                {
                    // Manifest 已被改写为更高的版本号，可直接覆盖注册已安装的新版本（降级）
                    InstallWithRegister(filePath, versionId);
                }
                catch (Exception firstEx)
                {
                    if (migrateStore || ModBedrockUwp.GetInstalledPackage() is null)
                        throw;

                    // 覆盖注册失败时，保留存档卸载后重新安装
                    ModBase.Log($"[Bedrock] 覆盖注册失败，改为保留存档卸载后重试: {firstEx.Message}",
                        ModBase.LogLevel.Normal);

                    ModBase.RunInUi(() =>
                    {
                        LabStatus.Text = Lang.Text("Bedrock.Install.Replacing");
                        LabDetail.Text = Lang.Text("Bedrock.Install.PreservingData");
                    });

                    ModBedrockUwp.UninstallPreservingData();
                    InstallWithRegister(filePath, versionId);
                }

                if (dataBackup is not null)
                {
                    ModBase.Log($"[Bedrock] 正在恢复商店版数据: {dataBackup}", ModBase.LogLevel.Normal);
                    ModBase.RunInUi(() =>
                    {
                        LabStatus.Text = Lang.Text("Bedrock.Install.RestoringData");
                        LabDetail.Text = Lang.Text("Bedrock.Install.RestoringDataDetail");
                    });
                    ModBedrockUwp.RestorePackageData(dataBackup);
                }

                ModBase.Log($"[Bedrock] 安装完成: {versionId}",
                    ModBase.LogLevel.Normal,
                    userSummary: Lang.Text("Bedrock.Install.SuccessSummary", versionId));

                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Install.Success");
                    LabDetail.Text = Lang.Text("Bedrock.Install.InstalledVersion", versionId);
                    BtnLaunch.IsEnabled = true;
                    BtnLaunch.Text = Lang.Text("Bedrock.Action.Launch");
                });
            }
            catch (Exception ex)
            {
                var summary = Lang.Text("Bedrock.Install.FailedSummary", versionId, ex.Message);
                if (dataBackup is not null)
                {
                    summary += Environment.NewLine + $"商店版数据已备份到：{dataBackup}";
                    ModBase.Log($"[Bedrock] 商店版数据备份保留在 {dataBackup}，修复后可在该目录手动恢复存档",
                        ModBase.LogLevel.Normal);
                }

                ModBase.Log(ex, "基岩版安装失败", ModBase.LogLevel.Feedback, userSummary: summary);

                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Install.Failed");
                    LabDetail.Text = Lang.Text("Bedrock.Install.FailedHint", filePath);
                });
            }
            finally
            {
                _installingVersions.Remove(versionId);
                ModBase.RunInUi(() =>
                {
                    RefreshLocalVersions();
                    DetectBedrockInstallation();
                });
            }
        });
    }

    /// <summary>
    ///     参考 BedrockBoot：使用开发模式注册安装基岩版包。
    ///     开发模式（Add-AppxPackage -Register）安装的包不会被 Microsoft Store 自动更新。
    ///     对所有 UWP 格式（.appx / .msix / .appxbundle / .msixbundle）统一处理：解压 → 删除签名 → 注册。
    ///     GDK 的 .msixvc 走 <see cref="InstallGdkPackage" />，不会进入这里。
    /// </summary>
    /// <param name="filePath">安装包文件路径</param>
    /// <param name="versionId">版本标识</param>
    private void InstallWithRegister(string filePath, string versionId)
    {
        var extractDir = ModBedrockUwp.GetVersionDirectory(Path.Combine(BedrockVersionsDir, "extracted"), versionId);

        ModBase.Log($"[Bedrock] 解压安装包到: {extractDir}", ModBase.LogLevel.Normal);

        // 清理旧解压目录
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        // 解压安装包（.appx / .msix / .msixvc / .appxbundle 本质都是 ZIP 格式）
        try
        {
            ZipFile.ExtractToDirectory(filePath, extractDir);
        }
        catch (Exception ex)
        {
            // 解压失败说明是加密格式（如加密的 .msixvc），尝试直接安装
            ModBase.Log($"[Bedrock] 解压失败，尝试直接安装: {ex.Message}", ModBase.LogLevel.Normal);
            var fallbackResult = ModBase.ShellAndGetOutput("powershell.exe",
                ModBedrockUwp.GetAppxInstallArguments(filePath),
                timeout: 300000);
            if (fallbackResult.Contains("Exception") || fallbackResult.Contains("错误"))
                throw new Exception(fallbackResult.Length > 300 ? fallbackResult[..300] : fallbackResult);
            return;
        }

        // 验证 AppxManifest.xml 存在；.appxbundle / .msixbundle 解压后只有内部包，需要再解一层
        var manifestPath = Path.Combine(extractDir, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            var innerPackage = Directory.GetFiles(extractDir, "*.appx")
                .Concat(Directory.GetFiles(extractDir, "*.msix"))
                .OrderByDescending(f => Path.GetFileName(f).Contains("x64", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (innerPackage is null)
                throw new Exception(Lang.Text("Bedrock.Install.ManifestMissing"));

            var innerDir = Path.Combine(extractDir, "inner");
            if (Directory.Exists(innerDir)) Directory.Delete(innerDir, true);
            Directory.CreateDirectory(innerDir);
            ZipFile.ExtractToDirectory(innerPackage, innerDir);

            manifestPath = Path.Combine(innerDir, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
                throw new Exception(Lang.Text("Bedrock.Install.ManifestMissing"));

            extractDir = innerDir;
            ModBase.Log($"[Bedrock] 已从包束中取出内部安装包: {Path.GetFileName(innerPackage)}",
                ModBase.LogLevel.Normal);
        }

        // 参考 BedrockBoot：删除签名文件，阻断 Microsoft Store 更新
        var sigPath = Path.Combine(extractDir, "AppxSignature.p7x");
        if (File.Exists(sigPath))
        {
            File.Delete(sigPath);
            ModBase.Log("[Bedrock] 已删除 AppxSignature.p7x，阻断 Store 自动更新", ModBase.LogLevel.Normal);
        }
        var blockMapPath = Path.Combine(extractDir, "AppxBlockMap.xml");
        if (File.Exists(blockMapPath)) File.Delete(blockMapPath);

        // 参考 BedrockLauncher.Core：改写 Manifest（提升版本号、TrustLevel、runFullTrust、SCCD、Extensions）
        // 旧版本包直接注册会因版本号更低、缺少受信任能力而失败
        ModBedrockUwp.EditManifest(extractDir, $"Minecraft {versionId}");
        ModBase.Log("[Bedrock] 已改写 AppxManifest.xml 以支持旧版本安装", ModBase.LogLevel.Normal);

        // 补全 .msixvc 增量更新包可能缺失的资源文件
        var splashPath = Path.Combine(extractDir, "MCSplashScreen.png");
        if (!File.Exists(splashPath))
        {
            ModBase.Log("[Bedrock] 补全缺失的 MCSplashScreen.png", ModBase.LogLevel.Normal);
            CreateMinimalPng(splashPath);
        }

        // 参考 BedrockBoot：检查开发者模式，-Register 需要开发者模式或旁加载模式
        if (!IsDeveloperModeEnabled())
        {
            ModBase.Log("[Bedrock] 开发者模式未启用，尝试自动启用...", ModBase.LogLevel.Normal);
            if (!EnableDeveloperMode())
            {
                ModBase.Log("[Bedrock] 无法启用开发者模式（需要管理员权限），回退到直接安装", ModBase.LogLevel.Normal);
                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = Lang.Text("Bedrock.Install.DeveloperModeHint");
                    LabDetail.Text = Lang.Text("Bedrock.Install.DeveloperModeDetail");
                });
            }
        }

        // 方式1：使用 Add-AppxPackage -Register 注册（开发模式，Store 不会自动更新）
        if (IsDeveloperModeEnabled())
        {
            var registerResult = ModBase.ShellAndGetResult("powershell.exe",
                ModBedrockUwp.GetAppxInstallArguments(manifestPath, register: true),
                timeout: 300000);

            if (registerResult.Succeeded)
            {
                ModBase.Log("[Bedrock] 开发模式注册成功，Store 不会自动更新此版本", ModBase.LogLevel.Normal);
                return;
            }

            ModBase.Log($"[Bedrock] -Register 失败（退出码 {registerResult.ExitCode}），尝试重新打包为 .appx: {Tail(registerResult.Output)}",
                ModBase.LogLevel.Normal);
        }

        // 方式2：重新打包为 .appx 后安装（回退方案）
        var appxPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"Minecraft_Bedrock_{versionId}.appx");
        if (File.Exists(appxPath)) File.Delete(appxPath);
        ZipFile.CreateFromDirectory(extractDir, appxPath);

        var appxResult = ModBase.ShellAndGetResult("powershell.exe",
            ModBedrockUwp.GetAppxInstallArguments(appxPath),
            timeout: 300000);

        if (!appxResult.Succeeded)
            throw new Exception($"安装包部署失败（退出码 {appxResult.ExitCode}）：{Tail(appxResult.Output)}");
    }

    private static string Tail(string text, int length = 400)
    {
        var value = text.Trim();
        return value.Length <= length ? value : "..." + value[^length..];
    }

    #region 开发者模式检测（参考 BedrockBoot DeveloperModeHelper）

    /// <summary>
    ///     参考 BedrockBoot DeveloperModeHelper.IsDeveloperModeViaPowerShell：
    ///     检查注册表确认开发者模式或旁加载模式是否已启用。
    ///     开发者模式（AllowDevelopmentWithoutDevLicense = 1）或
    ///     旁加载模式（AllowAllTrustedApps = 1）均可支持 Add-AppxPackage -Register。
    /// </summary>
    private static bool IsDeveloperModeEnabled()
    {
        try
        {
            var result = ModBase.ShellAndGetOutput("powershell.exe",
                "-NoProfile -Command \"" +
                "$paths = @('HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock', " +
                "'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Appx'); " +
                "foreach ($p in $paths) { " +
                "  try { $v = Get-ItemProperty -Path $p -Name 'AllowDevelopmentWithoutDevLicense' -ErrorAction Stop; " +
                "    if ($v.AllowDevelopmentWithoutDevLicense -eq 1) { Write-Output 'true'; exit } " +
                "  } catch { } " +
                "}; " +
                "try { $v = Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock' -Name 'AllowAllTrustedApps' -ErrorAction Stop; " +
                "  if ($v.AllowAllTrustedApps -eq 1) { Write-Output 'true'; exit } " +
                "} catch { }; " +
                "Write-Output 'false'\"",
                timeout: 10000);

            return result.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 开发者模式检测失败: {ex.Message}", ModBase.LogLevel.Normal);
            return false;
        }
    }

    /// <summary>
    ///     尝试启用开发者模式（需要管理员权限）。
    ///     参考 BedrockBoot CoreOptions.IsAutoOpenDevelopment。
    /// </summary>
    private static bool EnableDeveloperMode()
    {
        try
        {
            var result = ModBase.ShellAndGetOutput("powershell.exe",
                "-NoProfile -Command \"" +
                "try { " +
                "  New-Item -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock' -Force -ErrorAction SilentlyContinue | Out-Null; " +
                "  Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock' -Name 'AllowDevelopmentWithoutDevLicense' -Value 1 -ErrorAction Stop; " +
                "  Write-Output 'true' " +
                "} catch { Write-Output 'false' }\"",
                timeout: 10000);

            return result.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 启用开发者模式失败: {ex.Message}", ModBase.LogLevel.Normal);
            return false;
        }
    }

    #endregion

    /// <summary>
    ///     创建一个最小的 1x1 白色 PNG 文件，用于补全 .msixvc 增量更新包缺失的启动画面。
    /// </summary>
    private static void CreateMinimalPng(string path)
    {
        // 有效的 1x1 白色 PNG（base64），使用 System.Drawing 生成
        try
        {
            // 尝试通过 PowerShell 生成（利用系统自带的 GDI+）
            var psScript = $@"
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap(1, 1)
$bmp.SetPixel(0, 0, [System.Drawing.Color]::White)
$bmp.Save('{path.Replace("'", "''")}', [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
";
            ModBase.ShellAndGetOutput("powershell.exe",
                $"-NoProfile -Command \"{psScript}\"",
                timeout: 10000);
        }
        catch
        {
            // 回退：写入硬编码的最小 PNG
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP4z8BQDwAFeAFK5RqavAAAAABJRU5ErkJggg==");
            File.WriteAllBytes(path, png);
        }
    }

    #endregion
}
