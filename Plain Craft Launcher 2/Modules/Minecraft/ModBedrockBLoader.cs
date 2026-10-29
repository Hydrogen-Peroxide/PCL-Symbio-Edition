using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace PCL;

/// <summary>
///     BLoader（https://github.com/Chlna6666/BLoader，GPL-3.0）的随启动器分发与释放。
///     BLoader.dll 以资源形式内置于启动器，首次选用时释放到
///     .minecraft\bedrock\loaders\BLoader\版本\，校验 SHA-256 后再注入游戏进程。
/// </summary>
internal static class ModBedrockBLoader
{
    internal const string Version = "0.2.80";

    private const string DllName = "BLoader.dll";
    private const string EmbeddedDll = "Resources/BLoader/BLoader.dll";

    // 发布 DLL 的 SHA-256，来自上游 manifest.json；释放后校验，避免使用损坏或被替换的文件
    private const string DllSha256 = "FC99379BC3BA3338C4E74850CCC178EDF37AEA285867840453AF4AA2AC78CA9B";

    /// <summary>BLoader 部署目录：.minecraft\bedrock\loaders\BLoader\版本\。</summary>
    internal static string GetInstallDirectory() => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "bedrock", "loaders", "BLoader", Version);

    internal static string GetDllPath() => Path.Combine(GetInstallDirectory(), DllName);

    internal static bool IsInstalled() => HashMatches(GetDllPath(), DllSha256);

    /// <summary>
    ///     确保 BLoader.dll 已释放且校验通过；文件缺失或损坏时从启动器内置资源重建。
    ///     失败会抛出异常，由调用方决定是否退回原版启动。
    /// </summary>
    internal static string EnsureInstalled(CancellationToken token = default)
    {
        var dllPath = GetDllPath();
        if (IsInstalled()) return dllPath;

        Directory.CreateDirectory(GetInstallDirectory());
        var tempPath = dllPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            token.ThrowIfCancellationRequested();
            var resource = ModBase.GetResourceStream(EmbeddedDll)
                           ?? throw new FileNotFoundException("启动器未内置 BLoader.dll。", EmbeddedDll);

            using (resource)
            using (var output = File.Create(tempPath))
                resource.CopyTo(output);

            token.ThrowIfCancellationRequested();
            if (!HashMatches(tempPath, DllSha256))
                throw new InvalidDataException("启动器内置的 BLoader.dll 校验失败，已取消加载。");

            File.Move(tempPath, dllPath, true);
            ModBase.Log($"[Bedrock] 已从启动器资源释放 BLoader {Version}: {dllPath}", ModBase.LogLevel.Normal);
            return dllPath;
        }
        finally
        {
            TryDelete(tempPath);
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 临时文件清理失败不影响释放结果
        }
    }
}
