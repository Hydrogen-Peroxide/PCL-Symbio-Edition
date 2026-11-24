// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using PCLLF.Wpf.Motion;

namespace PCLLF.Wpf.Glass;

/// <summary>Native visual shell; all launcher operations are supplied by its owner.</summary>
public partial class GlassLauncherView : UserControl
{
    private readonly SpringMotion _expansionMotion;
    private readonly SpringMotion _railMotion;
    private readonly SpringMotion _navigationMotion;
    private readonly SpringMotion _accountMotion;
    private readonly SpringMotion _navigationPopoverMotion;
    private readonly SpringMotion _homeMotion;
    private readonly SpringMotion _pageMotion;
    private readonly SpringMotion _titleMotion;
    private readonly SpringMotion _optionsMotion;
    private GlassLauncherViewState _state = new();
    private bool _updatingState;
    private bool _expanded;
    private bool _hasSidebar;
    private bool _accountOpen;
    private bool _navigationOpen;
    private bool _layoutPending;
    private bool _keyboardInteraction;
    private bool _hasPageSnapshot;
    private double _homeProgress = 1;
    private double _pageProgress = 1;
    private ImageSource _fallbackInstanceIcon;
    private ImageSource _fallbackAccountAvatar;
    private bool _homeTextShadowEnabled;
    private IReadOnlyList<GlassWindowRegion> _lastRegions = Array.Empty<GlassWindowRegion>();
    private FrameworkElement _surfaceMaskHost;

    public event EventHandler StartRequested;
    public event EventHandler CancelRequested;
    public event EventHandler SelectInstanceRequested;
    public event EventHandler InstanceSettingsRequested;
    public event EventHandler AccountRequested;
    public event EventHandler NavigationMenuRequested;
    public event EventHandler MinimizeRequested;
    public event EventHandler CloseRequested;
    public event EventHandler RecentWorldRequested;
    public event EventHandler WallpaperRequested;
    public event EventHandler BackRequested;
    public event EventHandler DragRequested;
    public event EventHandler RegionsChanged;
    public event Action<bool> DependencyCheckChanged;
    public event Action<bool> AutoFillChanged;
    public event Action<int> NavigationRequested;

    public ContentControl PageContentHost => PageContent;
    private double _hostedSidebarWidth;

    public void SetHostedWallpaperOpacity(double opacity)
    {
        WallpaperImage.Opacity = Math.Clamp(opacity, 0, 1);
    }

    public void SetHostedSidebar(UIElement content)
    {
        SideRail.Child = content;
        SecondaryHeaderRow.Height = new GridLength(70);
        SecondaryFooterRow.Height = new GridLength(52);
        SecondarySurface.Margin = new Thickness(24, 24, 0, 24);
        SecondaryLayout.Margin = new Thickness(12, 0, 12, 0);
        MainContentGrid.MaxWidth = 1000;
        MainContentGrid.Margin = new Thickness(24, 0, 24, 24);
        UpdateSidebarAvailability();
    }

    public void SetHostedSidebarWidth(double width, bool show = true)
    {
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (_hostedSidebarWidth == width && SecondarySurface.Visibility == visibility) return;
        _hostedSidebarWidth = width;
        SecondarySurface.Visibility = visibility;
        UpdateSecondaryColumn();
        ScheduleLayout();
    }

    public void SetSecondarySurfaceVisible(bool visible)
    {
        SecondarySurface.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateSecondaryColumn();
        ScheduleLayout();
    }

    public void SetHostedEditionSwitch(UIElement? content)
    {
        if (!ReferenceEquals(HostedEditionSwitch.Content, content))
            HostedEditionSwitch.Content = content;
        HostedEditionSwitch.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetHostedPageHeader(UIElement? header)
    {
        if (!ReferenceEquals(HostedPageHeader.Content, header))
            HostedPageHeader.Content = header;
        HostedPageHeader.Visibility = header is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetMainNavigationVisibility(Visibility download, Visibility settings, Visibility tools)
    {
        NavDownload.Visibility = download;
        NavSettings.Visibility = settings;
        NavOther.Visibility = tools;
        ScheduleLayout();
    }
    public Geometry SurfaceClip => FrameClip;
    public Brush SurfaceOpacityMask => FrameMask;

    public void SetSurfaceMaskHost(FrameworkElement host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_surfaceMaskHost != null && !ReferenceEquals(_surfaceMaskHost, host)) _surfaceMaskHost.OpacityMask = null;
        _surfaceMaskHost = host;
        host.OpacityMask = FrameMask;
        UpdateWallpaperClip();
    }

    public GlassLauncherView()
    {
        InitializeComponent();
        _surfaceMaskHost = MainSurface;
        _expansionMotion = new SpringMotion(this, 0, ApplyExpansion, MotionTokens.Spatial);
        _railMotion = new SpringMotion(this, 0, ApplyRail, MotionTokens.Spatial);
        _navigationMotion = new SpringMotion(this, 0, value => NavigationTranslation.Y = value, MotionTokens.Reveal);
        _accountMotion = new SpringMotion(this, 0, ApplyAccountPopover, MotionTokens.Reveal);
        _navigationPopoverMotion = new SpringMotion(this, 0, ApplyNavigationPopover, MotionTokens.Reveal);
        _homeMotion = new SpringMotion(this, 1, ApplyHomeTransition, MotionTokens.Reveal);
        _pageMotion = new SpringMotion(this, 1, ApplyPageTransition, MotionTokens.Reveal);
        _titleMotion = new SpringMotion(this, 0, ApplyTitleTransition, MotionTokens.Reveal);
        _optionsMotion = new SpringMotion(this, 1, ApplyOptionsAvailability, MotionTokens.Reveal);
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        LayoutUpdated += OnLayoutUpdated;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        SetState(_state);
    }

    public void SetState(GlassLauncherViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool optionsAvailabilityChanged = _state.LaunchOptionsAvailable != state.LaunchOptionsAvailable;
        bool homeChanged = _state.IsHome != state.IsHome;
        bool titleChanged = _state.CanGoBack != state.CanGoBack;
        _state = state;
        _updatingState = true;
        try
        {
            if (homeChanged) _homeMotion.Retarget(state.IsHome ? 1 : 0, ShouldSnapMotion);
            if (titleChanged) _titleMotion.Retarget(state.CanGoBack ? 1 : 0, ShouldSnapMotion);
            ApplyHomeTransition(_homeMotion.Value);
            ApplyRail(_railMotion.Value);
            ApplyTitleTransition(_titleMotion.Value);
            var showSidebarHeading = IsHostingSePage && SecondarySurface.Visibility == Visibility.Visible &&
                                     (state.IsHome || (!state.IsHome && !state.CanGoBack));
            var pageTitle = string.IsNullOrWhiteSpace(state.PageTitle) ? NavigationTitle(state.SelectedNavigationId) : state.PageTitle;
            PageTitleText.Text = state.IsHome || showSidebarHeading || HostedPageHeader.Content is not null ? string.Empty : pageTitle;
            SecondaryTitleText.Text = state.IsHome ? "主页" : showSidebarHeading ? pageTitle : NavigationTitle(state.SelectedNavigationId);
            if (IsHostingSePage)
            {
                SecondaryHeaderRow.Height = showSidebarHeading ? GridLength.Auto : new GridLength(0);
                ContextStrip.Visibility = showSidebarHeading ? Visibility.Visible : Visibility.Collapsed;
                SecondaryTitleText.FontWeight = FontWeights.Bold;
                SecondaryTitleText.Margin = new Thickness(12, 10, 8, 14);
            }
            AccountNameText.Text = string.IsNullOrWhiteSpace(state.AccountName) ? "未登录" : state.AccountName;
            AccountButton.ToolTip = AccountNameText.Text;
            AutomationProperties.SetName(AccountButton, "当前账户：" + AccountNameText.Text);
            ImageSource avatar = state.AccountAvatar ?? GetFallbackAccountAvatar();
            AccountAvatarImage.Source = avatar;
            AccountFallbackGlyph.Visibility = avatar is null ? Visibility.Visible : Visibility.Collapsed;

            InstanceImage.Source = state.InstanceIcon ?? GetFallbackInstanceIcon();
            InstanceImage.Visibility = Visibility.Visible;
            InstanceNameText.Text = string.IsNullOrWhiteSpace(state.InstanceName) ? "选择实例" : state.InstanceName;
            InstanceNameText.ToolTip = InstanceNameText.Text;
            InstanceDescriptionText.Text = state.InstanceDescription ?? string.Empty;
            InstanceDescriptionText.Visibility = state.IsLaunching || string.IsNullOrWhiteSpace(state.InstanceDescription) ? Visibility.Collapsed : Visibility.Visible;
            InstanceSettingsButton.IsEnabled = state.HasInstance && !state.IsLaunching;
            SelectInstanceButton.IsEnabled = !state.IsLaunching;
            SelectInstanceButton.Visibility = state.IsHome ? Visibility.Visible : Visibility.Hidden;
            InstanceSettingsButton.Visibility = state.IsHome ? Visibility.Visible : Visibility.Hidden;
            ExpandButtonContainer.Visibility = state.IsHome ? Visibility.Visible : Visibility.Hidden;
            PrimaryActions.ColumnDefinitions[3].Width = new GridLength(state.IsHome ? 0 : 36);
            LaunchButton.IsEnabled = state.IsLaunching || state.CanLaunch;
            LaunchButtonText.Text = state.IsLaunching ? "取消启动" : string.IsNullOrWhiteSpace(state.LaunchText) ? "启动游戏" : state.LaunchText;
            LaunchGlyph.Data = state.IsLaunching ? Geometry.Parse("M5,5 H19 V19 H5 Z") : (Geometry)Resources["IconPlay"];
            AutomationProperties.SetName(LaunchButton, LaunchButtonText.Text);
            LaunchStatusText.Text = state.LaunchStatus ?? string.Empty;
            LaunchStatusText.Visibility = state.IsLaunching && !string.IsNullOrWhiteSpace(state.LaunchStatus) ? Visibility.Visible : Visibility.Collapsed;
            LaunchProgressBar.Visibility = state.IsLaunching && state.LaunchProgress.HasValue ? Visibility.Visible : Visibility.Collapsed;
            LaunchProgressBar.Value = Math.Clamp(state.LaunchProgress ?? 0, 0, 1);
            ExpandButton.IsEnabled = state.LaunchOptionsAvailable;
            if (optionsAvailabilityChanged)
            {
                _expansionMotion.Retarget(_expanded && state.LaunchOptionsAvailable ? 1 : 0, ShouldSnapMotion);
                _optionsMotion.Retarget(state.LaunchOptionsAvailable ? 1 : 0, ShouldSnapMotion);
            }

            // 「启动前检查/自动补全依赖」卡已移除（SE 未实现该 LF 功能），
            // 状态字段保留以便日后接回，但不再触碰不存在的控件。
            RecentWorldButton.Visibility = state.RecentWorldAvailable ? Visibility.Visible : Visibility.Collapsed;
            RecentWorldButton.IsEnabled = state.RecentWorldAvailable && state.CanLaunch && !state.IsLaunching;
            NoRecentWorldText.Visibility = state.RecentWorldAvailable ? Visibility.Collapsed : Visibility.Visible;
            RecentWorldNameText.Text = state.RecentWorldName ?? string.Empty;
            RecentWorldDetailText.Text = state.RecentWorldDetail ?? string.Empty;
            RecentWorldImage.Source = state.RecentWorldIcon;
            WorldFallbackGlyph.Visibility = state.RecentWorldIcon is null ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(RecentWorldButton, "继续游玩：" + (state.RecentWorldName ?? string.Empty));
            ApplyPalette(state.UseDarkText);
            WorkspaceHint.Text = "页面选项";
            WorkspaceHint.Visibility = state.IsHome || _hasSidebar ? Visibility.Hidden : Visibility.Visible;
            SetSelectedNavigation(ShouldSnapMotion);
            if (ShouldSnapMotion)
            {
                _expansionMotion.Snap(_expanded && state.LaunchOptionsAvailable ? 1 : 0);
                _railMotion.Snap(_hasSidebar ? 1 : 0);
                _accountMotion.Snap(_accountOpen ? 1 : 0);
                _navigationPopoverMotion.Snap(_navigationOpen ? 1 : 0);
                _homeMotion.Snap(state.IsHome ? 1 : 0);
                _titleMotion.Snap(state.CanGoBack ? 1 : 0);
                _optionsMotion.Snap(state.LaunchOptionsAvailable ? 1 : 0);
                _pageMotion.Snap(1);
            }
        }
        finally
        {
            _updatingState = false;
        }
        ScheduleLayout();
    }

    public void SetPageContent(UIElement content)
    {
        if (ReferenceEquals(PageContent.Content, content)) return;
        bool animate = !ShouldSnapMotion && content != null && (!_state.IsHome || _homeProgress < .999);
        BitmapSource previous = animate ? CapturePageLayer() : null;
        PageContent.Content = content;
        _hasPageSnapshot = previous != null;
        PageSnapshot.Source = previous;
        PageSnapshot.Visibility = _hasPageSnapshot ? Visibility.Visible : Visibility.Hidden;
        if (animate)
        {
            // A different page has different content. Capture the current composite on rapid navigation
            // so the replacement starts from exactly the displayed frame, with no stale completion callback.
            _pageMotion.Snap(0);
            _pageMotion.Retarget(1);
        }
        else _pageMotion.Snap(1);
    }

    public void SetSidebar(IEnumerable<GlassSidebarItem> items)
    {
        SidebarPanel.Children.Clear();
        foreach (GlassSidebarItem item in items ?? Enumerable.Empty<GlassSidebarItem>())
        {
            var button = new Button
            {
                Style = (Style)Resources["GlassButton"],
                Height = 32,
                MinHeight = 32,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(11, 5, 11, 5),
                BorderThickness = new Thickness(0),
                Background = (Brush)Resources[item.IsSelected ? "GlassListSelectedBrush" : "GlassControlBrush"],
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = item.IsEnabled,
                ToolTip = string.IsNullOrWhiteSpace(item.ToolTip) ? item.Label : item.ToolTip,
            };
            AutomationProperties.SetName(button, item.Label ?? string.Empty);
            AutomationProperties.SetHelpText(button, item.ToolTip ?? string.Empty);
            var contents = new StackPanel { Orientation = Orientation.Horizontal };
            if (item.Image != null)
            {
                contents.Children.Add(new Image { Source = item.Image, Width = 14, Height = 14, Margin = new Thickness(0, 0, 7, 0), Stretch = Stretch.Uniform });
            }
            else
            {
                Geometry geometry = null;
                try { if (!string.IsNullOrWhiteSpace(item.IconData)) geometry = Geometry.Parse(item.IconData); }
                catch (FormatException) { }
                if (geometry != null && !geometry.Bounds.IsEmpty)
                {
                    var path = new Path { Data = geometry, Width = 14, Height = 14, Margin = new Thickness(0, 0, 7, 0), Stretch = Stretch.Uniform };
                    path.SetResourceReference(Shape.FillProperty, "GlassTextBrush");
                    contents.Children.Add(path);
                }
                else
                {
                    // Context labels remain visible rather than becoming an unlabeled floating icon strip.
                }
            }
            contents.Children.Add(new TextBlock { Text = item.Label ?? string.Empty, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            button.Content = contents;
            button.Click += (_, _) => { SetNavigationOpen(false); item.Invoked?.Invoke(); };
            SidebarPanel.Children.Add(button);
        }
        UpdateSidebarAvailability();
        ScheduleLayout();
    }

    private void UpdateSidebarAvailability()
    {
        _hasSidebar = IsHostingSePage || SidebarPanel.Children.Count > 0 || NavigationContent.Content != null;
        WorkspaceHint.Visibility = _hasSidebar || _state.IsHome ? Visibility.Hidden : Visibility.Visible;
        _railMotion.Retarget(_hasSidebar ? 1 : 0, ShouldSnapMotion);
        ApplyRail(_railMotion.Value);
    }

    public void SetAccountContent(UIElement content)
    {
        if (!ReferenceEquals(AccountContent.Content, content)) AccountContent.Content = content;
        if (content is null) SetAccountOpen(false);
    }

    public void SetAccountOpen(bool isOpen)
        => SetAccountOpenCore(isOpen, ShouldSnapMotion);

    public void CloseAccountPanel()
        => SetAccountOpenCore(false, true);

    private void SetAccountOpenCore(bool isOpen, bool immediate)
    {
        _accountOpen = isOpen && AccountContent.Content != null;
        if (_accountOpen) SetNavigationOpen(false);
        if (_accountOpen) AccountPopover.Visibility = Visibility.Visible;
        AccountPopover.IsHitTestVisible = _accountOpen;
        _accountMotion.Retarget(_accountOpen ? 1 : 0, immediate);
        AutomationProperties.SetHelpText(AccountButton, _accountOpen ? "账户面板已展开" : "打开账户面板");
        if (_accountOpen)
        {
            AccountPopover.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(AccountScroller.ScrollToHome));
        }
        ScheduleLayout();
    }

    public void SetNavigationContent(UIElement content)
    {
        if (!ReferenceEquals(NavigationContent.Content, content)) NavigationContent.Content = content;
        NavigationMenuButton.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
        if (content is null) SetNavigationOpen(false);
        UpdateSidebarAvailability();
        ScheduleLayout();
    }

    public void SetNavigationOpen(bool isOpen)
        => SetNavigationOpenCore(isOpen, ShouldSnapMotion);

    private void SetNavigationOpenCore(bool isOpen, bool immediate)
    {
        _navigationOpen = isOpen && NavigationContent.Content != null;
        if (_navigationOpen) SetAccountOpen(false);
        if (_navigationOpen) NavigationPopover.Visibility = Visibility.Visible;
        NavigationPopover.IsHitTestVisible = _navigationOpen;
        _navigationPopoverMotion.Retarget(_navigationOpen ? 1 : 0, immediate);
        AutomationProperties.SetName(NavigationMenuButton, _navigationOpen ? "收起全部页面选项" : "展开全部页面选项");
        if (_navigationOpen) NavigationPopover.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        ScheduleLayout();
    }

    public void SetWallpaper(ImageSource source)
    {
        WallpaperImage.Source = source;
        WallpaperImage.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        GlassMaterial.SetDesktopBackdropEnabled(ShellRoot, false);
        GlassMaterial.SetIsEnabled(ShellRoot, true);
        GlassMaterial.SetWallpaperSource(ShellRoot, source);
        GlassMaterial.SetWallpaperHost(ShellRoot, WallpaperImage);
        ApplyPalette(_state.UseDarkText);
        UpdateWallpaperClip();
    }

    /// <summary>The two painted rounded surfaces; the gap between them stays outside the window shape.</summary>
    public IReadOnlyList<GlassWindowRegion> GetSurfaceRegions()
    {
        if (NavigationSurface.ActualWidth <= 0 || BodyFrame.ActualWidth <= 0 || ActualHeight <= 0)
            return Array.Empty<GlassWindowRegion>();
        return new[] {
            new GlassWindowRegion(NavigationSurface.TransformToAncestor(this).TransformBounds(new Rect(NavigationSurface.RenderSize)), NavigationSurface.CornerRadius.TopLeft),
            new GlassWindowRegion(BodyFrame.TransformToAncestor(this).TransformBounds(new Rect(BodyFrame.RenderSize)), BodyFrame.CornerRadius.TopLeft)
        };
    }

    private static string NavigationTitle(int page) => page switch { 1 => "资源中心", 10 => "收藏", 3 => "设置", 4 => "更多", _ => "主页" };
    private double Radius(string resource) => ((CornerRadius)Resources[resource]).TopLeft;
    private bool ShouldSnapMotion => !IsLoaded || _keyboardInteraction;

    private void ApplyPalette(bool darkText)
    {
        if (SystemParameters.HighContrast)
        {
            foreach (string name in new[] { "GlassWindowBrush", "GlassMainBrush", "GlassCardBrush", "GlassChromeBrush" }) SetBrush(name, SystemColors.WindowColor.ToString());
            foreach (string name in new[] { "GlassTextBrush", "GlassSecondaryTextBrush", "HomeTextBrush", "HomeSecondaryTextBrush" }) SetBrush(name, SystemColors.WindowTextColor.ToString());
            SetBrush("GlassControlBrush", SystemColors.ControlColor.ToString());
            SetBrush("GlassHoverBrush", SystemColors.ControlDarkColor.ToString());
            SetBrush("GlassSelectedBrush", SystemColors.HighlightColor.ToString());
            SetBrush("GlassListSelectedBrush", SystemColors.HighlightColor.ToString());
            SetBrush("GlassOutlineBrush", SystemColors.WindowTextColor.ToString());
            SetBrush("GlassAccentBrush", SystemColors.HighlightColor.ToString());
            SetBrush("GlassAccentTextBrush", SystemColors.HighlightTextColor.ToString());
            GlassMaterial.SetFallbackColor(ShellRoot, SystemColors.WindowColor);
            WallpaperImage.Visibility = Visibility.Hidden;
            WorkspaceSurface.Background = SystemColors.WindowBrush;
            ContextStrip.Background = SystemColors.WindowBrush;
            SetHomeTextShadow(false);
            return;
        }
        bool wallpaper = WallpaperImage.Source != null;
        WallpaperImage.Visibility = wallpaper ? Visibility.Visible : Visibility.Collapsed;
        double density = wallpaper ? Math.Clamp(GlassMaterial.GetOpacityMultiplier(this), 0, 1) : 1;
        SetBrush("GlassWindowBrush", darkText ? "#FFF5F6F8" : "#FF181818");
        SetBrush("GlassTextBrush", darkText ? "#FF202B3A" : "#FFF4F4F4");
        SetBrush("GlassSecondaryTextBrush", darkText ? "#FF657284" : "#FFB8B8B8");
        SetBrush("GlassMainBrush", wallpaper ? "#00000000" : darkText ? "#FFF1F3F6" : "#FF181818");
        Color card = darkText ? Colors.White : Color.FromRgb(32, 32, 32);
        card.A = wallpaper ? (byte)Math.Round(224 * density) : (byte)255;
        SetBrush("GlassCardBrush", card.ToString());
        NavigationSurface.TintOpacity = wallpaper ? 1 : 0.48;
        SetBrush("GlassChromeBrush", darkText ? "#F7F8F9FB" : "#F5202020");
        SetBrush("GlassControlBrush", darkText ? "#E7E8EDF3" : "#FF333333");
        SetBrush("GlassHoverBrush", darkText ? "#FFDCE6EC" : "#FF414141");
        SetBrush("GlassSelectedBrush", darkText ? "#14000000" : "#24FFFFFF");
        SetBrush("GlassListSelectedBrush", darkText ? "#10000000" : "#1AFFFFFF");
        SetBrush("GlassOutlineBrush", darkText ? "#208094A8" : "#20FFFFFF");
        SetBrush("GlassAccentBrush", darkText ? "#FF2B2B2B" : "#FFE6E6E6");
        SetBrush("GlassAccentTextBrush", darkText ? "#FFFFFFFF" : "#FF1A1A1A");
        SetBrush("HomeTextBrush", darkText ? "#FF202B3A" : "#FFF8FAFD");
        SetBrush("HomeSecondaryTextBrush", darkText ? "#FF53657A" : "#FFE0EAF4");
        GlassMaterial.SetFallbackColor(ShellRoot, darkText ? Color.FromRgb(245, 246, 248) : Color.FromRgb(24, 24, 24));
        SetHomeTextShadow(false);
        WorkspaceSurface.Background = _state.IsHome || IsHostingSePage
            ? Brushes.Transparent
            : (Brush)Resources["GlassMainBrush"];
        ContextStrip.Background = _state.IsHome || IsHostingSePage
            ? Brushes.Transparent
            : (Brush)Resources["GlassMainBrush"];
        ContextStrip.BorderBrush = _state.IsHome ? Brushes.Transparent : (Brush)Resources["GlassOutlineBrush"];
    }

    private void SetHomeTextShadow(bool enabled)
    {
        if (_homeTextShadowEnabled == enabled) return;
        _homeTextShadowEnabled = enabled;
        System.Windows.Media.Effects.DropShadowEffect effect = null;
        if (enabled)
        {
            effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = .4, Color = Colors.Black };
            effect.Freeze();
        }
        InstanceNameText.Effect = effect;
        InstanceDescriptionText.Effect = effect;
        LaunchStatusText.Effect = effect;
    }



    private void SetBrush(string key, string color)
    {
        Color newColor = (Color)ColorConverter.ConvertFromString(color);
        if (Resources[key] is SolidColorBrush existing && existing.Color == newColor) return;
        Resources[key] = new SolidColorBrush(newColor);
    }

    private ImageSource GetFallbackInstanceIcon()
    {
        if (_fallbackInstanceIcon != null) return _fallbackInstanceIcon;
        try
        {
            var image = new BitmapImage(new Uri("pack://application:,,,/Plain Craft Launcher 2;component/Images/Icons/NoIcon.png", UriKind.Absolute));
            if (image.CanFreeze) image.Freeze();
            _fallbackInstanceIcon = image;
        }
        catch (Exception exception) when (exception is System.IO.IOException or InvalidOperationException or NotSupportedException)
        {
            // An isolated preview can omit the main app resources. The adapter supplies real icons at runtime.
        }
        return _fallbackInstanceIcon;
    }

    private ImageSource GetFallbackAccountAvatar()
    {
        if (_fallbackAccountAvatar != null) return _fallbackAccountAvatar;
        try
        {
            var resource = Application.GetResourceStream(new Uri("/Plain Craft Launcher 2;component/Images/Skins/Steve.png", UriKind.Relative));
            if (resource is null) return null;
            BitmapSource skin;
            using (resource.Stream) skin = BitmapFrame.Create(resource.Stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int unit = Math.Max(1, skin.PixelWidth / 64);
            var head = new CroppedBitmap(skin, new Int32Rect(8 * unit, 8 * unit, 8 * unit, 8 * unit));
            head.Freeze(); _fallbackAccountAvatar = head;
        }
        catch (Exception error) when (error is System.IO.IOException or InvalidOperationException or ArgumentException or NotSupportedException) { }
        return _fallbackAccountAvatar;
    }

    private void ApplyExpansion(double value)
    {
        double p = Math.Clamp(value, 0, 1);
        PrimaryTranslation.X = 0;
        ExpandTranslation.X = 0;
        AuxiliaryTranslation.X = -MotionTokens.AuxiliaryTravel * (1 - p);
        AuxiliaryTranslation.Y = 0;
        AuxiliaryCards.Opacity = p;
        ExpandRotation.Angle = p * 45;
        AuxiliaryCards.IsHitTestVisible = _expanded && _state.LaunchOptionsAvailable && p > MotionTokens.VisibilityThreshold;
        AuxiliaryCards.Visibility = p < MotionTokens.VisibilityThreshold && (!_expanded || !_state.LaunchOptionsAvailable) ? Visibility.Hidden : Visibility.Visible;
    }

    private void ApplyRail(double value)
    {
        double p = Math.Clamp(value, 0, 1);
        RailTranslation.X = 0;
        RailTranslation.Y = -MotionTokens.RailTravel * (1 - p);
        SideRail.Opacity = p;
        var canInteract = !_state.IsHome && _hasSidebar;
        SideRail.Visibility = canInteract || !_state.IsHome && p > MotionTokens.VisibilityThreshold
            ? Visibility.Visible
            : Visibility.Hidden;
        SideRail.IsHitTestVisible = canInteract;
        NotifyRegionsChanged();
    }

    private void ApplyAccountPopover(double value)
    {
        double p = Math.Clamp(value, 0, 1);
        AccountPopover.Opacity = p;
        AccountPopoverScale.ScaleX = AccountPopoverScale.ScaleY = 1;
        AccountPopoverTranslation.Y = MotionTokens.PopoverTravel * (1 - p);
        AccountPopover.Visibility = _accountOpen || p > MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Collapsed;
        AccountPopover.IsHitTestVisible = _accountOpen;
        NotifyRegionsChanged();
    }

    private void ApplyNavigationPopover(double value)
    {
        double p = Math.Clamp(value, 0, 1);
        NavigationPopover.Opacity = p;
        NavigationPopoverScale.ScaleX = NavigationPopoverScale.ScaleY = 1;
        NavigationPopoverTranslation.Y = -MotionTokens.PopoverTravel * (1 - p);
        NavigationPopover.Visibility = _navigationOpen || p > MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Collapsed;
        NavigationPopover.IsHitTestVisible = _navigationOpen;
        NotifyRegionsChanged();
    }

    private void ApplyHomeTransition(double value)
    {
        _homeProgress = Math.Clamp(value, 0, 1);
        HomePanel.Opacity = _homeProgress;
        HomeTranslation.X = -MotionTokens.PageTravel * (1 - _homeProgress);
        HomePanel.Visibility = _state.IsHome || _homeProgress > MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Hidden;
        HomePanel.IsHitTestVisible = _state.IsHome;
        PrimaryCard.Opacity = _homeProgress;
        PrimaryCard.Visibility = _state.IsHome || _homeProgress > MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Hidden;
        PrimaryCard.IsHitTestVisible = _state.IsHome;
        PageScene.Opacity = 1 - _homeProgress;
        PageSceneTranslation.X = MotionTokens.PageTravel * _homeProgress;
        PageScene.Visibility = !_state.IsHome || _homeProgress < 1 - MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Hidden;
        PageContent.Visibility = PageScene.Visibility;
        PageContent.IsHitTestVisible = !_state.IsHome;
    }

    private void ApplyPageTransition(double value)
    {
        _pageProgress = Math.Clamp(value, 0, 1);
        PageContent.Opacity = _pageProgress;
        PageEntryTranslation.X = MotionTokens.LayerTravel * (1 - _pageProgress);
        PageSnapshot.Opacity = 1 - _pageProgress;
        if (_hasPageSnapshot && _pageProgress >= 1 - MotionTokens.VisibilityThreshold)
        {
            _hasPageSnapshot = false;
            PageSnapshot.Source = null;
        }
        PageSnapshot.Visibility = _hasPageSnapshot ? Visibility.Visible : Visibility.Hidden;
    }

    private void ApplyTitleTransition(double value)
    {
        double p = Math.Clamp(value, 0, 1);
        NavigationButtons.Opacity = 1;
        NavigationButtons.Visibility = Visibility.Visible;
        NavigationButtons.IsHitTestVisible = true;
        BackTitlePanel.Opacity = p;
        BackTitleTranslation.X = -MotionTokens.LayerTravel * (1 - p);
        BackTitlePanel.Visibility = _state.CanGoBack || p > MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Collapsed;
        BackTitlePanel.IsHitTestVisible = _state.CanGoBack;
    }

    private void ApplyOptionsAvailability(double value)
    {
        double p = Math.Clamp(value, 0, 1);
        ExpandButtonContainer.Opacity = p;
        ExpandAvailabilityScale.ScaleX = ExpandAvailabilityScale.ScaleY = 1;
        ExpandButton.Visibility = _state.LaunchOptionsAvailable || p > MotionTokens.VisibilityThreshold ? Visibility.Visible : Visibility.Hidden;
        ExpandButtonContainer.IsHitTestVisible = _state.IsHome && _state.LaunchOptionsAvailable;
    }

    private BitmapSource CapturePageLayer()
    {
        if (PageLayer.ActualWidth <= 0 || PageLayer.ActualHeight <= 0 || !PageLayer.IsVisible) return null;
        try
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(PageLayer);
            int width = (int)Math.Ceiling(PageLayer.ActualWidth * dpi.DpiScaleX);
            int height = (int)Math.Ceiling(PageLayer.ActualHeight * dpi.DpiScaleY);
            if (width < 1 || height < 1 || width > 8192 || height > 8192) return null;
            var snapshot = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            snapshot.Render(PageLayer);
            snapshot.Freeze();
            return snapshot;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // A disappearing/unrenderable visual must never delay actual page navigation.
            return null;
        }
    }

    private void ArrangeHomeDeck()
    {
        if (HomeDeck.ActualWidth <= 0) return;
        bool compact = ActualWidth < 960;
        PrimaryCard.Width = double.NaN;
        PrimaryCard.Height = double.NaN;
        InstanceImage.Width = InstanceImage.Height = 64;
        InstanceNameText.FontSize = 18;
        InstanceCenter.Margin = new Thickness(0);
        LaunchButton.Width = compact ? 180 : 190;
        LaunchButton.Height = 48;
        LaunchButton.Margin = new Thickness(0, 22, 0, 0);
        AuxiliaryCards.Width = compact ? 244 : 272;
        ApplyExpansion(_expansionMotion.Value);
    }

    private void SetSelectedNavigation(bool immediate)
    {
        int index = _state.SelectedNavigationId switch { 1 or 10 => 1, 16 => 2, 17 => 3, 3 => 4, 4 => 5, _ => 0 };
        Button[] buttons = [NavLaunch, NavDownload, NavSkinLibrary, NavLan, NavSettings, NavOther];
        Button selected = buttons[index];
        if (selected.ActualHeight > 0 && NavigationButtons.ActualHeight > 0)
        {
            NavSelection.Width = selected.ActualWidth;
            NavSelection.Height = selected.ActualHeight;
            Rect slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(selected);
            _navigationMotion.Retarget(slot.Top + (slot.Height - selected.ActualHeight) / 2, immediate);
        }
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].FontWeight = i == index ? FontWeights.Medium : FontWeights.Normal;
            AutomationProperties.SetItemStatus(buttons[i], i == index ? "当前页面" : string.Empty);
        }
    }

    private void ScheduleLayout()
    {
        if (_layoutPending) return;
        _layoutPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _layoutPending = false;
            UpdateSecondaryColumn();
            ArrangeHomeDeck();
            SetSelectedNavigation(ShouldSnapMotion);
            NotifyRegionsChanged();
        }));
    }

    private void NotifyRegionsChanged()
    {
        if (!IsLoaded) return;
        IReadOnlyList<GlassWindowRegion> regions = GetSurfaceRegions();
        if (_lastRegions.Count == regions.Count && _lastRegions.SequenceEqual(regions)) return;
        _lastRegions = regions;
        RegionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScheduleLayout();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = ActualWidth < 960;
        if (!IsHostingSePage)
            MainContentGrid.Margin = new Thickness(compact ? 16 : 24, 0, compact ? 16 : 24, 24);
        UpdateSecondaryColumn();
        AccountNameText.MaxWidth = compact ? 90 : 128;
        AccountPopover.HorizontalAlignment = HorizontalAlignment.Left;
        AccountPopover.VerticalAlignment = VerticalAlignment.Bottom;
        AccountPopover.Margin = new Thickness(84, 0, 0, 16);
        AccountPopover.Width = Math.Min(920, Math.Max(600, ActualWidth - 110));
        AccountPopover.MaxHeight = Math.Max(160, ActualHeight - 48);
        NavigationPopover.Margin = new Thickness(0, 48, compact ? 16 : 24, 16);
        NavigationPopover.MaxHeight = Math.Max(160, ActualHeight - 132);
        ScheduleLayout();
    }

    // 侧栏里放的是原启动器页面（PageXxxLeft）还是内置的导航项列表
    private bool IsHostingSePage => !ReferenceEquals(SideRail.Child, SidebarScroller);

    private void UpdateSecondaryColumn()
    {
        if (SecondarySurface.Visibility == Visibility.Collapsed)
        {
            SecondaryColumn.Width = new GridLength(0);
            return;
        }

        // 外壳内边距/边框：内容宽度之外还要留出这些，否则内容会被裁掉
        var frameInsets = SecondarySurface.Margin.Left + SecondarySurface.Margin.Right +
                          SecondaryLayout.Margin.Left + SecondaryLayout.Margin.Right +
                          SecondarySurface.BorderThickness.Left + SecondarySurface.BorderThickness.Right +
                          SecondarySurface.Padding.Left + SecondarySurface.Padding.Right;

        // 只用调用方给的显式宽度。绝不能回退到 child.ActualWidth：
        // 那个值是本列宽决定的，拿它算列宽会形成反馈回路，列会被一轮轮撑大、右侧内容被挤扁。
        var compact = ActualWidth < 960;
        var standardWidth = _state.IsHome ? (compact ? 284 : 304) : (compact ? 230 : 254);
        var requiredWidth = _hostedSidebarWidth > 1d ? _hostedSidebarWidth + frameInsets : 0d;
        SecondaryColumn.Width = new GridLength(Math.Max(standardWidth, requiredWidth));
    }

    private void UpdateWallpaperClip()
    {
        if (MainSurface.ActualWidth <= 0 || MainSurface.ActualHeight <= 0) return;
        IReadOnlyList<GlassWindowRegion> regions = GetSurfaceRegions();
        GeneralTransform transform = this.TransformToVisual(_surfaceMaskHost);
        UpdateClip(FrameClip, regions, transform);
        Rect viewport = transform.TransformBounds(new Rect(RenderSize));
        if (FrameMask.Viewbox != viewport) FrameMask.Viewbox = viewport;
        if (FrameMask.Viewport != viewport) FrameMask.Viewport = viewport;
    }

    private static void UpdateClip(GeometryGroup clip, IReadOnlyList<GlassWindowRegion> regions, GeneralTransform transform)
    {
        for (int index = 0; index < regions.Count; index++)
        {
            GlassWindowRegion region = regions[index];
            Rect bounds = transform.TransformBounds(region.Bounds);
            if (index >= clip.Children.Count) clip.Children.Add(new RectangleGeometry());
            var rectangle = (RectangleGeometry)clip.Children[index];
            if (rectangle.Rect != bounds) rectangle.Rect = bounds;
            if (rectangle.RadiusX != region.CornerRadius) rectangle.RadiusX = rectangle.RadiusY = region.CornerRadius;
        }
        while (clip.Children.Count > regions.Count) clip.Children.RemoveAt(clip.Children.Count - 1);
    }

    private void OnLayoutUpdated(object sender, EventArgs e)
    {
        UpdateWallpaperClip();
        NotifyRegionsChanged();
    }
    private void HomeDeck_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangeHomeDeck();
    private void NavigationButtons_SizeChanged(object sender, SizeChangedEventArgs e) => SetSelectedNavigation(ShouldSnapMotion);

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_state.LaunchOptionsAvailable) return;
        _expanded = !_expanded;
        if (_expanded) AuxiliaryCards.Visibility = Visibility.Visible;
        ExpandButton.ToolTip = _expanded ? "收起启动选项" : "更多启动选项";
        AutomationProperties.SetName(ExpandButton, _expanded ? "收起启动选项" : "展开启动选项");
        _expansionMotion.Retarget(_expanded ? 1 : 0, ShouldSnapMotion);
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.IsLaunching) CancelRequested?.Invoke(this, EventArgs.Empty);
        else StartRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        SetAccountOpen(false);
        SetNavigationOpen(false);
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out int page)) NavigationRequested?.Invoke(page);
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        AccountRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NavigationMenuButton_Click(object sender, RoutedEventArgs e)
    {
        NavigationMenuRequested?.Invoke(this, EventArgs.Empty);
        SetNavigationOpen(!_navigationOpen);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _keyboardInteraction = true;
        if (e.Key == Key.Escape && _accountOpen)
        {
            SetAccountOpenCore(false, true);
            AccountButton.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _navigationOpen)
        {
            SetNavigationOpenCore(false, true);
            NavigationMenuButton.Focus();
            e.Handled = true;
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _keyboardInteraction = false;
        if (e.OriginalSource is not DependencyObject source) return;
        if (_accountOpen && !IsWithin(source, AccountPopover) && !IsWithin(source, AccountButton)) SetAccountOpen(false);
        if (_navigationOpen && !IsWithin(source, NavigationPopover) && !IsWithin(source, NavigationMenuButton)) SetNavigationOpen(false);
    }

    private static bool IsWithin(DependencyObject source, DependencyObject ancestor)
    {
        for (DependencyObject item = source; item != null; item = item is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
            if (ReferenceEquals(item, ancestor)) return true;
        return false;
    }

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is DependencyObject source)
        {
            for (DependencyObject item = source; item != null && !ReferenceEquals(item, sender); item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
                if (item is System.Windows.Controls.Primitives.ButtonBase) return;
        }
        DragRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void ShellRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        if (IsWithin(source, PageContent) || IsWithin(source, HostedPageHeader) || IsWithin(source, AccountPopover) || IsWithin(source, NavigationPopover) || IsWithin(source, SideRail)) return;
        for (DependencyObject item = source; item != null && !ReferenceEquals(item, ShellRoot); item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
            if (item is System.Windows.Controls.Primitives.ButtonBase) return;
        DragRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void SelectInstanceButton_Click(object sender, RoutedEventArgs e) => SelectInstanceRequested?.Invoke(this, EventArgs.Empty);
    private void InstanceSettingsButton_Click(object sender, RoutedEventArgs e) => InstanceSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => MinimizeRequested?.Invoke(this, EventArgs.Empty);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void RecentWorldButton_Click(object sender, RoutedEventArgs e) => RecentWorldRequested?.Invoke(this, EventArgs.Empty);
    private void WallpaperButton_Click(object sender, RoutedEventArgs e) => WallpaperRequested?.Invoke(this, EventArgs.Empty);
    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void CloseAccountButton_Click(object sender, RoutedEventArgs e) { SetAccountOpen(false); AccountButton.Focus(); }
    private void CloseNavigationButton_Click(object sender, RoutedEventArgs e) { SetNavigationOpen(false); NavigationMenuButton.Focus(); }
}
