using PCL.Core.Multiplayer;

namespace PCL;

/// <summary>
///     内置的 PaperConnect（基岩版）模块：把现有的 <see cref="ModPaperConnect" /> 静态实现
///     接到 <see cref="IMultiplayerModule" /> 契约上。
///     这一步只做适配、不搬业务——真正的实现搬到独立插件 DLL 时，删掉这个类即可，
///     UI 侧因为只依赖接口而无需改动。
/// </summary>
internal sealed class BuiltInBedrockModule : IMultiplayerModule
{
    private bool? available;

    public string Id => "bedrock.paperconnect";

    public MultiplayerEdition Edition => MultiplayerEdition.Bedrock;

    public string DisplayName => "PaperConnect（基岩版）";

    public bool IsAvailable => available ??= ModPaperConnect.LocateEasyTier();

    public RoomSnapshot Snapshot
    {
        get
        {
            var role = ModPaperConnect.Role switch
            {
                ModPaperConnect.RoomRole.Host => RoomRole.Host,
                ModPaperConnect.RoomRole.Guest => RoomRole.Guest,
                _ => RoomRole.None
            };

            var status = role switch
            {
                RoomRole.None => ModPaperConnect.IsBusy ? RoomStatus.Starting : RoomStatus.Idle,
                _ => string.IsNullOrEmpty(ModPaperConnect.LastError) ? RoomStatus.Ready : RoomStatus.Failed
            };

            var members = ModPaperConnect.Players
                .Select(player => new RoomMember(player.PlayerName, player.ClientId, player.IsRoomHost))
                .ToArray();

            return new RoomSnapshot(
                ModPaperConnect.RoomCode,
                role,
                status,
                ModPaperConnect.Status,
                ModPaperConnect.PlayAddress,
                members,
                ModPaperConnect.LastError);
        }
    }

    public event Action? Changed
    {
        add => ModPaperConnect.Changed += value;
        remove => ModPaperConnect.Changed -= value;
    }

    public Task<string> HostAsync(string playerName, CancellationToken cancellationToken = default) =>
        ModPaperConnect.HostAsync(playerName, ModPaperConnect.DefaultGamePort);

    public Task JoinAsync(string code, string playerName, CancellationToken cancellationToken = default) =>
        ModPaperConnect.JoinAsync(code, playerName, ModPaperConnect.DefaultGamePort);

    public void Stop() => ModPaperConnect.Stop();
}
