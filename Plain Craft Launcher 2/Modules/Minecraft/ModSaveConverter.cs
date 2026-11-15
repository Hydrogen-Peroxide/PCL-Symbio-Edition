using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PCL.Core.Minecraft;
using PCL.Network;

namespace PCL;

/// <summary>
///     调用 Chunker CLI（https://github.com/HiveGamesOSS/Chunker）在本地完成 Java ↔ 基岩版存档转换。
/// </summary>
public static class ModSaveConverter
{
    private const string LatestReleaseApi = "https://api.github.com/repos/HiveGamesOSS/Chunker/releases/latest";

    private static readonly Regex FormatPattern = new(@"\b(JAVA|BEDROCK)_[A-Z0-9][A-Z0-9_]*\b", RegexOptions.Compiled);

    public static string CacheDirectory => Path.Combine(ModBase.pathTemp, "Chunker");

    /// <summary>返回已缓存（或刚下载）的 Chunker CLI jar 路径。</summary>
    public static async Task<string> EnsureCliAsync()
    {
        var existing = FindCliJar();
        if (existing is not null) return existing;

        Directory.CreateDirectory(CacheDirectory);
        var release = await Requester.FetchJsonAsync(LatestReleaseApi);
        var assets = release?["assets"] as JsonArray;
        if (assets is null) throw new InvalidOperationException("无法读取 Chunker 的最新版本信息");

        var asset = assets
            .Select(item => item as JsonObject)
            .FirstOrDefault(item =>
            {
                var name = item?["name"]?.GetValue<string>();
                return name is not null &&
                       name.Contains("cli", StringComparison.OrdinalIgnoreCase) &&
                       name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            });
        var url = asset?["browser_download_url"]?.GetValue<string>();
        var fileName = asset?["name"]?.GetValue<string>();
        if (url is null || fileName is null) throw new InvalidOperationException("Chunker 的最新版本中没有找到 CLI jar");

        var target = Path.Combine(CacheDirectory, fileName);
        await Requester.DownloadFileAsync(url, target);
        return target;
    }

    public static string? FindCliJar()
    {
        if (!Directory.Exists(CacheDirectory)) return null;
        return Directory.GetFiles(CacheDirectory, "chunker-cli-*.jar")
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>找一个 17 及以上的 Java；找不到时退回 PATH 中的 java。</summary>
    public static string? FindJava()
    {
        try
        {
            var list = JavaService.JavaManager.GetSortedJavaList();
            var entry = list
                .Where(item => item.Installation.MajorVersion >= 17 && item.Installation.IsStillAvailable)
                .OrderByDescending(item => item.Installation.MajorVersion)
                .FirstOrDefault();
            if (entry is not null) return entry.Installation.JavaExePath;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "获取 Java 列表失败，将尝试使用 PATH 中的 java", ModBase.LogLevel.Debug);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), "java.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>运行 Chunker 转换；返回输出内容。</summary>
    public static string Convert(string javaPath, string cliPath, string inputDirectory, string outputFormat,
        string outputDirectory)
    {
        var arguments =
            $"-jar \"{cliPath}\" -i \"{inputDirectory}\" -f {outputFormat} -o \"{outputDirectory}\"";
        return RunProcess(javaPath, arguments, 30 * 60 * 1000);
    }

    /// <summary>取得 CLI 支持的格式列表（JAVA_x / BEDROCK_x）。</summary>
    public static List<string> ListFormats(string javaPath, string cliPath)
    {
        // 故意传非法格式让 CLI 把支持的格式列表打到 stderr，此时退出码非 0 是正常的
        var output = RunProcess(javaPath, $"-jar \"{cliPath}\" -f ?", 120 * 1000, false);
        return FormatPattern.Matches(output)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>在格式列表里取目标平台的最新版本，例如 JAVA_1_21_11。</summary>
    public static string? PickLatestFormat(IEnumerable<string> formats, string prefix)
    {
        return formats
            .Where(format => format.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(format => ParseVersionKey(format))
            .FirstOrDefault();
    }

    /// <summary>基岩版优先选 1.21.x，其次 1.20.x / 1.19.x，避免直接输出到很新的 26.x 格式。</summary>
    public static string? PickBedrockFormat(IEnumerable<string> formats)
    {
        var list = formats.Where(item => item.StartsWith("BEDROCK_", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var prefix in new[] { "BEDROCK_R21", "BEDROCK_R20", "BEDROCK_R19" })
        {
            var hit = list
                .Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseVersionKey)
                .FirstOrDefault();
            if (hit is not null) return hit;
        }

        return list.OrderByDescending(ParseVersionKey).FirstOrDefault();
    }

    private static long ParseVersionKey(string format)
    {
        var body = format[(format.IndexOf('_') + 1)..];
        if (body.StartsWith("R", StringComparison.OrdinalIgnoreCase)) body = body[1..];
        var parts = body.Split('_');
        long[] weights = [1_000_000_000L, 1_000_000L, 1_000L, 1L];
        long key = 0;
        for (var i = 0; i < parts.Length && i < weights.Length; i++)
            key += (long.TryParse(parts[i], out var value) ? Math.Clamp(value, 0, 999) : 0) * weights[i];
        return key;
    }

    private static string RunProcess(string fileName, string arguments, int timeoutMilliseconds,
        bool throwOnError = true)
    {
        var info = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 Java 进程");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // 忽略
            }

            throw new TimeoutException("Chunker 运行超时");
        }

        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        return output + "\n" + error;
    }
}
