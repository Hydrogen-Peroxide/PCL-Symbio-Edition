using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Network;

namespace PCL;

/// <summary>Owns Bedrock detection, registration, installation and launch state independently of the desktop pages.</summary>
internal sealed class BedrockLaunchSession
{
    internal static BedrockLaunchSession Current { get; } = new();
    private BedrockLaunchSession() { }
    internal event Action? StateChanged;
    internal event Action? LibraryChanged;
    internal event Action? LaunchStarted;
    internal event Action<bool, string>? LaunchFinished;
    internal event Action<string>? LogLine;
    internal event Action? CatalogRequested;
    private readonly HashSet<string> installingVersions = [];
    internal IReadOnlySet<string> InstallingVersions => installingVersions;
    private string? _registeredVersionId;
    private bool _switchingVersion, _isStorePackage, _isGdkPortable, _launching, _detecting;
    private int _launchGeneration;
    internal bool IsLaunching => _launching;
    internal bool IsSwitching => _switchingVersion;
    internal bool IsStorePackage => _isStorePackage;
    internal bool IsPortable => _isGdkPortable;
    internal string? RegisteredVersionId => _registeredVersionId;
    internal bool CanExecute => Enabled && !_launching && !_switchingVersion && installingVersions.Count == 0;
    internal bool IsBedrockInstalled { get; private set; }
    internal string? BedrockVersion { get; private set; }
    internal string? BedrockInstallPath { get; private set; }
    internal double Progress { get; private set; }
    internal string Status { get; private set { field = value; NotifyStateChanged(); } } = "";
    internal string Detail { get; private set { field = value; NotifyStateChanged(); } } = "";
    internal string InstalledVersionText { get; private set { field = value; NotifyStateChanged(); } } = "";
    internal string LaunchText { get; private set { field = value; NotifyStateChanged(); } } = "";
    private bool Enabled { get; set { field = value; NotifyStateChanged(); } } = true;
    private static string BedrockVersionsDir => ModBedrockInstance.VersionsDirectory;

    internal bool IsImporting { get; private set; }

    internal void ImportPackage(string sourcePath)
    {
        if (IsImporting) return;


        Status = Lang.Text("Bedrock.Import.Importing");
        Detail = Path.GetFileName(sourcePath);
        IsImporting = true;
        NotifyStateChanged();

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
                    NotifyLibraryChanged();
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
                    Status = Lang.Text("Bedrock.Import.Failed");
                    Detail = ex.Message;
                });
            }
            finally
            {
                ModBase.RunInUi(() => { IsImporting = false; NotifyStateChanged(); });
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

            var versions = BedrockVersionCatalog.Versions;
            if (versions is not null)
            {
                foreach (var node in versions)
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

    internal void CopyVersion(string versionId, string newVersionId)
    {
        var displayName = ModBedrockInstance.GetDisplayName(BedrockVersionsDir, versionId);
        if (!installingVersions.Add(newVersionId)) return;
        Status = Lang.Text("Bedrock.Local.Copying");
        Detail = Lang.Text("Bedrock.Local.CopyingDetail", displayName);

        Task.Run(() =>
        {
            try
            {
                ModBedrockInstance.CopyPortableInstance(BedrockVersionsDir, versionId, newVersionId,
                    text => ModBase.RunInUi(() => Detail = text));
                ModBedrockLoader.SetLoader(newVersionId, ModBedrockLoader.GetLoader(versionId));

                ModBase.Log($"[Bedrock] 已复制实例: {versionId} -> {newVersionId}",
                    ModBase.LogLevel.Normal,
                    userSummary: Lang.Text("Bedrock.Local.Copied", newVersionId));
                ModBase.RunInUi(() =>
                {
                    Status = Lang.Text("Bedrock.Local.Copied", newVersionId);
                    Detail = Lang.Text("Bedrock.Local.CopiedDetail", newVersionId);
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "复制基岩版实例失败",
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Bedrock.Local.CopyFailed", ex.Message));
                ModBase.RunInUi(() =>
                {
                    Status = Lang.Text("Bedrock.Local.CopyFailed", ex.Message);
                    Detail = "";
                });
            }
            finally
            {
                ModBase.RunInUi(() =>
                {
                    installingVersions.Remove(newVersionId);
                    NotifyLibraryChanged();
                });
            }
        });
    }

    private void WriteLog(string text) => LogLine?.Invoke(text);
    private void NotifyStateChanged() => StateChanged?.Invoke();
    internal void NotifyLibraryChanged()
    {
        UpdateInstalledVersionId();
        LibraryChanged?.Invoke();
        NotifyStateChanged();
    }
    internal void ClearInstallation() => ApplyInstallationState(null, null, null);

    private static readonly string[] BedrockPackageExtensions = [".appx", ".msix", ".appxbundle", ".msixbundle", ".msixvc"];
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public void DetectBedrockInstallation()
    {
        if (_detecting || _launching || _switchingVersion) return;
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
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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

            Status = Lang.Text("Bedrock.Status.Detected");
            Detail = Lang.Text("Bedrock.Status.CopyPathHint");
            InstalledVersionText = string.IsNullOrEmpty(bedrockVersion)
                ? "Minecraft Bedrock Edition"
                : Lang.Text("Bedrock.Status.Version", bedrockVersion);
            Enabled = true;
            LaunchText = Lang.Text("Bedrock.Action.Launch");
        }
        else
        {

            Status = Lang.Text("Bedrock.Status.NotFound");
            Detail = Lang.Text("Bedrock.Status.DownloadOrImportHint");
            InstalledVersionText = Lang.Text("Bedrock.Status.NotInstalled");
            Enabled = true;
            LaunchText = Lang.Text("Bedrock.Action.Download");
        }

        // 检测结果会影响「已安装」标记，重新渲染本地版本行
        NotifyLibraryChanged();
        
        // 同步刷新左侧页面的状态，否则左侧会一直停留在检测前的“未检测到”
        NotifyStateChanged();
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
                // UWP 版与 GDK 版同时安装时会返回多行，整段 Trim 后不是一个有效路径，
                // 会被误判成「未安装」，所以逐行取第一个真实存在的目录
                foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (Directory.Exists(trimmed))
                        return trimmed;
                }
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
    internal static bool IsGdkVersion(string versionId)
    {
        // 外部登记的版本目录也要参与判断，否则外部 GDK 版本会被当成普通 UWP 版本
        var roots = new List<string> { BedrockVersionsDir };
        roots.AddRange(ModBedrockInstance.ExternalDirectories);
        foreach (var root in roots)
        {
            var dir = ModBedrockUwp.GetVersionDirectory(root, versionId);
            if (GetPackageFiles(dir).Any(ModBedrockGdk.IsGdkPackage)) return true;
            // 已解包的 GDK 版本没有安装包，目录本身就是 GDK 构建
            if (ModBedrockGdk.IsGdkBuild(dir)) return true;
        }

        return false;
    }

    /// <summary>该版本的 GDK 便携目录（已解包、可直接运行），找不到返回 null。</summary>
    internal static string? FindGdkReadyDirectory(string versionId) => FindPortableGdkDirectory(versionId);

    /// <summary>
    ///     路径是否为可直接运行的 GDK 便携版目录：extracted\&lt;版本号&gt; 或版本目录本身。
    ///     系统注册包的 InstallLocation 只要本身是 GDK 构建（带 MicrosoftGame.Config）同样算：
    ///     那样可以直接跑主程序完成 XUser 注入，而不是用 explorer 拉起包、把账号注入整个丢掉。
    /// </summary>
    private static bool IsPortableGdkPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var root = Path.GetFullPath(BedrockVersionsDir).TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var segments = full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar);
                var versionIndex = segments[0].Equals("extracted", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                if (segments.Length > versionIndex && !string.IsNullOrEmpty(segments[versionIndex]))
                    // UWP 版虽然也有 Minecraft.Windows.exe，但必须注册成系统包才能启动，不能当便携版直接跑
                    return ModBedrockGdk.IsGdkBuild(full);
            }

            // 启动器目录之外的注册安装位置（例如用户手动注册到别处的 GDK 版）：
            // 只要它本身就是 GDK 构建，就直接跑主程序，否则注入链路永远走不到
            return ModBedrockGdk.IsGdkBuild(full);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     查找某个版本可直接运行的 GDK 目录：优先 extracted\&lt;版本号&gt;，其次是版本目录本身。
    ///     会同时搜索已登记的外部版本目录；不是 GDK 构建或还没解包时返回 null，
    ///     由调用方继续走 UWP 的包注册流程。
    /// </summary>
    internal static string? FindPortableGdkDirectory(string versionId)
    {
        var roots = new List<string> { BedrockVersionsDir };
        roots.AddRange(ModBedrockInstance.ExternalDirectories);

        foreach (var root in roots)
        {
            foreach (var candidate in new[] { Path.Combine(root, "extracted"), root })
            {
                var directory = ModBedrockUwp.GetVersionDirectory(candidate, versionId);
                if (ModBedrockGdk.IsGdkBuild(directory)) return directory;
            }
        }

        return null;
    }

    /// <summary>
    ///     在已解包目录中寻找可用的 GDK 便携版，取版本号最高的一个。
    /// </summary>
    private static (string Path, string Version)? FindPortableGdkInstallation()
    {
        try
        {
            (string Path, string Version)? best = null;
            foreach (var root in new[] { Path.Combine(BedrockVersionsDir, "extracted"), BedrockVersionsDir })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var versionId = Path.GetFileName(dir);
                    if (versionId.Equals("extracted", StringComparison.OrdinalIgnoreCase) ||
                        versionId.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                        versionId.StartsWith('.')) continue;
                    if (!ModBedrockGdk.IsGdkBuild(dir)) continue;
                    if (best is null || ModDownload.CompareBedrockVersions(versionId, best.Value.Version) > 0)
                        best = (dir, versionId);
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    internal void RequestLaunch()
    {
        ModBase.Log(
            $"[Bedrock] 启动请求：选中={Config.Launch.BedrockSelectedVersion}，已安装={IsBedrockInstalled}，本地版本={(string.Join(",", ModBedrockInstance.GetLocalVersions()))}",
            ModBase.LogLevel.Debug);
        if (!CanExecute) return;
        // 实例选择页选中的实例优先：数据目录里存在就直接启动，
        // 不依赖系统里是否注册过基岩版（便携版/自己解包的版本本来就不在系统注册表里）
        var selectedInstance = Config.Launch.BedrockSelectedVersion;
        if (!string.IsNullOrEmpty(selectedInstance) &&
            ModBedrockInstance.GetLocalVersions().Contains(selectedInstance))
        {
            LaunchLocalVersion(selectedInstance);
            return;
        }

        if (!IsBedrockInstalled)
        {
            // 尚未检测到安装（或检测仍在后台进行），刷新一次检测并引导到下载列表
            DetectBedrockInstallation();
            CatalogRequested?.Invoke();
            return;
        }

        // 记住的选中版本优先：当前注册的不是它时，先切换到该版本
        var selected = Config.Launch.BedrockSelectedVersion;
        if (!string.IsNullOrEmpty(selected) && ModBedrockInstance.GetLocalVersions().Contains(selected) &&
            !string.Equals(selected, _registeredVersionId, StringComparison.OrdinalIgnoreCase))
        {
            LaunchLocalVersion(selected);
            return;
        }

        LaunchBedrock();
    }

    /// <summary>开始显示启动进度条，并禁用启动按钮直到本次启动结束。</summary>
    private void BeginLaunchProgress(string detail)
    {
        _launching = true;
        _launchGeneration++;
        Progress = .02;
        Enabled = false;
        Status = Lang.Text("Bedrock.Status.Launching");
        Detail = detail;
        WriteLog(Status + " " + detail);
        LaunchStarted?.Invoke();
        NotifyStateChanged();
    }

    /// <summary>推进到实际阶段进度（只会前进），可在后台线程调用。</summary>
    private void SetLaunchStage(double progress, string? detail = null)
    {
        ModBase.RunInUi(() =>
        {
            if (!_launching) return;
            Progress = Math.Clamp(Math.Max(Progress, progress), 0, 1);
            if (detail is not null)
            {
                Detail = detail;
                WriteLog(detail);
            }
            NotifyStateChanged();
        });
    }

    /// <summary>结束本次启动：成功时先补满进度条再收起，失败时立即收起并显示错误。</summary>
    private void EndLaunchProgress(bool success, string status, string detail)
    {
        ModBase.RunInUi(() =>
        {
            Status = status;
            Detail = detail;
            WriteLog(string.IsNullOrWhiteSpace(detail) ? status : status + "：" + detail);
            LaunchFinished?.Invoke(success, detail);
            var generation = ++_launchGeneration;
            if (!success) { Finish(); return; }
            Progress = 1;
            NotifyStateChanged();
            _ = Task.Delay(700).ContinueWith(_ => ModBase.RunInUi(Finish));
            void Finish()
            {
                if (generation != _launchGeneration) return;
                _launching = false;
                Enabled = true;
                NotifyStateChanged();
            }
        });
    }

    /// <summary>
    ///     把本次启动计入总启动次数：每次启动都计一次，
    ///     在游戏进程 / 包真正拉起后调用，不依赖窗口检测是否成功（与 Java 版一致）。
    /// </summary>
    private static void CountLaunch()
    {
        States.System.LaunchCount += 1;
    }

    /// <summary>
    ///     切换版本时已用 BeginLaunchProgress 占住了启动状态，切换成功后紧接着调用 LaunchBedrock
    ///     会被开头的防重入检查直接挡回去，游戏根本不会启动。这里先解除占用，让启动流程重新走一遍。
    /// </summary>
    private void ReleaseLaunchGate()
    {
        _launching = false;
    }

    /// <summary>
    ///     启动流程提前退出（找不到安装包等）时收起进度条。
    ///     不收起的话浮层会一直转圈，看起来就像「点了启动没反应」。
    /// </summary>
    private void AbortLaunch(string status, string detail)
    {
        EndLaunchProgress(false, status, detail);
    }

    /// <summary>
    ///     等待游戏窗口出现；拿不到进程句柄时（注册式启动）按进程名查找。
    ///     注册式启动由 explorer 拉起进程，拿不到句柄，所以还要看窗口标题——
    ///     UWP 版的窗口属于 ApplicationFrameHost，它的 MainWindowHandle 一直是 0。
    /// </summary>
    /// <param name="process">直接启动拿到的游戏进程；注册式启动为 null。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="failure">明确的失败原因（进程启动后立即退出 / 始终没有出现进程）；仅超时但有进程时为 null。</param>
    private static bool WaitForGameWindow(Process? process, TimeSpan timeout, out string? failure)
    {
        failure = null;
        var startedAt = DateTime.UtcNow;
        var deadline = startedAt + timeout;
        var observed = process;
        var ownsObserved = false;

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (observed is null)
                    {
                        observed = FindGameProcess();
                        ownsObserved = observed is not null;
                    }

                    if (observed is not null)
                    {
                        if (observed.HasExited)
                        {
                            failure = Lang.Text("Bedrock.Launch.Exited", observed.ExitCode);
                            return false;
                        }

                        observed.Refresh();
                        if (observed.MainWindowHandle != IntPtr.Zero) return true;
                    }

                    if (HasMinecraftWindow()) return true;

                    // 20 秒还没有游戏进程，说明激活被系统拦住了（包不完整、依赖缺失等），不必干等满 60 秒
                    if (observed is null && DateTime.UtcNow - startedAt > TimeSpan.FromSeconds(20))
                    {
                        failure = Lang.Text("Bedrock.Launch.NotStarted");
                        return false;
                    }
                }
                catch
                {
                    // 进程可能在枚举后立刻退出，忽略并继续轮询
                }

                Thread.Sleep(200);
            }
        }
        finally
        {
            if (ownsObserved) observed?.Dispose();
        }

        return false;
    }

    /// <summary>按进程名查找已激活的游戏进程。</summary>
    private static Process? FindGameProcess()
    {
        foreach (var name in new[] { "Minecraft.Windows", "Minecraft.Win10.DX11" })
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length == 0) continue;

                var first = processes[0];
                for (var i = 1; i < processes.Length; i++) processes[i].Dispose();
                return first;
            }
            catch
            {
                // 忽略单个进程的访问错误
            }
        }

        return null;
    }

    /// <summary>是否已经出现 Minecraft 的游戏窗口（兼容 UWP 的 ApplicationFrameHost 宿主）。</summary>
    private static bool HasMinecraftWindow()
    {
        var found = false;
        try
        {
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                var length = GetWindowTextLength(hwnd);
                if (length <= 0) return true;

                var title = new StringBuilder(length + 1);
                GetWindowText(hwnd, title, title.Capacity);
                if (!title.ToString().Contains("Minecraft", StringComparison.OrdinalIgnoreCase)) return true;
                if (!IsGameWindowProcess(hwnd)) return true;

                found = true;
                return false;
            }, IntPtr.Zero);
        }
        catch
        {
            // 枚举窗口失败不影响主流程，交给进程句柄判断
        }

        return found;
    }

    /// <summary>窗口是否属于游戏本体或它的 UWP 宿主，避免把商店/Xbox 窗口当成游戏窗口。</summary>
    private static bool IsGameWindowProcess(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0) return false;

            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName is "Minecraft.Windows" or "Minecraft.Win10.DX11" or "ApplicationFrameHost";
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public void LaunchBedrock()
    {
        if (_launching) return;
        if (string.IsNullOrEmpty(BedrockInstallPath))
        {
            DetectBedrockInstallation();
            return;
        }

        // 启动方式由实际目录决定：GDK 目录直接跑主程序，UWP 目录必须走包注册。
        // 只用缓存标记的话，切换版本后可能沿用上一个版本的启动方式（例如把 UWP 版当便携版裸跑）
        _isGdkPortable = IsPortableGdkPath(BedrockInstallPath);

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

                    // 启动前按「已选中的基岩版账号」准备会话。内存里的会话只在本次运行有效，
                    // 重启启动器后会为空，必须用保存的 refresh_token 续期；
                    // 否则注入链路不会启用，游戏会退回系统登录的 Xbox 账号。
                    SetLaunchStage(0.14, Lang.Text("Bedrock.Account.Preparing"));
                    BLoaderXUserBridge.Session? xuserSession;
                    try
                    {
                        xuserSession = ModBedrockAccounts.EnsureActiveSessionAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        // 明确失败，而不是悄悄用系统账号启动——那正是「进游戏还是本机账号」的成因
                        ModBase.Log(ex, "准备基岩版账号会话失败", ModBase.LogLevel.Hint,
                            userSummary: Lang.Text("Bedrock.Account.SessionExpired"));
                        throw new InvalidOperationException(Lang.Text("Bedrock.Account.SessionExpired"), ex);
                    }

                    var useBLoader = loader == ModBedrockLoader.BLoader || xuserSession is not null;

                    // BLoader 按需释放并校验 DLL；登录会话存在时即使用户选的是原版加载器也必须启用。
                    if (useBLoader)
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
                        xuserSession is null &&
                        TryLaunchGdkRegistered(installPath, BedrockVersion))
                    {
                        SetLaunchStage(0.6, Lang.Text("Bedrock.Launch.Stage.WaitingWindow"));
                    }
                    else
                    {
                        process = ModBedrockGdk.Launch(installPath, null, BedrockVersion, xuserSession)
                                  ?? throw new FileNotFoundException(Lang.Text("Bedrock.Gdk.ExecutableMissing"));
                        SetLaunchStage(0.55, Lang.Text("Bedrock.Launch.Stage.WaitingWindow"));
                    }

                    CountLaunch();

                    // 版本隔离的还原交给后端：登记本次启动，等游戏进程退出后再恢复共享目录；
                    // 注册式启动拿不到进程句柄时，后端会先等待进程出现，避免游戏还没启动就还原
                    if (isolationSession is not null)
                    {
                        ModBedrockIsolation.RestoreAfterGameExit(isolationSession, process);
                        watchingExit = true;
                    }

                    if (!WaitForGameWindow(process, TimeSpan.FromSeconds(60), out var gdkFailure))
                    {
                        // 进程在出窗口前就退出了，说明启动确实失败，不能报成功
                        if (gdkFailure is not null) throw new InvalidOperationException(gdkFailure);
                        ModBase.Log("[Bedrock] 60 秒内未检测到游戏窗口，游戏可能仍在启动",
                            ModBase.LogLevel.Hint);
                    }

                    // 进程句柄统一在 finally 中回收，隔离还原由后端负责

                    ModBase.Log($"[Bedrock] 正在启动 GDK 版本: {installPath}", ModBase.LogLevel.Normal);
                    SetLaunchStage(0.9);
                    EndLaunchProgress(true, Lang.Text("Bedrock.Status.Launched"), installPath);
                    return;
                }

                // 优先按当前实例的包启动：旧版 Manifest 被设为 AppListEntry=none，
                // minecraft:// 协议可能被商店版或其他版本抢占；直接启动“第一个已安装的基岩版包”
                // 也会在同一台机器注册过多个通道时启动成旧版本
                SetLaunchStage(0.4, Lang.Text("Bedrock.Launch.Stage.Starting"));
                if (!TryLaunchCurrentPackage())
                {
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
                }

                CountLaunch();

                SetLaunchStage(0.6, Lang.Text("Bedrock.Launch.Stage.WaitingWindow"));
                if (!WaitForGameWindow(null, TimeSpan.FromSeconds(60), out var uwpFailure))
                {
                    // 游戏进程都没起来（激活被系统拦截）或者起来后立刻退出，都说明这次启动失败了，
                    // 不能再报“已启动”让用户对着桌面等
                    if (uwpFailure is not null) throw new InvalidOperationException(uwpFailure);
                    ModBase.Log("[Bedrock] 60 秒内未检测到游戏窗口，游戏可能仍在启动",
                        ModBase.LogLevel.Hint);
                }

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

    /// <summary>
    ///     按当前实例目录精确启动其对应的已注册包。
    ///     只有确认系统里注册的包指向这个目录才会启动，避免切到新版本后启动的仍是旧版本。
    /// </summary>
    private bool TryLaunchCurrentPackage()
    {
        if (string.IsNullOrEmpty(BedrockInstallPath)) return false;

        if (ModBedrockUwp.LaunchInstalledPackageFromDirectory(BedrockInstallPath, out var detail))
        {
            ModBase.Log($"[Bedrock] 正在启动 Minecraft Bedrock Edition: {detail}", ModBase.LogLevel.Normal);
            return true;
        }

        ModBase.Log($"[Bedrock] 无法按当前实例启动（{detail}），改用系统默认的基岩版包", ModBase.LogLevel.Hint);
        return false;
    }

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
                // 带 desktop6:RegistryWriteVirtualization / FileSystemWriteVirtualization 的 GDK 清单
                // 注册时要求 unvirtualizedResources 受信任功能，而清单里没有声明，注册必然报 0x80080204。
                // 改写清单只会把版本目录弄脏（版本号、AppListEntry 都会被改），所以这类版本直接运行主程序
                if (NeedsUnvirtualizedResources(manifestPath))
                {
                    ModBase.Log("[Bedrock] 该 GDK 清单不支持注册式启动，直接运行主程序", ModBase.LogLevel.Debug);
                    return false;
                }

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

    /// <summary>
    ///     清单关掉了写虚拟化（RegistryWriteVirtualization / FileSystemWriteVirtualization），
    ///     这类 GDK 清单注册时需要 unvirtualizedResources 受限功能，而 EditManifest 会删掉所有 rescap 能力、
    ///     只补回 runFullTrust，改写后注册必然报 0x80080204。这种版本只能直接运行主程序。
    /// </summary>
    private static bool NeedsUnvirtualizedResources(string manifestPath)
    {
        try
        {
            return Regex.IsMatch(File.ReadAllText(manifestPath),
                "RegistryWriteVirtualization|FileSystemWriteVirtualization", RegexOptions.IgnoreCase);
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

    /// <summary>
    ///     判断当前安装的基岩版是否由启动器从某个本地版本注册而来。
    ///     兼容两种布局：extracted\&lt;版本号&gt;（含包束多出来的 inner）与版本目录本身。
    /// </summary>
    internal void UpdateInstalledVersionId()
    {
        _registeredVersionId = null;
        if (string.IsNullOrEmpty(BedrockInstallPath)) return;

        try
        {
            var root = Path.GetFullPath(BedrockVersionsDir).TrimEnd(Path.DirectorySeparatorChar);
            var installPath = Path.GetFullPath(BedrockInstallPath).TrimEnd(Path.DirectorySeparatorChar);
            if (!installPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;

            var segments = installPath[(root.Length + 1)..].Split(Path.DirectorySeparatorChar);
            var versionIndex = segments[0].Equals("extracted", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (segments.Length <= versionIndex) return;

            var segment = segments[versionIndex];
            if (!string.IsNullOrEmpty(segment))
                _registeredVersionId = segment;
        }
        catch
        {
            // 路径异常时忽略，仅影响「已安装」标记
        }
    }

    /// <summary>
    ///     查找版本解压后的可注册目录。包束会多解一层 inner。
    /// </summary>
    internal static (string? Directory, string? Manifest) FindExtractedVersion(string versionId)
    {
        var extractDir = ModBedrockUwp.GetVersionDirectory(Path.Combine(BedrockVersionsDir, "extracted"), versionId);
        var innerDir = Path.Combine(extractDir, "inner");

        var innerManifest = Path.Combine(innerDir, "AppxManifest.xml");
        if (File.Exists(innerManifest)) return (innerDir, innerManifest);

        var manifest = Path.Combine(extractDir, "AppxManifest.xml");
        return File.Exists(manifest) ? (extractDir, manifest) : (null, null);
    }

    /// <summary>
    ///     启动指定本地版本，并把它记为当前选中版本（之后「启动基岩版」会跟随）。
    /// </summary>
    internal void LaunchLocalVersion(string versionId)
    {
        if (_switchingVersion)
        {
            ModBase.Log($"[Bedrock] 上一个版本切换还没结束，忽略本次启动请求（{versionId}）",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Bedrock.Launching.Busy"));
            return;
        }

        // 切版本 / 重新注册可能要十几秒，先立刻把启动浮层顶上来，别让按钮看着没反应
        // 注意：本方法在后台线程执行，操作控件必须切回 UI 线程，否则会抛跨线程异常
        ModBase.Log($"[Bedrock] 准备切换版本 {versionId}（当前线程 {Environment.CurrentManagedThreadId}）",
            ModBase.LogLevel.Debug);
        try
        {
            ModBase.RunInUi(() =>
            {
                if (!_launching)
                    BeginLaunchProgress(Lang.Text("Bedrock.Launching.Switch", versionId));
            }, true);
            ModBase.Log("[Bedrock] 启动浮层已显示", ModBase.LogLevel.Debug);
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 显示启动浮层失败（已忽略，继续启动）：{ex}", ModBase.LogLevel.Debug);
        }

        // GDK 构建（带 MicrosoftGame.Config）直接运行主程序：不注册系统包，也就没有“切换”这回事，
        // 因此不存在切换到一半、启动成旧版本的问题；加载器（LeviLamina / BLoader）也只有这条路才生效
        var portableDir = FindPortableGdkDirectory(versionId);
        if (portableDir is not null)
        {
            ActivateGdkVersion(versionId, portableDir);
            return;
        }

        // 只有 GDK 安装包、还没解包：先解包成便携版
        if (IsGdkVersion(versionId))
        {
            var files = GetPackageFiles(ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId));
            if (files.Count == 0)
            {
                ModBase.Log($"[Bedrock] 版本 {versionId} 的安装包已被删除，无法解包启动",
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Bedrock.Local.PackageMissing"));
                AbortLaunch(Lang.Text("Bedrock.Status.LaunchFailed"), Lang.Text("Bedrock.Local.PackageMissing"));
                return;
            }

            // 尚未解包：先解包；因为这次是「启动」触发的，解包完成后要继续启动，而不是停在这里
            StartInstall(files[0], versionId, launchAfterInstall: true);
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
        Status = Lang.Text("Bedrock.Status.Switching");
        Detail = Lang.Text("Bedrock.Status.SwitchingDetail", versionId);
        Enabled = false;

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
                if (manifestPath is null)
                {
                    // 用户自己解包到 versions\<版本> 的目录：里面有 AppxManifest 就直接注册它
                    var directDir = ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId);
                    var directManifest = Path.Combine(directDir, "AppxManifest.xml");
                    if (File.Exists(directManifest))
                    {
                        targetDir = directDir;
                        manifestPath = directManifest;
                    }
                }

                if (manifestPath is not null)
                {
                    var result = RegisterExtractedVersion(manifestPath);

                    // 系统里已经装了更高版本的基岩版时，Windows 会拒绝降级注册（0x80073D06）。
                    // 解包目录本身是完好的，保留存档卸载掉那个更高版本再注册，才能真正切到目标版本；
                    // 否则会一路掉到「重新安装」，而安装包多半早已被清理，最后报成安装包缺失。
                    if (IsRegisterFailed(result) && result.Contains("0x80073D06", StringComparison.OrdinalIgnoreCase))
                    {
                        ModBase.Log($"[Bedrock] 已安装更高版本的基岩版，保留数据卸载后重新注册 {versionId}",
                            ModBase.LogLevel.Normal,
                            userSummary: Lang.Text("Bedrock.Launching.Downgrade", versionId));
                        ModBedrockUwp.UninstallPreservingData();
                        result = RegisterExtractedVersion(manifestPath);
                    }

                    // 注册命令没报错也不代表真的切过去了：系统里可能仍指向商店版或其它目录。
                    // 但查询本身可能失败或返回看不懂的内容，只有明确查到“指向别处”才回退重装，
                    // 否则会把一次成功的切换误判成失败（本次 1.21.113 就是这么被挡住的）
                    if (IsRegisterFailed(result))
                    {
                        ModBase.Log($"[Bedrock] 注册 {versionId} 失败，改为重新安装: {result.Trim()}",
                            ModBase.LogLevel.Normal);
                        targetDir = null;
                    }
                    else
                    {
                        var registered = VerifyRegisteredTarget(targetDir);
                        if (registered == false)
                        {
                            ModBase.Log($"[Bedrock] 注册命令已执行，但系统里注册的基岩版并未指向 {targetDir}",
                                ModBase.LogLevel.Normal);
                            targetDir = null;
                        }
                        else if (registered is null)
                        {
                            ModBase.Log("[Bedrock] 无法确认注册位置，按注册成功继续启动", ModBase.LogLevel.Normal);
                        }
                    }
                }

                if (targetDir is null)
                {
                    // 解压目录缺失或被清理：用已下载的安装包重新解压并注册
                    var files = GetPackageFiles(ModBedrockUwp.GetVersionDirectory(BedrockVersionsDir, versionId));
                    if (files.Count == 0)
                        throw new InvalidOperationException(Lang.Text("Bedrock.Local.SwitchFailed", versionId));
                    InstallWithRegister(files[0], versionId);
                    (targetDir, _) = FindExtractedVersion(versionId);
                    if (targetDir is null || VerifyRegisteredTarget(targetDir) == false)
                        throw new InvalidOperationException(Lang.Text("Bedrock.Local.SwitchFailed", versionId));
                }

                installedPath = targetDir;
                var package = ModBedrockUwp.ReadDirectoryManifest(targetDir);
                ModBase.Log(
                    package is null
                        ? $"[Bedrock] 已切换到版本 {versionId}"
                        : $"[Bedrock] 已切换到版本 {versionId}（{package.Value.Name} {package.Value.Version}）",
                    ModBase.LogLevel.Normal);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            ModBase.RunInUi(() =>
            {
                _switchingVersion = false;
                Enabled = true;

                if (error is not null)
                {
                    ModBase.Log(error, $"切换到基岩版 {versionId} 失败",
                        ModBase.LogLevel.Feedback,
                        userSummary: Lang.Text("Bedrock.Launch.Error"));
                    AbortLaunch(Lang.Text("Bedrock.Status.LaunchFailed"), error.Message);
                    NotifyLibraryChanged();
                    return;
                }

                IsBedrockInstalled = true;
                BedrockInstallPath = installedPath;
                BedrockVersion = versionId;
                _isStorePackage = false;
                // 刚刚注册的是 UWP 包，不能再按 GDK 便携版直接跑主程序
                _isGdkPortable = false;
                _registeredVersionId = versionId;
                InstalledVersionText = Lang.Text("Bedrock.Status.Version", BedrockVersion ?? "");
                SelectVersion(versionId);
                NotifyLibraryChanged();
                // 左侧状态栏也同步显示切换后的版本
                NotifyStateChanged();
                ReleaseLaunchGate();
                LaunchBedrock();
            });
        });
    }

    /// <summary>
    ///     确认系统里注册的基岩版是否指向这个解包目录。
    ///     返回 true 已指向该目录，false 明确指向别处，null 无法确认（查询失败或输出看不懂）。
    /// </summary>
    private static bool? VerifyRegisteredTarget(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return false;

        var identity = ModBedrockUwp.ReadDirectoryManifest(directory)?.Name;
        if (string.IsNullOrEmpty(identity)) return null;

        try
        {
            return ModBedrockRegistration.IsRegisteredFromDirectory(identity, directory);
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 校验注册位置失败（按未确认处理）: {ex.Message}", ModBase.LogLevel.Normal);
            return null;
        }
    }

    /// <summary>以开发模式注册解包目录，返回 PowerShell 的完整输出。</summary>
    private static string RegisterExtractedVersion(string manifestPath)
    {
        return ModBase.ShellAndGetOutput("powershell.exe",
            ModBedrockUwp.GetAppxInstallArguments(manifestPath, register: true),
            timeout: 300000);
    }

    /// <summary>PowerShell 命令出错时会输出 Exception 或「错误」，以此判断注册是否失败。</summary>
    private static bool IsRegisterFailed(string output)
    {
        return output.Contains("Exception", StringComparison.OrdinalIgnoreCase) || output.Contains("错误");
    }

    /// <summary>
    ///     将启动目标切换为 GDK 便携版并直接启动。不触碰系统包注册，因此其他版本仍可继续运行。
    /// </summary>
    internal void ActivateGdkVersion(string versionId, string extractDir, bool launchAfterActivate = true)
    {
        IsBedrockInstalled = true;
        BedrockInstallPath = extractDir;
        BedrockVersion = versionId;
        _isStorePackage = false;
        _isGdkPortable = true;
        InstalledVersionText = Lang.Text("Bedrock.Status.Version", BedrockVersion ?? "");

        InstalledVersionText = Lang.Text("Bedrock.Status.Version", versionId);
        SelectVersion(versionId);
        NotifyLibraryChanged();
        NotifyStateChanged();
        ReleaseLaunchGate();
        // 切换实例时只应用选择，不立刻启动
        if (launchAfterActivate) LaunchBedrock();
    }

    /// <summary>记住选中的版本，重启启动器后「启动基岩版」仍会跟随它。</summary>
    internal void SelectVersion(string versionId)
    {
        Config.Launch.BedrockSelectedVersion = versionId;
        NotifyStateChanged();
    }

    internal void StartInstall(string filePath, string versionId, bool launchAfterInstall = false)
    {
        if (installingVersions.Contains(versionId))
            return;

        // GDK 版是加密的 .msixvc，不能侧载注册，改为解密解包成便携版
        if (ModBedrockGdk.IsGdkPackage(filePath))
        {
            StartInstallGdk(filePath, versionId, launchAfterInstall);
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

        installingVersions.Add(versionId);
        NotifyLibraryChanged();

        Status = Lang.Text("Bedrock.Install.Installing");
        Detail = Lang.Text("Bedrock.Install.InstallingVersion", versionId);
        Enabled = false;

        InstallBedrockPackage(filePath, versionId, migrateStore);
    }

    /// <summary>
    ///     GDK 版安装：解包 .msixvc 到独立目录后直接可运行，不做系统包注册。
    /// </summary>
    private void StartInstallGdk(string filePath, string versionId, bool launchAfterInstall = false)
    {
        if (installingVersions.Contains(versionId))
            return;

        // 构建未内置 CIK 时直接提示，避免先结束进程、清空目录再抛出异常
        if (!ModBedrockGdk.HasContentKey(filePath))
        {
            ModBase.Log($"[Bedrock] 当前构建未内置 GDK 内容密钥（CIK），无法解包: {filePath}",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Bedrock.Gdk.CikMissing"));
            Status = Lang.Text("Bedrock.Install.Failed");
            Detail = Lang.Text("Bedrock.Gdk.CikMissing");
            return;
        }

        installingVersions.Add(versionId);
        NotifyLibraryChanged();

        Status = Lang.Text("Bedrock.Gdk.Extracting");
        Detail = Lang.Text("Bedrock.Gdk.ExtractingDetail", versionId);
        Enabled = false;

        InstallGdkPackage(filePath, versionId, launchAfterInstall);
    }

    private void InstallGdkPackage(string filePath, string versionId, bool launchAfterInstall = false)
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
                        ModBase.RunInUi(() => Detail =
                            Lang.Text("Bedrock.Gdk.ExtractingFile", versionId, percent));
                    });

                ModBase.Log($"[Bedrock] GDK 版本已解包: {versionId}",
                    ModBase.LogLevel.Normal,
                    userSummary: Lang.Text("Bedrock.Gdk.ExtractSuccessSummary", versionId));

                ModBase.RunInUi(() =>
                {
                    if (launchAfterInstall)
                    {
                        // 这次是「启动」触发的安装，解包完成后直接接着启动，不用再点一次
                        ActivateGdkVersion(versionId, extractDir);
                        return;
                    }

                    IsBedrockInstalled = true;
                    BedrockInstallPath = extractDir;
                    BedrockVersion = versionId;
                    _isStorePackage = false;
                    _isGdkPortable = true;
                    InstalledVersionText = Lang.Text("Bedrock.Status.Version", BedrockVersion ?? "");

                    Status = Lang.Text("Bedrock.Gdk.Extracted");
                    Detail = Lang.Text("Bedrock.Status.CopyPathHint");
                    InstalledVersionText = Lang.Text("Bedrock.Status.Version", versionId);
                    Enabled = true;
                    LaunchText = Lang.Text("Bedrock.Action.Launch");
                    NotifyLibraryChanged();
                    NotifyStateChanged();
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "GDK 版本解包失败",
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Bedrock.Gdk.ExtractFailed", versionId, ex.Message));

                ModBase.RunInUi(() =>
                {
                    Status = Lang.Text("Bedrock.Install.Failed");
                    Detail = Lang.Text("Bedrock.Install.FailedHint", filePath);
                    Enabled = true;
                });
            }
            finally
            {
                ModBase.RunInUi(() =>
                {
                    installingVersions.Remove(versionId);
                    NotifyLibraryChanged();
                });
            }
        });
    }

    /// <summary>
    ///     获取目录内的基岩版安装包文件（按扩展名过滤）。
    /// </summary>
    internal static List<string> GetPackageFiles(string directory)
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
    internal static bool IsPackageFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return BedrockPackageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());
    }

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
                    Status = Lang.Text("Bedrock.Install.CheckingEnvironment");
                    Detail = Lang.Text("Bedrock.Install.CheckingDependencies");
                });

                // 结束正在运行的基岩版，避免文件被占用
                ModBedrockUwp.StopMinecraftProcesses();

                // 参考 BedrockBoot：安装前补齐安装包 Manifest 声明的 VCLibs / .NET Native 等 UWP 依赖
                await ModBedrockUwp.EnsureDependenciesAsync(filePath).ConfigureAwait(false);

                ModBase.RunInUi(() =>
                {
                    Status = Lang.Text("Bedrock.Install.Installing");
                    Detail = Lang.Text("Bedrock.Install.InstallingVersion", versionId);
                });

                // 商店版是「已打包」版本，未打包的旧版本无法覆盖注册（0x80073CFB）。
                // 先备份应用数据再完整卸载，安装完成后恢复；开发者注册版则可以直接覆盖注册。
                if (migrateStore)
                {
                    ModBase.Log("[Bedrock] 检测到商店版基岩版，先备份应用数据并完整卸载", ModBase.LogLevel.Normal);
                    ModBase.RunInUi(() =>
                    {
                        Status = Lang.Text("Bedrock.Install.SavingData");
                        Detail = Lang.Text("Bedrock.Install.SavingDataDetail");
                    });
                    dataBackup = ModBedrockUwp.BackupPackageData(versionId,
                        text => ModBase.RunInUi(() => Detail = text));
                    ModBase.RunInUi(() =>
                    {
                        Status = Lang.Text("Bedrock.Install.Replacing");
                        Detail = Lang.Text("Bedrock.Install.PreservingData");
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
                        Status = Lang.Text("Bedrock.Install.Replacing");
                        Detail = Lang.Text("Bedrock.Install.PreservingData");
                    });

                    ModBedrockUwp.UninstallPreservingData();
                    InstallWithRegister(filePath, versionId);
                }

                if (dataBackup is not null)
                {
                    ModBase.Log($"[Bedrock] 正在恢复商店版数据: {dataBackup}", ModBase.LogLevel.Normal);
                    ModBase.RunInUi(() =>
                    {
                        Status = Lang.Text("Bedrock.Install.RestoringData");
                        Detail = Lang.Text("Bedrock.Install.RestoringDataDetail");
                    });
                    ModBedrockUwp.RestorePackageData(dataBackup);
                }

                ModBase.Log($"[Bedrock] 安装完成: {versionId}",
                    ModBase.LogLevel.Normal,
                    userSummary: Lang.Text("Bedrock.Install.SuccessSummary", versionId));

                ModBase.RunInUi(() =>
                {
                    Status = Lang.Text("Bedrock.Install.Success");
                    Detail = Lang.Text("Bedrock.Install.InstalledVersion", versionId);
                    Enabled = true;
                    LaunchText = Lang.Text("Bedrock.Action.Launch");
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
                    Status = Lang.Text("Bedrock.Install.Failed");
                    Detail = Lang.Text("Bedrock.Install.FailedHint", filePath);
                });
            }
            finally
            {
                ModBase.RunInUi(() =>
                {
                    installingVersions.Remove(versionId);
                    NotifyLibraryChanged();
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
                    Status = Lang.Text("Bedrock.Install.DeveloperModeHint");
                    Detail = Lang.Text("Bedrock.Install.DeveloperModeDetail");
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

}
