using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using PCL.Core.IO.Net.Http;

namespace PCL;

/// <summary>
///     BLoader（https://github.com/Chlna6666/BLoader，GPL-3.0）的运行时下载与部署。
///     BLoader 以 BLoader.dll 注入游戏进程，提供原生 Mod 装载、日志捕获与文件重定向。
///     仅在版本选择了 BLoader 加载器时按需下载，DLL 按版本隔离存放，不随启动器分发。
/// </summary>
internal static class ModBedrockBLoader
{
    internal const string Version = "0.2.80";

    private const string DllName = "BLoader.dll";

    // GitHub 直连失败时回退到 ghproxy，适配国内网络
    private static readonly string[] ReleaseUrls =
    [
        "https://github.com/Chlna6666/BLoader/releases/download/v0.2.80/BLoader-0.2.80-windows-x64.zip",
        "https://ghproxy.net/https://github.com/Chlna6666/BLoader/releases/download/v0.2.80/BLoader-0.2.80-windows-x64.zip"
    ];

    // 发布包与 DLL 的 SHA-256，来自 release 的 manifest.json；下载后逐一校验
    private const string ZipSha256 = "30196EAAF421EBC519521A1CE66FA8229FD5E4EE662ECF34FF8954A635D3FC88";
    private const string DllSha256 = "FC99379BC3BA3338C4E74850CCC178EDF37AEA285867840453AF4AA2AC78CA9B";

    /// <summary>BLoader 部署目录：.minecraft\bedrock\loaders\BLoader\版本\。</summary>
    internal static string GetInstallDirectory() => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "bedrock", "loaders", "BLoader", Version);

    internal static string GetDllPath() => Path.Combine(GetInstallDirectory(), DllName);

    internal static bool IsInstalled() => HashMatches(GetDllPath(), DllSha256);

    /// <summary>
    ///     确保 BLoader.dll 存在且校验通过；缺失时从 GitHub Release 下载并解压。
    ///     网络或校验失败会抛出异常，由调用方决定是否退回原版启动。
    /// </summary>
    internal static string EnsureInstalled(CancellationToken token = default)
    {
        var dllPath = GetDllPath();
        if (IsInstalled()) return dllPath;

        var directory = GetInstallDirectory();
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(Path.GetTempPath(), $"BLoader-{Version}-{Guid.NewGuid():N}.zip");
        try
        {
            ModBase.Log($"[Bedrock] 正在下载 BLoader {Version}...", ModBase.LogLevel.Normal);
            var bytes = Download(token);
            File.WriteAllBytes(zipPath, bytes);

            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(DllName) ?? throw new InvalidDataException("BLoader 安装包内缺少 BLoader.dll。");
            entry.ExtractToFile(dllPath, true);

            if (!HashMatches(dllPath, DllSha256))
            {
                TryDelete(dllPath);
                throw new InvalidDataException("BLoader.dll 校验失败，已删除损坏文件。");
            }

            ModBase.Log($"[Bedrock] BLoader {Version} 已就绪: {dllPath}", ModBase.LogLevel.Normal);
            return dllPath;
        }
        catch
        {
            if (File.Exists(dllPath) && !HashMatches(dllPath, DllSha256)) TryDelete(dllPath);
            throw;
        }
        finally
        {
            TryDelete(zipPath);
        }
    }

    /// <summary>依次尝试直连与镜像，返回通过 SHA-256 校验的发布包内容。</summary>
    private static byte[] Download(CancellationToken token)
    {
        Exception? lastError = null;
        foreach (var url in ReleaseUrls)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var response = HttpRequest.Create(url)
                    .SendAsync(cancellationToken: token).GetAwaiter().GetResult();
                if (!response.IsSuccess)
                    throw new IOException($"HTTP {(int)response.StatusCode}");
                var bytes = response.AsByteArrayAsync(token).GetAwaiter().GetResult();
                if (!string.Equals(Sha256(bytes), ZipSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("BLoader 安装包校验失败。");
                ModBase.Log($"[Bedrock] 已从 {new Uri(url).Host} 下载 BLoader {Version}", ModBase.LogLevel.Normal);
                return bytes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                ModBase.Log($"[Bedrock] 从 {url} 下载 BLoader 失败: {ex.Message}", ModBase.LogLevel.Normal);
            }
        }

        throw new IOException("无法下载 BLoader，请检查网络后重试。", lastError);
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 临时文件清理失败不影响安装结果
        }
    }
}
