using System;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using PCL.Core.App.Localization;

namespace PCL;

public partial class LanWorkspaceView
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _busy;
    private bool _refreshing;
    private int attachment;
    private string _roomCode = "";
    private string _address = "";

    public LanWorkspaceView()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            attachment++;
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) =>
        {
            attachment++;
            _timer.Stop();
        };
    }

    /// <summary>当前玩家的名字，交给陶瓦用于在房间里标识自己。</summary>
    private static string? PlayerName => ModProfile.selectedProfile?.Username;

    private async void BtnStart_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetButtons();
        try
        {
            await ModTerracotta.StartAsync();
            HintService.Hint(Lang.Text("Tools.Lan.Started"), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 启动联机失败", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Tools.Lan.StartFailed", ex.Message));
        }
        finally
        {
            _busy = false;
            await RefreshAsync();
        }
    }

    private async void BtnStop_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetButtons();
        try
        {
            await ModTerracotta.StopAsync();
            HintService.Hint(Lang.Text("Tools.Lan.Stopped"));
        }
        finally
        {
            _busy = false;
            await RefreshAsync();
        }
    }

    private void BtnWeb_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var url = ModTerracotta.WebUrl;
        if (string.IsNullOrEmpty(url)) return;
        ModBase.OpenWebsite(url);
    }

    private async void BtnHost_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_busy) return;
        if (!IsMinecraftRunning())
        {
            HintService.Hint("未检测到 Minecraft 正在运行，请先打开 Minecraft 并进入已开放局域网的世界。",
                HintType.Info);
            return;
        }
        _busy = true;
        SetButtons();
        try
        {
            if (!await ModTerracotta.HostAsync(TextRoom.Text, PlayerName))
                HintService.Hint(Lang.Text("Tools.Lan.HostFailed"), HintType.Error);
        }
        finally
        {
            _busy = false;
            await RefreshAsync();
        }
    }

    private static bool IsMinecraftRunning()
    {
        try
        {
            return Process.GetProcessesByName("javaw").Length > 0 ||
                   Process.GetProcessesByName("java").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async void BtnJoin_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_busy) return;
        var room = TextRoom.Text.Trim();
        if (string.IsNullOrEmpty(room))
        {
            HintService.Hint(Lang.Text("Tools.Lan.RoomRequired"), HintType.Info);
            return;
        }

        _busy = true;
        SetButtons();
        try
        {
            if (!await ModTerracotta.JoinAsync(room, PlayerName))
                HintService.Hint(Lang.Text("Tools.Lan.JoinFailed"), HintType.Error);
        }
        finally
        {
            _busy = false;
            await RefreshAsync();
        }
    }

    private async void BtnCancelRoom_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetButtons();
        try
        {
            await ModTerracotta.ResetAsync();
        }
        finally
        {
            _busy = false;
            await RefreshAsync();
        }
    }

    /// <summary>拉取一次状态并刷新左右两栏。</summary>
    private async Task RefreshAsync()
    {
        // 上一次请求还没回来就跳过这一拍，避免状态请求堆积
        if (_refreshing || !IsLoaded) return;
        var version = attachment;
        _refreshing = true;
        try
        {
            var running = ModTerracotta.IsRunning;
            var state = running ? await ModTerracotta.GetStateAsync() : null;
            // 请求不通说明服务已经没了，回到未启动状态
            if (running && state is null) running = false;

            if (version != attachment || !IsLoaded) return;
            var code = state?["state"]?.ToString() ?? string.Empty;
            LabState.Text = code switch
            {
                "host-scanning" => Lang.Text("Tools.Lan.State.Scanning"),
                "host-starting" => Lang.Text("Tools.Lan.State.HostStarting"),
                "host-ok" => Lang.Text("Tools.Lan.State.HostOk"),
                "guest-connecting" => Lang.Text("Tools.Lan.State.GuestConnecting"),
                "guest-starting" => Lang.Text("Tools.Lan.State.GuestStarting"),
                "guest-ok" => Lang.Text("Tools.Lan.State.GuestOk"),
                "exception" => Lang.Text("Tools.Lan.State.Exception"),
                "waiting" => Lang.Text("Tools.Lan.State.Ready"),
                _ => running ? Lang.Text("Tools.Lan.State.Ready") : Lang.Text("Tools.Lan.State.Stopped")
            };

            LabDetail.Text = code switch
            {
                "host-scanning" => Lang.Text("Tools.Lan.State.Scanning.Hint"),
                "guest-ok" => Lang.Text("Tools.Lan.State.GuestOk.Hint", state?["url"]?.ToString() ?? ""),
                "exception" => "未检测到 Minecraft 局域网世界，请先打开 Minecraft 并在世界中选择「对局域网开放」，然后重试。",
                _ => running ? Lang.Text("Tools.Lan.State.Ready.Hint") : Lang.Text("Tools.Lan.State.Stopped.Hint")
            };

            var room = state?["room"]?.ToString();
            if (!string.IsNullOrWhiteSpace(room))
            {
                LabRoom.Visibility = System.Windows.Visibility.Visible;
                LabRoom.Text = Lang.Text("Tools.Lan.RoomCode", room);
            }
            else
            {
                LabRoom.Visibility = System.Windows.Visibility.Collapsed;
            }

            BtnStart.IsEnabled = !running && !_busy;
            BtnStop.IsEnabled = running && !_busy;
            BtnWeb.IsEnabled = running;
            BtnHost.IsEnabled = running && !_busy && code is "waiting" or "";
            BtnJoin.IsEnabled = running && !_busy && code is "waiting" or "";
            BtnCancelRoom.IsEnabled = running && !_busy && code is not ("waiting" or "");

            UpdateLobby(state, running);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SetButtons()
    {
        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = false;
        BtnHost.IsEnabled = false;
        BtnJoin.IsEnabled = false;
        BtnCancelRoom.IsEnabled = false;
    }

    private void UpdateLobby(JsonObject? state, bool running)
    {
        var code = state?["state"]?.ToString() ?? string.Empty;
        _roomCode = state?["room"]?.ToString() ?? string.Empty;
        _address = state?["url"]?.ToString() ?? string.Empty;

        if (!string.IsNullOrEmpty(_roomCode) && code is "host-ok" or "host-starting")
        {
            LabRoomCode.Text = _roomCode;
            LabRoomHint.Text = Lang.Text("Tools.Lan.Lobby.RoomHint");
        }
        else if (!string.IsNullOrEmpty(_address) && code is "guest-ok")
        {
            LabRoomCode.Text = _address;
            LabRoomHint.Text = Lang.Text("Tools.Lan.Lobby.AddressHint");
        }
        else if (!running)
        {
            LabRoomCode.Text = Lang.Text("Tools.Lan.Lobby.None");
            LabRoomHint.Text = Lang.Text("Tools.Lan.Lobby.None.Hint");
        }
        else if (code == "host-scanning")
        {
            LabRoomCode.Text = Lang.Text("Tools.Lan.Lobby.Scanning");
            LabRoomHint.Text = Lang.Text("Tools.Lan.State.Scanning.Hint");
        }
        else
        {
            LabRoomCode.Text = Lang.Text("Tools.Lan.Lobby.Idle");
            LabRoomHint.Text = Lang.Text("Tools.Lan.Lobby.Idle.Hint");
        }

        BtnCopyCode.IsEnabled = !string.IsNullOrEmpty(_roomCode);
        BtnCopyAddress.IsEnabled = !string.IsNullOrEmpty(_address);
    }

    private void BtnCopyCode_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_roomCode)) return;
        ModBase.ClipboardSet(_roomCode);
        HintService.Hint(Lang.Text("Tools.Lan.Copied", _roomCode), HintType.Success);
    }

    private void BtnCopyAddress_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_address)) return;
        ModBase.ClipboardSet(_address);
        HintService.Hint(Lang.Text("Tools.Lan.Copied", _address), HintType.Success);
    }
}
