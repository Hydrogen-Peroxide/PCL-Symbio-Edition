using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
    private const string LipApi = "https://api.github.com/repos/LiteLDev/lip/releases/latest";
    private const string MirrorPrefix = "https://ghproxy.net/";
    private const string PackageId = "github.com/LiteLDev/LeviLamina#client";

    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(20);

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

    /// <summary>当前游戏版本是否有对应的 LeviLamina 客户端构建；没有时返回 null。</summary>
    internal static string? ResolveVersion(string gameVersion)
    {
        var series = GetSeries(gameVersion);
        if (series is null) return null;

        var json = HttpRequest.GetStringAsync(ReleaseApi).GetAwaiter().GetResult();
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
    ///     不支持的版本、下载或 lip 执行失败会抛出异常，由调用方决定是否退回原版启动。
    /// </summary>
    internal static string EnsureInstalled(string gameDirectory, string gameVersion, CancellationToken token = default)
    {
        var version = ResolveVersion(gameVersion)
                      ?? throw new InvalidOperationException(
                          $"当前游戏版本 {gameVersion} 暂无 LeviLamina 客户端支持。");

        if (IsInstalled(gameDirectory, version)) return version;

        var lip = EnsureLip(token);
        ModBase.Log($"[Bedrock] 正在通过 lip 安装 LeviLamina {version}（首次约 50 MB）...", ModBase.LogLevel.Normal);

        // lip 下载依赖时偶尔会因网络中断失败；失败后重跑会复用已下载的部分
        string? lastOutput = null;
        var installed = false;
        for (var attempt = 1; attempt <= 3 && !installed; attempt++)
        {
            var (exitCode, output) = RunLip(lip, $"install {PackageId}@{version} --rid win-x64", gameDirectory, token);
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
            var (exitCode, output) = RunLip(lip, $"update {PackageId}@{version} --rid win-x64", gameDirectory, token);
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

    /// <summary>确保 lip.exe 可用；缺失时从官方 release 下载 win-x64 包。</summary>
    private static string EnsureLip(CancellationToken token)
    {
        var lipPath = GetLipPath();
        if (File.Exists(lipPath)) return lipPath;

        var directory = GetLipDirectory();
        Directory.CreateDirectory(directory);

        var json = HttpRequest.GetStringAsync(LipApi).GetAwaiter().GetResult();
        var node = JsonNode.Parse(json) ?? throw new InvalidDataException("无法读取 lip 发布信息。");
        var asset = node["assets"]?.AsArray()
            .FirstOrDefault(a => a?["name"]?.ToString().EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase) == true);
        var url = asset?["browser_download_url"]?.ToString()
                  ?? throw new InvalidDataException("lip 发布页中未找到 win-x64 压缩包。");

        var bytes = Download(url, token);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry("lip.exe")
                    ?? throw new InvalidDataException("lip 压缩包中缺少 lip.exe。");
        entry.ExtractToFile(lipPath, true);
        ModBase.Log($"[Bedrock] lip 已就绪: {lipPath}", ModBase.LogLevel.Normal);
        return lipPath;
    }

    private static byte[] Download(string url, CancellationToken token)
    {
        try
        {
            return GetBytes(url, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ModBase.Log($"[Bedrock] 直接下载失败（{ex.Message}），尝试镜像", ModBase.LogLevel.Normal);
            return GetBytes(MirrorPrefix + url, token);
        }
    }

    private static byte[] GetBytes(string url, CancellationToken token)
    {
        using var response = HttpRequest.Create(url).SendAsync(cancellationToken: token).GetAwaiter().GetResult();
        if (!response.IsSuccess)
            throw new IOException($"下载失败：HTTP {(int)response.StatusCode}");
        return response.AsByteArrayAsync(token).GetAwaiter().GetResult();
    }

    private static (int ExitCode, string Output) RunLip(string lipPath, string arguments, string workingDirectory,
        CancellationToken token)
    {
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
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        var deadline = DateTime.UtcNow + InstallTimeout;
        while (!process.WaitForExit(250))
        {
            token.ThrowIfCancellationRequested();
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
        return (process.ExitCode, $"{stdout.GetAwaiter().GetResult()}\n{stderr.GetAwaiter().GetResult()}".Trim());
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
