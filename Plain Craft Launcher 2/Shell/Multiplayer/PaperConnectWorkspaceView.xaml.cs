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
        Loaded += async (_, _) =>
        {
            runtimeFound = ModPaperConnect.LocateEasyTier();
            TextPeers.Text = Config.Preference.PaperConnectPeers ?? string.Empty;
            CheckTun.Checked = Config.Preference.PaperConnectTunMode;
            timer.Start();
            Refresh();
            // 页面打开时自动检测本机基岩版世界，省去手填端口
            await AutoDetectPortAsync();
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

    // ---- 自动检测本机基岩版端口 ----

    private async void BtnDetectPort_Click(object sender, RoutedEventArgs e) =>
        _ = await AutoDetectPortAsync();

    /// <summary>
    ///     扫描本机基岩版世界端口（Minecraft.Windows UDP 监听端口 + 默认 19132/19133）。
    ///     找到后自动填入「游戏端口」并显示世界名称，让房主不用手填端口。
    /// </summary>
    private async Task<bool> AutoDetectPortAsync()
    {
        BtnDetectPort.IsEnabled = false;
        LabDetectedWorld.Text = "正在检测…";
        LabDetectedWorld.Visibility = Visibility.Visible;
        try
        {
            var worlds = await BedrockWorldProbe.DiscoverLocalAsync(timeout: TimeSpan.FromSeconds(1.5));
            if (worlds.Count > 0)
            {
                var w = worlds[0];
                TextPort.Text = w.Port.ToString();
                LabDetectedWorld.Text = $"✔ 已检测到：{w.Name}（{w.Version}，端口 {w.Port}）";
                // 多于一个时追加提示
                if (worlds.Count > 1)
                    LabDetectedWorld.Text +=
                        $"，另有 {worlds.Count - 1} 个（端口 {string.Join("/", worlds.Skip(1).Select(x => x.Port))}）";
                return true;
            }
            else
            {
                LabDetectedWorld.Text = ModBedrockIsolation.IsAnyGameRunning()
                    ? "已检测到 Minecraft，但当前没有可应答的基岩版世界。请在游戏世界的暂停菜单选择「对局域网开放」，再重新检测。"
                    : "未检测到 Minecraft 正在运行。请先打开 Minecraft，进入世界并选择「对局域网开放」，再重新检测。";
                // 无世界时不清空已填的端口
                return false;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 自动检测端口失败", ModBase.LogLevel.Debug);
            LabDetectedWorld.Text = "检测失败，请手动填写端口。";
            return false;
        }
        finally
        {
            BtnDetectPort.IsEnabled = true;
        }
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
            if (string.IsNullOrWhiteSpace(TextPort.Text))
                if (!await AutoDetectPortAsync())
                {
                    HintService.Hint(ModBedrockIsolation.IsAnyGameRunning()
                        ? "已检测到 Minecraft，但没有检测到可应答的基岩版世界。请在游戏世界的暂停菜单选择「对局域网开放」，再重新检测。"
                        : "未检测到 Minecraft 正在运行。请先打开 Minecraft，进入世界并选择「对局域网开放」，再重新检测。",
                        HintType.Info);
                    return;
                }
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

    /// <summary>缺组件时一键自动下载 EasyTier（走统一下载管线，带进度）。</summary>
    private async void BtnRuntime_Click(object sender, RoutedEventArgs e)
    {
        BtnRuntime.IsEnabled = false;
        BarRuntime.Visibility = Visibility.Visible;
        BarRuntime.Value = 0;
        LabRuntime.Text = "正在下载联机组件…";

        var progress = new Progress<double>(value =>
        {
            BarRuntime.Value = value;
            LabRuntime.Text = $"正在下载联机组件… {value:P0}";
        });

        try
        {
            runtimeFound = await ModPaperConnect.EnsureEasyTierAsync(progress);
            if (runtimeFound)
            {
                LabRuntime.Text = "联机组件已就绪。";
                BarRuntime.Value = 1;
                HintService.Hint("联机组件下载完成", HintType.Success);
            }
            else
            {
                LabRuntime.Text = "下载失败。可以重试，或手动把 EasyTier 发行包解压到启动器缓存目录。";
                BtnRuntime.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 自动下载联机组件失败", ModBase.LogLevel.Hint);
            LabRuntime.Text = "下载失败：" + ex.Message;
            BtnRuntime.IsEnabled = true;
        }
        finally
        {
            BarRuntime.Visibility = runtimeFound ? Visibility.Collapsed : Visibility.Visible;
            Refresh();
        }
    }

    private void Refresh()
    {
        IsEnabled = !busy;
        CardRuntime.Visibility = runtimeFound ? Visibility.Collapsed : Visibility.Visible;
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

        if (!runtimeFound)
        {
            LabState.Text = "缺少联机运行文件";
            LabDetail.Text = "PaperConnect 需要 EasyTier 运行文件。点击右侧卡片即可自动下载联机组件。";
        }

        // 缺组件也允许点：点下去会自动下载组件再继续，不再让用户先自己找运行文件
        BtnHost.IsEnabled = !busy && !running;
        BtnJoin.IsEnabled = !busy && !running;
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

        // 房主端端口自检结果：本机没探测到基岩版世界时，把原因直接摆出来
        if (role == ModPaperConnect.RoomRole.Host && !string.IsNullOrEmpty(ModPaperConnect.WorldProbeWarning))
            LabRoomHint.Text = "⚠ " + ModPaperConnect.WorldProbeWarning + "。"
                               + "否则房客（尤其 BedrockBoot）会报「未检测到本地基岩房间」。";

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
