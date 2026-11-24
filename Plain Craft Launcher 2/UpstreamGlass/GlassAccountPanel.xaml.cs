using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PCL.Core.App;

namespace PCL;

public partial class GlassAccountPanel
{
    /// <summary>Raised when the account surface needs to hand a login flow back to the launcher.</summary>
    public event Action<ModLaunch.McLoginType>? LoginRequested;

    /// <summary>Raised for the Xbox device-code login used by the Bedrock account type.</summary>
    public event Action? BedrockLoginRequested;

    public string LoginInputText => LoginInput.Text.Trim();

    public void RefreshAccounts() => Refresh(true);

    public void SetLoginStatus(string title, string detail, bool canRetry)
    {
        LoginTitle.Text = title;
        LoginDetail.Text = detail;
        BtnLoginStart.IsEnabled = canRetry;
    }

    private ModLaunch.McLoginType selectedType = ModLaunch.McLoginType.Ms;
    private bool bedrockMode;
    private bool loginMode;
    private ModLaunch.McLoginType loginType;
    private int page;
    private const int PageSize = 2;

    // 账号卡与「添加账号」卡必须共用同一套尺寸参数，否则两类卡片并排时看起来大小不一。
    private const double CardGap = 12;
    private const double CardPadding = 16;
    private const double CardGlyphSize = 70; // 与左侧「当前账号」的 70x70 头像框保持一致
    private const double CardCornerRadius = 14;
    private const double CardMinWidth = 168;
    private const double CardMaxWidth = 220;

    /// <summary>
    ///     按右侧账号区实际可用宽度计算卡片宽度，让所有卡片严格等宽。
    /// </summary>
    private double ResolveCardWidth()
    {
        var host = DeckHost.ActualWidth;
        if (host <= 1d) host = Math.Max(0d, ActualWidth - 303d); // 24 边距 + 240 左栏 + 1 分隔线 + 14 间距
        if (host <= 1d) return CardMaxWidth;
        return Math.Clamp((host - CardGap * PageSize) / PageSize, CardMinWidth, CardMaxWidth);
    }

    public GlassAccountPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Refresh(false);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => AnimateCards(true)));
        };
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && !loginMode) Refresh(false);
        };
    }

    private void TypeButton_Click(object sender, RoutedEventArgs e)
    {
        HideLogin();
        bedrockMode = ((Button)sender).Tag?.ToString() == "Bedrock";
        selectedType = ((Button)sender).Tag?.ToString() switch
        {
            "Legacy" => ModLaunch.McLoginType.Legacy,
            "Auth" => ModLaunch.McLoginType.Auth,
            _ => ModLaunch.McLoginType.Ms
        };
        page = 0;
        Refresh(true);
    }

    private void Refresh(bool animate)
    {
        ModProfile.GetProfile();
        if (bedrockMode)
        {
            var bedrockAccounts = ModBedrockAccounts.All;
            DeckBackCard.Visibility = bedrockAccounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AccountItems.Items.Clear();
            foreach (var entry in bedrockAccounts)
                AccountItems.Items.Add(CreateBedrockAccountCard(entry));
            AccountItems.Items.Add(CreateBedrockAddCard());
            PageText.Text = "1 / 1";
            BtnPrevious.IsEnabled = false;
            BtnNext.IsEnabled = false;
            // 面板显示的是「已保存并选中的账号」，而不是内存会话：
            // 会话要等真正启动游戏时才续期，否则重启后会显示成未登录。
            var activeBedrock = ModBedrockAccounts.Active;
            CurrentName.Text = activeBedrock?.Gamertag ?? "未登录";
            CurrentInfo.Text = activeBedrock is null
                ? "基岩版账号 · 未登录"
                : $"基岩版账号 · XUID {activeBedrock.Xuid}";
            CurrentGlyph.Text = activeBedrock?.Gamertag is { Length: > 0 } name ? name[..1].ToUpperInvariant() : "?";
            SetTypeOpacity();
            AnimateCards(animate);
            return;
        }
        var profiles = ModProfile.profileList.Where(x => x.Type == selectedType).ToList();
        DeckBackCard.Visibility = Visibility.Visible;
        var totalPages = Math.Max(1, (profiles.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, totalPages - 1);
        AccountItems.Items.Clear();
        foreach (var profile in profiles.Skip(page * PageSize).Take(PageSize))
            AccountItems.Items.Add(CreateCard(profile));
        if (AccountItems.Items.Count == 0)
            AccountItems.Items.Add(CreateLoginCard(selectedType));
        else if (page == totalPages - 1)
            AccountItems.Items.Add(CreateAddCard(selectedType));
        PageText.Text = $"{page + 1} / {totalPages}";
        BtnPrevious.IsEnabled = page > 0;
        BtnNext.IsEnabled = page + 1 < totalPages;
        var current = ModProfile.selectedProfile;
        CurrentName.Text = current?.Username ?? "未登录";
        CurrentInfo.Text = current is null ? "正版账号 · 未登录" : ModProfile.GetProfileInfo(current)?.ToString() ?? "已选择账号";
        CurrentGlyph.Text = string.IsNullOrWhiteSpace(current?.Username) ? "?" : current.Username[..1].ToUpperInvariant();
        BtnMicrosoft.Opacity = selectedType == ModLaunch.McLoginType.Ms ? 1 : 0.72;
        BtnOffline.Opacity = selectedType == ModLaunch.McLoginType.Legacy ? 1 : 0.72;
        SetTypeOpacity();
        AnimateCards(animate);
    }

    private void SetTypeOpacity()
    {
        SetTypeButton(BtnMicrosoft, !bedrockMode && selectedType == ModLaunch.McLoginType.Ms);
        SetTypeButton(BtnOffline, !bedrockMode && selectedType == ModLaunch.McLoginType.Legacy);
        SetTypeButton(BtnAuth, !bedrockMode && selectedType == ModLaunch.McLoginType.Auth);
        SetTypeButton(BtnBedrock, bedrockMode);
    }

    private void SetTypeButton(Button button, bool selected)
    {
        button.Opacity = 1;
        button.Background = selected ? Brushes.White : (Brush)FindResource("GlassControlBrush");
        button.Foreground = selected ? Brushes.Black : (Brush)FindResource("GlassTextBrush");
    }

    private FrameworkElement CreateLoginCard(ModLaunch.McLoginType type)
    {
        var card = CreateActionCard(
            type switch
            {
                ModLaunch.McLoginType.Legacy => "添加离线账号",
                ModLaunch.McLoginType.Auth => "添加外置账号",
                _ => "添加正版账号"
            },
            type switch
            {
                ModLaunch.McLoginType.Legacy => "输入游戏用户名，建议只使用英文、数字和下划线。",
                ModLaunch.McLoginType.Auth => "连接外置验证服务器，登录后账号会出现在这里。",
                _ => "使用 Microsoft 账号登录，登录完成后会出现在这里。"
            },
            type switch
            {
                ModLaunch.McLoginType.Legacy => "登录",
                ModLaunch.McLoginType.Auth => "登录外置",
                _ => "登录 Microsoft"
            });
        ((Button)((Grid)card.Child).Children[2]).Click += (_, _) => ShowLogin(type);
        return card;
    }

    private FrameworkElement CreateAddCard(ModLaunch.McLoginType type)
    {
        var card = CreateActionCard("添加账号", "继续添加一个" + (type == ModLaunch.McLoginType.Legacy ? "离线" : type == ModLaunch.McLoginType.Auth ? "外置" : "正版") + "账号", "添加");
        ((Button)((Grid)card.Child).Children[2]).Click += (_, _) => ShowLogin(type);
        return card;
    }

    private void ShowLogin(ModLaunch.McLoginType type)
    {
        loginMode = true;
        loginType = type;
        LoginTitle.Text = type switch
        {
            ModLaunch.McLoginType.Legacy => "创建离线档案",
            ModLaunch.McLoginType.Auth => "登录外置账号",
            _ => "登录微软账号"
        };
        LoginDetail.Text = type switch
        {
            ModLaunch.McLoginType.Legacy => "使用玩家名创建本地档案，可直接离线启动游戏。",
            ModLaunch.McLoginType.Auth => "连接外置验证服务器，登录后账号会显示在当前账号列表。",
            _ => "在浏览器中完成 Microsoft 验证，登录凭据由原有认证服务管理。"
        };
        LoginInput.Visibility = type == ModLaunch.McLoginType.Ms ? Visibility.Collapsed : Visibility.Visible;
        LoginInput.Text = string.Empty;
        LoginInput.ToolTip = type == ModLaunch.McLoginType.Auth ? "玩家名或邮箱" : "玩家名";
        AccountListScroll.Visibility = Visibility.Collapsed;
        LoginHost.Visibility = Visibility.Visible;
        AccountListScroll.ScrollToHome();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(AccountListScroll.ScrollToHome));
        BtnPrevious.Visibility = Visibility.Collapsed;
        BtnNext.Visibility = Visibility.Collapsed;
        PageText.Visibility = Visibility.Collapsed;
        AnimateElement(LoginHost, 16);
    }

    private void HideLogin()
    {
        loginMode = false;
        LoginInput.Visibility = Visibility.Collapsed;
        LoginHost.Visibility = Visibility.Collapsed;
        AccountListScroll.Visibility = Visibility.Visible;
        AccountListScroll.ScrollToHome();
        BtnPrevious.Visibility = Visibility.Visible;
        BtnNext.Visibility = Visibility.Visible;
        PageText.Visibility = Visibility.Visible;
        AnimateCards(true);
    }

    private void LoginStart_Click(object sender, RoutedEventArgs e)
    {
        if (loginMode) LoginRequested?.Invoke(loginType);
    }

    private void LoginBack_Click(object sender, RoutedEventArgs e) => HideLogin();

    public void PlayPageTransition()
    {
        if (IsLoaded)
        {
            AnimateElement(this, 18);
            return;
        }

        // 账号页是把本面板从账户浮层重新挂到页面宿主里生成的，此刻元素刚被重新挂载，
        // IsLoaded 仍为 false。原来在这里直接 return，导致入场动效永远不播放；改为等 Loaded 后再播。
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            AnimateElement(this, 18);
        }

        Loaded += OnLoaded;
    }

    private Border CreateActionCard(string title, string detail, string action)
    {
        var cardWidth = ResolveCardWidth();
        var card = new Border
        {
            Width = cardWidth,
            Height = cardWidth * 1.25,
            Margin = new Thickness(0, 0, CardGap, 0),
            Padding = new Thickness(CardPadding),
            CornerRadius = new CornerRadius(CardCornerRadius),
            Background = (System.Windows.Media.Brush)FindResource("GlassMainBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("GlassOutlineBrush"),
            BorderThickness = new Thickness(1)
        };
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var glyph = new Border
        {
            Width = CardGlyphSize,
            Height = CardGlyphSize,
            CornerRadius = new CornerRadius(CardCornerRadius),
            Background = Brushes.Transparent,
            BorderBrush = (System.Windows.Media.Brush)FindResource("GlassOutlineBrush"),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 26,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("GlassTextBrush")
            }
        };
        body.Children.Add(glyph);
        var text = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("GlassTextBrush") });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("GlassSecondaryTextBrush") });
        Grid.SetRow(text, 1);
        body.Children.Add(text);
        var button = new Button { Content = action, Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), Style = (Style)FindResource("GlassPillButton") };
        Grid.SetRow(button, 2);
        body.Children.Add(button);
        card.Child = body;
        return card;
    }

    private FrameworkElement CreateBedrockAccountCard(ModBedrockAccounts.Entry entry)
    {
        var isActive = ModBedrockAccounts.IsActive(entry);
        var cardWidth = ResolveCardWidth();
        var card = new Border
        {
            Width = cardWidth, Height = cardWidth * 1.25, Margin = new Thickness(0, 0, CardGap, 0), Padding = new Thickness(CardPadding),
            CornerRadius = new CornerRadius(CardCornerRadius), Background = (System.Windows.Media.Brush)FindResource("GlassMainBrush"),
            BorderThickness = new Thickness(isActive ? 1.6 : 1)
        };
        // 选中的账号用玻璃主题的强调色描边，和未选中的区分开
        if (isActive) card.SetResourceReference(Border.BorderBrushProperty, "GlassAccentBrush");
        else card.SetResourceReference(Border.BorderBrushProperty, "GlassOutlineBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        root.RowDefinitions.Add(new RowDefinition());
        var delete = new Button
        {
            Content = "♜", Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)FindResource("GlassCircleButton"), Tag = entry,
            ToolTip = $"删除账号 {entry.Gamertag}"
        };
        delete.Click += DeleteBedrock_Click;
        root.Children.Add(delete);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var glyph = new Border { Width = CardGlyphSize, Height = CardGlyphSize, CornerRadius = new CornerRadius(CardCornerRadius), Background = (System.Windows.Media.Brush)FindResource("GlassControlBrush"), HorizontalAlignment = HorizontalAlignment.Center };
        glyph.Child = new TextBlock { Text = entry.Gamertag is { Length: > 0 } name ? name[..1].ToUpperInvariant() : "?", FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("GlassTextBrush") };
        body.Children.Add(glyph);
        body.Children.Add(new TextBlock { Text = entry.Gamertag, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("GlassTextBrush") });
        body.Children.Add(new TextBlock
        {
            Text = isActive ? $"使用中 · XUID {entry.Xuid}" : "基岩版 Xbox 账号",
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("GlassSecondaryTextBrush")
        });
        var use = new Button
        {
            Content = isActive ? "使用中" : "使用此账号",
            Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0), Style = (Style)FindResource("GlassPillButton"),
            Tag = entry, IsEnabled = !isActive
        };
        use.Click += UseBedrock_Click;
        body.Children.Add(use);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        card.Child = root;
        return card;
    }

    /// <summary>切换基岩版账号：立即续期一次，确保切过去就能直接启动游戏。</summary>
    private async void UseBedrock_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not ModBedrockAccounts.Entry entry) return;
        var button = (Button)sender;
        button.IsEnabled = false;
        var original = button.Content;
        button.Content = "切换中…";
        try
        {
            await Task.Run(() => ModBedrockAccounts.SetActiveAsync(entry.Xuid));
            ModBase.Log($"[Bedrock] 已切换到账号 {entry.Gamertag}", ModBase.LogLevel.Normal);
            HintService.Hint($"已切换到基岩版账号：{entry.Gamertag}", HintType.Success);
            Refresh(true);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "切换基岩版账号失败", ModBase.LogLevel.Hint,
                userSummary: $"切换基岩版账号失败：{ex.Message}");
            HintService.Hint($"切换基岩版账号失败：{ex.Message}", HintType.Error);
            button.Content = original;
            button.IsEnabled = true;
        }
    }

    private void DeleteBedrock_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not ModBedrockAccounts.Entry entry) return;
        if (ModMain.MyMsgBox($"确定要移除基岩版账号 {entry.Gamertag} 吗？之后可以重新登录添加。",
                "移除基岩版账号", "移除", "取消", isWarn: true, forceWait: true) == 2)
            return;
        ModBedrockAccounts.Remove(entry.Xuid);
        HintService.Hint($"已移除基岩版账号：{entry.Gamertag}");
        Refresh(false);
    }

    private FrameworkElement CreateBedrockAddCard()
    {
        var card = CreateActionCard(
            "添加基岩账号",
            "使用 Microsoft 账号登录基岩版，登录完成后会显示在这里。",
            "登录基岩版");
        ((Button)((Grid)card.Child).Children[2]).Click += BedrockLogin_Click;
        return card;
    }

    private void AnimateCards(bool animate)
    {
        if (!animate) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            // IsLoaded 要在布局完成后再判断：Refresh 可能在面板刚被重新挂载时调用。
            if (!IsLoaded) return;
            var cards = AccountItems.Items.OfType<FrameworkElement>().ToList();
            for (var index = 0; index < cards.Count; index++)
            {
                var card = cards[index];
                var transform = new TranslateTransform(0, 16);
                card.RenderTransform = transform;
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                card.Opacity = 0;
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                var delay = TimeSpan.FromMilliseconds(index * 65);
                card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
                {
                    BeginTime = delay,
                    EasingFunction = easing
                });
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(320))
                {
                    BeginTime = delay,
                    EasingFunction = easing
                });
            }
        }));
    }

    private void AnimateElement(UIElement element, double offset)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            // 同样把 IsLoaded 判断放到布局之后，否则刚挂载的页面会被直接跳过。
            if (!IsLoaded) return;
            // 基值保持最终状态，动画用 FillBehavior.Stop 播放，避免动画被移除后元素停留在透明状态。
            element.Opacity = 1d;
            var transform = new TranslateTransform(0, 0);
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(340))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
        }));
    }

    private void BedrockLogin_Click(object sender, RoutedEventArgs e)
    {
        BedrockLoginRequested?.Invoke();
    }

    private FrameworkElement CreateCard(ModProfile.McProfile profile)
    {
        var cardWidth = ResolveCardWidth();
        var card = new Border
        {
            Width = cardWidth,
            Height = cardWidth * 1.25,
            Margin = new Thickness(0, 0, CardGap, 0),
            Padding = new Thickness(CardPadding),
            CornerRadius = new CornerRadius(CardCornerRadius),
            Background = (System.Windows.Media.Brush)FindResource("GlassMainBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("GlassOutlineBrush"),
            BorderThickness = new Thickness(1)
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        root.RowDefinitions.Add(new RowDefinition());
        var delete = new Button { Content = "♜", Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)FindResource("GlassCircleButton"), Tag = profile };
        delete.Click += Delete_Click;
        root.Children.Add(delete);
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var glyph = new Border { Width = CardGlyphSize, Height = CardGlyphSize, CornerRadius = new CornerRadius(CardCornerRadius), Background = (System.Windows.Media.Brush)FindResource("GlassControlBrush"), HorizontalAlignment = HorizontalAlignment.Center };
        glyph.Child = new TextBlock { Text = string.IsNullOrWhiteSpace(profile.Username) ? "?" : profile.Username[..1].ToUpperInvariant(), FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("GlassTextBrush") };
        body.Children.Add(glyph);
        body.Children.Add(new TextBlock { Text = profile.Username, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("GlassTextBrush") });
        body.Children.Add(new TextBlock { Text = ModProfile.GetProfileInfo(profile)?.ToString() ?? "游戏账号", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("GlassSecondaryTextBrush") });
        var use = new Button { Content = "使用此账号", Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0), Style = (Style)FindResource("GlassPillButton"), Tag = profile };
        use.Click += Use_Click;
        body.Children.Add(use);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        card.Child = root;
        return card;
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        ModProfile.selectedProfile = (ModProfile.McProfile)((Button)sender).Tag;
        ModProfile.SaveProfile();
        Refresh(true);
        ModMain.frmLaunchLeft?.RefreshPage(true);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        ModProfile.RemoveProfile((ModProfile.McProfile)((Button)sender).Tag);
        Refresh(true);
    }

    private void Current_Click(object sender, RoutedEventArgs e)
    {
        if (bedrockMode)
            BedrockLoginRequested?.Invoke();
        else
            ShowLogin(selectedType);
    }
    private void Skin_Click(object sender, RoutedEventArgs e) => ModMain.frmLaunchLeft?.RefreshPage(true);
    private void Previous_Click(object sender, RoutedEventArgs e) { page--; Refresh(true); }
    private void Next_Click(object sender, RoutedEventArgs e) { page++; Refresh(true); }
}
