using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;

namespace PCL.Core.Minecraft.Bedrock;

/// <summary>
/// 基岩版下载/安装服务。
/// </summary>
public class BedrockDownloadService
{
    private static readonly HttpClient _httpClient = new();
    private readonly string _installRoot;

    /// <summary>
    /// 下载进度。
    /// </summary>
    public IProgress<DownloadProgress>? DownloadProgress { get; set; }

    /// <summary>
    /// 状态文本更新。
    /// </summary>
    public Action<string>? StatusText { get; set; }

    /// <summary>
    /// 安装完成回调。
    /// </summary>
    public Action<BedrockInstance>? Completed { get; set; }

    /// <summary>
    /// 错误回调。
    /// </summary>
    public Action<string, string, Exception?>? ErrorOccurred { get; set; }

    public BedrockDownloadService(string installRoot)
    {
        _installRoot = installRoot;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PCL-Symbio/1.0");
    }

    /// <summary>
    /// 安装指定版本的基岩版。
    /// </summary>
    public async Task InstallAsync(BedrockBuildInfo buildInfo, string gameName, CancellationToken token = default)
    {
        try
        {
            StatusText?.Invoke("准备下载目录...");
            var versionSaveDir = Path.Combine(_installRoot, "version_save");
            Directory.CreateDirectory(versionSaveDir);

            // 1. 获取下载地址
            StatusText?.Invoke("获取下载地址...");
            var downloadUrl = await ResolveDownloadUrlAsync(buildInfo);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                ErrorOccurred?.Invoke("获取地址失败", "无法获取基岩版下载地址", null);
                return;
            }

            // 2. 下载包
            var packagePath = Path.Combine(versionSaveDir, $"{buildInfo.ID}.insPack");
            StatusText?.Invoke("正在下载游戏包...");

            if (File.Exists(packagePath) && await ValidatePackageAsync(packagePath, buildInfo))
            {
                StatusText?.Invoke("使用缓存包");
            }
            else
            {
                await DownloadPackageAsync(downloadUrl, packagePath, token);
                token.ThrowIfCancellationRequested();

                if (!await ValidatePackageAsync(packagePath, buildInfo))
                {
                    ErrorOccurred?.Invoke("校验失败", "下载的包不完整，请重试", null);
                    return;
                }
            }

            // 3. 安装包
            StatusText?.Invoke("正在安装游戏...");
            var installDir = Path.Combine(_installRoot, gameName);
            InstallPackage(packagePath, installDir, buildInfo);

            // 4. 创建实例配置
            var instance = new BedrockInstance
            {
                Version = buildInfo.ID,
                BuildType = buildInfo.BuildType,
                VersionName = gameName,
                VersionType = buildInfo.Type,
                InstallPath = installDir,
                FirstPlayTime = DateTime.Now
            };

            SaveInstanceConfig(instance);
            Completed?.Invoke(instance);
        }
        catch (OperationCanceledException)
        {
            // 取消操作
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("安装失败", $"游戏 {gameName} 安装失败", ex);
        }
    }

    /// <summary>
    /// 解析下载地址。
    /// </summary>
    private static async Task<string?> ResolveDownloadUrlAsync(BedrockBuildInfo buildInfo)
    {
        try
        {
            var url = $"https://data.mcappx.com/v2/package/{buildInfo.ID}?arch=x64&type={buildInfo.BuildType}";
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("url", out var urlProp))
                return urlProp.GetString();
        }
        catch
        {
            // 降级处理
        }

        return null;
    }

    private async Task DownloadPackageAsync(string url, string destination, CancellationToken token)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var contentStream = await response.Content.ReadAsStreamAsync(token);
        await using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long downloadedBytes = 0;
        var lastReportTime = DateTime.UtcNow;

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            downloadedBytes += bytesRead;

            var now = DateTime.UtcNow;
            if ((now - lastReportTime).TotalMilliseconds >= 200)
            {
                lastReportTime = now;
                var percentage = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
                DownloadProgress?.Report(new DownloadProgress(percentage, downloadedBytes, totalBytes));
            }
        }

        DownloadProgress?.Report(new DownloadProgress(100, downloadedBytes, totalBytes));
    }

    private static async Task<bool> ValidatePackageAsync(string filePath, BedrockBuildInfo buildInfo)
    {
        if (!File.Exists(filePath)) return false;

        var md5 = await ComputeFileMd5Async(filePath);
        return buildInfo.Variations.Any(v =>
            string.Equals(v.MD5, md5, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ComputeFileMd5Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static void InstallPackage(string packagePath, string installDir, BedrockBuildInfo buildInfo)
    {
        if (buildInfo.IsGdk)
        {
            // GDK 版本：直接解压到目录
            Directory.CreateDirectory(installDir);
            ZipFile.ExtractToDirectory(packagePath, installDir, overwriteFiles: true);
        }
        else
        {
            // UWP 版本：使用 PowerShell 的 Add-AppxPackage 命令部署
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"Add-AppxPackage -Path '{packagePath}'\"",
                UseShellExecute = true,
                Verb = "runas"
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();

            if (process?.ExitCode != 0)
            {
                throw new Exception($"UWP 包部署失败，退出码: {process?.ExitCode}");
            }
        }
    }

    private void SaveInstanceConfig(BedrockInstance instance)
    {
        var configDir = Path.Combine(_installRoot, ".bedrock");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, $"{instance.Version}.json");
        var json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);
    }

    /// <summary>
    /// 获取已安装的基岩版实例列表。
    /// </summary>
    public static List<BedrockInstance> GetInstalledInstances(string installRoot)
    {
        var configDir = Path.Combine(installRoot, ".bedrock");
        if (!Directory.Exists(configDir)) return [];

        var instances = new List<BedrockInstance>();
        foreach (var configFile in Directory.GetFiles(configDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(configFile);
                var instance = JsonSerializer.Deserialize<BedrockInstance>(json);
                if (instance != null)
                    instances.Add(instance);
            }
            catch
            {
                // 忽略损坏的配置
            }
        }
        return instances;
    }
}

/// <summary>
/// 下载进度信息。
/// </summary>
public class DownloadProgress
{
    public double Percentage { get; }
    public long DownloadedBytes { get; }
    public long TotalBytes { get; }

    public DownloadProgress(double percentage, long downloadedBytes, long totalBytes)
    {
        Percentage = percentage;
        DownloadedBytes = downloadedBytes;
        TotalBytes = totalBytes;
    }
}