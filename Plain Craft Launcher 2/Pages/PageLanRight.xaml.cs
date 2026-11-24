using System.Text.Json.Nodes;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     联机大厅右侧：展示当前房间码 / 直连地址，并提供复制与使用说明。
///     状态由 <see cref="PageLanLeft" /> 轮询后推过来，这里不自己发请求。
/// </summary>
public partial class PageLanRight
{
    private string _roomCode = string.Empty;
    private string _address = string.Empty;

    public PageLanRight()
    {
        InitializeComponent();
    }

    /// <summary>由左侧面板在每次状态刷新后调用。</summary>
    internal void UpdateState(JsonObject? state, bool running)
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

    private void BtnCopyCode_Click(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_roomCode)) return;
        ModBase.ClipboardSet(_roomCode);
        HintService.Hint(Lang.Text("Tools.Lan.Copied", _roomCode), HintType.Success);
    }

    private void BtnCopyAddress_Click(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_address)) return;
        ModBase.ClipboardSet(_address);
        HintService.Hint(Lang.Text("Tools.Lan.Copied", _address), HintType.Success);
    }
}
