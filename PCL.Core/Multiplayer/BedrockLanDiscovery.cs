using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Multiplayer;

/// <summary>局域网/同网段发现到的基岩版世界。</summary>
/// <param name="Name">世界名（MOTD 第一个字段）。</param>
/// <param name="Address">来源 IP。</param>
/// <param name="Port">游戏端口。</param>
/// <param name="Version">游戏版本字符串。</param>
/// <param name="Players">当前人数。</param>
/// <param name="MaxPlayers">人数上限。</param>
public sealed record BedrockLanWorld(string Name, string Address, int Port, string Version, int Players, int MaxPlayers);

/// <summary>
///     基岩版局域网发现：向广播地址发 RakNet UnconnectedPing，解析 UnconnectedPong 的 MOTD。
///     不依赖基岩版进程、不写它的任何文件，纯 UDP 探测；用于「局域网发现」列表。
/// </summary>
public static class BedrockLanDiscovery
{
    /// <summary>RakNet 离线消息魔数。</summary>
    private static readonly byte[] Magic =
    [
        0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe,
        0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78
    ];

    private static readonly int[] DefaultPorts = [19132, 19133];

    /// <summary>
    ///     扫描一次。默认发广播到 19132/19133 并等待 <paramref name="timeout" />。
    ///     同网段多台机器开会各自回包，按「IP:端口」去重。
    /// </summary>
    public static async Task<IReadOnlyList<BedrockLanWorld>> ScanAsync(TimeSpan? timeout = null,
        IReadOnlyList<int>? ports = null, CancellationToken cancellationToken = default)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(2);
        var targets = ports ?? DefaultPorts;
        var found = new Dictionary<string, BedrockLanWorld>(StringComparer.OrdinalIgnoreCase);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.EnableBroadcast = true;
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        foreach (var port in targets)
        {
            try
            {
                var payload = BuildPing();
                await client.SendAsync(payload, payload.Length,
                    new IPEndPoint(IPAddress.Broadcast, port)).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // 某些网络禁用广播，忽略继续听单播回包
            }
        }

        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(remaining);
            try
            {
                var result = await client.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
                var world = ParsePong(result.Buffer, result.RemoteEndPoint);
                if (world is null) continue;
                found[$"{world.Address}:{world.Port}"] = world;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }

        return found.Values.OrderBy(world => world.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>构造一个 RakNet UnconnectedPing（带 magic 与时间戳）。</summary>
    public static byte[] BuildPing()
    {
        var payload = new byte[1 + 8 + 16 + 8];
        payload[0] = 0x01; // UnconnectedPing
        BitConverter.TryWriteBytes(payload.AsSpan(1, 8), (long)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Magic.CopyTo(payload, 9);
        BitConverter.TryWriteBytes(payload.AsSpan(25, 8), 0L); // client guid
        return payload;
    }

    /// <summary>
    ///     解析 UnconnectedPong：<c>0x1C | time(8) | serverGuid(8) | magic(16) | len(2,BE) | MOTD</c>，
    ///     MOTD 形如 <c>MCPE;世界名;协议版本;游戏版本;在线;上限;服务器ID;...</c>。
    /// </summary>
    public static BedrockLanWorld? ParsePong(byte[] data, IPEndPoint? from)
    {
        if (data.Length < 35 || data[0] != 0x1C || from is null) return null;

        var offset = 1 + 8 + 8 + 16;
        if (data.Length < offset + 2) return null;
        var length = data[offset] << 8 | data[offset + 1]; // 大端
        offset += 2;
        if (length <= 0 || offset + length > data.Length) return null;

        var motd = Encoding.UTF8.GetString(data, offset, length);
        var fields = motd.Split(';');
        if (fields.Length < 6) return null;
        if (!fields[0].StartsWith("MCPE", StringComparison.OrdinalIgnoreCase) &&
            !fields[0].StartsWith("MCEE", StringComparison.OrdinalIgnoreCase))
            return null;

        _ = int.TryParse(fields[4], out var players);
        _ = int.TryParse(fields[5], out var maxPlayers);
        return new BedrockLanWorld(
            string.IsNullOrWhiteSpace(fields[1]) ? from.Address.ToString() : fields[1],
            from.Address.ToString(),
            from.Port,
            fields[3],
            players,
            maxPlayers);
    }
}
