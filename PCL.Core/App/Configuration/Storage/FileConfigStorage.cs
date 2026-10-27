using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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
    private readonly Task _writeTask;
    private int _saveFailureReportPending;

    public FileConfigStorage(IKeyValueFileProvider file) : this(file, TimeSpan.FromSeconds(10)) { }

    // 测试可缩短间隔；生产构造函数始终使用十秒节流。
    internal FileConfigStorage(IKeyValueFileProvider file, TimeSpan syncInterval)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (syncInterval <= TimeSpan.Zero || syncInterval.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(syncInterval));
        File = file;
        _writeActionChannel = Channel.CreateUnbounded<(string, Action)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        _writeTask = Task.Run(() => ProcessWriteActionsAsync(syncInterval));
    }

    private async Task ProcessWriteActionsAsync(TimeSpan syncInterval)
    {
        var intervalMs = Math.Max(1L, (long)syncInterval.TotalMilliseconds);
        var lastSyncTick = Environment.TickCount64 - intervalMs;
        var writeActionMap = new Dictionary<string, Action>();
        var reader = _writeActionChannel.Reader;
        try
        {
            while (true)
            {
                // 每次取一项后重新检查期限，连续写入也不能无限推迟落盘。
                if (writeActionMap.Count > 0 && Environment.TickCount64 - lastSyncTick >= intervalMs)
                {
                    if (Sync()) writeActionMap.Clear();
                    lastSyncTick = Environment.TickCount64;
                }
                if (reader.TryRead(out var item))
                {
                    writeActionMap[item.Item1] = item.Item2;
                    continue;
                }
                if (reader.Completion.IsCompleted) break;
                if (writeActionMap.Count == 0)
                {
                    if (!await reader.WaitToReadAsync().ConfigureAwait(false)) break;
                    continue;
                }

                // 无新消息时仍在最后一次同步后的期限唤醒；无需等下一次写入或 Stop。
                var remainingMs = Math.Max(1, intervalMs - (Environment.TickCount64 - lastSyncTick));
                using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(remainingMs));
                try
                {
                    if (!await reader.WaitToReadAsync(deadline.Token).ConfigureAwait(false)) break;
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            }
        }
        finally
        {
            while (reader.TryRead(out var item)) writeActionMap[item.Item1] = item.Item2;
            // Stop 完成前排空此前成功入队的写入，并执行最终同步。
            Sync();
        }

        bool Sync()
        {
            try
            {
                LogWrapper.Trace("Config", $"正在保存 {File.FilePath}");
                foreach (var action in writeActionMap.Values) action();
                File.Sync();
                return true;
            }
            catch (Exception ex)
            {
                ReportSaveFailure(ex);
                return false;
            }
        }
    }

    private void ReportSaveFailure(Exception error)
    {
        // 报告回调可能调度 UI 或等待隐藏窗口；保存线程和 Stop 均不能等待它。
        // 同一实例只允许一个未完成的报告，避免持续保存失败时堆积 UI 通知。
        if (Interlocked.CompareExchange(ref _saveFailureReportPending, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                try { LogWrapper.Error(error, "Config", "配置文件保存失败"); }
                catch (Exception) { /* 报告失败不得影响保存工作。 */ }
                try
                {
                    var summary = Lang.Text("Config.Error.SaveFailed.Message", File.FilePath);
                    var message = ExceptionDetails.Compose(summary, error);
                    MsgBoxWrapper.Show(message, Lang.Text("Config.Error.SaveFailed.Title"), MsgBoxTheme.Error, block: false);
                }
                catch (Exception reportError)
                {
                    try { LogWrapper.Error(reportError, "Config", "配置保存失败提示无法显示"); }
                    catch (Exception) { /* UI 和日志处理器均可能在退出期间失效。 */ }
                }
            }
            finally { Volatile.Write(ref _saveFailureReportPending, 0); }
        });
    }

    protected override void OnStop()
    {
        _writeActionChannel.Writer.TryComplete();
        _writeTask.GetAwaiter().GetResult();
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
