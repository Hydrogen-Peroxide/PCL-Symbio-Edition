using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PCL;

/// <summary>
///     Terracotta | 陶瓦联机（https://github.com/burningtnt/Terracotta，AGPL-3.0-or-later）的接入层。
///     启动器不链接、不修改它，只把官方发行的二进制释放到缓存目录，
///     再用官方提供的 <c>--hmcl</c> 启动器协议（启动后写出端口文件）取到本地 HTTP 端口，
///     之后全部通过它自己的 HTTP API 交互。这样属于 AGPL 例外条款允许的用法。
/// </summary>
internal static class ModTerracotta
{
    private const string ExeName = "terracotta.exe";
    private const string RuntimeName = "VCRUNTIME140.DLL";

    private const string EmbeddedExe = "Resources/Terracotta/terracotta.exe";
    private const string EmbeddedRuntime = "Resources/Terracotta/VCRUNTIME140.DLL";

    // 官方 v0.4.2 windows-x86_64 发行包的校验值，释放前后都要核对
    private const string ExeSha256 = "74C10568A7FEA9C1D38CF8D2D4CA90BAF1517F8E5A26C63D3349DB70BC449796";
    private const string RuntimeSha256 = "475AB98B7722E965BD38C8FA6ED23502309582CCF294FF1061CB290C7988F0D1";

    private static readonly object Sync = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>联机运行时所在目录。</summary>
    internal static string InstallDirectory => Path.Combine(ModBase.pathTemp, "Cache", "Terracotta");

    internal static string ExePath => Path.Combine(InstallDirectory, ExeName);

    /// <summary>当前可用的本地端口；未运行时为 0。</summary>
    internal static int Port { get; private set; }

    internal static bool IsRunning => Port > 0;

    /// <summary>陶瓦自带的 Web 界面地址。</summary>
    internal static string WebUrl => IsRunning ? $"http://127.0.0.1:{Port}/" : string.Empty;

    /// <summary>陶瓦的源仓库，界面上用于标注来源与许可。</summary>
    internal const string ProjectUrl = "https://github.com/burningtnt/Terracotta";

    internal const string Version = "0.4.2";

    /// <summary>
    ///     确保二进制已释放且校验通过；缺失或损坏时从启动器内置资源重建。
    /// </summary>
    internal static void EnsureInstalled()
    {
        Directory.CreateDirectory(InstallDirectory);

        ExtractIfNeeded(EmbeddedExe, ExePath, ExeSha256);
        ExtractIfNeeded(EmbeddedRuntime, Path.Combine(InstallDirectory, RuntimeName), RuntimeSha256);
    }

    private static void ExtractIfNeeded(string resourcePath, string targetPath, string expectedHash)
    {
        if (HashMatches(targetPath, expectedHash)) return;

        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var resource = ModBase.GetResourceStream(resourcePath)
                           ?? throw new FileNotFoundException("启动器未内置陶瓦联机所需文件。", resourcePath);

            using (resource)
            using (var output = File.Create(tempPath))
                resource.CopyTo(output);

            if (!HashMatches(tempPath, expectedHash))
                throw new InvalidDataException($"启动器内置的 {Path.GetFileName(targetPath)} 校验失败，已取消使用。");

            File.Move(tempPath, targetPath, true);
            ModBase.Log($"[Terracotta] 已释放 {Path.GetFileName(targetPath)} 到 {targetPath}",
                ModBase.LogLevel.Debug);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // 临时文件清理失败不影响结果
            }
        }
    }

    private static bool HashMatches(string path, string expected)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     启动陶瓦联机并返回本地端口。
    ///     官方在 --hmcl 模式下会把真正的服务进程分离出去，自己只负责写端口文件后退出，
    ///     因此这里不依赖进程句柄存活，连关停也走 HTTP 接口。
    /// </summary>
    internal static async Task<int> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (Sync)
        {
            if (IsRunning) return Port;
        }

        EnsureInstalled();

        var portFile = Path.Combine(InstallDirectory, "port.json");
        try
        {
            if (File.Exists(portFile)) File.Delete(portFile);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 清理旧的端口文件失败", ModBase.LogLevel.Debug);
        }

        var info = new ProcessStartInfo(ExePath, $"--hmcl \"{portFile}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = InstallDirectory
        };
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("无法启动陶瓦联机进程。");

        // 父进程写完端口文件就会退出，最多等 40 秒
        var deadline = Environment.TickCount64 + 40_000;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(portFile))
            {
                string? text = null;
                try
                {
                    using var stream = new FileStream(portFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    text = await reader.ReadToEndAsync(cancellationToken);
                }
                catch (IOException)
                {
                    // 文件正在被写入，下一轮再读
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (int.TryParse(JsonNode.Parse(text)?["port"]?.ToString(), out var port) && port > 0)
                    {
                        lock (Sync) Port = port;
                        ModBase.Log($"[Terracotta] 联机服务已就绪，端口 {port}", ModBase.LogLevel.Normal);
                        return port;
                    }
                }
            }

            await Task.Delay(300, cancellationToken);
        }

        throw new TimeoutException("陶瓦联机在 40 秒内没有就绪，请查看它的日志或重试。");
    }

    /// <summary>通过官方 HTTP 接口请求陶瓦退出。</summary>
    internal static async Task StopAsync()
    {
        int port;
        lock (Sync) port = Port;
        if (port <= 0) return;

        try
        {
            await Http.GetAsync($"http://127.0.0.1:{port}/panic?peaceful=true");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 请求联机服务退出失败", ModBase.LogLevel.Debug);
        }
        finally
        {
            lock (Sync) Port = 0;
            ModBase.Log("[Terracotta] 联机服务已停止", ModBase.LogLevel.Normal);
        }
    }

    /// <summary>
    ///     读取陶瓦的当前状态（waiting / host-scanning / host-ok / guest-ok / exception 等）。
    ///     服务未运行或请求失败时返回 null。
    /// </summary>
    internal static async Task<JsonObject?> GetStateAsync()
    {
        int port;
        lock (Sync) port = Port;
        if (port <= 0) return null;

        try
        {
            var text = await Http.GetStringAsync($"http://127.0.0.1:{port}/state/");
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 读取联机状态失败", ModBase.LogLevel.Debug);
            return null;
        }
    }

    /// <summary>
    ///     开始当房主：陶瓦会扫描本机的局域网世界，发现后自动建立房间。
    ///     room 为空时由陶瓦随机生成房间码。
    /// </summary>
    internal static async Task<bool> HostAsync(string? room, string? player)
    {
        int port;
        lock (Sync) port = Port;
        if (port <= 0) return false;

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(room)) query.Add("room=" + Uri.EscapeDataString(room.Trim()));
        if (!string.IsNullOrWhiteSpace(player)) query.Add("player=" + Uri.EscapeDataString(player.Trim()));

        var url = $"http://127.0.0.1:{port}/state/scanning" +
                  (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        try
        {
            using var response = await Http.GetAsync(url);
            ModBase.Log($"[Terracotta] 请求开房，返回 {(int)response.StatusCode}", ModBase.LogLevel.Normal);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 请求开房失败", ModBase.LogLevel.Hint);
            return false;
        }
    }

    /// <summary>加入一个房间。</summary>
    internal static async Task<bool> JoinAsync(string room, string? player)
    {
        int port;
        lock (Sync) port = Port;
        if (port <= 0 || string.IsNullOrWhiteSpace(room)) return false;

        var query = new List<string> { "room=" + Uri.EscapeDataString(room.Trim()) };
        if (!string.IsNullOrWhiteSpace(player)) query.Add("player=" + Uri.EscapeDataString(player.Trim()));

        try
        {
            using var response = await Http.GetAsync(
                $"http://127.0.0.1:{port}/state/guesting?{string.Join("&", query)}");
            ModBase.Log($"[Terracotta] 请求加入房间 {room}，返回 {(int)response.StatusCode}",
                ModBase.LogLevel.Normal);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 请求加入房间失败", ModBase.LogLevel.Hint);
            return false;
        }
    }

    /// <summary>把当前状态复位到等待（取消开房 / 退出房间）。</summary>
    internal static async Task ResetAsync()
    {
        int port;
        lock (Sync) port = Port;
        if (port <= 0) return;

        try
        {
            await Http.GetAsync($"http://127.0.0.1:{port}/state/ide");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 复位联机状态失败", ModBase.LogLevel.Debug);
        }
    }

    /// <summary>
    ///     启动器退出时收尾，避免留下后台服务。
    ///     注意：这里在 UI 线程上同步等待，因此不能调用 StopAsync——
    ///     那个 async 方法的续体会回到 UI 线程，而我们正阻塞着它，会死锁。
    ///     HttpClient 内部不捕获同步上下文，直接阻塞是安全的。
    /// </summary>
    internal static void Shutdown()
    {
        int port;
        lock (Sync) port = Port;
        if (port <= 0) return;

        try
        {
            Http.GetAsync($"http://127.0.0.1:{port}/panic?peaceful=true").GetAwaiter().GetResult();
            ModBase.Log("[Terracotta] 已请求联机服务随启动器退出", ModBase.LogLevel.Normal);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 退出时停止联机服务失败", ModBase.LogLevel.Debug);
        }
        finally
        {
            lock (Sync) Port = 0;
        }
    }
}
