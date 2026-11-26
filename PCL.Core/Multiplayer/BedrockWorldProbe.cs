using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Multiplayer;

/// <summary>探测到的一个基岩版世界或 Minecraft 进程拥有的游戏端口。</summary>
public sealed record BedrockWorldInfo(
    string Name,
    string Address,
    int Port,
    string Version,
    int Players,
    int MaxPlayers,
    bool IsRakNetResponsive = true);

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
            var family = host.Contains(':', StringComparison.Ordinal)
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;
            using var client = new UdpClient(family);
            client.Client.Bind(new IPEndPoint(
                family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));

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
    public static async Task<BedrockWorldInfo?> PingLocalAsync(int port, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // 部分商店版 / GDK 版本只在 IPv6 回环地址上监听，两个回环地址都尝试。
        var ipv4 = PingAsync("127.0.0.1", port, timeout, cancellationToken);
        var ipv6 = PingAsync("::1", port, timeout, cancellationToken);
        var first = await Task.WhenAny(ipv4, ipv6).ConfigureAwait(false);
        var result = await first.ConfigureAwait(false);
        if (result is not null) return result;
        return await (ReferenceEquals(first, ipv4) ? ipv6 : ipv4).ConfigureAwait(false);
    }

    /// <summary>
    ///     自动发现本机正在开放局域网的基岩版世界。
    ///     1) 先查 Minecraft.Windows 进程实际监听的 UDP 端口（netstat + PID 过滤）；
    ///     2) 加上默认的 19132/19133 以及调用方提供的附加端口；
    ///     3) 并发 RakNet ping。新版本不回复 RakNet 时，回退到 Minecraft 进程拥有的端口。
    /// </summary>
    public static async Task<IReadOnlyList<BedrockWorldInfo>> DiscoverLocalAsync(
        IEnumerable<int>? extraPorts = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var ports = new SortedSet<int> { 19132, 19133 };

        // 从 Minecraft.Windows 进程的 UDP 监听列表里补充端口（nethernet 类房间用随机高端口）。
        // 某些 UWP 容器不会把真实 PID 暴露给 netstat，此时再用系统监听表作候选，最终仍由 RakNet 应答确认。
        var minecraftPorts = GetMinecraftUdpPorts();
        foreach (var port in minecraftPorts.Concat(GetActiveUdpPorts()))
            ports.Add(port);

        if (extraPorts is not null)
            foreach (var port in extraPorts)
                if (port is > 0 and <= 65535)
                    ports.Add(port);

        var tasks = ports.Select(port => PingLocalAsync(port, timeout, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var detected = results.Where(item => item is not null).Select(item => item!).ToArray();
        if (detected.Length > 0 || minecraftPorts.Count == 0) return detected;

        // 26.50 之后的基岩版逐步改用 NetherNet。此时端口仍属于 Minecraft，
        // 但不会回复 RakNet UnconnectedPing，只能把进程拥有的端口作为已确认候选交给上层。
        return minecraftPorts
            .Select(port => new BedrockWorldInfo("Minecraft", "127.0.0.1", port, string.Empty, 0, 0,
                IsRakNetResponsive: false))
            .ToArray();
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
            // 找基岩版进程。UWP、GDK 和旧版客户端的进程名略有不同，不能只匹配一个固定名称。
            var mcPids = new HashSet<int>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.ProcessName.Contains("Minecraft", StringComparison.OrdinalIgnoreCase))
                        mcPids.Add(process.Id);
                }
                catch
                {
                    // 进程在探测过程中退出时忽略这一项，继续使用其余进程。
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (mcPids.Count == 0) return [];

            // netstat -ano 输出 UDP 行，格式：UDP  0.0.0.0:19132  *:*  <PID>
            var psi = new ProcessStartInfo("netstat", "-ano -p udp")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return [];
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            var ports = new HashSet<int>();
            foreach (var line in output.Split('\n'))
            {
                // 不依赖本地化列宽和地址格式，兼容 0.0.0.0:PORT、[::]:PORT 等输出。
                var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length < 4 || !columns[0].Equals("UDP", StringComparison.OrdinalIgnoreCase)) continue;
                var localAddress = columns[1];
                var separator = localAddress.LastIndexOf(':');
                if (separator < 0 || !int.TryParse(localAddress[(separator + 1)..], out var port)) continue;
                if (!int.TryParse(columns[^1], out var pid)) continue;
                if (mcPids.Contains(pid) && port > 1024)
                    ports.Add(port);
            }

            if (ports.Count > 0) return ports.ToArray();

            // Get-NetUDPEndpoint 使用 Windows 端点表，某些 UWP 容器在 netstat 中不显示拥有者时仍可查询。
            var endpointPsi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            endpointPsi.ArgumentList.Add("-NoProfile");
            endpointPsi.ArgumentList.Add("-NonInteractive");
            endpointPsi.ArgumentList.Add("-Command");
            endpointPsi.ArgumentList.Add(
                "Get-NetUDPEndpoint -ErrorAction SilentlyContinue | " +
                "ForEach-Object { Write-Output ($_.OwningProcess.ToString() + '|' + $_.LocalPort.ToString()) }");

            using var endpointProcess = Process.Start(endpointPsi);
            if (endpointProcess is null) return [];
            var endpointOutput = endpointProcess.StandardOutput.ReadToEnd();
            endpointProcess.WaitForExit(5000);
            foreach (var line in endpointOutput.Split('\n'))
            {
                var columns = line.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length != 2 || !int.TryParse(columns[0], out var pid) ||
                    !int.TryParse(columns[1], out var port)) continue;
                if (mcPids.Contains(pid) && port > 1024)
                    ports.Add(port);
            }

            return ports.ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>获取系统当前所有 UDP 监听端口，作为 UWP PID 不可见时的候选集合。</summary>
    private static IReadOnlyList<int> GetActiveUdpPorts()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveUdpListeners()
                .Select(endpoint => endpoint.Port)
                .Where(port => port > 1024)
                .Distinct()
                .ToArray();
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
