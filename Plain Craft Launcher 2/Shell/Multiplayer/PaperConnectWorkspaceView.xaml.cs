using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.Multiplayer;
using PaperConnect.Core.Entry;

namespace PCL;

/// <summary>
///     基岩版联机页：PaperConnect 协议（联机码 + EasyTier 组网）。与 Java 版的陶瓦联机并列，
///     在基岩版模式下进入「联机」时显示。
/// </summary>
public partial class PaperConnectWorkspaceView
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool busy;
    private bool runtimeFound = true;

    public PaperConnectWorkspaceView()
    {
        InitializeComponent();
        ModPaperConnect.Changed += OnChanged;
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            runtimeFound = ModPaperConnect.LocateEasyTier();
            TextPeers.Text = Config.Preference.PaperConnectPeers ?? string.Empty;
            CheckTun.Checked = Config.Preference.PaperConnectTunMode;
            RefreshServers();
            RefreshFriends();
            timer.Start();
            Refresh();
        };
        Unloaded += (_, _) => timer.Stop();
    }

    /// <summary>增强模式（TUN）开关：只响应用户操作，避免加载配置时反复写盘。</summary>
    private void Tun_Change(object sender, bool user)
    {
        if (!user) return;
        Config.Preference.PaperConnectTunMode = CheckTun.Checked == true;
        Refresh();
    }

    private void OnChanged() => ModBase.RunInUi(Refresh);

    /// <summary>房主 / 房客在房间里的名字：优先用基岩版 Xbox 玩家代号。</summary>
    private static string PlayerName =>
        ModBedrockAccounts.Active?.Gamertag is { Length: > 0 } gamertag ? gamertag : "Steve";

    private int ReadGamePort() =>
        int.TryParse(TextPort.Text.Trim(), out var port) && port > 1024 && port <= 65535
            ? port
            : ModPaperConnect.DefaultGamePort;

    /// <summary>共享节点地址随用随存，下次打开页面还在。</summary>
    private void SavePeers() => Config.Preference.PaperConnectPeers = (TextPeers.Text ?? string.Empty).Trim();

    private async void BtnHost_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        busy = true;
        Refresh();
        try
        {
            SavePeers();
            var code = await ModPaperConnect.HostAsync(PlayerName, ReadGamePort());
            HintService.Hint($"房间已创建，联机码：{code}", HintType.Success);
        }
        catch (Exception ex)
        {
            HintService.Hint($"创建房间失败：{ex.Message}", HintType.Error);
            runtimeFound = ModPaperConnect.LocateEasyTier();
        }
        finally
        {
            busy = false;
            Refresh();
        }
    }

    private async void BtnJoin_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var code = ModPaperConnect.Normalize(TextRoom.Text ?? string.Empty);
        if (!ModPaperConnect.IsValidRoomCode(code))
        {
            HintService.Hint("联机码格式不正确，应形如 P/NNNN-NNNN-SSSS-SSSS", HintType.Info);
            return;
        }

        busy = true;
        Refresh();
        try
        {
            SavePeers();
            await ModPaperConnect.JoinAsync(code, PlayerName, ReadGamePort());
            HintService.Hint("已加入房间，打开基岩版即可加入游戏", HintType.Success);
        }
        catch (Exception ex)
        {
            HintService.Hint($"加入房间失败：{ex.Message}", HintType.Error);
            runtimeFound = ModPaperConnect.LocateEasyTier();
        }
        finally
        {
            busy = false;
            Refresh();
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        ModPaperConnect.Stop();
        HintService.Hint("已断开联机");
        Refresh();
    }

    private void BtnCopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ModPaperConnect.RoomCode)) return;
        ModBase.ClipboardSet(ModPaperConnect.RoomCode);
        HintService.Hint($"已复制联机码：{ModPaperConnect.RoomCode}", HintType.Success);
    }

    private void BtnCopyAddress_Click(object sender, RoutedEventArgs e)
    {
        var address = ModPaperConnect.PlayAddress;
        if (string.IsNullOrEmpty(address)) return;
        ModBase.ClipboardSet(address);
        HintService.Hint($"已复制连接地址：{address}", HintType.Success);
    }

    private void BtnRuntime_Click(object sender, RoutedEventArgs e) =>
        ModBase.OpenWebsite(ModPaperConnect.EasyTierReleaseUrl);

    // ---- 好友（在线判定只看当前房间成员，不接任何账号体系）----
    private void BtnAddFriend_Click(object sender, RoutedEventArgs e)
    {
        var name = ModMain.MyMsgBoxInput("添加好友", "填写对方在联机房间里显示的玩家名");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            if (!BedrockFriendList.Add(name)) HintService.Hint("这个玩家已经在名单里了", HintType.Info);
            RefreshFriends();
        }
        catch (Exception ex)
        {
            HintService.Hint(ex.Message, HintType.Error);
        }
    }

    private void RefreshFriends()
    {
        var friends = BedrockFriendList.All;
        var inRoom = ModPaperConnect.IsRunning;
        var members = ModPaperConnect.Players
            .Select(player => new RoomMember(player.PlayerName, player.ClientId, player.IsRoomHost))
            .ToArray();

        PanelFriends.Children.Clear();
        LabFriendEmpty.Visibility = friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var friend in friends)
        {
            var (online, text) = BedrockFriendList.Describe(friend, members, inRoom);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Text = $"{friend.Name} · {text}",
                Opacity = online ? 1 : 0.6
            };
            row.Children.Add(label);

            var remove = new SymbioButton
            {
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Text = "移除"
            };
            Grid.SetColumn(remove, 1);
            remove.Click += (_, _) =>
            {
                BedrockFriendList.Remove(friend.Name);
                RefreshFriends();
            };
            row.Children.Add(remove);

            PanelFriends.Children.Add(row);
        }
    }

    // ---- 局域网发现 ----
    private async void BtnScanLan_Click(object sender, RoutedEventArgs e)
    {
        BtnScanLan.IsEnabled = false;
        LabScanState.Text = "正在扫描…";
        try
        {
            var worlds = await BedrockLanDiscovery.ScanAsync(TimeSpan.FromSeconds(2));
            PanelLanWorlds.Children.Clear();
            LabLanEmpty.Visibility = worlds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var world in worlds)
            {
                var row = new SymbioButton
                {
                    Height = 30,
                    Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Text = $"{world.Name} · {world.Players}/{world.MaxPlayers} · {world.Address}:{world.Port}"
                };
                row.Click += (_, _) =>
                {
                    ModBase.ClipboardSet($"{world.Address}:{world.Port}");
                    HintService.Hint($"已复制 {world.Name} 的地址", HintType.Success);
                };
                PanelLanWorlds.Children.Add(row);
            }

            LabScanState.Text = worlds.Count == 0 ? "没有发现房间" : $"发现 {worlds.Count} 个房间，点条目复制地址";
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 局域网扫描失败", ModBase.LogLevel.Hint);
            LabScanState.Text = "扫描失败：" + ex.Message;
        }
        finally
        {
            BtnScanLan.IsEnabled = true;
        }
    }

    // ---- 我的服务器 ----
    private void BtnAddServer_Click(object sender, RoutedEventArgs e)
    {
        var input = ModMain.MyMsgBoxInput("添加服务器", "格式：名称 地址:端口（端口可省略，默认 19132）");
        if (string.IsNullOrWhiteSpace(input)) return;
        try
        {
            BedrockServerList.Add(BedrockServerList.Parse(input));
            RefreshServers();
            RefreshFriends();
            HintService.Hint("已添加", HintType.Success);
        }
        catch (Exception ex)
        {
            HintService.Hint(ex.Message, HintType.Error);
        }
    }

    private void RefreshServers()
    {
        var servers = BedrockServerList.All;
        PanelServers.Children.Clear();
        LabServerEmpty.Visibility = servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var server in servers)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var copy = new SymbioButton
            {
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Text = $"{server.Name} · {server.Address}"
            };
            copy.Click += (_, _) =>
            {
                ModBase.ClipboardSet(server.Address);
                HintService.Hint($"已复制 {server.Address}", HintType.Success);
            };
            row.Children.Add(copy);

            var remove = new SymbioButton
            {
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Text = "删除"
            };
            Grid.SetColumn(remove, 1);
            remove.Click += (_, _) =>
            {
                BedrockServerList.Remove(server.Address);
                RefreshServers();
            RefreshFriends();
            };
            row.Children.Add(remove);

            PanelServers.Children.Add(row);
        }
    }

    private void Refresh()
    {
        IsEnabled = !busy;
        CardRuntime.Visibility = runtimeFound ? Visibility.Collapsed : Visibility.Visible;
        if (!runtimeFound)
        {
            LabState.Text = "缺少联机运行文件";
            LabDetail.Text = "把 EasyTier 的 easytier-core.exe 与 easytier-cli.exe 放到启动器缓存目录后重试。";
        }

        var running = ModPaperConnect.IsRunning;
        var role = ModPaperConnect.Role;

        LabState.Text = role switch
        {
            ModPaperConnect.RoomRole.Host => ModPaperConnect.IsBusy ? "正在创建房间…" : "房间已创建",
            ModPaperConnect.RoomRole.Guest => "已加入房间",
            _ => runtimeFound ? "未联机" : "缺少联机运行文件"
        };
        LabDetail.Text = string.IsNullOrWhiteSpace(ModPaperConnect.LastError)
            ? string.IsNullOrWhiteSpace(ModPaperConnect.Status)
                ? "创建房间后把联机码发给朋友，双方运行基岩版即可互连。"
                : ModPaperConnect.Status
            : ModPaperConnect.LastError;

        BtnHost.IsEnabled = !busy && !running && runtimeFound;
        BtnJoin.IsEnabled = !busy && !running && runtimeFound;
        BtnStop.IsEnabled = !busy && running;

        LabRoomCode.Text = role switch
        {
            ModPaperConnect.RoomRole.Host => ModPaperConnect.RoomCode,
            ModPaperConnect.RoomRole.Guest when !string.IsNullOrEmpty(ModPaperConnect.PlayAddress) =>
                ModPaperConnect.PlayAddress,
            _ => "还没有加入或创建房间"
        };
        LabRoomHint.Text = role switch
        {
            ModPaperConnect.RoomRole.Host => string.IsNullOrEmpty(ModPaperConnect.DirectAddress)
                ? "把联机码发给朋友；对方填入联机码加入即可。"
                : $"把联机码发给朋友；同一局域网也可以把直连地址 {ModPaperConnect.DirectAddress} 填进对方的「共享节点」。",
            ModPaperConnect.RoomRole.Guest => ModPaperConnect.UseTun
                ? "增强模式：基岩版「好友 / 局域网」列表里应能看到房主的世界；也可以直接连上面这个地址。"
                : "在基岩版里添加服务器，地址填上面这一行（本机隧道地址）。",
            _ => "创建房间 → 复制联机码发给朋友 → 朋友填入联机码加入。"
        };

        BtnCopyCode.IsEnabled = !string.IsNullOrEmpty(ModPaperConnect.RoomCode);
        BtnCopyAddress.IsEnabled = !string.IsNullOrEmpty(ModPaperConnect.PlayAddress);

        LabRoom.Visibility = string.IsNullOrEmpty(ModPaperConnect.RoomCode)
            ? Visibility.Collapsed
            : Visibility.Visible;
        LabRoom.Text = role == ModPaperConnect.RoomRole.Host
            ? $"联机码：{ModPaperConnect.RoomCode}"
            : $"连接地址：{ModPaperConnect.PlayAddress}";

        RefreshPlayers();
        RefreshFriends();
    }

    private void RefreshPlayers()
    {
        var players = ModPaperConnect.Players;
        LabPlayerCount.Text = players.Count == 0 ? "还没有成员" : $"联机人数：{players.Count}";
        if (PanelPlayers.Children.Count == players.Count &&
            PanelPlayers.Children.Cast<TextBlock>().Select((text, index) => text.Text == Describe(players[index]))
                .All(match => match))
            return;

        PanelPlayers.Children.Clear();
        foreach (var player in players) PanelPlayers.Children.Add(new TextBlock { Text = Describe(player) });
    }

    private static string Describe(AgreementEntry.PlayerEntry player) =>
        player.IsRoomHost ? $"★ {player.PlayerName}（房主）" : $"· {player.PlayerName}";
}
