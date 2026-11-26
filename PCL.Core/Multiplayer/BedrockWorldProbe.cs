using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Multiplayer;

/// <summary>探测到的一个基岩版世界（单播 ping 结果）。</summary>
public sealed record BedrockWorldInfo(string Name, string Address, int Port, string Version, int Players, int MaxPlayers);

/// <summary>
///     基岩版世界的 RakNet 单播探测。
///     用途：房主开房前确认「本机这个端口上真的有一个世界在应答」——
///     否则房客（尤其 BedrockBoot）会报「未检测到本地基岩房间」而看不出是谁的问题。
///     实现与局域网发现同一套协议，但只发一次单播 ping，不广播、不扫描网段。
/// </summary>
public static class BedrockWorldProbe
{
    /// <summary>RakNet 离线消息魔数。</summary>
    private static readonly byte[] Magic =
    [
        0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe,
        0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78
    ];

    /// <summary>
    ///     向指定地址发一次 UnconnectedPing 并等待响应。
    ///     没有响应返回 null（端口没开、世界没开或非基岩版服务）。
    /// </summary>
    public static async Task<BedrockWorldInfo?> PingAsync(string host, int port,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) return null;
        var wait = timeout ?? TimeSpan.FromSeconds(1.5);

        try
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var payload = BuildPing();
            await client.SendAsync(payload, payload.Length, host, port).ConfigureAwait(false);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(wait);
            var result = await client.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
            return ParsePong(result.Buffer, result.RemoteEndPoint);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>本机端口探测的便捷入口。</summary>
    public static Task<BedrockWorldInfo?> PingLocalAsync(int port, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        PingAsync("127.0.0.1", port, timeout, cancellationToken);

    /// <summary>构造 RakNet UnconnectedPing（带 magic 与时间戳）。</summary>
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
    public static BedrockWorldInfo? ParsePong(byte[] data, IPEndPoint? from)
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
        return new BedrockWorldInfo(
            string.IsNullOrWhiteSpace(fields[1]) ? from.Address.ToString() : fields[1],
            from.Address.ToString(),
            from.Port,
            fields[3],
            players,
            maxPlayers);
    }
}
