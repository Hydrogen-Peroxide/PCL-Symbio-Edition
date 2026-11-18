using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using PCL.Core.IO.Net.Http;

namespace PCL;

/// <summary>
///     按游戏版本自动安装 LeviLamina 客户端。
///     LeviLamina 客户端不是单纯注入 DLL：它的 lip 包会放置 PreLoader / PeEditor / 运行时数据，
///     并由 PeEditor 给 Minecraft.Windows.exe 打补丁，因此这里调用官方 lip 包管理器完成安装。
/// </summary>
internal static class ModBedrockLeviLamina
{
    private const string ReleaseApi = "https://api.github.com/repos/LiteLDev/LeviLamina/releases?per_page=100";

    /// <summary>
    ///     必须固定 lip 版本：0.41 起 lip 只管理服务端工作区，客户端安装会被直接拒绝
    ///     （"Client workspaces are managed by launchers such as LeviLauncher"），
    ///     而 LeviLamina 客户端模组只能通过 0.34.x 这一代 lip 安装。
    /// </summary>
    private const string LipVersion = "0.34.9";
    private const string LipApi = $"https://api.github.com/repos/LiteLDev/lip/releases/tags/v{LipVersion}";
    private const string LipVersionMarker = "lip.version";
    private const string PackageId = "github.com/LiteLDev/LeviLamina#client";

    // lip 0.34.x 的运行时配置（%APPDATA%\lip\liprc.json），用镜像替换默认的直连 GitHub / proxy.golang.org
    private const string LipConfigFile = "liprc.json";
    private const string LipConfigUuid = "289f771f-2c9a-4d73-9f3f-8492495a924d";
    private const string LipGithubProxy = "https://gh-proxy.com/https://github.com/";
    private const string LipGoModuleProxy = "https://goproxy.cn";

    /// <summary>
    ///     GitHub 直连与公共加速镜像，按顺序尝试。留空字符串表示直连。
    ///     国内网络直连 release 资源经常卡在 TLS 握手直到超时，因此每一路都要有独立的超时限制。
    /// </summary>
    private static readonly string[] DownloadMirrors =
    [
        "",
        "https://gh-proxy.com/",
        "https://ghproxy.net/"
    ];

    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadAttemptTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTotalTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(20);

    /// <summary>
    ///     git 克隆时如果文件不再增长，说明连接已经卡死（换镜像、被限速断开都会这样）。
    ///     lip 自己不会超时，会一直挂着，所以这里主动判定并在无进展 3 分钟后中止。
    /// </summary>
    private static readonly TimeSpan CloneStallTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan CloneCheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>同一时刻只允许一个 lip 进程：并发跑会互相踩 lip 的缓存和临时目录。</summary>
    private static readonly object LipGate = new();

    /// <summary>同一时刻只允许一个"确保 LeviLamina 已安装"的流程，避免连点启动时重复克隆整份仓库。</summary>
    private static readonly object InstallGate = new();

    internal static string GetLipDirectory() => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "bedrock", "tools", "lip");

    internal static string GetLipPath() => Path.Combine(GetLipDirectory(), "lip.exe");

    private static string GetMarkerPath(string gameDirectory) => Path.Combine(gameDirectory, ".pcl-levilamina.json");

    /// <summary>该游戏目录是否已按指定版本安装 LeviLamina。</summary>
    internal static bool IsInstalled(string gameDirectory, string version)
    {
        try
        {
            if (!File.Exists(Path.Combine(gameDirectory, "mods", "LeviLamina", "LeviLamina.dll"))) return false;
            var marker = GetMarkerPath(gameDirectory);
            if (!File.Exists(marker)) return false;
            var node = JsonNode.Parse(File.ReadAllText(marker));
            return string.Equals(node?["version"]?.ToString(), version, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>实例里记录的 LeviLamina 版本（以 mods\LeviLamina\LeviLamina.dll 存在为准）；没有则返回 null。</summary>
    internal static string? GetRecordedVersion(string gameDirectory)
    {
        try
        {
            if (!File.Exists(Path.Combine(gameDirectory, "mods", "LeviLamina", "LeviLamina.dll"))) return null;
            var marker = GetMarkerPath(gameDirectory);
            if (!File.Exists(marker)) return null;
            return JsonNode.Parse(File.ReadAllText(marker))?["version"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>当前游戏版本是否有对应的 LeviLamina 客户端构建；没有时返回 null。</summary>
    internal static string? ResolveVersion(string gameVersion)
    {
        var series = GetSeries(gameVersion);
        if (series is null) return null;

        var json = GetString(ReleaseApi, CancellationToken.None, ApiTimeout);
        if (JsonNode.Parse(json) is not JsonArray releases) return null;

        string? best = null;
        Version? bestVersion = null;
        foreach (var release in releases)
        {
            var tag = release?["tag_name"]?.ToString();
            if (tag is null || !tag.StartsWith("v", StringComparison.Ordinal)) continue;
            if (!Version.TryParse(tag[1..], out var version)) continue;
            if ($"{version.Major}.{version.Minor}" != series) continue;
            if (bestVersion is not null && version <= bestVersion) continue;
            bestVersion = version;
            best = tag[1..];
        }

        return best;
    }

    /// <summary>
    ///     确保指定游戏目录已安装合适的 LeviLamina 客户端；返回实际安装的版本。
    ///     不支持的版本、下载或 lip 执行失败会抛出异常，由调用方决定是否退回原版启动。<br/>
    ///     同一时刻只跑一个安装流程：连点启动时第二个请求会等第一个装完，而不是再克隆一遍整份仓库。
    /// </summary>
    internal static string EnsureInstalled(string gameDirectory, string gameVersion, CancellationToken token = default)
    {
        lock (InstallGate)
        {
            // 排队期间可能已经被别的线程装好了，进来先复查
            var installed = GetRecordedVersion(gameDirectory);
            if (installed is not null && GetSeries(installed) == GetSeries(gameVersion)) return installed;
            return EnsureInstalledCore(gameDirectory, gameVersion, token);
        }
    }

    private static string EnsureInstalledCore(string gameDirectory, string gameVersion, CancellationToken token)
    {
        // 实例里已经装过同系列的客户端时直接用记录里的版本：装插件不必每次都联网查版本，
        // 网络不稳时也不会因为查版本失败而装不了插件。
        var recorded = GetRecordedVersion(gameDirectory);
        if (recorded is not null && GetSeries(recorded) == GetSeries(gameVersion)) return recorded;

        string version;
        try
        {
            version = ResolveVersion(gameVersion)
                      ?? throw new InvalidOperationException(
                          $"当前游戏版本 {gameVersion} 暂无 LeviLamina 客户端支持，DLL 模组无法加载。" +
                          "请先在下载页安装受支持的基岩版版本（如 26.40 / 26.51 / 26.60）。");
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not OperationCanceledException)
        {
            throw new IOException($"获取 LeviLamina 版本列表失败（{Describe(ex)}），请检查网络后重试。", ex);
        }

        if (IsInstalled(gameDirectory, version)) return version;

        // 只有解包完成的 GDK 客户端才有 Minecraft.Windows.exe；只有 msixvc 时 lip 会对着空目录折腾很久
        if (!File.Exists(Path.Combine(gameDirectory, "Minecraft.Windows.exe")))
            throw new InvalidOperationException(
                $"「{gameVersion}」还没有安装完成（目录中找不到 Minecraft.Windows.exe）。" +
                "请先在「启动 → 基岩版」里完成该版本的安装，再安装 LeviLamina。");

        var lip = EnsureLip(token);
        ModBase.Log($"[Bedrock] 正在通过 lip 安装 LeviLamina {version}（首次约 50 MB）...", ModBase.LogLevel.Normal);

        // lip 下载依赖时偶尔会因网络中断失败；失败后重跑会复用已下载的部分
        string? lastOutput = null;
        var installed = false;
        for (var attempt = 1; attempt <= 3 && !installed; attempt++)
        {
            var (exitCode, output) = RunLip(lip, $"install {PackageId}@{version}", gameDirectory, token);
            lastOutput = output;
            if (exitCode == 0)
            {
                installed = true;
                break;
            }

            ModBase.Log($"[Bedrock] lip install 第 {attempt}/3 次失败（{exitCode}）：{Tail(output)}",
                ModBase.LogLevel.Normal);
            Thread.Sleep(TimeSpan.FromSeconds(attempt));
        }

        if (!installed)
        {
            // 已经装过但文件被版本更新覆盖时，lip 会拒绝重复 install，用 update 重新落盘
            var (exitCode, output) = RunLip(lip, $"update {PackageId}@{version}", gameDirectory, token);
            lastOutput = output;
            if (exitCode != 0)
                throw new InvalidOperationException($"lip 安装 LeviLamina 失败（退出码 {exitCode}）：{Tail(lastOutput)}");
        }

        if (!File.Exists(Path.Combine(gameDirectory, "mods", "LeviLamina", "LeviLamina.dll")))
            throw new InvalidDataException("lip 执行完成，但没有找到 mods\\LeviLamina\\LeviLamina.dll。");

        Directory.CreateDirectory(Path.GetDirectoryName(GetMarkerPath(gameDirectory))!);
        File.WriteAllText(GetMarkerPath(gameDirectory),
            new JsonObject { ["version"] = version }.ToJsonString());
        ModBase.Log($"[Bedrock] LeviLamina {version} 安装完成", ModBase.LogLevel.Normal);
        return version;
    }

    /// <summary>供模组下载页调用：确保 lip 就绪后在指定目录执行 lip 命令。</summary>
    internal static (int ExitCode, string Output) RunLipCommand(string arguments, string workingDirectory,
        CancellationToken token = default, Action<string>? onLine = null)
    {
        return RunLip(EnsureLip(token), arguments, workingDirectory, token, onLine);
    }

    /// <summary>
    ///     确保 lip.exe 可用，且是本类固定的客户端可用版本（见 <see cref="LipVersion"/>）。
    ///     目录里已有的旧版（例如被更新到 0.41 的版本）必须替换，否则客户端安装会被拒绝。
    /// </summary>
    private static string EnsureLip(CancellationToken token)
    {
        // 不管 lip.exe 是否已就绪都要保证配置里带镜像，否则它可能直接 git clone github.com 卡死
        EnsureLipConfig();

        var lipPath = GetLipPath();
        var directory = GetLipDirectory();
        var markerPath = Path.Combine(directory, LipVersionMarker);
        if (File.Exists(lipPath))
        {
            try
            {
                if (File.ReadAllText(markerPath).Trim() == LipVersion) return lipPath;
            }
            catch
            {
                // 标记文件缺失或损坏时按需要重新下载处理
            }

            ModBase.Log($"[Bedrock] 现有 lip 版本不适用于客户端安装，重新下载 {LipVersion}", ModBase.LogLevel.Normal);
        }

        Directory.CreateDirectory(directory);

        var json = GetString(LipApi, token, ApiTimeout);
        var node = JsonNode.Parse(json) ?? throw new InvalidDataException("无法读取 lip 发布信息。");
        var asset = node["assets"]?.AsArray()
            .FirstOrDefault(a => a?["name"]?.ToString().EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase) == true);
        var url = asset?["browser_download_url"]?.ToString()
                  ?? throw new InvalidDataException("lip 发布页中未找到 win-x64 压缩包。");

        var bytes = Download(url, token);
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entry = archive.GetEntry("lip.exe")
                        ?? throw new InvalidDataException("lip 压缩包中缺少 lip.exe。");
            entry.ExtractToFile(lipPath, true);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("下载到的 lip 安装包已损坏（可能是镜像返回了错误页面），请重试。", ex);
        }

        File.WriteAllText(markerPath, LipVersion);
        ModBase.Log($"[Bedrock] lip {LipVersion} 已就绪: {lipPath}", ModBase.LogLevel.Normal);
        return lipPath;
    }

    /// <summary>
    ///     给 lip 0.34.x 写一份带镜像的 liprc.json（它读 %APPDATA%\lip\liprc.json）。
    ///     不写的话 lip 会直接 git clone github.com，在国内可能卡上几分钟甚至更久。
    ///     已经存在的字段一律保留，只补空。
    /// </summary>
    private static void EnsureLipConfig()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lip");
            var path = Path.Combine(directory, LipConfigFile);
            var config = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            var changed = false;
            if (config["format_version"]?.ToString() != "3")
            {
                config["format_version"] = 3;
                changed = true;
            }

            if (config["format_uuid"]?.ToString() != LipConfigUuid)
            {
                config["format_uuid"] = LipConfigUuid;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(config["github_proxy"]?.ToString()))
            {
                config["github_proxy"] = LipGithubProxy;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(config["go_module_proxy"]?.ToString()))
            {
                config["go_module_proxy"] = LipGoModuleProxy;
                changed = true;
            }

            if (!changed) return;

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ModBase.Log($"[Bedrock] 已写入 lip 镜像配置: {path}", ModBase.LogLevel.Debug);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 写入 lip 配置失败，将使用 lip 默认配置", ModBase.LogLevel.Debug);
        }
    }

    /// <summary>
    ///     依次尝试直连与各镜像下载。超时产生的 TaskCanceledException 同样是 OperationCanceledException，
    ///     但那是网络超时而非用户取消，必须继续回退到下一个源。
    /// </summary>
    private static byte[] Download(string url, CancellationToken token)
    {
        var failures = new List<string>();
        var deadline = DateTime.UtcNow + DownloadTotalTimeout;

        foreach (var mirror in DownloadMirrors)
        {
            token.ThrowIfCancellationRequested();
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.FromSeconds(3)) break;

            var target = mirror + url;
            var source = DescribeSource(target);
            var timeout = remaining < DownloadAttemptTimeout ? remaining : DownloadAttemptTimeout;

            try
            {
                var bytes = GetBytes(target, token, timeout);
                ModBase.Log($"[Bedrock] 已从 {source} 下载 lip 安装包（{bytes.Length / 1024} KB）",
                    ModBase.LogLevel.Debug);
                return bytes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                var reason = Describe(ex);
                failures.Add($"{source}: {reason}");
                ModBase.Log($"[Bedrock] 从 {source} 下载 lip 失败（{reason}），尝试下一个源", ModBase.LogLevel.Normal);
            }
        }

        throw new IOException(failures.Count == 0
            ? "下载 lip 安装包失败：网络不可用，请检查网络后重试。"
            : $"下载 lip 安装包失败，直连与镜像均不可用（{failures[^1]}），请检查网络后重试。");
    }

    private static byte[] GetBytes(string url, CancellationToken token, TimeSpan timeout)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(timeout);
        try
        {
            using var response = HttpRequest.Create(url)
                .SendAsync(retryTimes: 0, cancellationToken: timeoutCts.Token)
                .GetAwaiter().GetResult();
            if (!response.IsSuccess)
                throw new IOException($"HTTP {(int)response.StatusCode}");
            return response.AsByteArrayAsync(timeoutCts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"等待 {timeout.TotalSeconds:N0} 秒未完成");
        }
    }

    private static string GetString(string url, CancellationToken token, TimeSpan timeout)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(timeout);
        try
        {
            using var response = HttpRequest.Create(url)
                .SendAsync(retryTimes: 0, cancellationToken: timeoutCts.Token)
                .GetAwaiter().GetResult();
            if (!response.IsSuccess)
                throw new IOException($"HTTP {(int)response.StatusCode}");
            return response.AsStringAsync(timeoutCts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"等待 {timeout.TotalSeconds:N0} 秒未完成");
        }
    }

    private static string DescribeSource(string url)
        => url.StartsWith("https://api.github.com/", StringComparison.OrdinalIgnoreCase) ? "GitHub 直连"
            : new Uri(url).Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase) ? "GitHub 直连"
            : new Uri(url).Host;

    private static string Describe(Exception ex)
    {
        // 异常信息可能带换行或很啰嗦，压成一行并截断，避免刷爆日志和提示框
        var message = ex.Message.Replace("\r", " ").Replace("\n", " ").Trim();
        return message.Length <= 80 ? message : message[..80] + "…";
    }

    private static (int ExitCode, string Output) RunLip(string lipPath, string arguments, string workingDirectory,
        CancellationToken token, Action<string>? onLine = null)
    {
        // 串行化：两个 lip 同时跑会抢同一份缓存和临时目录，还会把同一个仓库克隆两遍
        lock (LipGate)
        {
            ModBase.Log($"[Bedrock] lip {arguments}（{workingDirectory}）", ModBase.LogLevel.Normal);

            var startInfo = new ProcessStartInfo
            {
                FileName = lipPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("无法启动 lip。");

            // lip 解析/下载依赖时可能长时间没有动静，把它的输出实时写进日志，方便判断卡在哪一步
            var output = new StringBuilder();
            var outputLock = new object();
            string? cloneDirectory = null;
            var cloneSize = -1L;
            var cloneLastGrowth = DateTime.UtcNow;

            void OnLine(string? line)
            {
                if (line is null) return;
                lock (outputLock) output.AppendLine(line);
                if (line.Length == 0) return;
                ModBase.Log($"[lip] {line}", ModBase.LogLevel.Debug);
                try
                {
                    onLine?.Invoke(line);
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "[Bedrock] 处理 lip 输出回调时出错", ModBase.LogLevel.Debug);
                }

                // lip 克隆仓库时会打印 Cloning into '<目录>'，据此盯住克隆进度
                var marker = line.IndexOf("Cloning into '", StringComparison.Ordinal);
                if (marker < 0) return;
                var start = marker + "Cloning into '".Length;
                var end = line.LastIndexOf('\'');
                if (end <= start) return;
                cloneDirectory = line[start..end];
                cloneSize = -1;
                cloneLastGrowth = DateTime.UtcNow;
            }

            process.OutputDataReceived += (_, e) => OnLine(e.Data);
            process.ErrorDataReceived += (_, e) => OnLine(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var deadline = DateTime.UtcNow + InstallTimeout;
            var nextCloneCheck = DateTime.UtcNow + CloneCheckInterval;
            while (!process.WaitForExit(250))
            {
                token.ThrowIfCancellationRequested();

                // git 克隆被镜像卡住时 lip 自己不会超时，这里按"克隆目录不再变大"判定卡死
                if (DateTime.UtcNow >= nextCloneCheck)
                {
                    nextCloneCheck = DateTime.UtcNow + CloneCheckInterval;
                    if (cloneDirectory is { } directory)
                    {
                        var size = Math.Max(0, GetDirectorySize(directory));
                        if (size != cloneSize)
                        {
                            cloneSize = size;
                            cloneLastGrowth = DateTime.UtcNow;
                        }
                        else if (DateTime.UtcNow - cloneLastGrowth >= CloneStallTimeout)
                        {
                            ModBase.Log(
                                $"[Bedrock] git 克隆已经 {(int)(DateTime.UtcNow - cloneLastGrowth).TotalSeconds} 秒没有增长，判定卡死并中止",
                                ModBase.LogLevel.Hint);
                            try
                            {
                                process.Kill(true);
                            }
                            catch
                            {
                                // 结束失败也按卡死处理
                            }

                            process.WaitForExit(5000);
                            lock (outputLock)
                            {
                                return (-1, output + "\n[PCL] git 克隆长时间没有进展，已中止（可重试）");
                            }
                        }
                    }
                }

                if (DateTime.UtcNow < deadline) continue;
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // 忽略结束失败，交由下面的超时异常报告
                }

                throw new TimeoutException("lip 安装超时，请检查网络后重试。");
            }

            process.WaitForExit();
            lock (outputLock) return (process.ExitCode, output.ToString().Trim());
        }
    }

    /// <summary>目录总大小；目录还没建出来或读不到时返回 -1。</summary>
    private static long GetDirectorySize(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return -1;
            var total = 0L;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    total += file.Length;
                }
                catch
                {
                    // 文件刚好被 git 改名，忽略
                }
            }

            return total;
        }
        catch
        {
            return -1;
        }
    }

    private static string Tail(string text, int length = 600)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= length ? trimmed : "..." + trimmed[^length..];
    }

    private static string? GetSeries(string gameVersion)
    {
        var match = Regex.Match(gameVersion, @"(\d+)\.(\d+)");
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : null;
    }
}
