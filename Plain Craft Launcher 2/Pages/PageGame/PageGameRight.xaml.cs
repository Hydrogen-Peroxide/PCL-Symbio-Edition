using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PCL;

/// <summary>
///     统一的游戏启动页右侧面板：承载 Java 版主页与基岩版管理页，跟随左侧开关一起切换。
/// </summary>
public partial class PageGameRight
{
    private bool _applied;

    public PageGameRight()
    {
        InitializeComponent();
        ModMain.frmLaunchRight ??= new PageLaunchRight();
        PanHost.Children.Add(ModMain.frmLaunchRight);
        Loaded += (_, _) => ApplyMode(PageGameLeft.IsBedrockMode, false);
    }

    public void SwitchEdition(bool bedrock, bool animate = true) => ApplyMode(bedrock, animate);

    /// <summary>
    ///     提前构建并挂载基岩版右侧页面，让第一次切换时不再临时创建。
    /// </summary>
    public void PrewarmBedrock()
    {
        if (ModMain.frmBedrockRight is not null)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (ModMain.frmBedrockRight is not null)
                return;
            try
            {
                ModMain.frmBedrockRight = new PageBedrockRight();
                PanHost.Children.Add(ModMain.frmBedrockRight);
                ModMain.frmBedrockRight.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[Game] 预创建基岩版右侧页面失败", ModBase.LogLevel.Debug);
            }
        }));
    }

    private void ApplyMode(bool bedrock, bool animate)
    {
        if (bedrock && ModMain.frmBedrockRight is null)
        {
            ModMain.frmBedrockRight = new PageBedrockRight();
            PanHost.Children.Add(ModMain.frmBedrockRight);
            ModMain.frmBedrockRight.Visibility = Visibility.Collapsed;
        }

        FrameworkElement show = bedrock ? ModMain.frmBedrockRight : ModMain.frmLaunchRight;
        FrameworkElement? hide = bedrock ? ModMain.frmLaunchRight : ModMain.frmBedrockRight;
        // 返回顶部按钮只对当前可见的页面生效
        PanScroll = bedrock ? null : ModMain.frmLaunchRight.PanScroll;

        if (show.Visibility == Visibility.Visible && _applied)
            return;
        _applied = true;

        if (!animate)
        {
            ModAnimation.AniStop("Game Edition Right");
            if (hide is not null)
            {
                hide.Visibility = Visibility.Collapsed;
                hide.Opacity = 1d;
                hide.RenderTransform = null;
                hide.CacheMode = null;
            }

            show.Visibility = Visibility.Visible;
            show.Opacity = 1d;
            show.RenderTransform = null;
            show.CacheMode = null;
            return;
        }

        // 与左侧一致：快照交叉淡化，动画只作用在一张位图上
        ModAnimation.AniStop("Game Edition Right");
        if (!GameEditionTransition.CrossFade(PanHost, hide, show, bedrock ? -14d : 14d, 240, "Game Edition Right"))
        {
            if (hide is not null)
            {
                hide.Visibility = Visibility.Collapsed;
                hide.Opacity = 1d;
                hide.RenderTransform = null;
                hide.CacheMode = null;
            }

            show.Visibility = Visibility.Visible;
            show.Opacity = 1d;
            show.RenderTransform = null;
            show.CacheMode = null;
        }
    }
}
