using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SvgIconControl = PCL.Core.UI.Controls.SvgIcon.SvgIcon;

namespace PCL;

/// <summary>
///     统一的游戏启动页左侧面板：Java 版与基岩版共用同一个入口，通过顶部的分段开关切换。
///     两个版本原有的左侧面板都保留在这一个页面里，只是同一时间只显示其中一个。
/// </summary>
public partial class PageGameLeft
{
    /// <summary>
    ///     当前是否处于基岩版模式。页面实例会被复用，因此用静态字段记住上次的选择。
    /// </summary>
    public static bool IsBedrockMode { get; private set; }

    private bool _applied;
    private bool _prewarmed;
    private bool _thumbReady;

    public PageGameLeft()
    {
        InitializeComponent();
        ModMain.frmLaunchLeft ??= new PageLaunchLeft();
        PanHost.Children.Add(ModMain.frmLaunchLeft);
        Loaded += (_, _) =>
        {
            SyncSwitch(false);
            ApplyMode(IsBedrockMode, false);
        };
        PanSwitch.SizeChanged += (_, _) => SyncSwitch(false);
    }

    /// <summary>
    ///     切换到指定版本。
    /// </summary>
    public void SwitchEdition(bool bedrock, bool animate = true) => ApplyMode(bedrock, animate);

    private void JavaArea_Click(object sender, MouseButtonEventArgs e) => ApplyMode(false, true);

    private void BedrockArea_Click(object sender, MouseButtonEventArgs e) => ApplyMode(true, true);

    private void PanSwitch_MouseEnter(object sender, MouseEventArgs e) => PrewarmBedrock();

    /// <summary>
    ///     鼠标移到开关上时就开始构建基岩版界面，避免第一次点击时才临时创建导致卡顿。
    /// </summary>
    private void PrewarmBedrock()
    {
        if (_prewarmed)
            return;
        _prewarmed = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                if (ModMain.frmBedrockLeft is null)
                {
                    ModMain.frmBedrockLeft = new PageBedrockLeft();
                    PanHost.Children.Add(ModMain.frmBedrockLeft);
                    ModMain.frmBedrockLeft.Visibility = Visibility.Collapsed;
                }

                ModMain.frmGameRight?.PrewarmBedrock();
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[Game] 预创建基岩版界面失败", ModBase.LogLevel.Debug);
            }
        }));
    }

    private void ApplyMode(bool bedrock, bool animate)
    {
        IsBedrockMode = bedrock;

        // 兜底：没有经过鼠标悬停就直接切换时，仍然保证基岩版面板存在
        if (bedrock && ModMain.frmBedrockLeft is null)
        {
            ModMain.frmBedrockLeft = new PageBedrockLeft();
            PanHost.Children.Add(ModMain.frmBedrockLeft);
            ModMain.frmBedrockLeft.Visibility = Visibility.Collapsed;
        }

        SyncSwitch(animate && _applied);

        FrameworkElement show = bedrock ? ModMain.frmBedrockLeft : ModMain.frmLaunchLeft;
        FrameworkElement? hide = bedrock ? ModMain.frmLaunchLeft : ModMain.frmBedrockLeft;
        ModMain.frmGameRight?.SwitchEdition(bedrock, animate || !_applied);

        if (show.Visibility == Visibility.Visible && _applied)
            return;
        _applied = true;

        if (!animate)
        {
            ModAnimation.AniStop("Game Edition Left");
            if (hide is not null) hide.Visibility = Visibility.Collapsed;
            show.Visibility = Visibility.Visible;
            show.Opacity = 1d;
            show.RenderTransform = null;
            show.CacheMode = null;
            return;
        }

        // 把即将隐藏的页面拍成快照，真正的页面瞬间切换，动画只作用在快照上，
        // 避免整页内容（滚动列表、SVG 图标、大量文本）在动效期间逐帧重绘
        ModAnimation.AniStop("Game Edition Left");
        if (!GameEditionTransition.CrossFade(PanHost, hide, show, bedrock ? -12d : 12d, 220, "Game Edition Left"))
        {
            if (hide is not null) hide.Visibility = Visibility.Collapsed;
            show.Visibility = Visibility.Visible;
            show.Opacity = 1d;
            show.RenderTransform = null;
            show.CacheMode = null;
        }
    }

    /// <summary>
    ///     同步分段开关的滑块位置、文字与图标颜色。
    /// </summary>
    private void SyncSwitch(bool animate)
    {
        var half = PanSwitch.ActualWidth / 2d;
        if (half < 20d)
            return;

        ShapeSwitchThumb.Width = half - 4d;
        var target = IsBedrockMode ? half : 0d;
        if (!animate || !_thumbReady || Math.Abs(SwitchThumbPos.X - target) < 0.5d)
        {
            ModAnimation.AniStop("Game Edition Thumb");
            SwitchThumbPos.X = target;
        }
        else
        {
            ModAnimation.AniStart(
                ModAnimation.AaTranslateX(ShapeSwitchThumb, target - SwitchThumbPos.X, 180,
                    ease: new ModAnimation.AniEaseOutFluent()), "Game Edition Thumb");
        }

        _thumbReady = true;
        SetLabelState(!IsBedrockMode, LabJava, IcoJava);
        SetLabelState(IsBedrockMode, LabBedrock, IcoBedrock);
    }

    private static void SetLabelState(bool active, TextBlock text, SvgIconControl icon)
    {
        if (active)
        {
            text.Foreground = Brushes.White;
            icon.IconBrush = Brushes.White;
        }
        else
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3");
            icon.SetResourceReference(SvgIconControl.IconBrushProperty, "ColorBrush3");
        }
    }
}
