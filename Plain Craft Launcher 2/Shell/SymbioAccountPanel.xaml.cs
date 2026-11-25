using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCLLF.Wpf.Motion;
using SvgIconControl = PCL.Core.UI.Controls.SvgIcon.SvgIcon;

namespace PCL;

/// <summary>
///     Account page of the Symbio shell, laid out like a Fluent settings page: the current account and the
///     account kinds on the left, the accounts of the selected kind as rows on the right. Account actions use the same profile store as the launcher.
/// </summary>
public partial class SymbioAccountPanel
{
    private enum AccountKind { Microsoft, Offline, Auth, Bedrock }

    /// <summary>Raised for the Xbox device-code login used by Bedrock accounts.</summary>
    public event Action? BedrockLoginRequested;

    /// <summary>Raised when the user asks for the skin library.</summary>
    public event Action? SkinLibraryRequested;

    /// <summary>Raised after the selected account or the account list changed.</summary>
    public event Action? AccountsChanged;

    public string LoginInputText => LoginInput.Text.Trim();

    private AccountKind kind = AccountKind.Microsoft;
    private bool loginOpen;
    private ModYggdrasilConnect.ServerInfo? discoveredServer;
    private string? resolvedServer;
    private CancellationTokenSource? serverProbe;
    private int loginRevision;
    private bool signingIn;

    public SymbioAccountPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh(true);
    }

    public void OpenLogin(ModLaunch.McLoginType type, string? server = null)
    {
        kind = KindOf(type);
        Refresh(false);
        ShowLogin(type);
        if (server is not null) ServerAddress.Text = server;
    }

    /// <summary>Opens the page on the account kind that matches the current edition.</summary>
    public void ShowFor(bool bedrock)
    {
        var target = bedrock ? AccountKind.Bedrock : KindOf(ModProfile.selectedProfile?.Type ?? ModLaunch.McLoginType.Ms);
        if (target != kind) HideLogin();
        kind = target;
        if (IsLoaded) Refresh(true);
    }

    public void RefreshAccounts() => Refresh(true);

    public void SetLoginStatus(string title, string detail, bool canRetry)
    {
        LoginTitle.Text = title;
        LoginDetail.Text = detail;
        BtnLoginStart.IsEnabled = canRetry;
    }

    public void PlayPageTransition()
    {
        if (IsLoaded) StaggeredEntrance.Play(AccountItems.Children.OfType<UIElement>());
    }

    #region List

    private static AccountKind KindOf(ModLaunch.McLoginType type) => type switch
    {
        ModLaunch.McLoginType.Legacy => AccountKind.Offline,
        ModLaunch.McLoginType.Auth => AccountKind.Auth,
        _ => AccountKind.Microsoft
    };

    private static ModLaunch.McLoginType LoginTypeOf(AccountKind kind) => kind switch
    {
        AccountKind.Offline => ModLaunch.McLoginType.Legacy,
        AccountKind.Auth => ModLaunch.McLoginType.Auth,
        _ => ModLaunch.McLoginType.Ms
    };

    private void Refresh(bool animate)
    {
        BtnMicrosoft.Tag = kind == AccountKind.Microsoft ? "Selected" : null;
        BtnOffline.Tag = kind == AccountKind.Offline ? "Selected" : null;
        BtnAuth.Tag = kind == AccountKind.Auth ? "Selected" : null;
        BtnBedrock.Tag = kind == AccountKind.Bedrock ? "Selected" : null;

        (ListTitle.Text, ListDetail.Text, BtnAddText.Text) = kind switch
        {
            AccountKind.Offline => ("离线账号", "使用玩家名创建本地档案，不需要联网验证。", "添加离线账号"),
            AccountKind.Auth => ("外置账号", "通过第三方验证服务器（如 LittleSkin）登录。", "添加外置账号"),
            AccountKind.Bedrock => ("基岩版账号", "用于基岩版 GDK 启动的 Xbox 身份。", "登录基岩版"),
            _ => ("正版账号", "使用 Microsoft 账号登录 Minecraft Java 版。", "登录 Microsoft")
        };

        AccountItems.Children.Clear();
        if (kind == AccountKind.Bedrock)
        {
            foreach (var entry in ModBedrockAccounts.All)
                AccountItems.Children.Add(CreateBedrockRow(entry));
            var active = ModBedrockAccounts.Active;
            SetCurrent(active?.Gamertag, active is null ? "基岩版 · 未登录" : "基岩版 · Xbox", SymbioAvatars.Bedrock(active?.Gamertag));
            EmptyText.Text = "还没有基岩版账号。登录后，基岩版 GDK 启动会使用这个 Xbox 身份。";
        }
        else
        {
            ModProfile.GetProfile();
            var type = LoginTypeOf(kind);
            foreach (var profile in ModProfile.profileList.Where(x => x.Type == type))
                AccountItems.Children.Add(CreateJavaRow(profile));
            var current = ModProfile.selectedProfile;
            SetCurrent(current?.Username, current is null ? "Java 版 · 未登录" : ModProfile.GetProfileInfo(current)?.ToString() ?? "Java 版",
                SymbioAvatars.Java(current));
            EmptyText.Text = kind switch
            {
                AccountKind.Offline => "还没有离线账号。",
                AccountKind.Auth => "还没有外置账号。",
                _ => "还没有正版账号。"
            };
        }

        EmptyText.Visibility = AccountItems.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (animate && IsLoaded) StaggeredEntrance.Play(AccountItems.Children.OfType<UIElement>());
    }

    private void SetCurrent(string? name, string info, ImageSource? avatar)
    {
        CurrentName.Text = string.IsNullOrWhiteSpace(name) ? "未登录" : name;
        CurrentInfo.Text = info;
        CurrentAvatar.Source = avatar;
        CurrentGlyph.Text = string.IsNullOrWhiteSpace(name) ? "?" : name[..1].ToUpperInvariant();
        CurrentGlyph.Visibility = avatar is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border CreateJavaRow(ModProfile.McProfile profile)
    {
        var isActive = ReferenceEquals(profile, ModProfile.selectedProfile);
        var info = ModProfile.GetProfileInfo(profile)?.ToString() ?? "Java 版";
        var row = CreateRow(profile.Username, info, SymbioAvatars.Java(profile), isActive,
            () =>
            {
                ModProfile.selectedProfile = profile;
                ModProfile.SaveProfile();
                ProfileSkins.LoadSelected();
                Refresh(false);
                AccountsChanged?.Invoke();
            },
            () =>
            {
                if (ModMain.MyMsgBox($"确定要移除账号 {profile.Username} 吗？", "移除账号", "移除", "取消",
                        isWarn: true, forceWait: true) == 2)
                    return;
                ModProfile.RemoveProfile(profile);
                Refresh(false);
                AccountsChanged?.Invoke();
            });
        var menu = new ContextMenu();
        void Add(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("复制 UUID", () => ModBase.ClipboardSet(profile.Uuid));
        if (profile.Type == ModLaunch.McLoginType.Legacy)
            Add("修改离线 UUID", () => { ModProfile.EditOfflineUuid(profile); RefreshAccounts(); AccountsChanged?.Invoke(); });
        if (profile.Type == ModLaunch.McLoginType.Auth)
            Add("修改服务器名称", () =>
            {
                var name = ModMain.MyMsgBoxInput(Lang.Text("Launch.Account.Profile.EditServerName.Title"),
                    Lang.Text("Launch.Account.Profile.EditServerName.Message"), profile.ServerName);
                if (name is not null) { ModProfile.EditAuthServerName(profile, name); RefreshAccounts(); AccountsChanged?.Invoke(); }
            });
        if (isActive)
        {
            Add("刷新头像", ProfileSkins.RefreshSelected);
            if (profile.Type != ModLaunch.McLoginType.Legacy)
            {
                Add("修改玩家名", ModProfile.EditProfileId);
                Add("修改密码", () => ModBase.OpenWebsite(profile.Type == ModLaunch.McLoginType.Ms
                    ? "https://account.live.com/password/Change"
                    : profile.Server.TrimEnd('/').Replace("/api/yggdrasil/authserver", "/user/profile")));
            }
        }
        row.ContextMenu = menu;
        return row;
    }

    private Border CreateBedrockRow(ModBedrockAccounts.Entry entry)
    {
        var isActive = ModBedrockAccounts.IsActive(entry);
        Border? row = null;
        row = CreateRow(entry.Gamertag, $"XUID {entry.Xuid}", SymbioAvatars.Bedrock(entry.Gamertag), isActive,
            async () =>
            {
                // Switching renews the session right away so the next launch can use it directly.
                if (row?.Tag is Button use)
                {
                    use.IsEnabled = false;
                    use.Content = "切换中…";
                }
                try
                {
                    await Task.Run(() => ModBedrockAccounts.SetActiveAsync(entry.Xuid));
                    HintService.Hint($"已切换到基岩版账号：{entry.Gamertag}", HintType.Success);
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "切换基岩版账号失败", ModBase.LogLevel.Hint, userSummary: $"切换基岩版账号失败：{ex.Message}");
                    HintService.Hint($"切换基岩版账号失败：{ex.Message}", HintType.Error);
                }
                Refresh(false);
                AccountsChanged?.Invoke();
            },
            () =>
            {
                if (ModMain.MyMsgBox($"确定要移除基岩版账号 {entry.Gamertag} 吗？之后可以重新登录添加。",
                        "移除基岩版账号", "移除", "取消", isWarn: true, forceWait: true) == 2)
                    return;
                ModBedrockAccounts.Remove(entry.Xuid);
                HintService.Hint($"已移除基岩版账号：{entry.Gamertag}");
                Refresh(false);
                AccountsChanged?.Invoke();
            });
        return row;
    }

    /// <summary>A Fluent settings-card row: avatar, name and detail, then the use and remove actions.</summary>
    private Border CreateRow(string name, string detail, ImageSource? avatar, bool isActive, Action use, Action remove)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 10, 10, 10),
            Margin = new Thickness(0, 0, 0, 4),
            BorderThickness = new Thickness(1),
            MinHeight = 64
        };
        row.SetResourceReference(Border.BackgroundProperty, "SyCardFill");
        row.SetResourceReference(Border.BorderBrushProperty, "SyCardStroke");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(CreateAvatar(name, avatar, 36));

        var text = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var info = new TextBlock { Text = detail, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        info.SetResourceReference(TextBlock.ForegroundProperty, "SyTextSecondary");
        text.Children.Add(info);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        FrameworkElement action;
        if (isActive)
        {
            var badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 5), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BackgroundProperty, "SySubtleHover");
            var badgeText = new TextBlock { Text = "使用中", FontSize = 12, FontWeight = FontWeights.SemiBold };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "SyAccentText");
            badge.Child = badgeText;
            action = badge;
        }
        else
        {
            var useButton = new Button { Content = "使用", MinWidth = 72, VerticalAlignment = VerticalAlignment.Center };
            useButton.SetResourceReference(StyleProperty, "SyButton");
            useButton.Click += (_, _) => use();
            row.Tag = useButton;
            action = useButton;
        }
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);

        var delete = new Button { Margin = new Thickness(6, 0, 0, 0), ToolTip = $"移除 {name}", VerticalAlignment = VerticalAlignment.Center };
        delete.SetResourceReference(StyleProperty, "SyIconButton");
        var icon = new SvgIconControl { Icon = "lucide/trash-2" };
        icon.SetResourceReference(StyleProperty, "SyIcon");
        delete.Content = icon;
        delete.Click += (_, _) => remove();
        Grid.SetColumn(delete, 3);
        grid.Children.Add(delete);

        row.Child = grid;
        return row;
    }

    private static FrameworkElement CreateAvatar(string name, ImageSource? avatar, double size)
    {
        var host = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var disc = new System.Windows.Shapes.Ellipse();
        disc.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SyControl");
        host.Children.Add(disc);
        if (avatar is null)
        {
            var glyph = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(name) ? "?" : name[..1].ToUpperInvariant(),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "SyTextSecondary");
            host.Children.Add(glyph);
        }
        else
        {
            var image = new Image
            {
                Source = avatar,
                Stretch = Stretch.UniformToFill,
                Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2)
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            host.Children.Add(image);
        }
        return host;
    }

    #endregion

    #region Login

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (kind == AccountKind.Bedrock)
        {
            BedrockLoginRequested?.Invoke();
            return;
        }
        ShowLogin(LoginTypeOf(kind));
    }

    private void ShowLogin(ModLaunch.McLoginType type)
    {
        loginOpen = true;
        LoginTitle.Text = type switch
        {
            ModLaunch.McLoginType.Legacy => "创建离线档案",
            ModLaunch.McLoginType.Auth => "登录外置账号",
            _ => "登录 Microsoft 账号"
        };
        LoginDetail.Text = type switch
        {
            ModLaunch.McLoginType.Legacy => "输入玩家名，建议只使用英文、数字和下划线。",
            ModLaunch.McLoginType.Auth => "选择或填写验证服务器，再输入账号和密码；支持网页登录的服务器会显示网页登录按钮。",
            _ => "将在浏览器中完成 Microsoft 验证，登录凭据由原有认证服务管理。"
        };
        ++loginRevision;
        BtnLoginStart.IsEnabled = !signingIn;
        LoginInput.Text = CustomUuid.Text = "";
        UuidMode.SelectedIndex = 0;
        var offline = type == ModLaunch.McLoginType.Legacy;
        LoginInput.Visibility = OfflineOptions.Visibility = offline ? Visibility.Visible : Visibility.Collapsed;
        AuthName.Clear();
        AuthPassword.Clear();
        ServerAddress.Clear();
        ServerName.Text = "";
        discoveredServer = null;
        WebSignIn.Visibility = Visibility.Collapsed;
        AuthForm.Visibility = type == ModLaunch.McLoginType.Auth ? Visibility.Visible : Visibility.Collapsed;
        UpdateRegistration();

        LoginHost.Visibility = Visibility.Visible;
        StaggeredEntrance.Play([LoginHost]);
        if (LoginInput.Visibility == Visibility.Visible)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => LoginInput.Focus()));
    }

    private void HideLogin()
    {
        loginOpen = false;
        serverProbe?.Cancel();
        ++loginRevision;
        AuthPassword.Clear();
        LoginHost.Visibility = Visibility.Collapsed;
    }

    private void UuidMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CustomUuid is not null) CustomUuid.Visibility = UuidMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LittleSkin_Click(object sender, RoutedEventArgs e) => ServerAddress.Text = "https://littleskin.cn/api/yggdrasil";

    private async void Server_Changed(object sender, TextChangedEventArgs e)
    {
        serverProbe?.Cancel();
        resolvedServer = null;
        discoveredServer = null;
        if (WebSignIn is not null) WebSignIn.Visibility = Visibility.Collapsed;
        if (ServerName is not null) ServerName.Text = "";
        var input = ServerAddress.Text.Trim();
        if (!loginOpen || !Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http")) return;
        var revision = loginRevision;
        using var probe = new CancellationTokenSource();
        serverProbe = probe;
        bool Current() => !probe.IsCancellationRequested && revision == loginRevision && ServerAddress.Text.Trim() == input;
        try
        {
            await Task.Delay(250, probe.Token);
            try
            {
                var resolved = await AccountSignIn.ResolveServerAsync(input);
                if (!Current()) return;
                resolvedServer = resolved.Root;
                ServerName.Text = resolved.Name;
            }
            catch (Exception ex) when (!probe.IsCancellationRequested)
            {
                ModBase.Log(ex, "获取验证服务器名称失败", ModBase.LogLevel.Debug);
            }
            if (!Current()) return;
            var info = await ModYggdrasilConnect.DiscoverAsync(resolvedServer ?? input, probe.Token);
            if (!Current()) return;
            discoveredServer = info;
            WebSignIn.Visibility = info is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) when (probe.IsCancellationRequested) { }
        catch (Exception ex) { ModBase.Log(ex, "探测网页登录支持失败", ModBase.LogLevel.Debug); }
        finally { if (ReferenceEquals(serverProbe, probe)) serverProbe = null; }
    }

    private void UpdateRegistration()
    {
        if (Registration is null) return;
        var address = Config.InstanceAuth.AuthRegisterAddress.ToString();
        Registration.Visibility = Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? Visibility.Visible : Visibility.Collapsed;
        Registration.Content = Lang.Text(string.IsNullOrEmpty(AuthName.Text) ? "Launch.Account.Auth.Register" : "Launch.Account.Auth.ForgotPassword");
    }

    private void AuthName_Changed(object sender, TextChangedEventArgs e) => UpdateRegistration();

    private void Registration_Click(object sender, RoutedEventArgs e)
    {
        var url = Config.InstanceAuth.AuthRegisterAddress.ToString();
        ModBase.OpenWebsite(string.IsNullOrEmpty(AuthName.Text) ? url : url.Replace("/auth/register", "/auth/forgot"));
    }

    private async void WebSignIn_Click(object sender, RoutedEventArgs e) => await SignInAsync(true);
    private async void LoginStart_Click(object sender, RoutedEventArgs e) => await SignInAsync(false);

    private async Task SignInAsync(bool browser)
    {
        if (!loginOpen || signingIn) return;
        var revision = loginRevision;
        if (!AccountSignIn.TryBegin())
        {
            HintService.Hint("请等待当前启动或登录完成。");
            return;
        }
        signingIn = true;
        try
        {
            AccountsChanged?.Invoke();
            BtnLoginStart.IsEnabled = WebSignIn.IsEnabled = false;
            switch (kind)
            {
                case AccountKind.Microsoft:
                    SetLoginStatus("正在登录 Microsoft 账号", "请在浏览器中完成验证。", false);
                    await NewMicrosoftLogin.LoginAsync();
                    break;
                case AccountKind.Offline:
                    var name = LoginInputText;
                    if (name.Length == 0) throw new ArgumentException("请输入玩家名");
                    if (!AccountSignIn.ValidPlayerName(name) && ModMain.MyMsgBox(
                        Lang.Text("Launch.Account.Offline.InvalidPlayerId.Message"), Lang.Text("Launch.Account.Offline.InvalidPlayerId.Title"),
                        Lang.Text("Common.Action.Continue"), Lang.Text("Common.Action.Cancel"), isWarn: true, forceWait: true) == 2) return;
                    AccountSignIn.CreateOffline(name, (OfflineUuidMode)UuidMode.SelectedIndex, CustomUuid.Text.Trim());
                    break;
                case AccountKind.Auth when browser && discoveredServer is { } info:
                    await AccountSignIn.LoginWithBrowserAsync(info, code => ModBase.RunInUi(() =>
                    {
                        var url = string.IsNullOrWhiteSpace(code.VerificationUriComplete) ? code.VerificationUri : code.VerificationUriComplete;
                        ModBase.ClipboardSet(code.UserCode ?? "");
                        if (!string.IsNullOrWhiteSpace(url)) ModBase.OpenWebsite(url);
                        ModMain.MyMsgBox(Lang.Text("Launch.Account.Auth.WebLogin.Instructions", code.UserCode ?? "", url),
                            Lang.Text("Launch.Account.Auth.WebLogin.Title"));
                    }));
                    break;
                case AccountKind.Auth when !browser:
                    var progress = new Progress<double>(p => { if (signingIn && revision == loginRevision) LoginDetail.Text = Lang.Number(p, "P0"); });
                    await AccountSignIn.LoginWithPasswordAsync(resolvedServer ?? ServerAddress.Text.Trim(), AuthName.Text.Trim(), AuthPassword.Password, progress);
                    break;
                default: return;
            }
            if (revision == loginRevision) CompleteLogin();
            else AccountsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            if (revision == loginRevision) SetLoginStatus("登录未完成", ex.Message.TrimStart('$'), true);
            ModBase.Log(ex, "账号登录未完成", ModBase.LogLevel.Debug);
        }
        finally
        {
            signingIn = false;
            AccountSignIn.End();
            AccountsChanged?.Invoke();
            BtnLoginStart.IsEnabled = WebSignIn.IsEnabled = true;
        }
    }

    private void LoginInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        LoginStart_Click(sender, e);
        e.Handled = true;
    }

    private void LoginBack_Click(object sender, RoutedEventArgs e) => HideLogin();

    /// <summary>Called by the host once a login flow has saved a new account.</summary>
    public void CompleteLogin()
    {
        HideLogin();
        ProfileSkins.LoadSelected();
        Refresh(true);
        AccountsChanged?.Invoke();
    }

    #endregion

    private void TypeButton_Click(object sender, RoutedEventArgs e)
    {
        var next = sender == BtnOffline ? AccountKind.Offline
            : sender == BtnAuth ? AccountKind.Auth
            : sender == BtnBedrock ? AccountKind.Bedrock
            : AccountKind.Microsoft;
        if (next == kind) return;
        HideLogin();
        kind = next;
        Refresh(true);
    }

    private void Skin_Click(object sender, RoutedEventArgs e) => SkinLibraryRequested?.Invoke();
}
