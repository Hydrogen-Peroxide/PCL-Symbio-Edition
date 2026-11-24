using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using PCL.Core.App;
using PCLLF.Wpf.Glass;

namespace PCL;

public partial class FormMain
{
    private static ImageSource? GetAccountAvatar()
    {
        var profile = ModProfile.selectedProfile;
        if (profile is null || string.IsNullOrWhiteSpace(profile.SkinHeadId)) return null;
        var file = Path.Combine(ModBase.pathTemp, "Cache", "Skin", "Head", profile.SkinHeadId + ".png");
        if (!File.Exists(file)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? GetInstanceIcon(bool isBedrock, McInstance? instance)
    {
        if (!isBedrock && instance is not null && !instance.IsLoaded)
        {
            try
            {
                instance.Load();
            }
            catch
            {
                // The normal instance card can still fall back to the launcher icon.
            }
        }
        var source = isBedrock
            ? Path.Combine(ModBase.pathImage, "Blocks", "Grass.png")
            : instance?.Logo;
        if (string.IsNullOrWhiteSpace(source)) return null;

        if (!source.StartsWith("pack://", StringComparison.OrdinalIgnoreCase) &&
            !Path.IsPathRooted(source) && instance is not null &&
            !string.IsNullOrWhiteSpace(instance.PathInstance))
            source = Path.Combine(instance.PathInstance, source);

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = source.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(source, UriKind.Absolute)
                : new Uri(source, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private GlassLauncherView? upstreamGlass;
    private GlassAccountPanel? upstreamAccountPanel;
    private ImageSource? upstreamWallpaper;
    private bool upstreamLoginOpen;
    private bool upstreamAccountPageOpen;
    internal bool HasUpstreamGlass => upstreamGlass is not null;

    private void AttachUpstreamGlass()
    {
        var view = new GlassLauncherView();
        var accountPanel = new GlassAccountPanel();
        upstreamGlass = view;
        upstreamAccountPanel = accountPanel;
        view.NavigationRequested += page =>
        {
            // 导航到账号页/登录态之外时先退出这些临时态
            var wasTemporary = upstreamLoginOpen || upstreamAccountPageOpen;
            upstreamLoginOpen = false;
            upstreamAccountPageOpen = false;

            // 只有确实需要离开临时页面时才动页面内容：
            // 否则在当前页再点一次导航，会白白重建 PanMain，看起来像「点了没反应」。
            if (wasTemporary) view.SetPageContent(PanMain);

            switch (page)
            {
                case 0:
                    if (ModMain.frmLaunchLeft?.IsLoginPageVisible == true)
                        ModMain.frmLaunchLeft.RefreshPage(false);
                    PageChange(PageType.Launch);
                    break;
                case 1: PageChange(PageType.Download); break;
                case (int)PageType.SkinLibrary: PageChange(PageType.SkinLibrary); break;
                case (int)PageType.Lan: PageChange(PageType.Lan); break;
                case 3: PageChange(PageType.Setup); break;
                case 4: PageChange(PageType.Tools); break;
            }
            RefreshUpstreamGlass();
        };
        view.MinimizeRequested += (_, args) => BtnTitleMin_Click(this, args);
        view.CloseRequested += (_, args) => BtnTitleClose_Click(this, args);
        view.BackRequested += (_, _) =>
        {
            if (upstreamAccountPageOpen)
            {
                upstreamAccountPageOpen = false;
                view.SetPageContent(PanMain);
                // 账号页是把 accountPanel 重新挂到页面宿主上的，返回时必须把它挂回账户浮层，
                // 否则右上角账户浮层会变成空白。
                view.SetAccountContent(accountPanel);
                RefreshUpstreamGlass();
            }
            else if (upstreamLoginOpen || ModMain.frmLaunchLeft?.IsLoginPageVisible == true)
            {
                upstreamLoginOpen = false;
                ModMain.frmLaunchLeft.RefreshPage(true);
                RefreshUpstreamGlass();
            }
            else
            {
                PageBack();
            }
        };
        view.DragRequested += (_, _) => DragMove();
        view.WallpaperRequested += (_, _) => PageChange(PageType.Setup, PageSubType.SetupUI);
        view.StartRequested += (_, _) =>
        {
            if (PageGameLeft.IsBedrockMode)
                ModMain.frmBedrockLeft?.LaunchButtonClick();
            else
                ModMain.frmLaunchLeft?.LaunchButtonClick();
        };
        view.CancelRequested += (_, _) => ModLaunch.mcLaunchLoader.Abort();
        view.SelectInstanceRequested += (_, _) =>
        {
            // 基岩版有自己的实例选择页（PageBedrockInstanceSelect），
            // 之前我跳到了基岩页并在页内滚动，等于把这个正确的选择页绕掉了。
            PageChange(PageGameLeft.IsBedrockMode ? PageType.BedrockInstanceSelect : PageType.InstanceSelect);
        };
        view.InstanceSettingsRequested += (_, _) =>
            PageChange(PageGameLeft.IsBedrockMode ? PageType.BedrockInstance : PageType.InstanceSetup);
        // 账户切换使用独立页面，登录动作仍交给原有认证页面，保证令牌和档案格式保持兼容。
        var msLoginHooked = false;
        var offlineLoginHooked = false;
        accountPanel.LoginRequested += loginType =>
        {
            ModBase.RunInUi(() =>
            {
                switch (loginType)
                {
                    case ModLaunch.McLoginType.Ms:
                    {
                        ModMain.frmLoginMs ??= new PageLoginMs();
                        if (!msLoginHooked)
                        {
                            ModMain.frmLoginMs.LoginCompleted += success => accountPanel.SetLoginStatus(
                                success ? "登录完成" : "登录失败",
                                success ? "账号已保存，可以返回账号列表查看。" : "登录没有完成，可以再次尝试。",
                                !success);
                            msLoginHooked = true;
                        }
                        accountPanel.SetLoginStatus("正在登录 Microsoft 账号", "请在浏览器中完成验证，当前账户页会保留在前台。", false);
                        ModMain.frmLoginMs.StartLogin();
                        break;
                    }
                    case ModLaunch.McLoginType.Legacy:
                    {
                        ModMain.frmLoginOffline ??= new PageLoginOffline();
                        if (!offlineLoginHooked)
                        {
                            ModMain.frmLoginOffline.LoginCompleted += () =>
                                accountPanel.SetLoginStatus("登录完成", "离线账号已添加，可以返回账号列表查看。", true);
                            offlineLoginHooked = true;
                        }
                        ModMain.frmLoginOffline.StartLogin(accountPanel.LoginInputText);
                        break;
                    }
                    default:
                        HintService.Hint("请先在外置登录设置中添加验证服务器。", HintType.Info);
                        break;
                }
            });
        };
        accountPanel.BedrockLoginRequested += () =>
        {
            StartBedrockAccountLogin(() => accountPanel.RefreshAccounts());
        };
        view.SetAccountContent(accountPanel);
        view.AccountRequested += (_, _) => ShowAccountPage();

        PanTitle.Visibility = Visibility.Collapsed;
        if (PanSidebar.Parent is FrameworkElement legacyNavigation)
            legacyNavigation.Visibility = Visibility.Collapsed;
        PanLeft.Visibility = Visibility.Collapsed;
        PanForm.Children.Remove(PanMain);
        Grid.SetRowSpan(view, 2);
        Grid.SetColumnSpan(view, 2);
        PanForm.Children.Insert(PanForm.Children.IndexOf(PanHint), view);
        var sidebarFrame = (Border)PanMainLeft.Parent;
        PanMain.Children.Remove(sidebarFrame);
        view.SetHostedSidebar(sidebarFrame);
        RefreshUpstreamGlass();
        view.SetPageContent(PanMain);
    }

    /// <summary>打开新UI的账号页，供标题栏账户按钮和基岩版侧栏的「切换账号」共用。</summary>
    internal void ShowAccountPage()
    {
        if (upstreamGlass is not { } view || upstreamAccountPanel is not { } panel) return;
        upstreamAccountPageOpen = true;
        view.CloseAccountPanel();
        RefreshUpstreamGlass();
        view.SetPageContent(panel);
        panel.PlayPageTransition();
    }

    /// <summary>
    ///     确保基岩版右侧页面已实例化。新外壳把 PageGameLeft 当侧栏托管，
    ///     某些路径下 PageGameRight.ApplyMode 还没跑过，此时 frmBedrockRight 为 null，
    ///     调用方（实例选择/实例设置）就会静默什么都不做。
    /// </summary>
    private static void EnsureBedrockRightReady()
    {
        if (ModMain.frmBedrockRight is not null) return;
        try
        {
            ModMain.frmBedrockRight = new PageBedrockRight();
            ModBase.Log("[Bedrock] 已按需创建基岩版右侧页面", ModBase.LogLevel.Debug);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 按需创建基岩版右侧页面失败", ModBase.LogLevel.Debug);
        }
    }

    internal void RefreshUpstreamGlass()
    {
        if (upstreamGlass is not { } view)
            return;

        if (upstreamLoginOpen && ModMain.frmLaunchLeft?.IsLoginPageVisible != true && ModProfile.selectedProfile is not null)
            upstreamLoginOpen = false;

        view.SetSecondarySurfaceVisible(!upstreamAccountPageOpen);
        view.SetMainNavigationVisibility(BtnTitleSelect2.Visibility, BtnTitleSelect3.Visibility, BtnTitleSelect4.Visibility);
        var editionSwitch = PanMainLeft.Child is PageGameLeft gamePage ? gamePage.TakeEditionSwitch() : null;
        view.SetHostedEditionSwitch(editionSwitch);
        var sidebarWidth = PanMainLeft.Child is FrameworkElement page && double.IsFinite(page.Width) ? page.Width : 0;
        var isSkinLibrary = pageCurrent.page == PageType.SkinLibrary;
        var isBedrock = PageGameLeft.IsBedrockMode;
        var javaInstance = ModInstanceList.McMcInstanceSelected;
        var savedJavaInstance = States.Game.SelectedInstance;
        var iconInstance = javaInstance;
        if (!isBedrock && iconInstance is null &&
            !string.IsNullOrWhiteSpace(savedJavaInstance) &&
            !string.IsNullOrWhiteSpace(ModFolder.mcFolderSelected))
            iconInstance = new McInstance(savedJavaInstance);
        var bedrockVersion = Config.Launch.BedrockSelectedVersion;
        var hasInstance = isBedrock
            ? !string.IsNullOrWhiteSpace(bedrockVersion)
            : javaInstance is not null || !string.IsNullOrWhiteSpace(savedJavaInstance);
        var instanceName = isBedrock
            ? (string.IsNullOrWhiteSpace(bedrockVersion)
                ? "选择基岩版实例"
                : ModBedrockInstance.GetDisplayName(ModBedrockInstance.VersionsDirectory, bedrockVersion))
            : javaInstance?.Name ?? savedJavaInstance ?? "选择实例";
        var instanceDescription = isBedrock
            ? "基岩版实例"
            : javaInstance?.Info?.vanilla?.ToString() ?? string.Empty;
        view.SetHostedSidebarWidth(sidebarWidth, !isSkinLibrary && !upstreamAccountPageOpen);
        view.SetHostedPageHeader(isSkinLibrary ? skinLibraryHeader : null);
        var wallpaper = ImgBack.Visibility == Visibility.Visible && ImgBack.Background is ImageBrush brush
            ? brush.ImageSource
            : null;
        var wallpaperOpacity = Config.Preference.Background.WallpaperOpacity / 1000d;
        ImgBack.Opacity = wallpaper is null ? wallpaperOpacity : 0;
        view.SetHostedWallpaperOpacity(wallpaperOpacity);
        if (!ReferenceEquals(upstreamWallpaper, wallpaper))
        {
            upstreamWallpaper = wallpaper;
            view.SetWallpaper(wallpaper);
        }

        view.SetState(new GlassLauncherViewState
        {
            // Java and Bedrock are both top-level launch pages. Keeping them in
            // the home state lets the launcher use the home deck layout instead
            // of treating the page as a secondary view.
            IsHome = (pageCurrent.page is PageType.Launch or PageType.Bedrock) &&
                     !upstreamLoginOpen && !upstreamAccountPageOpen &&
                     ModMain.frmLaunchLeft?.IsLoginPageVisible != true,
            HasInstance = hasInstance,
            InstanceName = instanceName,
            InstanceDescription = instanceDescription,
            InstanceIcon = GetInstanceIcon(isBedrock, iconInstance),
            CanLaunch = isBedrock
                ? ModMain.frmBedrockRight?.BtnLaunch.IsEnabled == true
                : ModMain.frmLaunchLeft?.BtnLaunch.IsEnabled == true,
            LaunchText = isBedrock ? "启动基岩版" : "启动游戏",
            AccountName = ModProfile.selectedProfile?.Name ?? string.Empty,
            AccountAvatar = GetAccountAvatar(),
            CanGoBack = upstreamAccountPageOpen || upstreamLoginOpen || ModMain.frmLaunchLeft?.IsLoginPageVisible == true || pageStack.Any() || !string.IsNullOrEmpty(PageNameGet(pageCurrent)),
            PageTitle = upstreamAccountPageOpen ? "切换账号" : upstreamLoginOpen || ModMain.frmLaunchLeft?.IsLoginPageVisible == true ? "登录账号" : pageCurrent.page switch
            {
                PageType.Bedrock => "基岩版",
                PageType.SkinLibrary => PCL.Core.App.Localization.Lang.Text("Tools.SkinLibrary.Title"),
                PageType.Lan => PCL.Core.App.Localization.Lang.Text("Main.Title.Lan"),
                _ => PageNameGet(pageCurrent)
            },
            SelectedNavigationId = pageCurrent.page switch
            {
                PageType.Download => PageCurrentSub == PageSubType.DownloadCompFavorites ? 10 : 1,
                PageType.SkinLibrary => (int)PageType.SkinLibrary,
                PageType.Lan => (int)PageType.Lan,
                PageType.Setup => 3,
                PageType.Tools => 4,
                _ => 0
            },
            UseDarkText = !ThemeManager.IsDarkMode,
            // SE 没有「启动前检查/自动补全依赖」这套 LF 功能，主页也不提供启动选项展开区
            LaunchOptionsAvailable = false,
            ReduceMotion = false
        });
    }

    private static int bedrockLoginRunning;

    private static void StartBedrockAccountLogin(Action? onSuccess = null)
    {
        // 设备码流程要等用户在浏览器里完成验证，期间重复点击会各开一条登录流程，
        // 后发起的会互相覆盖账号，所以只允许同时存在一条。
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

                // 账号必须落盘：只保留内存会话时，重启启动器后游戏会退回系统 Xbox 账号。
                ModBedrockAccounts.SaveLogin(result);
                if (string.IsNullOrWhiteSpace(result.RefreshToken))
                    ModBase.Log("[Bedrock] 本次登录未取得 refresh_token，账号将按票据有效期缓存，过期后需重新登录",
                        ModBase.LogLevel.Debug);

                // 登录是在后台线程完成的，但账户面板刷新与登录页切换都会改 WPF 属性，
                // 必须在 UI 线程执行，否则会抛「调用线程无法访问此对象」。
                ModBase.RunInUi(() =>
                {
                    onSuccess?.Invoke();
                    ModMain.frmLaunchLeft?.RefreshPage(true);
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
