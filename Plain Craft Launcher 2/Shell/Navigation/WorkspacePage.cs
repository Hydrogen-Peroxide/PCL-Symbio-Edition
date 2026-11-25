using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using static PCL.ModLoader;

namespace PCL;

public class WorkspacePage : AdornerDecorator
{
    public static readonly DependencyProperty PanScrollProperty = DependencyProperty.Register(
        nameof(PanScroll), typeof(System.Windows.Controls.ScrollViewer), typeof(WorkspacePage));
    public System.Windows.Controls.ScrollViewer? PanScroll
    {
        get => (System.Windows.Controls.ScrollViewer?)GetValue(PanScrollProperty);
        set => SetValue(PanScrollProperty, value);
    }

    private readonly PageLifecycle lifecycle;
    private DispatcherTimer? timer;
    private long transition;
    public WorkspacePhase PageState => lifecycle.State;
    public event Action? PageEnter;
    public event Action? PageExit;

    public WorkspacePage() => lifecycle = new(() => pageLoader?.State, Render, Schedule, Cancel);

    private void Cancel()
    {
        transition++;
        timer?.Stop();
        timer = null;
    }

    private void Schedule(int ms, Action completed)
    {
        var ticket = transition;
        timer?.Stop();
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
        var scheduled = timer;
        scheduled.Tick += (_, _) =>
        {
            scheduled.Stop();
            if (ticket != transition) return;
            timer = null;
            completed();
        };
        scheduled.Start();
    }

    private void Render(bool entering, PageSection[] sections)
    {
        var elements = sections.Select(section => section switch
        {
            PageSection.Common => panAlways,
            PageSection.Loader => panLoader,
            _ => panContent ?? Child as FrameworkElement
        }).OfType<FrameworkElement>().Distinct().ToArray();
        if (entering) TriggerEnterAnimation(elements);
        else TriggerExitAnimation(elements);
    }

    public void PageOnEnter() { PageEnter?.Invoke(); lifecycle.Enter(); }
    public void PageOnExit() { PageExit?.Invoke(); lifecycle.Leave(); }
    public void PageOnContentExit() => lifecycle.ReplaceContent();
    public void PageOnForceExit()
    {
        lifecycle.Detach();
        foreach (var element in new[] { panAlways, panLoader, panContent ?? Child as FrameworkElement }.OfType<FrameworkElement>())
        {
            SymbioMotion.Reset(element);
            element.Visibility = Visibility.Collapsed;
        }
    }

    #region 加载器

    private ModLoader.LoaderBase pageLoader;
    private Func<object>? pageLoaderInputInvoke;
    private LoadingIndicator? pageLoaderUi;
    private FrameworkElement panLoader;
    private FrameworkElement panContent;
    private FrameworkElement? panAlways;
    private bool pageLoaderAutoRun;

    // 初始化
    /// <summary>
    ///     表明页面存在需要在后台执行的加载器。
    /// </summary>
    /// <param name="loaderUi">LoadingIndicator 控件。</param>
    /// <param name="panLoader">LoadingIndicator 控件对应的卡片。</param>
    /// <param name="panContent">加载结束后出现的内容容器。</param>
    /// <param name="panAlways">无论是否在加载总是要显示的容器。可以为 Nothing。</param>
    /// <param name="realLoader">在工作线程执行的加载器。</param>
    /// <param name="finishedInvoke">当加载器执行完成，在 UI 线程触发的 UI 初始化事件。</param>
    public void PageLoaderInit(LoadingIndicator loaderUi, FrameworkElement panLoader, FrameworkElement panContent,
        FrameworkElement? panAlways, ModLoader.LoaderBase realLoader, Action<ModLoader.LoaderBase>? finishedInvoke = null,
        Func<object>? inputInvoke = null, bool autoRun = true)
    {
        // 初始化参数
        this.panLoader = panLoader;
        this.panContent = panContent;
        this.panAlways = panAlways;
        pageLoader = realLoader;
        pageLoaderUi = loaderUi;
        pageLoaderInputInvoke = inputInvoke;
        pageLoaderAutoRun = autoRun;
        // 添加结束 Invoke
        if (finishedInvoke is not null)
            realLoader.PreviewFinish += _ =>
            {
                while (PageState == WorkspacePhase.PageExit || PageState == WorkspacePhase.ContentExit)
                    Thread.Sleep(10); // 不在退出动画时执行 UI 线程操作，避免退出动画被重置
                ModBase.RunInUiWait(() => finishedInvoke(realLoader));
                Thread.Sleep(20); // 由于大量初始化控件会导致掉帧，延迟触发 State 改变事件
            };
        realLoader.OnStateChangedUi += (loader, newState, oldState) =>
            ModBase.RunInUi(() => lifecycle.LoaderChanged(newState, oldState));
        // 隐藏 UI
        panLoader.Visibility = Visibility.Collapsed;
        panContent.Visibility = Visibility.Collapsed;
        panAlways?.Visibility = Visibility.Collapsed;
        // 初次运行加载器
        if (pageLoaderAutoRun)
        {
            if (pageLoader is ModLoader.LoaderTask task)
            {
                task.Start(task.StartGetInputNoType(null, pageLoaderInputInvoke));
            }
            else
            {
                object? input = null;
                if (pageLoaderInputInvoke is not null)
                    input = pageLoaderInputInvoke();
                pageLoader.Start(input);
            }
        }

        if (pageLoader.State == ModBase.LoadState.Finished && finishedInvoke is not null)
            ModBase.RunInUiWait(() => finishedInvoke(realLoader)); // 加载器已提前完成，直接触发事件
        // 设置加载环
        pageLoaderUi.State = realLoader;
        pageLoaderUi.Click += (_, _) =>
        {
            if (realLoader.State == ModBase.LoadState.Failed) PageLoaderRestart();
        }; // 点击重试事件
    }

    // 重试
    public void PageLoaderRestart(object input = null, bool isForceRestart = true) // 由外部调用的重试
    {
        if (!pageLoaderAutoRun)
            return;
        if (pageLoader is LoaderTask task)
        {
            pageLoader.Start(task.StartGetInputNoType(input, pageLoaderInputInvoke), isForceRestart);
        }
        else
        {
            if (input is null && pageLoaderInputInvoke is not null)
                input = pageLoaderInputInvoke();
            pageLoader.Start(input, isForceRestart);
        }
    }

    #endregion

    private void TriggerEnterAnimation(params FrameworkElement[] elements)
    {
        var realElements = elements.Where(e => e is not null).ToList();
        foreach (var element in realElements)
        {
            element.Visibility = Visibility.Visible; // 页面均处于默认的隐藏状态
            SymbioMotion.Reset(element); // stop an exit that may still be running and restore full opacity
            element.Opacity = 1d;
        }

        var animated = new List<UIElement>();
        foreach (var element in realElements)
        {
            foreach (var control in GetAllAnimControls(element, true))
            {
                // 还原被隐藏的卡片的消失动画
                control.IsHitTestVisible = true;
                control.Opacity = 1d;
                if (control.RenderTransform is TranslateTransform)
                    control.RenderTransform = null;
            }

            foreach (var control in GetAllAnimControls(element))
                if (control is ProminentAction extraButton)
                    extraButton.Show = true;
                else
                    animated.Add(control);
        }

        SymbioMotion.Stagger(animated);
        var finish = (int)Math.Ceiling(SymbioMotion.StaggerDuration(animated.Count).TotalMilliseconds);
        Schedule(finish, lifecycle.Entered);
    }

    // 退出动画：the whole page drifts up a little, shrinks and fades on a quick spring (LF page exit)
    private void TriggerExitAnimation(params FrameworkElement[] elements)
    {
        var realElements = elements.Where(e => e is not null).ToList();
        foreach (var element in realElements)
        foreach (var control in GetAllAnimControls(element))
            if (control is ProminentAction extraButton)
                extraButton.Show = false;
            else
                control.IsHitTestVisible = false;

        var ticket = transition;
        if (realElements.Count == 0) { lifecycle.Exited(); return; }
        int remaining = realElements.Count;
        foreach (var element in realElements)
            SymbioMotion.ExitDepth(element, -8, .98, () =>
            {
                if (ticket != transition) return;
                element.Visibility = Visibility.Collapsed;
                if (--remaining == 0) lifecycle.Exited();
            });
    }

    /// <summary>
    ///     禁用页面切换动画的控件列表。
    /// </summary>
    public List<FrameworkElement> disabledPageAnimControls = new();

    /// <summary>
    ///     遍历获取所有需要生成动画的控件。
    /// </summary>
    internal IEnumerable<FrameworkElement> GetAllAnimControls(FrameworkElement element, bool ignoreInvisibility = false)
    {
        var allControls = new List<FrameworkElement>();
        _GetAllAnimControls(element, ref allControls, ignoreInvisibility);
        return allControls.Except(disabledPageAnimControls);
    }

    private void _GetAllAnimControls(FrameworkElement element, ref List<FrameworkElement> allControls,
        bool ignoreInvisibility)
    {
        if (!ignoreInvisibility && element.Visibility == Visibility.Collapsed)
            return;
        if (element is WorkspaceCard || element is SymbioNotice || element is ProminentAction || element is TextBlock ||
            element is SymbioButton)
        {
            allControls.Add(element);
        }
        else if (element is ContentControl)
        {
                var content = ((ContentControl)element).Content;
                if (content is FrameworkElement)
                    _GetAllAnimControls((FrameworkElement)content, ref allControls, ignoreInvisibility);
        }
        else if (element is Panel)
        {
            foreach (var Element2 in ((Panel)element).Children)
                if (Element2 is FrameworkElement)
                    _GetAllAnimControls((FrameworkElement)Element2, ref allControls, ignoreInvisibility);
        }
    }

}
