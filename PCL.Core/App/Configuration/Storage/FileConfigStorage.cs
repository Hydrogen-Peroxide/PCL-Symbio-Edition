using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PCL.Core.App.IoC;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.UI;

namespace PCL.Core.App.Configuration.Storage;

/// <summary>
/// 文件存取仓库。
/// </summary>
public class FileConfigStorage : ConfigStorage
{
    /// <summary>
    /// 键值文件实例。
    /// </summary>
    public IKeyValueFileProvider File { get; }

    private readonly Channel<(string, Action)> _writeActionChannel;
    private readonly CancellationTokenSource _writeActionCts;
    private readonly Task _writeTask;
    private int _stopRequested;
    private int _saveErrorNotificationPending;

    public FileConfigStorage(IKeyValueFileProvider file)
    {
        File = file;
        _writeActionChannel = Channel.CreateUnbounded<(string, Action)>();
        _writeActionCts = new CancellationTokenSource();
        _writeTask = Task.Run(async () =>
        {
            const long syncInterval = 10000; // ms
            var lastSyncTick = 0L;
            var cancelToken = _writeActionCts.Token;
            var writeActionMap = new Dictionary<string, Action>();
            var reader = _writeActionChannel.Reader;
            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    // 读入并合并暂存操作
                    var (key, action) = await reader.ReadAsync(cancelToken);
                    writeActionMap[key] = action;
                    if (Environment.TickCount64 - lastSyncTick < syncInterval || cancelToken.IsCancellationRequested) continue;
                    // 同步文件
                    Sync();
                    lastSyncTick = Environment.TickCount64;
                    writeActionMap.Clear();
                }
            }
            catch (OperationCanceledException) { /* ignoring*/ }
            finally
            {
                // 停止前先排空通道中尚未被 ReadAsync 读取的写入：
                // 取消令牌会让 ReadAsync 直接抛出，通道里剩余的操作若不取出，会在缓存被驱逐时随实例一起丢弃
                while (reader.TryRead(out var pendingItem))
                    writeActionMap[pendingItem.Item1] = pendingItem.Item2;
                // 结束时执行一次同步
                Sync();
            }
            void Sync()
            {
                try
                {
                    LogWrapper.Trace("Config", $"正在保存 {File.FilePath}");
                    foreach (var action in writeActionMap.Values) action();
                    File.Sync();
                }
                catch (Exception ex)
                {
                    LogWrapper.Error(ex, "Config", "配置文件保存失败");
                    NotifySaveFailure(ex);
                }
            }
        });
    }

    protected override void OnStop()
    {
        var firstStop = Interlocked.Exchange(ref _stopRequested, 1) == 0;
        if (firstStop) _writeActionCts.Cancel();
        try
        {
            // 等待真实写入任务：即使最终保存或错误处理抛异常，也不会漏发完成信号而永久等待。
            _writeTask.GetAwaiter().GetResult();
        }
        finally
        {
            if (firstStop) _writeActionCts.Dispose();
        }
    }

    private void NotifySaveFailure(Exception exception)
    {
        // 退出时主窗口可能已经隐藏或关闭，只保留上面的错误日志，不再等待交互。
        if (Volatile.Read(ref _stopRequested) != 0 || Lifecycle.HasShutdownStarted) return;
        if (Interlocked.CompareExchange(ref _saveErrorNotificationPending, 1, 0) != 0) return;

        // 提示处理器即使阻塞或抛异常，也不能拖住保存线程；每个存储最多保留一个待处理提示。
        _ = Task.Run(() =>
        {
            try
            {
                if (Volatile.Read(ref _stopRequested) != 0 || Lifecycle.HasShutdownStarted) return;
                var summary = Lang.Text("Config.Error.SaveFailed.Message", File.FilePath);
                MsgBoxWrapper.Show(
                    ExceptionDetails.Compose(summary, exception),
                    Lang.Text("Config.Error.SaveFailed.Title"),
                    MsgBoxTheme.Error);
            }
            catch (Exception notificationException)
            {
                // 日志订阅者也可能已经停止或出错，不能把二次异常留给未观察的后台任务。
                try { LogWrapper.Warn(notificationException, "Config", "配置文件保存失败提示无法显示"); }
                catch (Exception) { /* 退出期间通知失败不影响保存任务完成。 */ }
            }
            finally
            {
                Volatile.Write(ref _saveErrorNotificationPending, 0);
            }
        });
    }

    protected override bool OnAccess<TKey, TValue>(
        StorageAction action,
        ref TKey key,
        [NotNullWhen(true)] ref TValue value,
        object? argument)
    {
        if (key is not string strKey) throw new NotSupportedException($"Key '{key}' is not supported");
#pragma warning disable CS8762 // Parameter must have a non-null value when exiting in some condition.
        switch (action)
        {
            case StorageAction.Get:
                if (!File.Exists(strKey)) return false;
                try
                {
                    value = File.Get<TValue>(strKey);
                }
                catch (Exception ex) when (ex is JsonException
                                               or InvalidCastException
                                               or FormatException
                                               or OverflowException
                                               or ArgumentException
                                               or KeyNotFoundException
                                               or InvalidDataException)
                {
                    LogWrapper.Warn(ex, "Config", $"配置项 {strKey} 读取失败（可能已损坏），重置为默认值");
                    if (!_writeActionChannel.Writer.TryWrite((strKey, () => File.Remove(strKey))))
                    {
                        LogWrapper.Warn("Config", $"配置项 {strKey} 清理任务入队失败，改为同步删除");
                        try
                        {
                            File.Remove(strKey);
                            File.Sync();
                        }
                        catch (Exception cleanupEx)
                        {
                            LogWrapper.Error(cleanupEx, "Config", $"配置项 {strKey} 同步删除失败，可能需人工处理");
                        }
                    }
                    return false;
                }
                return true;
            case StorageAction.Exists:
                // 由于 Exists 的 value 类型一定是 bool，此处可 unsafe 直接赋值
                if (typeof(TValue) == typeof(bool)) Unsafe.As<TValue, bool>(ref value) = File.Exists(strKey);
                else throw new InvalidOperationException($"Storage action '{StorageAction.Exists}' must have a boolean value");
                return true;
            case StorageAction.Set:
                var localValue = value;
                _writeActionChannel.Writer.TryWrite((strKey, () => File.Set(strKey, localValue)));
                return false;
            case StorageAction.Delete:
                _writeActionChannel.Writer.TryWrite((strKey, () => File.Remove(strKey)));
                return false;
            default: throw new InvalidOperationException($"Invalid storage action: {action}");
        }
#pragma warning restore CS8762 // Parameter must have a non-null value when exiting in some condition.
    }

    public override string ToString() => $"{base.ToString()} ({File.FilePath})";
}
