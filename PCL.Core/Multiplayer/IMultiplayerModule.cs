using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Multiplayer;

/// <summary>联机模块所属的游戏版本。</summary>
public enum MultiplayerEdition
{
    Java = 0,
    Bedrock = 1
}

/// <summary>房间角色。</summary>
public enum RoomRole
{
    None = 0,
    Host = 1,
    Guest = 2
}

/// <summary>房间生命周期状态。UI 只认这几个值，不再各自解析字符串。</summary>
public enum RoomStatus
{
    /// <summary>未联机。</summary>
    Idle = 0,

    /// <summary>正在创建/寻找中。</summary>
    Starting = 1,

    /// <summary>房间已建立或已加入，等待游戏侧接入。</summary>
    Ready = 2,

    /// <summary>已失败，<see cref="RoomSnapshot.Error" /> 里有原因。</summary>
    Failed = 3
}

/// <summary>房间成员。</summary>
/// <param name="Name">玩家名（Java 用户名 / 基岩版 gamertag）。</param>
/// <param name="ClientId">客户端标识，形如 <c>PaperConnect v0.0.1</c>。</param>
/// <param name="IsHost">是否房主。</param>
public sealed record RoomMember(string Name, string ClientId, bool IsHost);

/// <summary>某一时刻的房间快照。值语义，便于 UI 做等值比较后跳过重绘。</summary>
/// <param name="Code">联机码；Java 侧为房间码，基岩侧为 <c>P/NNNN-NNNN-SSSS-SSSS</c>。</param>
/// <param name="Role">当前角色。</param>
/// <param name="Status">生命周期状态。</param>
/// <param name="Detail">面向用户的说明（最多一句）。</param>
/// <param name="PlayAddress">游戏侧应连接的地址；未就绪时为 null。</param>
/// <param name="Members">成员列表，按加入顺序。</param>
/// <param name="Error">失败原因；仅 <see cref="RoomStatus.Failed" /> 时有值。</param>
public sealed record RoomSnapshot(
    string Code,
    RoomRole Role,
    RoomStatus Status,
    string Detail,
    string? PlayAddress,
    IReadOnlyList<RoomMember> Members,
    string? Error)
{
    public static readonly RoomSnapshot Empty =
        new(string.Empty, RoomRole.None, RoomStatus.Idle, string.Empty, null, Array.Empty<RoomMember>(), null);
}

/// <summary>
///     联机模块契约。主程序只依赖这个接口；具体实现（陶瓦 / PaperConnect / 未来的其它方案）
///     可以内置，也可以作为插件 DLL 在运行时加载。
///     实现方**不得**引用 WPF，UI 只通过 <see cref="Changed" /> + <see cref="Snapshot" /> 拉取状态。
/// </summary>
public interface IMultiplayerModule
{
    /// <summary>稳定标识，如 <c>bedrock.paperconnect</c>、<c>java.terracotta</c>。</summary>
    string Id { get; }

    /// <summary>所属版本。</summary>
    MultiplayerEdition Edition { get; }

    /// <summary>界面展示名。</summary>
    string DisplayName { get; }

    /// <summary>运行所需的外部组件是否就绪（EasyTier / Terracotta 二进制）。</summary>
    bool IsAvailable { get; }

    /// <summary>当前快照。</summary>
    RoomSnapshot Snapshot { get; }

    /// <summary>状态变化通知；可能在任意线程触发，UI 需自行 marshal。</summary>
    event Action? Changed;

    /// <summary>开房，返回联机码。</summary>
    Task<string> HostAsync(string playerName, CancellationToken cancellationToken = default);

    /// <summary>加入房间。</summary>
    Task JoinAsync(string code, string playerName, CancellationToken cancellationToken = default);

    /// <summary>断开并清理资源。</summary>
    void Stop();
}
