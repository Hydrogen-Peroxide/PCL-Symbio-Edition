using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCLLF.Wpf.Glass;
using PCLLF.Wpf.Motion;

namespace PCL;

/// <summary>
///     The PCL SE "Symbio" launcher shell (Fluent 2): wallpaper stage, title bar with edition SelectorBar,
///     an acrylic launch card on home, an acrylic content surface hosting the legacy pages, and a
///     taskbar-style dock. All launcher operations are raised as events; FormMain decides what they do and
///     pushes a <see cref="SymbioShellState" /> back.
/// </summary>
public partial class SymbioShell : UserControl
{
    // LF-style spring depth: surfaces enter on an under-damped spring and settle with a soft overshoot;
    // selection indicators slide with a lighter bounce; press feedback uses LF's press spring.
    private static readonly SpringParameters EntranceSpring = SymbioMotion.Depth;
    private static readonly SpringParameters IndicatorSpring = new(.36, .7);
    private static readonly SpringParameters PressSpring = MotionTokens.Press;
    private static readonly SpringParameters TooltipSpring = new(.26, .8);

    private const double SheetTravel = 30;
    private const double SheetDepth = .05;
    private const double DockPressedScale = .82;

    private readonly SpringMotion _sheetMotion;
    private readonly SpringMotion _heroMotion;
    private readonly SpringMotion _editionMotion;
    private readonly SpringMotion _indicatorMotion;
    private readonly SpringMotion _labelXMotion;
    private readonly SpringMotion _labelOpacityMotion;
    private readonly Button[] _dockButtons;

    private SymbioShellState _state = new();
    private bool _stateApplied;
    private bool _indicatorPlaced;
    private bool _labelPlaced;
    private bool _layoutPending;
    private ImageSource? _fallbackInstanceIcon;

    public event Action<SymbioDestination>? NavigationRequested;
    public event Action<bool>? EditionRequested;
    internal event EventHandler? HomeDetailsRequested;
    private void HomeDetailsButton_Click(object sender, RoutedEventArgs e) => HomeDetailsRequested?.Invoke(this, e);
    public event EventHandler? StartRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? SelectInstanceRequested;
    public event EventHandler? InstanceSettingsRequested;
    public event EventHandler? AccountRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? DragRequested;

    public SymbioShell()
    {
        InitializeComponent();
        InitializeDockPlacement();
        _dockButtons = [DockLaunch, DockDownload, DockLan, DockSkinLibrary, DockSetup, DockTools];
        foreach (var button in _dockButtons) AttachPressSquish(button);

        _sheetMotion = new SpringMotion(this, 0, ApplySheet, EntranceSpring);
        _heroMotion = new SpringMotion(this, 0, ApplyHeroContent, EntranceSpring);
        _editionMotion = new SpringMotion(this, 0, value => EditionPillTranslation.X = value, IndicatorSpring);
        _indicatorMotion = new SpringMotion(this, 0, value => DockIndicatorTranslation.X = value, IndicatorSpring);
        _labelXMotion = new SpringMotion(this, 0, value => DockLabelTranslation.X = value, TooltipSpring);
        _labelOpacityMotion = new SpringMotion(this, 0, value => DockLabel.Opacity = Math.Clamp(value, 0, 1), TooltipSpring);

        Loaded += (_, _) => ScheduleLayout();
        SizeChanged += (_, _) => ScheduleLayout();
        EditionItems.SizeChanged += (_, _) => ScheduleLayout();
        ApplyState(_state, true);
    }

    private bool ShouldSnap => !IsLoaded || !MotionPreferences.AllowsAnimation;

    #region Hosting

    /// <summary>Places the legacy right-hand page container inside the content surface.</summary>
    internal event EventHandler? TitleHelpRequested;
    private void SeasonalHelp_Click(object sender, RoutedEventArgs e) => TitleHelpRequested?.Invoke(this, e);

    internal void SetStageBrush(Brush? brush)
    {
        if (brush is null) ShellRoot.SetResourceReference(Panel.BackgroundProperty, "SyWindowFallback");
        else ShellRoot.Background = brush;
    }

    internal void SetBrand(int mode, string customText, bool alignLeft, bool alternate)
    {
        TitleIdentity.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
        BrandIcon.Visibility = mode is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        BrandText.Visibility = mode is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        BrandText.Text = mode == 2 ? customText : alternate ? "PLC SE" : "PCL SE";
        CustomBrandImage.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
        SeasonalLogo.Visibility = SeasonalHelp.Visibility = mode == 4 ? Visibility.Visible : Visibility.Collapsed;
        EditionHost.HorizontalAlignment = mode == 0 && !alignLeft ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    }

    internal void LoadBrandImage(string? path)
    {
        CustomBrandImage.Source = null;
        if (path is null || !System.IO.File.Exists(path)) return;
        var image = new System.Windows.Media.Imaging.BitmapImage();
        using var stream = System.IO.File.OpenRead(path);
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        CustomBrandImage.Source = image;
    }

    public void SetPageContent(UIElement? content)
    {
        if (!ReferenceEquals(PageContent.Content, content)) PageContent.Content = content;
    }

    /// <summary>Places the legacy left-hand page container (secondary navigation) beside the content.</summary>
    public void SetHostedSidebar(UIElement? content)
    {
        if (!ReferenceEquals(SidebarHost.Child, content)) SidebarHost.Child = content;
    }

    public void SetHostedPageHeader(UIElement? header)
    {
        if (!ReferenceEquals(HostedPageHeader.Content, header)) HostedPageHeader.Content = header;
        HostedPageHeader.Visibility = header is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Respects the legacy "hide feature" preferences for the three optional destinations.</summary>
    public void SetDestinationVisibility(Visibility download, Visibility setup, Visibility tools)
    {
        DockDownload.Visibility = download;
        DockSetup.Visibility = setup;
        DockTools.Visibility = tools;
        ScheduleLayout();
    }

    /// <summary>Supplies the image the stage shows and the acrylic surfaces sample.</summary>
    public void SetBackdrop(ImageSource? wallpaper, double opacity, bool materialEnabled)
    {
        WallpaperImage.Source = wallpaper;
        WallpaperImage.Opacity = Math.Clamp(opacity, 0, 1);
        WallpaperImage.Visibility = wallpaper is null ? Visibility.Collapsed : Visibility.Visible;
        GlassMaterial.SetIsEnabled(ShellRoot, materialEnabled && wallpaper is not null);
        GlassMaterial.SetWallpaperSource(ShellRoot, wallpaper);
        GlassMaterial.SetWallpaperHost(ShellRoot, wallpaper is null ? null : WallpaperImage);
    }

    /// <summary>Acrylic fallback colour used when transparency is unavailable or turned off.</summary>
    public void SetMaterialFallback(Color color) => GlassMaterial.SetFallbackColor(ShellRoot, color);

    #endregion

    #region State

    public void SetState(SymbioShellState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ApplyState(state, false);
    }

    private void ApplyState(SymbioShellState state, bool initial)
    {
        var previous = _state;
        _state = state;
        var snap = initial || ShouldSnap || !_stateApplied;

        if (initial || previous.IsHome != state.IsHome)
            _sheetMotion.Retarget(state.IsHome ? 0 : 1, snap);
        ApplySheet(_sheetMotion.Value);

        // Edition: SelectorBar pill slides, the launch card content enters from the chosen side.
        if (!initial && previous.IsBedrock != state.IsBedrock && !snap && state.IsHome)
        {
            _heroMotion.Snap(state.IsBedrock ? 1 : -1);
            _heroMotion.Retarget(0);
        }
        HomeDetailsButton.ToolTip = state.IsBedrock ? "基岩版管理" : "主页内容和启动日志";
        SetForeground(JavaButton, state.IsBedrock ? "SyTextSecondary" : "SyText");
        SetForeground(BedrockButton, state.IsBedrock ? "SyText" : "SyTextSecondary");
        JavaText.FontWeight = state.IsBedrock ? FontWeights.Normal : FontWeights.SemiBold;
        BedrockText.FontWeight = state.IsBedrock ? FontWeights.SemiBold : FontWeights.Normal;
        EditionBadgeText.Text = state.IsBedrock ? "基岩版" : "Java 版";

        // Account
        var accountName = string.IsNullOrWhiteSpace(state.AccountName) ? "未登录" : state.AccountName;
        AccountNameText.Text = accountName;
        AccountDetailText.Text = state.AccountDetail;
        AccountDetailText.Visibility = string.IsNullOrWhiteSpace(state.AccountDetail) ? Visibility.Collapsed : Visibility.Visible;
        AccountAvatarImage.Source = state.AccountAvatar;
        AccountFallbackIcon.Visibility = state.AccountAvatar is null ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(AccountChip, $"当前账号：{accountName}");
        AccountChip.ToolTip = "切换账号";

        // Launch card
        InstanceNameText.Text = string.IsNullOrWhiteSpace(state.InstanceName) ? "选择实例" : state.InstanceName;
        InstanceNameText.ToolTip = InstanceNameText.Text;
        InstanceDescriptionText.Text = state.InstanceDescription;
        InstanceDescriptionText.Visibility = string.IsNullOrWhiteSpace(state.InstanceDescription) ? Visibility.Collapsed : Visibility.Visible;
        InstanceImage.Source = state.InstanceIcon ?? FallbackInstanceIcon();
        InstanceSettingsButton.IsEnabled = state.HasInstance && !state.IsLaunching;
        SelectInstanceButton.IsEnabled = !state.IsLaunching;

        var canCancel = state.IsLaunching && state.CanCancelLaunch;
        LaunchButton.IsEnabled = canCancel || (!state.IsLaunching && state.CanLaunch);
        LaunchButtonText.Text = canCancel ? "取消启动"
            : state.IsLaunching ? "正在启动"
            : string.IsNullOrWhiteSpace(state.LaunchText) ? "启动游戏" : state.LaunchText;
        LaunchIcon.Icon = canCancel ? "lucide/x" : "lucide/play";
        AutomationProperties.SetName(LaunchButton, LaunchButtonText.Text);

        LaunchProgressPanel.Visibility = state.IsLaunching ? Visibility.Visible : Visibility.Collapsed;
        LaunchStatusText.Text = state.LaunchStatus;
        LaunchProgressBar.IsIndeterminate = state.IsLaunching && state.LaunchProgress is null;
        LaunchProgressBar.Value = Math.Clamp(state.LaunchProgress ?? 0, 0, 1);
        LaunchPercentText.Text = state.LaunchProgress is { } progress ? $"{Math.Round(progress * 100)}%" : string.Empty;

        // Page header and sidebar
        BackButton.Visibility = state.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        PageTitleText.Text = state.PageTitle;
        PageTitleText.Visibility = string.IsNullOrWhiteSpace(state.PageTitle) ? Visibility.Collapsed : Visibility.Visible;
        var showSidebar = state.ShowSidebar && state.SidebarWidth > 1;
        SidebarHost.Visibility = showSidebar ? Visibility.Visible : Visibility.Collapsed;
        SidebarHost.Width = showSidebar ? state.SidebarWidth : double.NaN;

        // Dock
        for (var i = 0; i < _dockButtons.Length; i++)
        {
            var selected = i == (int)state.Destination;
            AutomationProperties.SetItemStatus(_dockButtons[i], selected ? "当前页面" : string.Empty);
        }

        _stateApplied = true;
        ScheduleLayout();
    }

    private ImageSource? FallbackInstanceIcon()
    {
        if (_fallbackInstanceIcon is not null) return _fallbackInstanceIcon;
        try
        {
            var image = new BitmapImage(new Uri("pack://application:,,,/Plain Craft Launcher 2;component/Images/Icons/NoIcon.png", UriKind.Absolute));
            image.Freeze();
            _fallbackInstanceIcon = image;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            // A missing fallback only leaves the icon frame empty.
        }

        return _fallbackInstanceIcon;
    }

    private static void SetForeground(Control control, string resourceKey) =>
        control.SetResourceReference(ForegroundProperty, resourceKey);

    #endregion

    #region Motion

    private void ApplySheet(double value)
    {
        // value may overshoot past 0/1 on the under-damped spring; scale and offset follow it, opacity is clamped.
        var p = Math.Clamp(value, 0, 1);
        SheetLayer.Opacity = Math.Clamp(value * 1.4, 0, 1);
        SheetTranslation.Y = SheetTravel * (1 - value);
        SheetScale.ScaleX = SheetScale.ScaleY = 1 - SheetDepth * (1 - value);
        SheetLayer.Visibility = !_state.IsHome || p > MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Hidden;
        SheetLayer.IsHitTestVisible = !_state.IsHome;

        // The launch card sinks back as the sheet rises, and springs up again on the way home.
        HomeLayer.Opacity = 1 - Math.Clamp(value * 1.4, 0, 1);
        HeroTranslation.Y = 20 * value;
        HeroScale.ScaleX = HeroScale.ScaleY = 1 - .04 * value;
        HomeLayer.Visibility = _state.IsHome || p < 1 - MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Hidden;
        HomeLayer.IsHitTestVisible = _state.IsHome;

        // The edition selector only applies to the home stage.
        EditionHost.Opacity = 1 - p;
        EditionHostTranslation.Y = -4 * p;
        EditionHost.IsHitTestVisible = _state.IsHome;
        EditionHost.Visibility = _state.IsHome || p < 1 - MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Hidden;
    }

    private void ApplyHeroContent(double value)
    {
        // value runs from ±1 (entering from the side of the chosen edition) to 0 (rest), overshooting a little.
        HeroContent.Opacity = 1 - Math.Min(1, Math.Abs(value) * 1.2);
        HeroContentTranslation.X = 26 * value;
    }

    private void AttachPressSquish(Button button)
    {
        if (button.Content is not FrameworkElement icon) return;
        var scale = new ScaleTransform(1, 1);
        icon.RenderTransformOrigin = new Point(.5, .5);
        icon.RenderTransform = scale;
        var motion = new SpringMotion(icon, 1, value => scale.ScaleX = scale.ScaleY = value, PressSpring);
        button.PreviewMouseLeftButtonDown += (_, _) => motion.Retarget(DockPressedScale, ShouldSnap);
        button.PreviewMouseLeftButtonUp += (_, _) => motion.Retarget(1, ShouldSnap);
        button.MouseLeave += (_, _) => motion.Retarget(1, ShouldSnap);
    }

    private void DockItems_MouseMove(object sender, MouseEventArgs e)
    {
        foreach (var button in _dockButtons)
        {
            if (button.Visibility != Visibility.Visible || !button.IsMouseOver) continue;
            ShowDockLabel(button);
            return;
        }
        HideDockLabel();
    }

    private void DockItems_MouseLeave(object sender, MouseEventArgs e) => HideDockLabel();

    private void ShowDockLabel(Button button)
    {
        var text = AutomationProperties.GetName(button);
        if (DockLabelText.Text != text)
        {
            DockLabelText.Text = text;
            DockLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        var center = button.TranslatePoint(new Point(button.ActualWidth / 2, 0), DockLayer).X;
        var x = center - DockLabel.DesiredSize.Width / 2;
        if (!_labelPlaced || DockLabel.Opacity < .05)
        {
            _labelXMotion.Snap(x);
            _labelPlaced = true;
        }
        else _labelXMotion.Retarget(x, ShouldSnap);
        _labelOpacityMotion.Retarget(1, ShouldSnap);
    }

    private void HideDockLabel() => _labelOpacityMotion.Retarget(0, ShouldSnap);

    private void PlaceIndicators(bool immediate)
    {
        // Dock: accent pill under the active destination, like an active taskbar app.
        var button = _dockButtons[(int)_state.Destination];
        if (button.ActualWidth > 0 && button.Visibility == Visibility.Visible && DockIndicator.Parent is UIElement dockParent)
        {
            DockIndicator.Visibility = Visibility.Visible;
            var slot = LayoutInformation.GetLayoutSlot(button);
            var origin = DockItems.TranslatePoint(new Point(0, 0), dockParent).X;
            var x = origin + slot.Left + slot.Width / 2 - DockIndicator.Width / 2;
            if (!_indicatorPlaced || immediate)
            {
                _indicatorMotion.Snap(x);
                _indicatorPlaced = true;
            }
            else _indicatorMotion.Retarget(x);
        }
        else DockIndicator.Visibility = Visibility.Hidden;

        // SelectorBar: pill under the selected edition.
        var item = _state.IsBedrock ? BedrockButton : JavaButton;
        if (item.ActualWidth > 0)
        {
            var left = item.TranslatePoint(new Point(item.ActualWidth / 2 - EditionPill.Width / 2, 0), EditionHost).X;
            _editionMotion.Retarget(left, immediate);
        }
    }

    private void ScheduleLayout()
    {
        if (_layoutPending) return;
        _layoutPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _layoutPending = false;
            PlaceIndicators(ShouldSnap);
        }));
    }

    #endregion

    #region Input

    private void ShellRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        if (IsWithin(source, PageContent) || IsWithin(source, SidebarHost) || IsWithin(source, HostedPageHeader)) return;
        for (var item = source; item is not null && !ReferenceEquals(item, ShellRoot); item = Parent(item))
            if (item is ButtonBase or TextBoxBase) return;
        DragRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private static DependencyObject? Parent(DependencyObject item) =>
        item is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);

    private static bool IsWithin(DependencyObject source, DependencyObject ancestor)
    {
        for (var item = source; item is not null; item = Parent(item))
            if (ReferenceEquals(item, ancestor)) return true;
        return false;
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
            NavigationRequested?.Invoke((SymbioDestination)index);
    }

    private void JavaButton_Click(object sender, RoutedEventArgs e) => EditionRequested?.Invoke(false);
    private void BedrockButton_Click(object sender, RoutedEventArgs e) => EditionRequested?.Invoke(true);

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.IsLaunching) CancelRequested?.Invoke(this, EventArgs.Empty);
        else StartRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SelectInstanceButton_Click(object sender, RoutedEventArgs e) => SelectInstanceRequested?.Invoke(this, EventArgs.Empty);
    private void InstanceSettingsButton_Click(object sender, RoutedEventArgs e) => InstanceSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void AccountChip_Click(object sender, RoutedEventArgs e) => AccountRequested?.Invoke(this, EventArgs.Empty);
    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => MinimizeRequested?.Invoke(this, EventArgs.Empty);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    #endregion
}
