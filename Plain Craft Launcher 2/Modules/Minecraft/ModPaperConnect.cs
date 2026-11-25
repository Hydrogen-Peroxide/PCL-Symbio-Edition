using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PCL;

/// <summary>
///     PaperConnect | 基岩版联机（https://github.com/Round-Studio/PaperConnect）的接入层。
///     协议规范：https://docs.roundstudio.top/docs/standard/paperconnect
///     实现方式：本模块自己实现联机码与 TCP 控制面协议，EasyTier 作为独立进程负责打洞与组网，
///     与 PaperConnect 官方客户端互通（联机码可互认）。
/// </summary>
internal static class ModPaperConnect
{
    #region 协议常量

    private const string RoomName = "paper-connect";
    private const string RoomCodeHeader = "P/";
    private const string CharSet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CharCount = 34;

    /// <summary>房主在 EasyTier 虚拟网络里的固定 IP，协议约定的联机中心地址。</summary>
    private const string HostVip = "10.144.144.1";

    /// <summary>客户端标识，官方实现为 "PaperConnect v0.0.1"，保持一致以便对端识别。</summary>
    private const string ClientId = "PaperConnect v0.0.1";

    /// <summary>基岩版默认局域网端口。</summary>
    internal const int DefaultGamePort = 19132;

    /// <summary>EasyTier 公共中继，用于两端不在同一局域网时建立连接。</summary>
    private static readonly string[] PublicRelays =
    [
        "tcp://public.easytier.top:11010",
        "udp://public.easytier.top:11010"
    ];

    /// <summary>与官方 EasyTierParameter.json 一致的启动参数。</summary>
    private static readonly string[] EasyTierParameters =
    [
        "--no-tun",
        "--multi-thread",
        "--enable-kcp-proxy",
        "--enable-quic-proxy",
        "--latency-first",
        "--encryption-algorithm", "aes-gcm",
        "--compression", "zstd",
        "--default-protocol", "tcp",
        "--private-mode", "true",
        "--p2p-only",
        "--console-log-level", "warn"
    ];

    #endregion

    #region 状态

    internal enum RoomRole
    {
        None = 0,
        Host = 1,
        Guest = 2
    }

    private static readonly object Sync = new();
    private static readonly List<PlayerInfo> PlayerList = [];

    private static Process? easyTier;
    private static CancellationTokenSource? session;
    private static TcpListener? server;
    private static int hostServerPort;

    internal static RoomRole Role { get; private set; } = RoomRole.None;
    internal static string RoomCode { get; private set; } = string.Empty;
    internal static string Status { get; private set; } = string.Empty;
    internal static string? LastError { get; private set; }
    internal static int GamePort { get; private set; } = DefaultGamePort;

    /// <summary>房客连接成功后，游戏应连接到的本机地址。</summary>
    internal static string PlayAddress => Role == RoomRole.Guest && IsRunning ? $"127.0.0.1:{GamePort}" : string.Empty;

    internal static bool IsRunning => Role != RoomRole.None;

    internal static bool IsBusy { get; private set; }

    /// <summary>玩家列表或状态变化时触发（UI 线程）。</summary>
    internal static event Action? Changed;

    internal static IReadOnlyList<PlayerInfo> Players
    {
        get
        {
            lock (Sync) return PlayerList.ToArray();
        }
    }

    private static void Notify()
    {
        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // UI 已销毁时忽略
        }
    }

    private static void SetStatus(string status)
    {
        Status = status;
        ModBase.RunInUi(Notify);
    }

    private static void SetError(string message)
    {
        LastError = message;
        Status = message;
        ModBase.Log($"[PaperConnect] {message}", ModBase.LogLevel.Hint);
        ModBase.RunInUi(Notify);
    }

    #endregion

    #region 联机码

    /// <summary>生成符合规范的联机码：P/NNNN-NNNN-SSSS-SSSS，S 段小端 base34 值需被 7 整除。</summary>
    internal static string GenerateRoomCode()
    {
        var random = new Random();
        Span<char> chars = stackalloc char[16];
        for (var i = 0; i < 16; i++) chars[i] = CharSet[random.Next(CharCount)];

        // 只调整 S 段（后 8 位）直到能被 7 整除；进位最多消耗 6 次递增
        for (var guard = 0; guard < 64 && RoomValue(chars[8..]) % 7 != 0; guard++)
        for (var i = 8; i < 16; i++)
        {
            var next = CharSet.IndexOf(chars[i]) + 1;
            if (next < CharCount)
            {
                chars[i] = CharSet[next];
                break;
            }

            chars[i] = CharSet[0];
        }

        var code = new string(chars);
        return $"{RoomCodeHeader}{code[..4]}-{code[4..8]}-{code[8..12]}-{code[12..]}";
    }

    /// <summary>校验联机码格式与校验位。</summary>
    internal static bool IsValidRoomCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var text = Normalize(code);
        if (!text.StartsWith(RoomCodeHeader, StringComparison.Ordinal)) return false;

        var parts = text[2..].Split('-');
        if (parts.Length != 4) return false;
        if (parts.Any(part => part.Length != 4 || part.Any(c => CharSet.IndexOf(c) < 0))) return false;

        var sPart = (parts[2] + parts[3]).ToCharArray();
        return RoomValue(sPart) % 7 == 0;
    }

    /// <summary>把用户输入整理成规范形态（去空格、去中文括号、大小写、O/I 纠正）。</summary>
    internal static string Normalize(string code)
    {
        var text = code.Trim().ToUpperInvariant().Replace(" ", "").Replace("　", "");
        if (text.StartsWith("【") || text.StartsWith("[")) text = text.TrimStart('【', '[').TrimEnd('】', ']');
        text = text.Replace("O", "0").Replace("I", "1");
        if (!text.StartsWith(RoomCodeHeader, StringComparison.Ordinal)) text = RoomCodeHeader + text.TrimStart('/');
        return text;
    }

    private static long RoomValue(ReadOnlySpan<char> chars)
    {
        long value = 0;
        long multiplier = 1;
        foreach (var c in chars)
        {
            value += CharSet.IndexOf(c) * multiplier;
            multiplier *= CharCount;
        }

        return value;
    }

    /// <summary>联机码 → EasyTier 网络名与密钥。</summary>
    private static (string Name, string Secret) ParseRoomCode(string code)
    {
        var parts = Normalize(code)[2..].Split('-');
        return ($"{RoomName}-{parts[0]}-{parts[1]}", $"{parts[2]}-{parts[3]}");
    }

    #endregion

    #region EasyTier

    /// <summary>EasyTier 运行文件所在目录（启动器自己的缓存）。</summary>
    private static string InstallDirectory => Path.Combine(ModBase.pathTemp, "Cache", "EasyTier");

    internal static string? EasyTierCore { get; private set; }
    internal static string? EasyTierCli { get; private set; }

    /// <summary>
    ///     查找 easytier-core.exe / easytier-cli.exe：优先启动器缓存，其次复用本机其它 PCL 系列启动器
    ///     已下载好的 EasyTier（避免重复下载）。找不到时返回 false，由 UI 提示用户。
    /// </summary>
    internal static bool LocateEasyTier()
    {
        if (EasyTierCore is not null && EasyTierCli is not null &&
            File.Exists(EasyTierCore) && File.Exists(EasyTierCli))
            return true;

        foreach (var dir in CandidateDirectories())
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                var core = Directory.EnumerateFiles(dir, "easytier-core.exe", SearchOption.AllDirectories).FirstOrDefault();
                var cli = Directory.EnumerateFiles(dir, "easytier-cli.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (core is null || cli is null) continue;

                EasyTierCore = core;
                EasyTierCli = cli;
                ModBase.Log($"[PaperConnect] 使用 EasyTier：{core}", ModBase.LogLevel.Debug);
                return true;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[PaperConnect] 扫描 EasyTier 目录失败：{dir}", ModBase.LogLevel.Debug);
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        yield return InstallDirectory;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCLSE", "EasyTier");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCLSE", "EasyTier");
        yield return Path.Combine(ModBase.pathTemp, "Cache", "PaperConnect");
    }

    /// <summary>EasyTier 官方发行页，缺少运行文件时引导用户前往。</summary>
    internal const string EasyTierReleaseUrl = "https://github.com/EasyTier/EasyTier/releases";

    private static Process StartEasyTier(string arguments)
    {
        if (EasyTierCore is null) throw new InvalidOperationException("尚未找到 EasyTier 运行文件。");
        var info = new ProcessStartInfo(EasyTierCore, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(EasyTierCore) ?? InstallDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 EasyTier 进程。");
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                ModBase.Log($"[EasyTier] {e.Data}", ModBase.LogLevel.Debug);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                ModBase.Log($"[EasyTier] {e.Data}", ModBase.LogLevel.Debug);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void StopEasyTier()
    {
        var process = easyTier;
        easyTier = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 结束 EasyTier 进程失败", ModBase.LogLevel.Debug);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>读取 EasyTier 对端列表（easytier-cli -o json peer）。</summary>
    private static List<PeerNode> ReadPeers()
    {
        if (EasyTierCli is null) return [];
        try
        {
            var info = new ProcessStartInfo(EasyTierCli, "-o json peer")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(EasyTierCli) ?? InstallDirectory
            };
            using var process = Process.Start(info);
            if (process is null) return [];
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (string.IsNullOrWhiteSpace(output)) return [];

            var start = output.IndexOf('[');
            var end = output.LastIndexOf(']');
            if (start < 0 || end <= start) return [];
            return JsonSerializer.Deserialize<List<PeerNode>>(output[start..(end + 1)], JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 读取 EasyTier 节点列表失败", ModBase.LogLevel.Debug);
            return [];
        }
    }

    /// <summary>轮询节点列表，等联机中心出现并返回它的控制面端口。</summary>
    private static async Task<int> WaitForCenterAsync(CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 90_000;
        while (Environment.TickCount64 < deadline)
        {
            token.ThrowIfCancellationRequested();
            foreach (var peer in ReadPeers())
            {
                var hostname = peer.Hostname ?? string.Empty;
                if (!hostname.Contains($"{RoomName}-server-", StringComparison.OrdinalIgnoreCase)) continue;
                var portText = hostname[(hostname.IndexOf($"{RoomName}-server-", StringComparison.OrdinalIgnoreCase) +
                                         $"{RoomName}-server-".Length)..];
                if (int.TryParse(portText, out var port) && port > 1024 && port <= 65535) return port;
            }

            SetStatus("正在寻找联机中心…");
            await Task.Delay(1500, token);
        }

        throw new TimeoutException("联机中心发现失败：90 秒内没有在对端列表里找到 paper-connect-server。");
    }

    #endregion

    #region 控制面 TCP（协议实现）

    internal sealed class PlayerInfo
    {
        [JsonPropertyName("player")] public string Player { get; set; } = "";
        [JsonPropertyName("clientId")] public string ClientId { get; set; } = "";
        [JsonPropertyName("isRoomHost")] public bool IsRoomHost { get; set; }
    }

    private sealed class PingRequest
    {
        [JsonPropertyName("time")] public long Time { get; set; }
    }

    private sealed class PingResponse
    {
        [JsonPropertyName("time")] public long Time { get; set; }
        [JsonPropertyName("returnTime")] public long ReturnTime { get; set; }
        [JsonPropertyName("gameType")] public string GameType { get; set; } = "MinecraftBedrock";
        [JsonPropertyName("gameProtocolType")] public string GameProtocolType { get; set; } = "UDP";
        [JsonPropertyName("gamePort")] public int GamePort { get; set; }
    }

    private sealed class PlayerRequest
    {
        [JsonPropertyName("clientId")] public string? ClientId { get; set; }
        [JsonPropertyName("playerName")] public string? PlayerName { get; set; }
    }

    private sealed class PlayerResponse
    {
        [JsonPropertyName("returnTime")] public long ReturnTime { get; set; }
        [JsonPropertyName("players")] public List<PlayerInfo> Players { get; set; } = [];
    }

    internal sealed class PeerNode
    {
        [JsonPropertyName("hostname")] public string? Hostname { get; set; }
        [JsonPropertyName("ipv4")] public string? Ipv4 { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>发送一条协议请求：namespace \0 json，响应是一段裸 JSON。</summary>
    private static async Task<T?> SendRequestAsync<T>(string host, int port, string ns, object body,
        CancellationToken token) where T : class
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, token);
        await using var stream = client.GetStream();

        var nsBytes = Encoding.UTF8.GetBytes(ns);
        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonOptions));
        var payload = new byte[nsBytes.Length + 1 + jsonBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, payload, 0, nsBytes.Length);
        payload[nsBytes.Length] = 0;
        Buffer.BlockCopy(jsonBytes, 0, payload, nsBytes.Length + 1, jsonBytes.Length);

        await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);

        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        while (true)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read <= 0) break;
            memory.Write(buffer, 0, read);
            // 响应没有长度前缀，靠 TCP 分包边界结束：数据到齐后 Json 能解析即视为完整
            var text = Encoding.UTF8.GetString(memory.ToArray());
            if (text.TrimEnd().EndsWith('}'))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<T>(text, JsonOptions);
                    if (parsed is not null) return parsed;
                }
                catch (JsonException)
                {
                    // 还没读全，继续
                }
            }
        }

        var raw = Encoding.UTF8.GetString(memory.ToArray());
        return string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }

    #endregion

    #region 房主

    /// <summary>
    ///     开房：启动控制面 TCP 服务，再用 EasyTier 把本机挂到 paper-connect-xxxx 网络，
    ///     房主固定 IP 10.144.144.1，主机名携带控制面端口供房客发现。
    /// </summary>
    internal static async Task<string> HostAsync(string playerName, int gamePort = DefaultGamePort)
    {
        if (IsRunning) throw new InvalidOperationException("已经在联机中，请先断开。");
        if (!LocateEasyTier()) throw new FileNotFoundException("没有找到 EasyTier 运行文件。");

        IsBusy = true;
        LastError = null;
        GamePort = gamePort;
        session = new CancellationTokenSource();
        try
        {
            SetStatus("正在创建房间…");

            // 1) 控制面服务：端口随机，主机名里要带上它
            server = new TcpListener(IPAddress.Any, 0);
            server.Start();
            hostServerPort = ((IPEndPoint)server.LocalEndpoint).Port;
            var code = GenerateRoomCode();
            var (networkName, networkSecret) = ParseRoomCode(code);

            lock (Sync)
            {
                PlayerList.Clear();
                PlayerList.Add(new PlayerInfo { Player = playerName, ClientId = ClientId, IsRoomHost = true });
            }

            _ = Task.Run(() => AcceptLoopAsync(server, playerName, session.Token), session.Token);
            _ = Task.Run(() => CleanupPlayersAsync(session.Token), session.Token);

            // 2) EasyTier：房主固定虚拟 IP + 主机名 paper-connect-server-<port>
            var args = $"-i {HostVip} --hostname {RoomName}-server-{hostServerPort} " +
                       $"--network-name {networkName} --network-secret {networkSecret} " +
                       string.Join(" ", EasyTierParameters) + " " +
                       string.Join(" ", PublicRelays.Select(relay => $"-p {relay}"));
            easyTier = StartEasyTier(args);

            Role = RoomRole.Host;
            RoomCode = code;
            SetStatus("房间已创建，等待其他玩家加入…");
            ModBase.Log($"[PaperConnect] 开房成功，联机码 {code}，控制面端口 {hostServerPort}，游戏端口 {gamePort}",
                ModBase.LogLevel.Normal);
            return code;
        }
        catch (Exception ex)
        {
            Shutdown();
            SetError($"创建房间失败：{ex.Message}");
            throw;
        }
        finally
        {
            IsBusy = false;
            ModBase.RunInUi(Notify);
        }
    }

    private static async Task AcceptLoopAsync(TcpListener listener, string hostName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client, hostName, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[PaperConnect] 接受连接失败", ModBase.LogLevel.Debug);
            }
        }
    }

    private static async Task HandleClientAsync(TcpClient client, string hostName, CancellationToken token)
    {
        using var _ = client;
        try
        {
            await using var stream = client.GetStream();
            using var memory = new MemoryStream();
            var buffer = new byte[2048];
            var deadline = Environment.TickCount64 + 8000;
            while (Environment.TickCount64 < deadline && memory.Length < 4096)
            {
                if (!stream.DataAvailable)
                {
                    await Task.Delay(10, token);
                    continue;
                }

                var read = await stream.ReadAsync(buffer, token);
                if (read <= 0) break;
                memory.Write(buffer, 0, read);
                if (memory.ToArray().Contains((byte)0)) break;
            }

            var raw = memory.ToArray();
            var split = Array.IndexOf(raw, (byte)0);
            if (split < 0)
            {
                await WriteJsonAsync(stream, new { error = "Missing null separator" }, token);
                return;
            }

            var ns = Encoding.UTF8.GetString(raw, 0, split);
            var json = Encoding.UTF8.GetString(raw, split + 1, raw.Length - split - 1);

            switch (ns)
            {
                case "c:ping":
                {
                    var request = JsonSerializer.Deserialize<PingRequest>(json, JsonOptions) ?? new PingRequest();
                    await WriteJsonAsync(stream, new PingResponse
                    {
                        Time = request.Time,
                        ReturnTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        GamePort = GamePort
                    }, token);
                    break;
                }
                case "c:player":
                {
                    var request = JsonSerializer.Deserialize<PlayerRequest>(json, JsonOptions);
                    if (string.IsNullOrWhiteSpace(request?.PlayerName) || string.IsNullOrWhiteSpace(request.ClientId))
                    {
                        await WriteJsonAsync(stream, new { error = "Missing playerName or clientId" }, token);
                        return;
                    }

                    lock (Sync)
                    {
                        var existing = PlayerList.FirstOrDefault(p =>
                            string.Equals(p.Player, request.PlayerName, StringComparison.OrdinalIgnoreCase));
                        if (existing is null)
                            PlayerList.Add(new PlayerInfo
                            {
                                Player = request.PlayerName, ClientId = request.ClientId, IsRoomHost = false
                            });
                        else
                            existing.ClientId = request.ClientId;
                        Heartbeats[request.PlayerName] = Environment.TickCount64;
                    }

                    await WriteJsonAsync(stream, new PlayerResponse
                    {
                        ReturnTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Players = Players.ToList()
                    }, token);
                    ModBase.RunInUi(Notify);
                    break;
                }
                default:
                    await WriteJsonAsync(stream, new { error = $"Unknown namespace: {ns}" }, token);
                    break;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 处理联机请求失败", ModBase.LogLevel.Debug);
        }
    }

    private static readonly Dictionary<string, long> Heartbeats = new();

    private static async Task WriteJsonAsync<T>(NetworkStream stream, T body, CancellationToken token)
    {
        var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonOptions));
        await stream.WriteAsync(data, token);
        await stream.FlushAsync(token);
    }

    /// <summary>10 秒没有心跳的房客视为离开房间。</summary>
    private static async Task CleanupPlayersAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(5000, token);
                var cutoff = Environment.TickCount64 - 10_000;
                var changed = false;
                lock (Sync)
                {
                    foreach (var player in PlayerList.Where(p => !p.IsRoomHost).ToArray())
                    {
                        if (!Heartbeats.TryGetValue(player.Player, out var seen) || seen < cutoff)
                        {
                            PlayerList.Remove(player);
                            Heartbeats.Remove(player.Player);
                            changed = true;
                        }
                    }
                }

                if (changed) ModBase.RunInUi(Notify);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    #endregion

    #region 房客

    /// <summary>
    ///     加入房间：先用 EasyTier 挂到房间里找到联机中心，再按它的控制面端口做本地端口转发，
    ///     使基岩版直接连 127.0.0.1:游戏端口 即可。
    /// </summary>
    internal static async Task JoinAsync(string code, string playerName, int gamePort = DefaultGamePort)
    {
        if (IsRunning) throw new InvalidOperationException("已经在联机中，请先断开。");
        if (!IsValidRoomCode(code)) throw new ArgumentException("联机码格式不正确。");
        if (!LocateEasyTier()) throw new FileNotFoundException("没有找到 EasyTier 运行文件。");

        IsBusy = true;
        LastError = null;
        GamePort = gamePort;
        session = new CancellationTokenSource();
        var token = session.Token;
        var normalized = Normalize(code);
        var (networkName, networkSecret) = ParseRoomCode(normalized);
        try
        {
            SetStatus("正在寻找联机中心…");

            // 第一段：只入网，不转发，用于发现房主
            var baseArgs = $"-d --network-name {networkName} --network-secret {networkSecret} " +
                           string.Join(" ", EasyTierParameters) + " " +
                           string.Join(" ", PublicRelays.Select(relay => $"-p {relay}"));
            easyTier = StartEasyTier(baseArgs);
            var centerPort = await WaitForCenterAsync(token);
            ModBase.Log($"[PaperConnect] 找到联机中心，控制面端口 {centerPort}", ModBase.LogLevel.Normal);

            // 第二段：转发控制面 TCP，问出游戏端口
            StopEasyTier();
            SetStatus("正在建立隧道…");
            easyTier = StartEasyTier(baseArgs + $" --port-forward tcp://0.0.0.0:{centerPort}/{HostVip}:{centerPort}");
            var ping = await PingWithRetryAsync(centerPort, token);
            if (ping is null) throw new TimeoutException("连上了联机中心，但没有拿到游戏端口。");

            GamePort = ping.GamePort > 1024 ? ping.GamePort : gamePort;

            // 第三段：补上游戏流量的 UDP 转发
            StopEasyTier();
            easyTier = StartEasyTier(baseArgs +
                                     $" --port-forward tcp://0.0.0.0:{centerPort}/{HostVip}:{centerPort}" +
                                     $" --port-forward udp://0.0.0.0:{GamePort}/{HostVip}:{GamePort}");
            var ready = await PingWithRetryAsync(centerPort, token);
            if (ready is null) throw new TimeoutException("隧道建立后联机中心没有响应。");

            Role = RoomRole.Guest;
            RoomCode = normalized;
            SetStatus("已加入房间，可以在基岩版里加入游戏了。");
            ModBase.Log($"[PaperConnect] 加入成功，联机码 {normalized}，游戏端口 {GamePort}", ModBase.LogLevel.Normal);

            _ = Task.Run(() => HeartbeatLoopAsync(centerPort, playerName, token), token);
        }
        catch (Exception ex)
        {
            Shutdown();
            SetError($"加入房间失败：{ex.Message}");
            throw;
        }
        finally
        {
            IsBusy = false;
            ModBase.RunInUi(Notify);
        }
    }

    private static async Task<PingResponse?> PingWithRetryAsync(int port, CancellationToken token)
    {
        for (var attempt = 0; attempt < 45; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var response = await SendRequestAsync<PingResponse>("127.0.0.1", port, "c:ping",
                    new PingRequest { Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, token);
                if (response is not null) return response;
            }
            catch (Exception ex) when (ex is SocketException or IOException or TimeoutException or OperationCanceledException)
            {
                if (token.IsCancellationRequested) throw;
            }

            await Task.Delay(1000, token);
        }

        return null;
    }

    private static async Task HeartbeatLoopAsync(int port, string playerName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var response = await SendRequestAsync<PlayerResponse>("127.0.0.1", port, "c:player",
                    new PlayerRequest { ClientId = ClientId, PlayerName = playerName }, token);
                if (response is not null)
                {
                    lock (Sync)
                    {
                        PlayerList.Clear();
                        PlayerList.AddRange(response.Players);
                    }

                    ModBase.RunInUi(Notify);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[PaperConnect] 心跳失败", ModBase.LogLevel.Debug);
            }

            try
            {
                await Task.Delay(5000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    #endregion

    #region 收尾

    /// <summary>断开当前房间并清理所有后台资源。</summary>
    internal static void Stop()
    {
        session?.Cancel();
        session?.Dispose();
        session = null;

        try
        {
            server?.Stop();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 关闭控制面服务失败", ModBase.LogLevel.Debug);
        }

        server = null;
        StopEasyTier();
        lock (Sync)
        {
            PlayerList.Clear();
            Heartbeats.Clear();
        }

        Role = RoomRole.None;
        RoomCode = string.Empty;
        hostServerPort = 0;
        Status = string.Empty;
        LastError = null;
        ModBase.RunInUi(Notify);
    }

    /// <summary>启动器退出时调用，同步结束，避免留下 EasyTier 进程。</summary>
    internal static void Shutdown()
    {
        try
        {
            session?.Cancel();
            server?.Stop();
        }
        catch
        {
            // 退出阶段忽略
        }

        StopEasyTier();
        Role = RoomRole.None;
    }

    #endregion
}
