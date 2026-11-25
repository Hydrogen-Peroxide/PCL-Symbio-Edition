using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL.Core.App;
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
            timer.Start();
            Refresh();
        };
        Unloaded += (_, _) => timer.Stop();
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
            ModPaperConnect.RoomRole.Guest => "在基岩版里添加服务器，地址填上面这一行（本机隧道地址）。",
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
