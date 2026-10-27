using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace PCL;

internal sealed record BedrockIsolationSession(string SharedRoot, string SessionId);

/// <summary>Owns the reversible filesystem transaction, independently of UI and process discovery.</summary>
internal sealed class BedrockIsolationManager
{
    private const string BackupSuffix = ".pcl-shared";
    private const string MarkerSuffix = ".pcl-isolation.json";
    private static readonly string[] RootNames = ["Minecraft Bedrock", "Minecraft Bedrock Preview"];
    private readonly string _appDataDirectory;
    private readonly string _isolationDirectory;
    private readonly Func<bool> _isGameRunning;
    private readonly Action<string, string> _createJunction;
    private readonly Action<string> _log;
    private readonly string _mutexName;
    private readonly int _ownerProcessId;
    private readonly long _ownerStartTimeTicks;

    internal BedrockIsolationManager(string appDataDirectory, string isolationDirectory,
        Func<bool> isGameRunning, Action<string, string>? createJunction = null, Action<string>? log = null)
    {
        _appDataDirectory = Path.GetFullPath(appDataDirectory);
        _isolationDirectory = Path.GetFullPath(isolationDirectory);
        _isGameRunning = isGameRunning;
        _createJunction = createJunction ?? BedrockFileSystem.CreateJunction;
        _log = log ?? (_ => { });
        var identity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(_appDataDirectory.ToUpperInvariant())));
        _mutexName = "PCLSE.BedrockIsolation." + identity[..24];
        using var owner = Process.GetCurrentProcess();
        _ownerProcessId = owner.Id;
        _ownerStartTimeTicks = owner.StartTime.ToUniversalTime().Ticks;
    }

    internal BedrockIsolationSession Apply(string sharedRoot, string versionId, bool reserveLaunch = false)
    {
        using var mutation = EnterMutation();
        if (_isGameRunning())
            throw new InvalidOperationException("请先关闭正在运行的基岩版，再切换版本隔离。");

        var shared = ValidateSharedRoot(sharedRoot);
        // A version ID must be a single safe directory component.
        if (string.IsNullOrWhiteSpace(versionId) || versionId.IndexOfAny(['\\', '/']) >= 0)
            throw new ArgumentException("无效的基岩版版本目录。", nameof(versionId));
        var target = BedrockFileSystem.GetChildPath(_isolationDirectory, versionId);
        var state = ReadMarker(shared);
        if (state is { LaunchPending: true } && IsOwnerAlive(state))
            throw new InvalidOperationException("另一个基岩版启动尚未完成，已保留它的数据目录。");

        if (BedrockFileSystem.IsReparsePoint(shared))
        {
            if (state is null || !OwnsLink(shared, state))
                throw new IOException("共享数据目录是非本启动器管理的联接，已保留原目录。");
            if (SamePath(state.Target, target))
            {
                var renewed = NewMarker(target, reserveLaunch);
                WriteMarker(shared, renewed);
                return new BedrockIsolationSession(shared, renewed.SessionId);
            }
        }

        if (state is not null && !RestoreRoot(shared, state))
            throw new IOException("上一次隔离尚未完整还原，请检查共享数据目录及备份。");
        if (BedrockFileSystem.Exists(shared + MarkerSuffix))
            throw new IOException("存在无法验证的隔离记录，已保留原数据目录。");
        if (BedrockFileSystem.Exists(shared + BackupSuffix))
            throw new IOException("隔离前的共享数据备份已存在，已停止操作以免覆盖。");

        Directory.CreateDirectory(target);
        var marker = NewMarker(target, reserveLaunch);
        WriteMarker(shared, marker);
        try
        {
            if (BedrockFileSystem.Exists(shared))
            {
                if (!Directory.Exists(shared)) throw new IOException("共享数据路径不是目录。");
                Directory.Move(shared, shared + BackupSuffix);
            }
            if (_isGameRunning())
                throw new InvalidOperationException("基岩版已开始运行，已取消目录切换。");
            _createJunction(shared, target);
            if (!OwnsLink(shared, marker))
                throw new IOException("未能确认创建的隔离目录联接。");
            _log($"[Bedrock] 已启用版本隔离: {shared} -> {target}");
            return new BedrockIsolationSession(shared, marker.SessionId);
        }
        catch
        {
            // Roll back a failed link creation instead of launching against an absent data path.
            if (!_isGameRunning())
            {
                try { RestoreRoot(shared, marker); }
                catch (Exception rollbackError) { _log("[Bedrock] 隔离回滚失败: " + rollbackError.Message); }
            }
            throw;
        }
    }

    internal bool Restore(BedrockIsolationSession? session = null)
    {
        using var mutation = EnterMutation();
        if (_isGameRunning()) return false;
        var restored = true;
        foreach (var name in RootNames)
        {
            var shared = Path.Combine(_appDataDirectory, name);
            if (session is not null && !SamePath(shared, session.SharedRoot)) continue;
            try
            {
                var marker = ReadMarker(shared);
                if (marker is null)
                {
                    // Unknown or legacy links are left intact; the target alone is not proof of ownership.
                    if (BedrockFileSystem.Exists(shared + MarkerSuffix)) restored = false;
                    continue;
                }
                if (session is not null && marker.SessionId != session.SessionId) continue;
                if (session is null && marker.LaunchPending && IsOwnerAlive(marker))
                {
                    restored = false;
                    continue;
                }
                restored &= RestoreRoot(shared, marker);
            }
            catch (Exception error)
            {
                restored = false;
                _log($"[Bedrock] 还原版本隔离失败 ({name}): {error.Message}");
            }
        }
        return restored;
    }

    internal void MarkLaunched(BedrockIsolationSession session)
    {
        using var mutation = EnterMutation();
        var shared = ValidateSharedRoot(session.SharedRoot);
        var marker = ReadMarker(shared);
        if (marker?.SessionId == session.SessionId && OwnsLink(shared, marker))
            WriteMarker(shared, marker with { LaunchPending = false });
    }

    private Marker NewMarker(string target, bool pending) => new(Guid.NewGuid().ToString("N"), target,
        _ownerProcessId, _ownerStartTimeTicks, pending);

    private static bool IsOwnerAlive(Marker marker)
    {
        if (marker.OwnerProcessId <= 0) return false;
        try
        {
            using var owner = Process.GetProcessById(marker.OwnerProcessId);
            return !owner.HasExited && owner.StartTime.ToUniversalTime().Ticks == marker.OwnerStartTimeTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch { return true; } // An inaccessible live owner is not permission to take its lease.
    }

    private bool RestoreRoot(string shared, Marker marker)
    {
        var backup = shared + BackupSuffix;
        if (BedrockFileSystem.IsReparsePoint(shared))
        {
            if (!OwnsLink(shared, marker)) return false;
            Directory.Delete(shared); // Delete only the verified link, never its target.
        }
        if (BedrockFileSystem.Exists(backup))
        {
            if (!Directory.Exists(backup) || BedrockFileSystem.IsReparsePoint(backup) ||
                BedrockFileSystem.Exists(shared))
            {
                _log($"[Bedrock] 数据目录或备份存在冲突，保留备份: {backup}");
                return false;
            }
            Directory.Move(backup, shared);
        }
        else if (BedrockFileSystem.Exists(shared))
        {
            // A user-created replacement is never removed or overwritten.
            return false;
        }
        File.Delete(shared + MarkerSuffix);
        return true;
    }

    private Marker? ReadMarker(string shared)
    {
        var path = shared + MarkerSuffix;
        if (!File.Exists(path) || BedrockFileSystem.IsReparsePoint(path)) return null;
        try
        {
            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
            if (marker is null || !Guid.TryParseExact(marker.SessionId, "N", out _) ||
                !BedrockFileSystem.IsChildPath(_isolationDirectory, marker.Target)) return null;
            return marker;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            _log("[Bedrock] 无法验证隔离记录，保留数据目录: " + error.Message);
            return null;
        }
    }

    private void WriteMarker(string shared, Marker marker)
    {
        Directory.CreateDirectory(_appDataDirectory);
        var destination = shared + MarkerSuffix;
        if (BedrockFileSystem.IsReparsePoint(destination))
            throw new IOException("隔离记录不能是符号链接。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(marker));
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private bool OwnsLink(string shared, Marker marker)
    {
        if (!BedrockFileSystem.IsReparsePoint(shared)) return false;
        var target = new DirectoryInfo(shared).LinkTarget;
        if (target is null) return false;
        var fullTarget = Path.GetFullPath(target, Path.GetDirectoryName(shared)!);
        return BedrockFileSystem.IsChildPath(_isolationDirectory, fullTarget) &&
               SamePath(fullTarget, marker.Target);
    }

    private string ValidateSharedRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        foreach (var name in RootNames)
            if (SamePath(fullPath, Path.Combine(_appDataDirectory, name))) return fullPath;
        throw new ArgumentException("无效的共享基岩版数据目录。", nameof(path));
    }

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private IDisposable EnterMutation()
    {
        var mutex = new Mutex(false, _mutexName);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("另一个启动器正在切换基岩版数据目录，请稍后重试。");
            return new MutationLock(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed record Marker(string SessionId, string Target, int OwnerProcessId = 0,
        long OwnerStartTimeTicks = 0, bool LaunchPending = false);
    private sealed class MutationLock(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
