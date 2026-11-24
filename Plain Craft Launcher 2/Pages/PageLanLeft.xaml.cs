using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     联机大厅的左侧操作面板：封装 Terracotta | 陶瓦联机 的启动与房间操作。
///     状态会同步给右侧的大厅页，真正的联机逻辑全部由陶瓦自己的服务完成。
/// </summary>
public partial class PageLanLeft
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _busy;
    private bool _refreshing;

    public PageLanLeft()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>当前玩家的名字，交给陶瓦用于在房间里标识自己。</summary>
    private static string? PlayerName => ModProfile.selectedProfile?.Username;

    private async void BtnStart_Click(object sender, MouseButtonEventArgs e)
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

    private async void BtnStop_Click(object sender, MouseButtonEventArgs e)
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

    private void BtnWeb_Click(object sender, MouseButtonEventArgs e)
    {
        var url = ModTerracotta.WebUrl;
        if (string.IsNullOrEmpty(url)) return;
        ModBase.OpenWebsite(url);
    }

    private async void BtnHost_Click(object sender, MouseButtonEventArgs e)
    {
        if (_busy) return;
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

    private async void BtnJoin_Click(object sender, MouseButtonEventArgs e)
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

    private async void BtnCancelRoom_Click(object sender, MouseButtonEventArgs e)
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
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var running = ModTerracotta.IsRunning;
            var state = running ? await ModTerracotta.GetStateAsync() : null;
            // 请求不通说明服务已经没了，回到未启动状态
            if (running && state is null) running = false;

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
                "exception" => Lang.Text("Tools.Lan.State.Exception.Hint"),
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

            ModMain.frmLanRight?.UpdateState(state, running);
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
}
