using System.Windows;
using System.Windows.Threading;
using PCLLF.Wpf.Motion;

namespace PCL;

/// <summary>
///     动画/交互门闩。
///     历史实现是裸的全局计数器：任何一处 <c>AniBeginControl()</c> 之后抛异常或 <c>await</c> 中断，
///     <c>AniEndControl()</c> 就永远不会执行，计数器永久 &gt; 0 —— 于是所有
///     <c>if (ModAnimation.AniExitsAnyControl()) return;</c> 的入口集体静默失效，
///     表现为「动画有概率不触发」和「点了没反应」。
///     现在提供三种自愈手段：
///     1) <see cref="Suppress" />：作用域化，Dispose 必定归零；
///     2) 启动闸门 <see cref="BeginStartupGate" />：带自动释放，跨窗口加载失败也不会卡死；
///     3) 看门狗：计数器超过 <see cref="StuckTimeoutMs" /> 没有变化就告警并清零。
/// </summary>
public static partial class ModAnimation
{
    public static double aniSpeed
    {
        get => MotionPreferences.PlaybackRate;
        set => MotionPreferences.PlaybackRate = value;
    }

    /// <summary>门闩多久没有变化就判定为泄漏。</summary>
    private const int StuckTimeoutMs = 5000;

    private static int controlSuppression;
    private static long lastChangeTicks = Environment.TickCount64;
    private static IDisposable? startupGate;
    private static DispatcherTimer? watchdog;

    /// <summary>当前门闩计数，仅用于诊断与看门狗。</summary>
    internal static int ControlSuppressionCount => Volatile.Read(ref controlSuppression);

    public static void AniBeginControl()
    {
        Touch();
        Interlocked.Increment(ref controlSuppression);
        EnsureWatchdog();
    }

    public static void AniEndControl()
    {
        Touch();
        // 不允许减到负数：曾经的成对写法在某些路径上会多减一次，负值会让门闩永远放开
        if (Volatile.Read(ref controlSuppression) > 0) Interlocked.Decrement(ref controlSuppression);
    }

    public static bool AniExitsAnyControl() => Volatile.Read(ref controlSuppression) != 0;

    public static int AniResetControl(int value)
    {
        Touch();
        return Interlocked.Exchange(ref controlSuppression, value);
    }

    public static int AniRestoreControl(int value)
    {
        Touch();
        return Interlocked.Add(ref controlSuppression, value);
    }

    /// <summary>
    ///     作用域化门闩：<c>using var _ = ModAnimation.Suppress();</c>。
    ///     无论正常返回、抛异常还是 await 中断，离开作用域即自动解锁。
    /// </summary>
    public static IDisposable Suppress()
    {
        AniBeginControl();
        return new SuppressionScope();
    }

    /// <summary>
    ///     启动期闸门：主窗口首帧之前禁止动画。带自动释放，主窗口加载失败也不会永久锁死动画。
    /// </summary>
    public static void BeginStartupGate(TimeSpan? autoRelease = null)
    {
        EndStartupGate();
        startupGate = Suppress();
        var timeout = autoRelease ?? TimeSpan.FromSeconds(10);
        if (Application.Current?.Dispatcher is not { } dispatcher) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = timeout };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (startupGate is null) return;
            ModBase.Log("[Anim] 启动闸门超时自动释放，主窗口可能未正常加载完成", ModBase.LogLevel.Hint);
            EndStartupGate();
        };
        timer.Start();
    }

    /// <summary>主窗口已就绪，结束启动期闸门。</summary>
    public static void EndStartupGate()
    {
        var gate = Interlocked.Exchange(ref startupGate, null);
        gate?.Dispose();
    }

    private static void Touch() => Interlocked.Exchange(ref lastChangeTicks, Environment.TickCount64);

    private static void EnsureWatchdog()
    {
        if (watchdog is not null) return;
        if (Application.Current?.Dispatcher is not { } dispatcher) return; // 非 UI 线程/启动早期，下次再挂
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Guard();
        timer.Start();
        watchdog = timer;
    }

    /// <summary>看门狗：门闩卡住就告警并清零，避免整个会话的动画与交互被静默禁用。</summary>
    private static void Guard()
    {
        if (Volatile.Read(ref controlSuppression) <= 0) return;
        var idle = Environment.TickCount64 - Interlocked.Read(ref lastChangeTicks);
        if (idle < StuckTimeoutMs) return;
        ModBase.Log($"[Anim] 门闩疑似泄漏（计数 {controlSuppression}，已 {idle} ms 未变化），已强制清零",
            ModBase.LogLevel.Hint);
        Interlocked.Exchange(ref controlSuppression, 0);
        Touch();
    }

    private sealed class SuppressionScope : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            AniEndControl();
        }
    }
}
