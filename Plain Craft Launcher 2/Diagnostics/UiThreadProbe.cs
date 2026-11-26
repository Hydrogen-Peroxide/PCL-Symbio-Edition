using System;
using System.Windows.Media;
using System.Windows.Threading;

namespace PCL;

/// <summary>
///     UI 线程基线探针（B 段「先测量」用）。
///     每 5 秒输出一行统计：渲染帧数 / 平均帧耗 / 最长帧 / 卡帧数（&gt;50 ms）/ Dispatcher 排队峰值。
///     只在 <c>Config.Preference.EnablePerfProbe</c> 开启时启动；关闭时零开销。
///     数据写入常规日志，前缀 <c>[Perf]</c>，便于 diff 改前/改后。
/// </summary>
internal static class UiThreadProbe
{
    /// <summary>单帧超过该阈值记为卡帧。</summary>
    private const double LongFrameMs = 50;

    private static readonly object Sync = new();
    private static Dispatcher? dispatcher;
    private static DispatcherTimer? timer;
    private static bool started;

    private static int queued;
    private static int peakQueued;
    private static int frames;
    private static int longFrames;
    private static double frameTotalMs;
    private static double maxFrameMs;
    private static long lastFrameTicks;
    private static long windowStartTicks;

    internal static bool IsRunning => started;

    internal static void Start()
    {
        if (started) return;
        dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.Hooks.OperationPosted += OnOperationPosted;
        dispatcher.Hooks.OperationCompleted += OnOperationCompleted;
        CompositionTarget.Rendering += OnRendering;

        windowStartTicks = Environment.TickCount64;
        lastFrameTicks = 0;
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => Flush();
        timer.Start();
        started = true;
        ModBase.Log("[Perf] UI 线程探针已启动（窗口 5s）", ModBase.LogLevel.Normal);
    }

    internal static void Stop()
    {
        if (!started) return;
        started = false;
        if (dispatcher is not null)
        {
            dispatcher.Hooks.OperationPosted -= OnOperationPosted;
            dispatcher.Hooks.OperationCompleted -= OnOperationCompleted;
        }

        CompositionTarget.Rendering -= OnRendering;
        timer?.Stop();
        timer = null;
    }

    private static void OnOperationPosted(object? sender, DispatcherHookEventArgs e)
    {
        var current = Interlocked.Increment(ref queued);
        if (current > Volatile.Read(ref peakQueued)) Volatile.Write(ref peakQueued, current);
    }

    private static void OnOperationCompleted(object? sender, DispatcherHookEventArgs e) =>
        Interlocked.Decrement(ref queued);

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        if (lastFrameTicks != 0)
        {
            var delta = now - lastFrameTicks;
            frameTotalMs += delta;
            if (delta > maxFrameMs) maxFrameMs = delta;
            if (delta > LongFrameMs) longFrames++;
        }

        lastFrameTicks = now;
        frames++;
    }

    /// <summary>输出并重置一个统计窗口。</summary>
    private static void Flush()
    {
        int windowFrames, windowLongFrames;
        double windowTotalMs, windowMaxFrameMs;
        int windowPeakQueued;
        long elapsed;
        lock (Sync)
        {
            if (frames == 0) return;
            windowFrames = frames;
            windowLongFrames = longFrames;
            windowTotalMs = frameTotalMs;
            windowMaxFrameMs = maxFrameMs;
            windowPeakQueued = peakQueued;
            elapsed = Environment.TickCount64 - windowStartTicks;

            frames = 0;
            longFrames = 0;
            frameTotalMs = 0;
            maxFrameMs = 0;
            peakQueued = 0;
            windowStartTicks = Environment.TickCount64;
        }

        var seconds = Math.Max(0.001, elapsed / 1000d);
        var fps = windowFrames / seconds;
        var average = windowFrames > 0 ? windowTotalMs / windowFrames : 0;
        var queueNow = Volatile.Read(ref queued);
        ModBase.Log(
            $"[Perf] {seconds:F1}s | 帧 {windowFrames} ({fps:F1} FPS) | 平均 {average:F1}ms | 最长 {windowMaxFrameMs:F1}ms | " +
            $"卡帧(>{LongFrameMs:F0}ms) {windowLongFrames} | Dispatcher 峰值队列 {windowPeakQueued}，当前 {queueNow}",
            ModBase.LogLevel.Normal);
    }
}
