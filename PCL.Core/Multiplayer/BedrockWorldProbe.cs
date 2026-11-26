using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Multiplayer;

/// <summary>探测到的一个基岩版世界（单播 ping 结果）。</summary>
public sealed record BedrockWorldInfo(string Name, string Address, int Port, string Version, int Players, int MaxPlayers);

/// <summary>
///     基岩版世界的 RakNet 单播探测。
///     核心用途：自动发现本机正在开放局域网的基岩版世界，无需手填端口。
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
        catch (OperationCanceledException) { return null; }
        catch (SocketException) { return null; }
    }

    /// <summary>本机端口探测的便捷入口。</summary>
    public static Task<BedrockWorldInfo?> PingLocalAsync(int port, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        PingAsync("127.0.0.1", port, timeout, cancellationToken);

    /// <summary>
    ///     自动发现本机正在开放局域网的基岩版世界。
    ///     1) 先查 Minecraft.Windows 进程实际监听的 UDP 端口（netstat + PID 过滤）；
    ///     2) 加上默认的 19132/19133 以及调用方提供的附加端口；
    ///     3) 并发 RakNet ping，只返回真正应答的端口。
    /// </summary>
    public static async Task<IReadOnlyList<BedrockWorldInfo>> DiscoverLocalAsync(
        IEnumerable<int>? extraPorts = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var ports = new SortedSet<int> { 19132, 19133 };

        // 从 Minecraft.Windows 进程的 UDP 监听列表里补充端口（nethernet 类房间用随机高端口）
        foreach (var port in GetMinecraftUdpPorts())
            ports.Add(port);

        if (extraPorts is not null)
            foreach (var port in extraPorts)
                if (port is > 0 and <= 65535)
                    ports.Add(port);

        var tasks = ports.Select(port => PingLocalAsync(port, timeout, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(item => item is not null).Select(item => item!).ToArray();
    }

    /// <summary>
    ///     获取 Minecraft.Windows（基岩版）进程正在监听的 UDP 端口。
    ///     通过 <c>netstat -ano -p UDP</c> 输出匹配 PID 获取；
    ///     任何异常都安静地返回空列表，不影响主流程。
    /// </summary>
    public static IReadOnlyList<int> GetMinecraftUdpPorts()
    {
        try
        {
            // 找 Minecraft.Windows 进程
            var mcPids = Process.GetProcessesByName("Minecraft.Windows")
                .Concat(Process.GetProcessesByName("Minecraft.Windows.exe"))
                .Select(p => p.Id)
                .ToHashSet();

            if (mcPids.Count == 0) return [];

            // netstat -ano 输出 UDP 行，格式：UDP  0.0.0.0:19132  *:*  <PID>
            var psi = new ProcessStartInfo("netstat", "-ano")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return [];
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            var ports = new List<int>();
            foreach (var line in output.Split('\n'))
            {
                // UDP  0.0.0.0:PORT  *:*  PID
                var m = Regex.Match(line, @"UDP\s+\S+:(\d+)\s+\*:\*\s+(\d+)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups[1].Value, out var port)) continue;
                if (!int.TryParse(m.Groups[2].Value, out var pid)) continue;
                if (mcPids.Contains(pid) && port > 1024)
                    ports.Add(port);
            }

            return ports;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>构造 RakNet UnconnectedPing（带 magic 与时间戳）。</summary>
    public static byte[] BuildPing()
    {
        var payload = new byte[1 + 8 + 16 + 8];
        payload[0] = 0x01;
        BitConverter.TryWriteBytes(payload.AsSpan(1, 8), (long)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Magic.CopyTo(payload, 9);
        BitConverter.TryWriteBytes(payload.AsSpan(25, 8), 0L);
        return payload;
    }

    /// <summary>解析 UnconnectedPong（MOTD 格式：MCPE;名称;协议;版本;在线;上限;...）。</summary>
    public static BedrockWorldInfo? ParsePong(byte[] data, IPEndPoint? from)
    {
        if (data.Length < 35 || data[0] != 0x1C || from is null) return null;
        var offset = 1 + 8 + 8 + 16;
        if (data.Length < offset + 2) return null;
        var length = data[offset] << 8 | data[offset + 1];
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
            from.Address.ToString(), from.Port, fields[3], players, maxPlayers);
    }
}
