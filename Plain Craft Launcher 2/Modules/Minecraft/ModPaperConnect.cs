using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.Multiplayer;
using PCL.Core.Provision;
using PaperConnect.Core;
using PaperConnect.Core.Entry;
using PaperConnect.Core.Entry.Easytier;
using PaperConnect.Core.Room;

namespace PCL;

/// <summary>
///     PaperConnect | 基岩版联机的启动器侧编排层。
///     联机码、控制面 TCP 服务端/客户端全部使用 PaperConnect 官方实现（<c>Modules\PaperConnect</c> 下原样引入的
///     <see cref="PaperConnectServer" /> / <see cref="PaperConnectClient" /> / <see cref="RoomCodeGenerator" />），
///     这里只负责：拉起与回收 EasyTier 进程、发现联机中心、配置端口转发、把状态同步给 UI。
///     官方 PaperConnectCore 没有直接复用，因为它把 config.toml 写到当前工作目录、依赖提权启动、
///     且 Stop 会杀掉所有 easytier-core 进程。
/// </summary>
internal static class ModPaperConnect
{
    #region 常量

    /// <summary>房主在 EasyTier 虚拟网络里的固定 IP（协议约定）。</summary>
    private const string HostVip = "10.144.144.1";

    internal const int DefaultGamePort = 19132;

    /// <summary>局域网直连时的监听端口（房主在这里等直连的房客）。</summary>
    internal const int DirectListenPort = 11010;

    /// <summary>
    ///     共享节点（EasyTier peer）地址来自配置，空格/逗号分隔；留空则不用公共中继。
    ///     官方 PaperConnect 没有内置节点清单，EasyTier 文档也只给 <c>&lt;共享节点 IP或域名&gt;</c> 占位，
    ///     所以这里不做硬编码 —— 填一个能连上的共享节点即可跨公网组网。
    /// </summary>
    /// <summary>
    ///     默认共享节点（EasyTier 公共节点），取自 BedrockBoot 的 PaperConnect 实际使用的节点清单
    ///     （GravityCone 日志里的 <c>[[peer]] uri</c>），本机实测 225284.xyz / et1.fuis.top / center.node.1tmc.top 可达。
    ///     配置里填了地址就用配置的，留空时回落到这里。
    /// </summary>
    private static readonly string[] DefaultRelays =
    [
        "tcp://et1.fuis.top:11010",
        "tcp://225284.xyz:11010",
        "wss://center.node.1tmc.top/",
        "tcp://public.easytier.bmcbl.com:54321"
    ];

    private static string[] ReadRelays()
    {
        var configured = (Config.Preference.PaperConnectPeers ?? string.Empty)
            .Split(new[] { ' ', '\t', ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(peer => peer.Contains("://"))
            .ToArray();
        return configured.Length > 0 ? configured : DefaultRelays;
    }

    /// <summary>把共享节点列表拼成 EasyTier 的 <c>-p</c> 参数。</summary>
    private static string BuildPeerArgs() =>
        string.Join(" ", ReadRelays().Select(relay => $"-p {relay}"));

    /// <summary>
    ///     房主在 EasyTier 里的 hostname 前缀。
    ///     实测（BedrockBoot 报错原文，两轮对照）：
    ///     - 用 <c>paper-connect-server-&lt;port&gt;</c> → 「未找到主机（前缀 pcs-）」，前缀都不匹配；
    ///     - 用 <c>pcs-&lt;port&gt;</c> → 「解析主机名失败: unrecognized hostname format: pcs-30933」，
    ///       前缀匹配上了但格式不完整。
    ///     所以是 <c>pcs-</c> 开头、结构比「纯端口」复杂。bedrockboot 的解析在闭源 CLI 里，
    ///     拿不到确切规则，这里同时发两个信息：端口 + 房间码 N 段（<c>pcs-&lt;端口&gt;-&lt;NNNN-NNNN&gt;</c>），
    ///     并让房客侧对两种写法都宽容（见 CenterHostnamePrefixes 的解析）。
    /// </summary>
    private const string HostHostnamePrefix = "pcs-";

    /// <summary>房客识别联机中心时接受的前缀，按优先级排列。</summary>
    private static readonly string[] CenterHostnamePrefixes =
    [
        "pcs-",
        $"{RoomCodeGenerator.ROOM_NAME}-server-",
        "scaffolding-mc-server-"
    ];

    /// <summary>房主的局域网直连地址，房客可以把它填进「共享节点」里直接连过来。</summary>
    internal static string DirectAddress
    {
        get
        {
            var address = LocalIPv4();
            return string.IsNullOrEmpty(address) ? string.Empty : $"tcp://{address}:{DirectListenPort}";
        }
    }

    private static string LocalIPv4()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                              nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                  !System.Net.IPAddress.IsLoopback(address) &&
                                  !IsAutomaticPrivateAddress(address))
                .Select(address => address.ToString())
                .FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsAutomaticPrivateAddress(System.Net.IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    /// <summary>
    ///     与 PaperConnect.Core 的 Manifest/EasyTierParameter.json 一致，仅去掉 <c>-c ./config.toml</c>
    ///     （我们不生成 ACL 配置文件；私有网络本身已靠 network-secret 隔离）与 <c>--no-tun</c>
    ///     （是否免 TUN 由「增强模式」开关决定，见 <see cref="UseTun" />）。
    /// </summary>
    private static readonly string[] EasyTierParameters =
    [
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

    /// <summary>
    ///     增强模式：不用 <c>--no-tun</c>，让 EasyTier 创建真实 TUN 虚拟网卡。
    ///     这样基岩版的局域网广播（7551/19132）能穿过隧道，nethernet 房间才会出现在「好友/局域网」里；
    ///     代价是需要管理员权限（创建网卡）。
    /// </summary>
    internal static bool UseTun => Config.Preference.PaperConnectTunMode;

    private static string[] BuildParameters()
    {
        if (UseTun) return EasyTierParameters;
        var parameters = new string[EasyTierParameters.Length + 1];
        parameters[0] = "--no-tun";
        EasyTierParameters.CopyTo(parameters, 1);
        return parameters;
    }

    #endregion

    #region 状态

    internal enum RoomRole
    {
        None = 0,
        Host = 1,
        Guest = 2
    }

    private static readonly object Sync = new();
    private static readonly List<AgreementEntry.PlayerEntry> PlayerList = [];

    private static Process? easyTier;
    private static CancellationTokenSource? session;
    private static PaperConnectServer? server;
    private static PaperConnectClient? client;
    private static readonly object EasyTierLogSync = new();
    private static readonly Dictionary<string, long> EasyTierLogLastWritten = [];
    private static long easyTierLogWindowStart;
    private static int easyTierLogWindowCount;

    internal static RoomRole Role { get; private set; } = RoomRole.None;
    internal static string RoomCode { get; private set; } = string.Empty;
    internal static string Status { get; private set; } = string.Empty;
    internal static string? LastError { get; private set; }

    /// <summary>
    ///     开房时本机端口探测的警告：本机没有基岩版世界应答时非空。
    ///     不阻断开房（有些环境 UDP 回环受限），只把原因直接告诉房主。
    /// </summary>
    internal static string? WorldProbeWarning { get; private set; }
    internal static int GamePort { get; private set; } = DefaultGamePort;
    private static int LocalGamePort { get; set; } = DefaultGamePort;
    internal static bool IsBusy { get; private set; }
    internal static bool IsRunning => Role != RoomRole.None;

    /// <summary>房客连接成功后游戏应连接的本机地址。</summary>
    internal static string PlayAddress => Role == RoomRole.Guest && IsRunning
        ? $"{(UseTun ? HostVip : "127.0.0.1")}:{(UseTun ? GamePort : LocalGamePort)}"
        : string.Empty;

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    internal static IReadOnlyList<AgreementEntry.PlayerEntry> Players
    {
        get
        {
            lock (Sync) return PlayerList.ToArray();
        }
    }

    internal static event Action? Changed;

    private static void Notify()
    {
        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // UI 已销毁
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

    private static void ApplyPlayers(IEnumerable<AgreementEntry.PlayerEntry>? players)
    {
        if (players is null) return;
        lock (Sync)
        {
            PlayerList.Clear();
            PlayerList.AddRange(players);
        }

        ModBase.RunInUi(Notify);
    }

    #endregion

    #region 联机码（转调官方 RoomCodeGenerator）

    internal static string GenerateRoomCode() => RoomCodeGenerator.GenerateRoomCode();

    internal static bool IsValidRoomCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && RoomCodeGenerator.IsValidFormat(Normalize(code));

    /// <summary>整理用户输入：去空格/括号、统一大写、纠正 O/I，补上 P/ 前缀。</summary>
    internal static string Normalize(string code)
    {
        var text = code.Trim().ToUpperInvariant().Replace(" ", "").Replace("　", "");
        if (text.StartsWith("【") || text.StartsWith("[")) text = text.TrimStart('【', '[').TrimEnd('】', ']');
        text = text.Replace("O", "0").Replace("I", "1");
        if (!text.StartsWith(RoomCodeGenerator.ROOM_CODE_HEADER, StringComparison.Ordinal))
            text = RoomCodeGenerator.ROOM_CODE_HEADER + text.TrimStart('/');
        return text;
    }

    #endregion

    #region EasyTier

    private static string InstallDirectory => Path.Combine(ModBase.pathTemp, "Cache", "EasyTier");

    internal static string? EasyTierCore { get; private set; }
    internal static string? EasyTierCli { get; private set; }

    internal const string EasyTierReleaseUrl = "https://github.com/EasyTier/EasyTier/releases";

    /// <summary>自动下载时使用的 EasyTier 版本（与 BedrockBoot 内置的 2.6.4 对齐）。</summary>
    private const string EasyTierVersion = "v2.6.4";

    private const string EasyTierToolsId = "EasyTier";

    /// <summary>
    ///     确保 EasyTier 就绪：先找现成的（缓存/本机已装），找不到就自动下载官方发行包并解压。
    ///     走统一的 <see cref="ToolProvisionService" />，落在 <c>%AppData%\PCLSE\Tools\EasyTier\v2.6.4\</c>，
    ///     下载进度通过 <paramref name="progress" /> 回调（0~1）。
    /// </summary>
    internal static async Task<bool> EnsureEasyTierAsync(IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (LocateEasyTier()) return true;

        var definition = new ToolDefinition(
            EasyTierToolsId,
            EasyTierVersion,
            new Uri($"https://github.com/EasyTier/EasyTier/releases/download/{EasyTierVersion}/easytier-windows-x86_64-{EasyTierVersion}.zip"),
            Sha256: null,
            EntryFile: "easytier-core.exe",
            IsArchive: true);

        try
        {
            var core = await ToolProvisionService.EnsureAsync(definition, progress, cancellationToken)
                .ConfigureAwait(false);
            // 解压目录里同时有 cli，找一下即可
            var directory = Path.GetDirectoryName(core);
            if (directory is null) return false;
            var cli = Directory.EnumerateFiles(directory, "easytier-cli.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (cli is null)
            {
                ModBase.Log("[PaperConnect] EasyTier 解压后缺少 easytier-cli.exe", ModBase.LogLevel.Hint);
                return false;
            }

            EasyTierCore = core;
            EasyTierCli = cli;
            ModBase.Log($"[PaperConnect] 已自动下载 EasyTier {EasyTierVersion}：{core}", ModBase.LogLevel.Normal);
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 自动下载 EasyTier 失败", ModBase.LogLevel.Hint,
                userSummary: "联机组件下载失败");
            return false;
        }
    }

    /// <summary>查找 easytier-core / easytier-cli：优先启动器缓存，其次复用本机已下载的 EasyTier。</summary>
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
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCLSE",
            "EasyTier");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCLSE",
            "EasyTier");
    }

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
        process.OutputDataReceived += (_, e) => ForwardEasyTierLog(e.Data);
        process.ErrorDataReceived += (_, e) => ForwardEasyTierLog(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>
    ///     EasyTier 在端口转发失败时会以毫秒级频率重复输出同一类错误。
    ///     只保留分类后的首条并设置总量上限，避免一个联机失败把启动器日志写到数十 GB。
    /// </summary>
    private static void ForwardEasyTierLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var key = line switch
        {
            _ when line.Contains("bind addr fail", StringComparison.OrdinalIgnoreCase) => "bind addr fail",
            _ when line.Contains("BrokenPipe", StringComparison.OrdinalIgnoreCase) => "BrokenPipe",
            _ when line.Contains("RouteError", StringComparison.OrdinalIgnoreCase) => "RouteError",
            _ when line.Contains("ConnectionReset", StringComparison.OrdinalIgnoreCase) => "ConnectionReset",
            _ when line.Contains("handshake timeout", StringComparison.OrdinalIgnoreCase) => "handshake timeout",
            _ when line.Contains("can not find interface", StringComparison.OrdinalIgnoreCase) => "interface lookup",
            _ => line.Length > 96 ? line[..96] : line
        };

        var now = Environment.TickCount64;
        lock (EasyTierLogSync)
        {
            if (now - easyTierLogWindowStart >= 60_000)
            {
                easyTierLogWindowStart = now;
                easyTierLogWindowCount = 0;
            }

            if (easyTierLogWindowCount >= 120) return;
            if (EasyTierLogLastWritten.TryGetValue(key, out var last) && now - last < 10_000) return;

            EasyTierLogLastWritten[key] = now;
            easyTierLogWindowCount++;
        }

        ModBase.Log($"[EasyTier] {line}", ModBase.LogLevel.Debug);
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

    /// <summary>用官方 PeerInfo 模型读取对端列表（easytier-cli -o json peer）。</summary>
    private static List<PeerInfo> ReadPeers()
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

            // EasyTier 2.6 返回数组，较新的 CLI 会包装成 {"peers":[...]}。
            // 两种格式都兼容，否则网络已经连上时仍会被误报为“没有联机中心”。
            var start = output.IndexOf('[');
            var end = output.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                var peers = JsonSerializer.Deserialize<List<PeerInfo>>(output[start..(end + 1)]);
                if (peers is not null) return peers;
            }

            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("peers", out var peerElement))
            {
                return JsonSerializer.Deserialize<List<PeerInfo>>(peerElement.GetRawText()) ?? [];
            }

            return [];
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 读取 EasyTier 节点列表失败", ModBase.LogLevel.Debug);
            return [];
        }
    }

    /// <summary>轮询对端列表，按 hostname 发现联机中心并解析它的控制面端口。</summary>
    private static async Task<int> WaitForCenterAsync(CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 90_000;
        var sawPeer = false;
        while (Environment.TickCount64 < deadline)
        {
            token.ThrowIfCancellationRequested();
            var peers = ReadPeers();
            ModBase.Log($"[PaperConnect] EasyTier 节点数 {peers.Count}：" +
                        string.Join(", ", peers.Select(peer =>
                            $"{peer.Hostname} ({peer.Ipv4})")), ModBase.LogLevel.Debug);
            foreach (var peer in peers)
            {
                sawPeer = true;
                var hostname = peer.Hostname ?? string.Empty;
                foreach (var prefix in CenterHostnamePrefixes)
                {
                    var index = hostname.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) continue;
                    var rest = hostname[(index + prefix.Length)..];
                    var digits = new string(rest.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var port) && port > 1024 && port <= 65535) return port;
                }
            }

            SetStatus("正在寻找联机中心…");
            await Task.Delay(1500, token);
        }

        throw new TimeoutException(sawPeer
            ? "联机中心发现失败：对端列表里没有 pcs-（或 paper-connect-server-）开头的主机，房间可能已经关闭。"
            : "没有连上任何共享节点：请在「共享节点」里填一个可用的 EasyTier 共享节点，或填房主的局域网直连地址。");
    }

    #endregion

    #region 房主

    internal static async Task<string> HostAsync(string playerName, int gamePort = DefaultGamePort)
    {
        if (IsRunning) throw new InvalidOperationException("已经在联机中，请先断开。");
        // 缺组件就自动补齐，不再直接把用户挡在门外
        if (!LocateEasyTier())
        {
            SetStatus("正在准备联机组件…");
            if (!await EnsureEasyTierAsync()) throw new FileNotFoundException("联机组件下载失败，请重试或检查网络。");
        }

        IsBusy = true;
        LastError = null;
        GamePort = gamePort;
        session = new CancellationTokenSource();
        try
        {
            // 开房前确认本机端口真有基岩版世界在应答：房客（尤其 BedrockBoot）加入时会 ping
            // 127.0.0.1:<gamePort>，本机没人应答它就报「未检测到本地基岩房间」，而房主看不出原因。
            SetStatus("正在检查本机基岩版世界…");
            var probe = await BedrockWorldProbe.PingLocalAsync(gamePort);
            var processOwnsPort = BedrockWorldProbe.GetMinecraftUdpPorts().Contains(gamePort);
            if (probe is null && !processOwnsPort)
            {
                WorldProbeWarning = $"本机 {gamePort} 端口没有探测到基岩版世界，请先在游戏里「对局域网开放」";
                ModBase.Log($"[PaperConnect] 本机 {gamePort} 端口无基岩版应答，房客可能加入失败",
                    ModBase.LogLevel.Hint);
                throw new InvalidOperationException(ModBedrockIsolation.IsAnyGameRunning()
                    ? "已检测到 Minecraft，但没有检测到可应答的基岩版世界。请在游戏世界的暂停菜单选择「对局域网开放」，再重新检测。"
                    : "未检测到 Minecraft 正在运行。请先打开 Minecraft，进入世界并选择「对局域网开放」，再重新检测。");
            }
            else if (probe is null)
            {
                WorldProbeWarning = $"已确认 Minecraft 正在使用本机 {gamePort} UDP 端口，当前世界未提供 RakNet 信息，已继续创建房间";
                ModBase.Log($"[PaperConnect] 已确认 Minecraft 进程拥有 UDP 端口 {gamePort}，跳过 RakNet 应答要求",
                    ModBase.LogLevel.Normal);
            }
            else
            {
                WorldProbeWarning = null;
                ModBase.Log(
                    $"[PaperConnect] 已确认本机世界：{probe.Name}（{probe.Version}，{probe.Players}/{probe.MaxPlayers}）",
                    ModBase.LogLevel.Normal);
            }

            SetStatus("正在创建房间…");

            // 官方控制面服务端：端口由它自己随机挑一个，主机名里要带上它
            var roomServer = new PaperConnectServer(playerName, gamePort);
            while (roomServer.ServerPort <= 1024) roomServer = new PaperConnectServer(playerName, gamePort);
            roomServer.OnPlayerInfoUpdated = ApplyPlayers;
            server = roomServer;
            _ = Task.Run(async () =>
            {
                try
                {
                    await roomServer.StartAsync();
                }
                catch (Exception ex)
                {
                    if (!session.IsCancellationRequested) SetError($"控制面服务异常：{ex.Message}");
                }
            }, session.Token);

            var code = GenerateRoomCode();
            var network = RoomCodeGenerator.ParseRoomCode(code);
            // BedrockBoot GravityCone 识别 pcs-<控制面端口>-g-<游戏端口>。
            var hostName = $"{HostHostnamePrefix}{roomServer.ServerPort}-g-{gamePort}";
            var args = $"-i {HostVip} --hostname {hostName} " +
                       $"--network-name {network.NetworkName} --network-secret {network.NetworkKey} " +
                       string.Join(" ", BuildParameters()) + " " +
                       $"--listeners tcp://0.0.0.0:{DirectListenPort} " +
                       BuildPeerArgs();
            easyTier = StartEasyTier(args);

            Role = RoomRole.Host;
            RoomCode = code;
            SetStatus("房间已创建，等待其他玩家加入…");
            ModBase.Log($"[PaperConnect] 房主 hostname = {hostName}",
                ModBase.LogLevel.Normal);
            ModBase.Log($"[PaperConnect] 开房成功，联机码 {code}，控制面端口 {roomServer.ServerPort}，游戏端口 {gamePort}，" +
                        $"直连地址 {DirectAddress}，共享节点 {(ReadRelays().Length == 0 ? "无" : string.Join(" ", ReadRelays()))}",
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

    #endregion

    #region 房客

    internal static async Task JoinAsync(string code, string playerName, int gamePort = DefaultGamePort)
    {
        if (IsRunning) throw new InvalidOperationException("已经在联机中，请先断开。");
        var normalized = Normalize(code);
        if (!RoomCodeGenerator.IsValidFormat(normalized)) throw new ArgumentException("联机码格式不正确。");
        // 缺组件就自动补齐，不再直接把用户挡在门外
        if (!LocateEasyTier())
        {
            SetStatus("正在准备联机组件…");
            if (!await EnsureEasyTierAsync()) throw new FileNotFoundException("联机组件下载失败，请重试或检查网络。");
        }

        IsBusy = true;
        LastError = null;
        GamePort = gamePort;
        session = new CancellationTokenSource();
        var token = session.Token;
        var network = RoomCodeGenerator.ParseRoomCode(normalized);
        var tun = UseTun;
        // TUN 模式：自己占一个 10.144.144.x，直连房主的 10.144.144.1，不再需要端口转发
        var guestVip = $"10.144.144.{new Random().Next(2, 255)}";
        var addressArgs = tun ? $"-i {guestVip}" : "-d";
        var controlHost = tun ? HostVip : "127.0.0.1";
        var baseArgs = $"{addressArgs} --network-name {network.NetworkName} --network-secret {network.NetworkKey} " +
                       string.Join(" ", BuildParameters()) + " " +
                       BuildPeerArgs();
        try
        {
            SetStatus("正在寻找联机中心…");

            // 第一段：只入网，用于发现房主
            easyTier = StartEasyTier(baseArgs);
            var centerPort = await WaitForCenterAsync(token);
            ModBase.Log($"[PaperConnect] 找到联机中心，控制面端口 {centerPort}，模式 {(tun ? "TUN" : "端口转发")}",
                ModBase.LogLevel.Normal);

            if (tun)
            {
                // TUN 模式：虚拟网卡已经能直达房主，控制面与游戏流量都走真实路由
                var tunClient = new PaperConnectClient(controlHost, centerPort, playerName);
                var ping = await PingWithRetryAsync(tunClient, token);
                if (ping is null) throw new TimeoutException("连上了联机中心，但没有拿到游戏端口。");
                GamePort = ping.GamePort > 1024 ? ping.GamePort : gamePort;

                tunClient.OnPlayerInfoUpdated = ApplyPlayers;
                client = tunClient;
                Role = RoomRole.Guest;
                RoomCode = normalized;
                SetStatus("已加入房间，基岩版「好友 / 局域网」里应该能看到房主的世界。");
                ModBase.Log($"[PaperConnect] 加入成功（TUN），联机码 {normalized}，游戏端口 {GamePort}，" +
                            $"本机虚拟 IP {guestVip}", ModBase.LogLevel.Normal);
                tunClient.StartHeartbeat();
                return;
            }

            // 第二段：转发控制面 TCP，问出游戏端口
            StopEasyTier();
            SetStatus("正在建立隧道…");
            var localControlPort = GetFreeTcpPort();
            easyTier = StartEasyTier(baseArgs + $" --port-forward tcp://127.0.0.1:{localControlPort}/{HostVip}:{centerPort}");
            var roomClient = new PaperConnectClient("127.0.0.1", localControlPort, playerName);
            var ping2 = await PingWithRetryAsync(roomClient, token);
            if (ping2 is null) throw new TimeoutException("连上了联机中心，但没有拿到游戏端口。");
            GamePort = ping2.GamePort > 1024 ? ping2.GamePort : gamePort;
            // 本机同时运行房主和房客时，远端控制端口与游戏端口都可能已经被占用。
            // 转发监听使用独立的回环端口，避免 EasyTier 反复 bind 失败。
            LocalGamePort = GetFreeTcpPort();

            // 第三段：补上游戏流量的 UDP/TCP 转发（nethernet 房间可能走 TCP）
            StopEasyTier();
            easyTier = StartEasyTier(baseArgs +
                                     $" --port-forward tcp://127.0.0.1:{localControlPort}/{HostVip}:{centerPort}" +
                                     $" --port-forward udp://127.0.0.1:{LocalGamePort}/{HostVip}:{GamePort}" +
                                     $" --port-forward tcp://127.0.0.1:{LocalGamePort}/{HostVip}:{GamePort}");
            roomClient = new PaperConnectClient("127.0.0.1", localControlPort, playerName);
            if (await PingWithRetryAsync(roomClient, token) is null)
                throw new TimeoutException("隧道建立后联机中心没有响应。");

            roomClient.OnPlayerInfoUpdated = ApplyPlayers;
            client = roomClient;
            Role = RoomRole.Guest;
            RoomCode = normalized;
            SetStatus("已加入房间，可以在基岩版里加入游戏了。");
            ModBase.Log($"[PaperConnect] 加入成功，联机码 {normalized}，游戏端口 {GamePort}", ModBase.LogLevel.Normal);
            roomClient.StartHeartbeat();
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

    private static async Task<AgreementEntry.PingResponse?> PingWithRetryAsync(PaperConnectClient roomClient,
        CancellationToken token)
    {
        for (var attempt = 0; attempt < 45; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var response = await roomClient.PingAsync(token);
                if (response is not null) return response;
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // 端口转发还没就绪，重试
            }

            await Task.Delay(1000, token);
        }

        return null;
    }

    #endregion

    #region 收尾

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

        try
        {
            client?.Stop();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[PaperConnect] 关闭控制面客户端失败", ModBase.LogLevel.Debug);
        }

        server = null;
        client = null;
        StopEasyTier();
        lock (Sync) PlayerList.Clear();

        Role = RoomRole.None;
        RoomCode = string.Empty;
        Status = string.Empty;
        LastError = null;
        ModBase.RunInUi(Notify);
    }

    internal static void Shutdown()
    {
        try
        {
            session?.Cancel();
            server?.Stop();
            client?.Stop();
        }
        catch
        {
            // 退出阶段忽略
        }

        server = null;
        client = null;
        StopEasyTier();
        Role = RoomRole.None;
    }

    #endregion
}
