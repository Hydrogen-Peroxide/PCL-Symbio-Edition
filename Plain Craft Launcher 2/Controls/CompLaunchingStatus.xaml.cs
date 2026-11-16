using System;
using System.Windows;
using System.Windows.Threading;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     Java 版同款启动中面板：旋转加载图标 + 版本名 + 进度条 + 当前步骤 / 验证方式 / 进度 + 取消。
///     基岩版与 Java 版共用同一份实现。
/// </summary>
public partial class CompLaunchingStatus
{
    private DispatcherTimer? _hideTimer;

    public CompLaunchingStatus()
    {
        InitializeComponent();
        BtnCancel.Click += (_, _) =>
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            Hide();
        };
    }

    /// <summary>点击取消时触发（只通知，不代表进程被终止）。</summary>
    public event EventHandler? Cancelled;

    /// <summary>显示面板：标题、版本名、验证方式。</summary>
    public void Show(string? title, string? name, string? method)
    {
        StopHideTimer();
        LabLaunchingTitle.Text = string.IsNullOrEmpty(title) ? Lang.Text("Bedrock.Launching.Title") : title;
        LabLaunchingName.Text = name ?? "";
        LabLaunchingMethod.Text = string.IsNullOrWhiteSpace(method) ? "—" : method;
        LabLaunchingStage.Text = "—";
        SetProgress(0.02d);
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Run;
        Visibility = Visibility.Visible;
        Opacity = 1d;
    }

    /// <summary>推进进度条与「当前步骤」。</summary>
    public void SetProgress(double progress, string? stage = null)
    {
        var value = Math.Clamp(progress, 0d, 1d);
        ProgressLaunchingFinished.Width = new GridLength(value, GridUnitType.Star);
        ProgressLaunchingUnfinished.Width = new GridLength(Math.Max(0.0001d, 1d - value), GridUnitType.Star);
        LabLaunchingProgress.Text = (value * 100d).ToString("0") + " %";
        if (!string.IsNullOrWhiteSpace(stage)) LabLaunchingStage.Text = stage;
    }

    /// <summary>结束：成功先显示「已启动游戏」并在 4 秒后收起，失败立即收起。</summary>
    public void Finish(bool success, string? title = null, string? stage = null)
    {
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Stop;
        if (!string.IsNullOrWhiteSpace(stage)) LabLaunchingStage.Text = stage;

        if (!success)
        {
            Hide();
            return;
        }

        LabLaunchingTitle.Text = string.IsNullOrEmpty(title) ? Lang.Text("Bedrock.Launching.Started") : title;
        SetProgress(1d);
        StopHideTimer();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4d) };
        _hideTimer.Tick += (_, _) =>
        {
            StopHideTimer();
            Hide();
        };
        _hideTimer.Start();
    }

    public void Hide()
    {
        StopHideTimer();
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Stop;
        Visibility = Visibility.Collapsed;
        Opacity = 0d;
    }

    private void StopHideTimer()
    {
        if (_hideTimer is null) return;
        _hideTimer.Stop();
        _hideTimer = null;
    }
}