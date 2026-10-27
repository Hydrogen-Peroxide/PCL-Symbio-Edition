using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PCL;

/// <summary>Stages and validates a replacement before touching the installed version.</summary>
internal static class BedrockInstallTransaction
{
    private static readonly string[] UserDirectories = ["mods", "preload", "plugins", "config", "configs", "logs"];

    internal static string Install(string installRoot, string versionId,
        Action<string, CancellationToken> extract, Func<string, bool> validate,
        Func<bool>? isGameRunning = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(versionId) || versionId.IndexOfAny(['\\', '/']) >= 0)
            throw new ArgumentException("无效的基岩版版本目录。", nameof(versionId));
        var root = Path.GetFullPath(installRoot);
        var destination = BedrockFileSystem.GetChildPath(root, versionId);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(destination.ToUpperInvariant())));
        using var mutex = new Mutex(false, "PCLSE.BedrockInstall." + identity[..24]);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("该基岩版正在安装，请稍后重试。");
        try { return InstallCore(root, destination, versionId, extract, validate, isGameRunning, token); }
        finally { mutex.ReleaseMutex(); }
    }

    private static string InstallCore(string root, string destination, string versionId,
        Action<string, CancellationToken> extract, Func<string, bool> validate,
        Func<bool>? isGameRunning, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        EnsureNotRunning(isGameRunning);
        Directory.CreateDirectory(root);
        var stagingRoot = BedrockFileSystem.GetChildPath(root, ".staging");
        var staging = BedrockFileSystem.GetChildPath(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            extract(staging, token);
            token.ThrowIfCancellationRequested();
            if (!validate(staging)) throw new InvalidDataException("安装包内未找到可运行的基岩版主程序。");

            if (BedrockFileSystem.Exists(destination))
            {
                if (!Directory.Exists(destination)) throw new IOException("安装位置已经被文件占用。");
                foreach (var directory in UserDirectories)
                {
                    var source = BedrockFileSystem.GetChildPath(destination, directory);
                    if (Directory.Exists(source)) CopyUserDirectory(source, staging, directory, token);
                }
            }
            token.ThrowIfCancellationRequested();
            EnsureNotRunning(isGameRunning);
            BedrockFileSystem.EnsureNoReparsePoints(root, destination);
            BedrockFileSystem.EnsureNoReparsePoints(stagingRoot, staging);

            string? backup = null;
            if (Directory.Exists(destination))
            {
                var backupRoot = BedrockFileSystem.GetChildPath(root, ".backups");
                var versionBackups = BedrockFileSystem.GetChildPath(backupRoot, versionId);
                backup = BedrockFileSystem.GetChildPath(versionBackups, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(versionBackups);
                // Preserve the entire previous installation, including unknown user-added files.
                Directory.Move(destination, backup);
            }
            try { Directory.Move(staging, destination); }
            catch (Exception installError)
            {
                if (backup is not null && !BedrockFileSystem.Exists(destination))
                {
                    try { Directory.Move(backup, destination); }
                    catch (Exception rollbackError)
                    {
                        throw new AggregateException("安装未完成，旧版本仍保留在备份目录: " + backup,
                            installError, rollbackError);
                    }
                }
                throw;
            }
            return destination;
        }
        finally
        {
            // This GUID directory is the only tree discarded; installed versions and backups are never deleted here.
            if (BedrockFileSystem.Exists(staging))
                DeleteStaging(stagingRoot, staging);
        }
    }

    private static void EnsureNotRunning(Func<bool>? isGameRunning)
    {
        if (isGameRunning?.Invoke() == true)
            throw new InvalidOperationException("请先关闭正在运行的基岩版，再安装或重装版本。");
    }

    private static void CopyUserDirectory(string source, string stagingRoot, string relative,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (BedrockFileSystem.IsReparsePoint(source))
            throw new IOException("用户数据目录包含联接或符号链接，已保留原版本并停止重装。");
        var target = BedrockFileSystem.GetChildPath(stagingRoot, relative);
        Directory.CreateDirectory(target);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("用户数据包含联接或符号链接，已保留原版本并停止重装。");
            var childRelative = Path.Combine(relative, entry.Name);
            if ((entry.Attributes & FileAttributes.Directory) != 0)
                CopyUserDirectory(entry.FullName, stagingRoot, childRelative, token);
            else
                File.Copy(entry.FullName, BedrockFileSystem.GetChildPath(stagingRoot, childRelative), true);
        }
    }

    private static void DeleteStaging(string root, string staging)
    {
        if (!BedrockFileSystem.IsChildPath(root, staging) ||
            !Guid.TryParseExact(Path.GetFileName(staging), "N", out _))
            throw new IOException("拒绝清理不属于当前安装的临时目录。");
        // Do not traverse reparse points even if an extractor unexpectedly leaves one behind.
        if (BedrockFileSystem.IsReparsePoint(staging)) { Directory.Delete(staging); return; }
        DeleteDirectory(staging);
    }

    private static void DeleteDirectory(string directory)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(entry.FullName);
                else DeleteDirectory(entry.FullName);
            }
            else File.Delete(entry.FullName);
        }
        Directory.Delete(directory);
    }
}
