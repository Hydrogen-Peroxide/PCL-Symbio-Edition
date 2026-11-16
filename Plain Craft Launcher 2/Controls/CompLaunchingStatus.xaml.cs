using System;
using System.Windows;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     Java 版同款启动中面板：旋转加载图标 + 版本名 + 进度条 + 当前步骤 / 验证方式 / 进度 + 你知道吗 + 取消。
///     基岩版与 Java 版共用同一份实现，动效也与 Java 版一致（面板 0.8 → 1 缩放 + 淡入）。
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

    /// <summary>面板收起动画结束时触发，宿主可据此恢复输入区的缩放与透明度。</summary>
    public event EventHandler? Hidden;

    /// <summary>显示面板：标题、版本名、验证方式，并播放与 Java 版相同的入场动画。</summary>
    public void Show(string? title, string? name, string? method)
    {
        StopHideTimer();
        LabLaunchingTitle.Text = string.IsNullOrEmpty(title) ? Lang.Text("Bedrock.Launching.Title") : title;
        LabLaunchingName.Text = name ?? "";
        LabLaunchingMethod.Text = string.IsNullOrWhiteSpace(method) ? "—" : method;
        LabLaunchingStage.Text = "—";
        SetProgress(0.02d);

        // 你知道吗：与 Java 版共用同一套随机文案
        if (Config.Preference.ShowLaunchingHint)
        {
            LabLaunchingHint.Text = PageLaunchRight.GetRandomHint(true, true);
            PanLaunchingHint.Visibility = Visibility.Collapsed;
            PanLaunchingHint.Opacity = 0d;
        }
        else
        {
            LabLaunchingHint.Text = "";
            PanLaunchingHint.Visibility = Visibility.Collapsed;
        }

        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Run;
        TransformScale.ScaleX = 0.8d;
        TransformScale.ScaleY = 0.8d;
        Opacity = 0d;
        Visibility = Visibility.Visible;
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaOpacity(this, 1d, 150, 100),
            ModAnimation.AaScaleTransform(this, 1d - TransformScale.ScaleX, 500, 100,
                new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak))
        }, "Bedrock Launching Panel");
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

    /// <summary>结束：成功先显示「已启动游戏」并淡入「你知道吗」，失败立即收起。</summary>
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

        if (Config.Preference.ShowLaunchingHint && !string.IsNullOrEmpty(LabLaunchingHint.Text))
        {
            PanLaunchingHint.Visibility = Visibility.Visible;
            ModAnimation.AniStart(
                ModAnimation.AaOpacity(PanLaunchingHint, 1d - PanLaunchingHint.Opacity, 120, 60),
                "Bedrock Launching Hint");
        }

        StopHideTimer();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4d) };
        _hideTimer.Tick += (_, _) =>
        {
            StopHideTimer();
            Hide();
        };
        _hideTimer.Start();
    }

    /// <summary>收起面板：与 Java 版相同的淡出 + 缩回 0.8。</summary>
    public void Hide()
    {
        StopHideTimer();
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Stop;
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaOpacity(this, -Opacity, 150),
            ModAnimation.AaScaleTransform(this, 0.8d - TransformScale.ScaleX, 150,
                ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)),
            ModAnimation.AaCode(() =>
            {
                Visibility = Visibility.Collapsed;
                PanLaunchingHint.Visibility = Visibility.Collapsed;
                PanLaunchingHint.Opacity = 0d;
                Hidden?.Invoke(this, EventArgs.Empty);
            }, after: true)
        }, "Bedrock Launching Panel");
    }

    private void StopHideTimer()
    {
        if (_hideTimer is null) return;
        _hideTimer.Stop();
        _hideTimer = null;
    }
}