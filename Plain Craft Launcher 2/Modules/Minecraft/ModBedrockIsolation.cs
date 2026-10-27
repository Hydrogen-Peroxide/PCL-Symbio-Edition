using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PCL;

/// <summary>
///     基岩版版本隔离（仅 GDK 便携版）。
///     参考 BedrockBoot：GDK 版把游戏数据放在 %APPDATA%\Minecraft Bedrock，隔离即把该目录指向版本自己的数据目录。
///     BedrockBoot 通过 PreloadCpp.dll 挂钩文件 API 实现；启动器没有注入能力，改为启动前用目录联接（junction）
///     把共享目录指向隔离目录，游戏退出后再还原。
/// </summary>
public static class ModBedrockIsolation
{
    private static readonly string[] GameProcessNames = ["Minecraft.Windows", "Minecraft.Win10.DX11"];

    private static readonly string IsolationDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "bedrock", "isolation");

    private static readonly string AppDataDirectory = GetAppDataDirectory();

    private static readonly BedrockIsolationManager Manager = new(
        AppDataDirectory, IsolationDirectory,
        IsAnyGameRunning, log: message => ModBase.Log(message, ModBase.LogLevel.Normal));

    /// <summary>该 GDK 版本对应的共享数据目录（正式版与预览版不同）。</summary>
    public static string GetSharedRoot(string gameDirectory)
    {
        var isPreview = IsPreviewBuild(gameDirectory);
        return Path.Combine(AppDataDirectory,
            isPreview ? "Minecraft Bedrock Preview" : "Minecraft Bedrock");
    }

    private static string GetAppDataDirectory()
    {
#if DEBUG
        var isolatedTestRoot = Environment.GetEnvironmentVariable("PCL_BEDROCK_APPDATA");
        if (!string.IsNullOrWhiteSpace(isolatedTestRoot)) return Path.GetFullPath(isolatedTestRoot);
#endif
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    /// <summary>该版本的独立数据目录。</summary>
    public static string GetIsolationRoot(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId) || versionId.IndexOfAny(['\\', '/']) >= 0)
            throw new ArgumentException("无效的基岩版版本目录。", nameof(versionId));
        return BedrockFileSystem.GetChildPath(IsolationDirectory, versionId);
    }

    /// <summary>是否有基岩版正在运行。切换目录联接前必须确认没有进程占用。</summary>
    public static bool IsAnyGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length == 0) continue;
                foreach (var process in processes) process.Dispose();
                return true;
            }
            catch (Exception error)
            {
                ModBase.Log("[Bedrock] 无法确认游戏运行状态，已停止目录切换: " + error.Message,
                    ModBase.LogLevel.Normal);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     启用隔离：把共享目录改名备份，再建立指向该版本独立目录的目录联接。
    /// </summary>
    public static void Apply(string gameDirectory, string versionId)
    {
        _ = Manager.Apply(GetSharedRoot(gameDirectory), versionId);
    }

    internal static BedrockIsolationSession BeginSession(string gameDirectory, string versionId) =>
        Manager.Apply(GetSharedRoot(gameDirectory), versionId, reserveLaunch: true);

    /// <summary>
    ///     关闭隔离：删除目录联接并把备份的共享目录移回原位。
    ///     启动器上次异常退出时，也可以在启动时调用此方法做恢复。
    /// </summary>
    public static bool Restore()
    {
        return Manager.Restore();
    }

    internal static bool Restore(BedrockIsolationSession session) => Manager.Restore(session);

    internal static void RestoreAfterGameExit(BedrockIsolationSession session, Process? process)
    {
        // Capture an ID; the foreground launch check owns and disposes its separate Process object.
        var processId = process?.Id;
        _ = Task.Run(() =>
        {
            try
            {
                if (processId is { } id)
                {
                    Manager.MarkLaunched(session);
                    try
                    {
                        using var watched = Process.GetProcessById(id);
                        watched.WaitForExit();
                    }
                    catch (ArgumentException) { /* The child already exited. */ }
                }
                else
                {
                    // Registered activation does not return a process. Never restore before it actually appears.
                    var deadline = DateTime.UtcNow.AddMinutes(1);
                    while (!IsAnyGameRunning())
                    {
                        if (DateTime.UtcNow >= deadline)
                        {
                            ModBase.Log("[Bedrock] 尚未确认注册启动的进程，保留隔离目录供后续安全恢复。",
                                ModBase.LogLevel.Normal);
                            return;
                        }
                        Thread.Sleep(250);
                    }
                    Manager.MarkLaunched(session);
                    while (IsAnyGameRunning()) Thread.Sleep(1000);
                }
                _ = Restore(session);
            }
            catch (Exception error)
            {
                ModBase.Log("[Bedrock] 无法确认本次游戏已退出，保留隔离记录: " + error.Message,
                    ModBase.LogLevel.Normal);
            }
        });
    }

    /// <summary>从解包目录的 AppxManifest 判断是否为预览版（数据目录名不同）。</summary>
    private static bool IsPreviewBuild(string gameDirectory)
    {
        try
        {
            if (!Directory.Exists(gameDirectory)) return false;

            string? identityName = null;
            var manifest = Directory.GetFiles(gameDirectory, "AppxManifest.xml", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (manifest is not null)
            {
                var match = Regex.Match(File.ReadAllText(manifest),
                    "<Identity\\s[^>]*Name=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (match.Success) identityName = match.Groups[1].Value;
            }

            if (!string.IsNullOrEmpty(identityName))
                return identityName.Contains("Beta", StringComparison.OrdinalIgnoreCase) ||
                       identityName.Contains("Preview", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 读取版本标识失败，按正式版处理: {ex.Message}", ModBase.LogLevel.Normal);
        }

        return gameDirectory.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
               gameDirectory.Contains("beta", StringComparison.OrdinalIgnoreCase);
    }
}
