using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     Host adapter between FormMain's legacy page system and the Symbio shell.
///     The shell owns presentation; routing, launching and accounts stay with the existing pages and modules.
/// </summary>
public partial class FormMain
{
    private SymbioShell? shell;
    private SymbioAccountPanel? shellAccountPanel;
    private bool shellAccountPageOpen;
    private ImageSource? shellWallpaper;
    private SymbioShellState shellState = new();

    /// <summary>RefreshShell 的合并标记：1 表示本帧已排过一次刷新。</summary>
    private int shellRefreshPending;

    /// <summary>
    ///     Dock 位置暂时锁定为「窗口外悬浮」：窗口内模式下 Dock 会覆盖面板底部，联动动效也还没稳定，
    ///     所以设置页里的「Dock 位置」开关一并关掉。想恢复可切换的开关，把这里改成 false 即可。
    /// </summary>
    private const bool DockPlacementLocked = true;

    /// <summary>锁定时使用的 Dock 位置：1 = 窗口外悬浮。</summary>
    private const int LockedDockPlacement = 1;

    /// <summary>右下角悬浮按钮与面板边缘的间距（与 FormMain.xaml 里 PanExtraButtons 的 Margin 保持一致）。</summary>
    private const double ExtraButtonMargin = 14;

    /// <summary>外置 Dock 时，扩展按钮与主面板底边之间的安全间距。</summary>
    private const double ExtraButtonInsideGap = 10;

    private static bool IsDetachedDock(int placement) =>
        (DockPlacementLocked ? LockedDockPlacement : placement) == 1;

    internal SymbioShell? Stage => shell;
    internal bool BedrockMode { get; private set; }

    private bool IsBedrockShell => BedrockMode || pageCurrent.page == PageType.Bedrock ||
                                   pageCurrent.page == PageType.BedrockLibrary ||
                                   pageCurrent.page == PageType.BedrockInstanceSelect ||
                                   pageCurrent.page == PageType.BedrockInstance;

    private void SwitchEdition(bool bedrock)
    {
        BedrockMode = bedrock;
        if (bedrock) BedrockLaunchSession.Current.DetectBedrockInstallation();
        RefreshShell();
    }

    internal void ImportBedrockPackage()
    {
        if (BedrockLaunchSession.Current.IsImporting) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Lang.Text("Bedrock.Import.DialogTitle"),
            Filter = Lang.Text("Bedrock.Import.Filter"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        BedrockLaunchSession.Current.ImportPackage(dialog.FileName);
    }

    private void LaunchSelectedEdition()
    {
        if (BedrockMode)
            BedrockLaunchSession.Current.RequestLaunch();
        else if (JavaLaunchSession.Action(!Config.Preference.Hide.PageDownload || ShellNavigation.RevealHidden) == JavaLaunchAction.Download)
            PageChange(PageType.Download, PageSubType.DownloadInstall);
        else
            JavaLaunchSession.TryLaunch();
    }

    private readonly bool alternateBrand = PCL.Core.Utils.RandomUtils.NextInt(1, 1000) == 233;
    internal void RefreshBrand() => shell?.SetBrand(
        PCL.Core.UI.Theme.ThemeService.CurrentTheme == ColorTheme.HmclBlue ? 4 : (int)Config.Preference.WindowTitleType,
        Config.Preference.WindowTitleCustomText, Config.Preference.TopBarLeftAlign, alternateBrand);
    internal void LoadBrandImage(string? path) => shell?.LoadBrandImage(path);

    private void AttachShell()
    {
        var view = new SymbioShell();
        var accountPanel = new SymbioAccountPanel();
        shell = view;
        shellAccountPanel = accountPanel;

        view.NavigationRequested += destination =>
        {
            LeaveAccountPage();
            switch (destination)
            {
                case SymbioDestination.Launch:
                    PageChange(PageType.Launch);
                    break;
                case SymbioDestination.Download: PageChange(PageType.Download); break;
                case SymbioDestination.Lan: PageChange(PageType.Lan); break;
                case SymbioDestination.SkinLibrary: PageChange(PageType.SkinLibrary); break;
                case SymbioDestination.Setup: PageChange(PageType.Setup); break;
                case SymbioDestination.Tools: PageChange(PageType.Tools); break;
            }
            RefreshShell();
        };
        view.EditionRequested += SwitchEdition;
        // 基岩版的“管理”入口保持为启动日志页；实例选择和实例设置由下方两个独立按钮负责。
        view.HomeDetailsRequested += (_, _) => PageChange(IsBedrockShell ? PageType.Bedrock : PageType.HomeContent);
        view.MinimizeRequested += (_, args) => BtnTitleMin_Click(this, args);
        view.CloseRequested += (_, args) => BtnTitleClose_Click(this, args);
        view.DragRequested += (_, _) => DragMove();
        view.BackRequested += (_, _) =>
        {
            if (shellAccountPageOpen)
            {
                LeaveAccountPage();
                RefreshShell();
            }
            else
            {
                PageBack();
            }
        };
        BedrockLaunchSession.Current.StateChanged += RefreshShellLaunch;
        BedrockLaunchSession.Current.LibraryChanged += RefreshShell;
        BedrockLaunchSession.Current.CatalogRequested += () => PageChange(PageType.Download, PageSubType.DownloadBedrock);
        ShellNavigation.VisibilityChanged += ShellNavigation.RefreshVisibility;
        JavaLaunchSession.Changed += RefreshShell;
        ProfileSkins.AvatarChanged += RefreshShell;
        Loaded += (_, _) => JavaLaunchSession.Initialize();
        LiveGameLogs.SessionsChanged += () => ModBase.RunInUi(() => BtnExtraLog?.ShowRefresh());
        view.StartRequested += (_, _) => LaunchSelectedEdition();
        view.CancelRequested += (_, _) =>
        {
            if (!BedrockMode) JavaLaunchSession.Cancel();
        };
        view.SelectInstanceRequested += (_, _) =>
            PageChange(IsBedrockShell ? PageType.BedrockInstanceSelect : PageType.InstanceSelect);
        view.InstanceSettingsRequested += (_, _) => OpenShellInstanceSettings();
        view.AccountRequested += (_, _) => ShowAccountPage();

        HookAccountPanel(accountPanel);
        ModLaunch.mcLaunchLoader.OnStateChangedUi += (_, _, _) => RefreshShell();
        // Theme and colour-mode changes rewrite the PCL ramp; the shell palette follows right after.
        PCL.Core.UI.Theme.ThemeService.ColorModeChanged += (_, _) => ModBase.RunInUi(RefreshShell);
        PCL.Core.UI.Theme.ThemeService.ColorThemeChanged += _ => ModBase.RunInUi(RefreshShell);

        view.TitleHelpRequested += BtnTitleHelp_Click;
        PanForm.Children.Remove(PanMain);
        Grid.SetRow(view, 0);
        Grid.SetRowSpan(view, 2);
        Grid.SetColumnSpan(view, 2);
        PanForm.Children.Insert(PanForm.Children.IndexOf(PanHint), view);

        // Detached dock: the window shape becomes main panel + dock island (see SymbioShell.Dock.cs).
        view.WindowShapeChanged += (_, _) => UpdateWindowShape();
        view.DockDetached = IsDetachedDock(Config.Preference.DockPlacement);

        // The left page frame becomes the sheet's secondary column; PanMain keeps only the right page.
        var sidebarFrame = (Border)PanMainLeft.Parent;
        PanMain.Children.Remove(sidebarFrame);
        view.SetHostedSidebar(sidebarFrame);
        view.SetPageContent(PanMain);
        // 窗口刚创建：必须同帧拿到正确状态，不能等 Background 派发
        RefreshShellNow();
    }

    private void OpenShellInstanceSettings()
    {
        if (!IsBedrockShell)
        {
            PageChange(PageType.InstanceSetup);
            return;
        }

        var version = Config.Launch.BedrockSelectedVersion;
        if (string.IsNullOrWhiteSpace(version)) version = ModBedrockInstance.SelectedVersionId;
        var versions = ModBedrockInstance.GetLocalVersions();
        if (string.IsNullOrWhiteSpace(version) ||
            !versions.Any(item => string.Equals(item, version, StringComparison.OrdinalIgnoreCase)))
        {
            PageChange(PageType.BedrockInstanceSelect);
            return;
        }

        BedrockInstanceWorkspace.CurrentVersion = version;
        ModMain.frmBedrockInstanceLeft ??= new BedrockInstanceNavigation();
        ModMain.frmBedrockInstanceLeft.pageID = PageSubType.VersionSetup;
        PageChange(PageType.BedrockInstance, PageSubType.VersionSetup);
    }

    private void HookAccountPanel(SymbioAccountPanel accountPanel)
    {
        accountPanel.BedrockLoginRequested += () => StartBedrockAccountLogin(() =>
        {
            accountPanel.RefreshAccounts();
            RefreshShell();
        });
        accountPanel.AccountsChanged += RefreshShell;
        accountPanel.SkinLibraryRequested += () =>
        {
            LeaveAccountPage();
            PageChange(PageType.SkinLibrary);
            RefreshShell();
        };
    }

    internal void ShowAccountLogin(ModLaunch.McLoginType type, string? server = null)
    {
        ShowAccountPage();
        shellAccountPanel?.OpenLogin(type, server);
    }

    /// <summary>Opens the account page inside the sheet. Shared by the title bar and the Bedrock sidebar.</summary>
    internal void ShowAccountPage()
    {
        if (shell is not { } view || shellAccountPanel is not { } panel) return;
        shellAccountPageOpen = true;
        panel.ShowFor(BedrockMode);
        view.SetPageContent(panel);
        RefreshShell();
        panel.PlayPageTransition();
    }

    /// <summary>
    ///     LF-style page entrance: the new page rises in on an under-damped spring from 97 %; moving forward along
    ///     the dock (or deeper into a sub-page) enters from below, moving back enters from above.
    /// </summary>
    private void PlayShellPageEntrance()
    {
        static int Order(PageType page) => page switch
        {
            PageType.Launch or PageType.Bedrock => 0,
            PageType.Download => 1,
            PageType.Lan => 2,
            PageType.SkinLibrary => 3,
            PageType.Setup => 4,
            PageType.Tools => 5,
            _ => 6 // sub-pages sit "below" their parents
        };

        var direction = Order(pageCurrent.page) >= Order(pageLast.page) ? 1 : -1;
        SymbioMotion.EnterDepth(PanMainRight, 22 * direction, .97);
    }

    private void LeaveAccountPage()
    {
        if (!shellAccountPageOpen || shell is not { } view) return;
        shellAccountPageOpen = false;
        view.SetPageContent(PanMain);
    }

    /// <summary>
    ///     合并刷新：多个数据源（启动状态、实例列表、主题、账号）会在同一瞬间各调一次，
    ///     这里用一次性 Background 派发把它们压成一帧，避免 24 个调用点叠加成"每来一条刷一次"。
    /// </summary>
    internal void RefreshShell()
    {
        if (shell is null) return;
        if (Interlocked.Exchange(ref shellRefreshPending, 1) != 0) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref shellRefreshPending, 0);
            RefreshShellCore();
        }));
    }

    /// <summary>立即刷新，用于窗口刚创建、必须同帧拿到正确状态的场景。</summary>
    internal void RefreshShellNow()
    {
        Interlocked.Exchange(ref shellRefreshPending, 0);
        RefreshShellCore();
    }

    private void RefreshShellCore()
    {
        if (shell is not { } view) return;

        SymbioPalette.Apply(ThemeManager.IsDarkMode);
        // Fluent acrylic fallback colours (AcrylicInAppFillColorDefault fallback).
        view.SetMaterialFallback(SymbioPalette.IsDark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xF9, 0xF9, 0xF9));
        var hide = Config.Preference.Hide;
        static Visibility Destination(bool hidden) => hidden && !ShellNavigation.RevealHidden ? Visibility.Collapsed : Visibility.Visible;
        view.SetDestinationVisibility(Destination(hide.PageDownload), Destination(hide.PageSetup), Destination(hide.PageTools));
        RefreshShellBackdrop(view);

        var isBedrock = BedrockMode;
        var isSkinLibrary = pageCurrent.page == PageType.SkinLibrary;
        // Pages without an explicit width get the standard column; empty placeholder pages get none.
        var sidebarWidth = PanMainLeft.Child switch
        {
            FrameworkElement { Width: var width } when double.IsFinite(width) => width,
            Panel { Children.Count: > 0 } => 216d,
            _ => 0d
        };
        view.SetHostedPageHeader(isSkinLibrary && !shellAccountPageOpen ? skinLibraryHeader : null);

        var (accountName, accountDetail, accountAvatar) = ResolveShellAccount(isBedrock);
        var instance = ResolveShellInstance(isBedrock);
        var launch = ResolveShellLaunch(isBedrock);

        var next = new SymbioShellState
        {
            IsHome = pageCurrent.page is PageType.Launch or PageType.Bedrock && !shellAccountPageOpen,
            Destination = pageCurrent.page switch
            {
                PageType.Download => SymbioDestination.Download,
                PageType.Lan => SymbioDestination.Lan,
                PageType.SkinLibrary => SymbioDestination.SkinLibrary,
                PageType.Setup => SymbioDestination.Setup,
                PageType.Tools => SymbioDestination.Tools,
                _ => SymbioDestination.Launch
            },
            PageTitle = ShellPageTitle(),
            CanGoBack = shellAccountPageOpen || pageStack.Any() || !string.IsNullOrEmpty(PageNameGet(pageCurrent)),
            ShowSidebar = !isSkinLibrary && !shellAccountPageOpen,
            SidebarWidth = sidebarWidth,
            IsBedrock = isBedrock,
            AccountName = accountName,
            AccountDetail = accountDetail,
            AccountAvatar = accountAvatar,
            HasInstance = instance.HasInstance,
            InstanceName = instance.Name,
            InstanceDescription = instance.Description,
            InstanceIcon = instance.Icon,
            CanLaunch = launch.CanLaunch,
            LaunchText = isBedrock ? "启动基岩版" : launch.Text,
            IsLaunching = launch.IsLaunching,
            CanCancelLaunch = launch.CanCancel,
            LaunchStatus = launch.Status,
            LaunchProgress = launch.Progress
        };
        // 状态没变就不重推一遍：record 是值比较，能挡掉大部分无效刷新（尤其是启动进度条那种高频回调）
        if (next == shellState) return;
        shellState = next;
        view.SetState(next);
    }

    /// <summary>
    ///     Cheap refresh used by the launch progress timers: only the launch fields of the last snapshot change.
    /// </summary>
    internal void RefreshShellLaunch()
    {
        if (shell is not { } view) return;
        var launch = ResolveShellLaunch(shellState.IsBedrock);
        var next = shellState with
        {
            CanLaunch = launch.CanLaunch,
            LaunchText = shellState.IsBedrock ? "启动基岩版" : launch.Text,
            IsLaunching = launch.IsLaunching,
            CanCancelLaunch = launch.CanCancel,
            LaunchStatus = launch.Status,
            LaunchProgress = launch.Progress
        };
        if (next == shellState) return;
        shellState = next;
        view.SetState(shellState);
    }

    private string ShellPageTitle()
    {
        if (shellAccountPageOpen) return "切换账号";

        return pageCurrent.page switch
        {
            PageType.Download => Lang.Text("Main.Tab.Download"),
            PageType.Setup => Lang.Text("Main.Tab.Settings"),
            PageType.Tools => Lang.Text("Main.Tab.Tools"),
            PageType.Lan => Lang.Text("Main.Title.Lan"),
            PageType.SkinLibrary => string.Empty, // the hosted header already carries the title
            PageType.Bedrock => Lang.Text("Main.Tab.Bedrock"),
            _ => PageNameGet(pageCurrent)
        };
    }

    /// <summary>Applies the dock placement setting (0 = inside the window, 1 = detached floating dock).</summary>
    internal void ApplyDockPlacement(int placement)
    {
        if (shell is null) return;
        shell.DockDetached = IsDetachedDock(placement);
        UpdateWindowShape();
    }

    /// <summary>Clips the window to the shell's shape: the rounded panel, or panel + detached dock island.</summary>
    internal void UpdateWindowShape()
    {
        var size = PanBack.RenderSize;
        RectForm.Rect = new Rect(size);
        var shape = shell?.BuildWindowShape(PanBack, size);
        PanBack.Clip = shape ?? (Geometry)RectForm;

        // 外置 Dock 时窗口被裁成「主面板 + Dock 岛」，主面板底部上方留出一条透明带；
        // 右下角的悬浮按钮（启动器扩展按钮）必须整体上移到主面板以内，Dock 本身仍保持外置。
        if (PanExtraButtons is not null)
            PanExtraButtons.Margin = new Thickness(0, 0, ExtraButtonMargin,
                ExtraButtonMargin + (shell?.DetachedDockBand ?? 0) + ExtraButtonInsideGap);
    }

    private void RefreshShellBackdrop(SymbioShell view)
    {
        // A user wallpaper is drawn by the shell itself so every glass surface can sample it.
        var userWallpaper = ImgBack.Visibility == Visibility.Visible && ImgBack.Background is ImageBrush brush
            ? brush.ImageSource
            : null;
        var isVideo = ImgBack.Visibility == Visibility.Visible && VideoBack.Source is not null;
        ImgBack.Opacity = userWallpaper is null && isVideo ? Config.Preference.Background.WallpaperOpacity / 1000d : 0;

        ImageSource? backdrop = userWallpaper;
        var opacity = userWallpaper is null ? 1 : Config.Preference.Background.WallpaperOpacity / 1000d;
        if (backdrop is null && !isVideo)
            backdrop = SymbioBackdrop.Create(SymbioPalette.AccentHue(), SymbioPalette.IsDark);

        // Frosted glass is the shell's base material (LF-style), not tied to PCL's old "advanced material" switch.
        // Windows "transparency effects off" and high contrast still fall back to solid surfaces inside GlassMaterial.
        shellWallpaper = backdrop;
        view.SetBackdrop(backdrop, opacity, true);
    }

    private (string Name, string Detail, ImageSource? Avatar) ResolveShellAccount(bool isBedrock)
    {
        if (isBedrock)
        {
            var active = ModBedrockAccounts.Active;
            if (active is null) return ("未登录", "基岩版 · Xbox", null);
            return (active.Gamertag, "基岩版 · Xbox", SymbioAvatars.Bedrock(active.Gamertag));
        }

        var profile = ModProfile.selectedProfile;
        if (profile is null) return ("未登录", "Java 版", null);
        var kind = profile.Type switch
        {
            ModLaunch.McLoginType.Ms => "正版",
            ModLaunch.McLoginType.Legacy => "离线",
            ModLaunch.McLoginType.Auth => "外置",
            _ => string.Empty
        };
        return (profile.Username, string.IsNullOrEmpty(kind) ? "Java 版" : $"Java 版 · {kind}", SymbioAvatars.Java(profile));
    }

    private (bool HasInstance, string Name, string Description, ImageSource? Icon) ResolveShellInstance(bool isBedrock)
    {
        if (isBedrock)
        {
            var version = Config.Launch.BedrockSelectedVersion;
            if (string.IsNullOrWhiteSpace(version)) return (false, "选择基岩版实例", "还没有选中实例", BedrockIcon());
            return (true, ModBedrockInstance.GetDisplayName(ModBedrockInstance.VersionsDirectory, version), "基岩版实例", BedrockIcon());
        }

        var instance = ModInstanceList.McMcInstanceSelected;
        var saved = States.Game.SelectedInstance;
        if (instance is null && !string.IsNullOrWhiteSpace(saved) && !string.IsNullOrWhiteSpace(ModFolder.mcFolderSelected))
            instance = new McInstance(saved);
        if (instance is null) return (false, "选择实例", "还没有选中实例", null);
        if (!instance.IsLoaded)
        {
            try
            {
                instance.Load();
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[Shell] 读取实例信息失败", ModBase.LogLevel.Debug);
            }
        }

        var description = instance.Info?.vanilla?.ToString() ?? string.Empty;
        return (true, instance.Name, description, JavaInstanceIcon(instance));
    }

    private static ImageSource? BedrockIcon() => SymbioAvatars.Load(Path.Combine(ModBase.pathImage, "Blocks", "Grass.png"));

    private static ImageSource? JavaInstanceIcon(McInstance instance)
    {
        var source = instance.Logo;
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (!source.StartsWith("pack://", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(source) &&
            !string.IsNullOrWhiteSpace(instance.PathInstance))
            source = Path.Combine(instance.PathInstance, source);
        return SymbioAvatars.Load(source);
    }

    private (bool CanLaunch, string Text, bool IsLaunching, bool CanCancel, string Status, double? Progress) ResolveShellLaunch(bool isBedrock)
    {
        if (isBedrock)
        {
            var session = BedrockLaunchSession.Current;
            return (session.CanExecute, "启动基岩版", session.IsLaunching, false, session.Detail,
                session.IsLaunching ? session.Progress : null);
        }

        if (ModLaunch.mcLaunchLoader.State == ModBase.LoadState.Loading)
        {
            var (stage, progress) = JavaLaunchSession.ReadProgress();
            return (false, Lang.Text("Launch.Home.Button.Launch"), true, true, stage, progress);
        }

        var action = JavaLaunchSession.Action(!Config.Preference.Hide.PageDownload || ShellNavigation.RevealHidden);
        var text = Lang.Text(action switch
        {
            JavaLaunchAction.Loading => "Launch.Home.Button.Loading",
            JavaLaunchAction.Download => "Launch.Home.Button.Download",
            _ => "Launch.Home.Button.Launch"
        });
        return (JavaLaunchSession.CanExecute(action), text, false, false, string.Empty, null);
    }

    private static int bedrockLoginRunning;

    private static void StartBedrockAccountLogin(Action? onSuccess = null)
    {
        // The device-code flow waits for the browser; concurrent flows would overwrite each other's account.
        if (Interlocked.Exchange(ref bedrockLoginRunning, 1) == 1)
        {
            HintService.Hint("基岩版 Xbox 登录正在进行，请先在浏览器中完成验证", HintType.Info);
            return;
        }

        ModBase.RunInNewThread(() =>
        {
            try
            {
                var result = BLoaderXUserLogin.LoginAsync((code, url) =>
                {
                    ModBase.ClipboardSet(code);
                    ModBase.OpenWebsite(url);
                    HintService.Hint($"已打开 Xbox 登录页面，验证码：{code}");
                }).GetAwaiter().GetResult();

                // Persist the account: a memory-only session falls back to the system Xbox account after restart.
                ModBedrockAccounts.SaveLogin(result);
                if (string.IsNullOrWhiteSpace(result.RefreshToken))
                    ModBase.Log("[Bedrock] 本次登录未取得 refresh_token，账号将按票据有效期缓存，过期后需重新登录",
                        ModBase.LogLevel.Debug);

                ModBase.RunInUi(() =>
                {
                    onSuccess?.Invoke();
                });
                HintService.Hint($"基岩版 Xbox 登录成功：{result.Session.XblGamertag}", HintType.Success);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "基岩版 Xbox 登录失败", ModBase.LogLevel.Hint,
                    userSummary: $"基岩版 Xbox 登录失败：{ex.Message}");
                HintService.Hint($"基岩版 Xbox 登录失败：{ex.Message}", HintType.Error);
            }
            finally
            {
                Interlocked.Exchange(ref bedrockLoginRunning, 0);
            }
        }, "Bedrock Xbox Login");
    }
}
