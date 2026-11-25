using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PCL.Core.App.Localization;
using PCLLF.Wpf.Motion;

namespace PCL;

public partial class LoadingIndicator
{
    public delegate void IsErrorChangedEventHandler(object sender, bool isError);
    public delegate void StateChangedEventHandler(LoadingIndicator sender, LoadingStatus newState, LoadingStatus oldState);
    public static readonly RoutedEvent ClickEvent = ButtonBase.ClickEvent.AddOwner(typeof(LoadingIndicator));
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }
    public event IsErrorChangedEventHandler? IsErrorChanged;
    public event StateChangedEventHandler? StateChanged;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(LoadingIndicator), new PropertyMetadata("", (d, _) => ((LoadingIndicator)d).QueueRender()));
    public static readonly DependencyProperty TextErrorProperty = DependencyProperty.Register(nameof(TextError), typeof(string),
        typeof(LoadingIndicator), new PropertyMetadata("", (d, _) => ((LoadingIndicator)d).QueueRender()));

    private ILoadingTrigger? state;
    private ILoadingTrigger? subscribed;
    private LoadingStatus displayed = LoadingStatus.Unloaded;
    private int pending;

    public LoadingIndicator()
    {
        InitializeComponent();
        SetCurrentValue(TextProperty, Lang.Text("Application.Control.Loading.Default"));
        SetCurrentValue(TextErrorProperty, Lang.Text("Application.Control.Loading.Failed"));
        Loaded += (_, _) => { Observe(State); Render(); };
        Unloaded += (_, _) => { Observe(null); Activity.IsIndeterminate = false; };
    }

    public bool AutoRun { get; set; } = true;
    public bool TextErrorInherit { get; set; } = true;
    public bool HasAnimation { get => field; set { field = value; QueueRender(); } } = true;
    public bool ShowProgress { get => field; set { field = value; QueueRender(); } }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string TextError { get => (string)GetValue(TextErrorProperty); set => SetValue(TextErrorProperty, value); }

    public ILoadingTrigger State
    {
        get => state ??= new ManualLoadingSignal(AutoRun ? LoadingStatus.Run : LoadingStatus.Stop);
        set
        {
            state = value;
            if (Dispatcher.CheckAccess())
            {
                if (IsLoaded) Observe(State);
            }
            else Dispatcher.BeginInvoke(() => { if (IsLoaded) Observe(State); });
            QueueRender();
        }
    }

    private void Observe(ILoadingTrigger? next)
    {
        if (ReferenceEquals(subscribed, next)) return;
        if (subscribed is not null)
        {
            subscribed.LoadingStateChanged -= OnStateChanged;
            subscribed.ProgressChanged -= OnProgressChanged;
        }
        subscribed = next;
        if (next is null) return;
        next.LoadingStateChanged += OnStateChanged;
        next.ProgressChanged += OnProgressChanged;
    }

    private void OnStateChanged(LoadingStatus next, LoadingStatus previous) => QueueRender();
    private void OnProgressChanged(double next, double previous) => QueueRender();

    private void QueueRender()
    {
        if (Dispatcher.CheckAccess())
        {
            if (IsLoaded) Render();
            return;
        }
        if (Interlocked.Exchange(ref pending, 1) != 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            Interlocked.Exchange(ref pending, 0);
            if (IsLoaded) Render();
        }));
    }

    private void Render()
    {
        var current = State;
        var status = current.LoadingState;
        var error = status == LoadingStatus.Error;
        var previous = displayed;
        displayed = status;

        Activity.Visibility = status == LoadingStatus.Run && HasAnimation ? Visibility.Visible : Visibility.Collapsed;
        Activity.IsIndeterminate = Activity.Visibility == Visibility.Visible && (!ShowProgress || !current.IsLoader) && MotionPreferences.AllowsAnimation;
        Activity.Value = ShowProgress && current.IsLoader && double.IsFinite(current.Progress) ? Math.Clamp(current.Progress, 0, 1) * 100 : 0;
        Retry.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        Caption.SetResourceReference(TextBlock.ForegroundProperty, error ? "SyCritical" : "SyTextSecondary");
        Caption.Text = error ? ErrorText(current) : ShowProgress && current.IsLoader
            ? Text + " - " + Lang.Number(current.Progress, "P0") : Text;

        if (status != previous)
        {
            StateChanged?.Invoke(this, status, previous);
            if (error != (previous == LoadingStatus.Error)) IsErrorChanged?.Invoke(this, error);
        }
    }

    private string ErrorText(ILoadingTrigger current)
    {
        if (!TextErrorInherit || !current.IsLoader) return TextError;
        var message = current.Error?.GetBaseException().Message;
        if (string.IsNullOrWhiteSpace(message)) return Lang.Text("Application.Control.Loading.UnknownError");
        return new[] { "远程主机强迫关闭了", "远程方已关闭传输流", "未能解析此远程名称", "由于目标计算机积极拒绝",
            "操作已超时", "操作超时", "服务器超时", "连接超时" }.Any(message.Contains)
            ? Lang.Text("Application.Control.Loading.NetworkUnstable") : message.Trim();
    }

}
