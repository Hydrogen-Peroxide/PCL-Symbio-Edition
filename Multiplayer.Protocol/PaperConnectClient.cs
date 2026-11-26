using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PaperConnect.Core.Entry;
using PaperConnect.Core.Interface;
using PaperConnect.Core.Module.Global;

namespace PaperConnect.Core;

public class PaperConnectClient : IDisposable, IPaperConnect
{
    private readonly string _serverIp;
    private readonly int _serverPort;
    private readonly string _playerName;
    private readonly CancellationTokenSource _heartbeatCts = new();
    private bool _disposed = false;
    public Action<List<AgreementEntry.PlayerEntry>> OnPlayerInfoUpdated { get; set; }

    public PaperConnectClient(string serverIp, int serverPort, string playerName)
    {
        if (string.IsNullOrWhiteSpace(serverIp))
            throw new ArgumentException("Server IP cannot be null or empty.", nameof(serverIp));
        if (serverPort <= 1024 || serverPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(serverPort), "Game port must be between 1025 and 65535.");
        if (string.IsNullOrWhiteSpace(playerName))
            throw new ArgumentException("Player name cannot be null or empty.", nameof(playerName));

        _serverIp = serverIp;
        _serverPort = serverPort;
        _playerName = playerName;
    }

    /// <summary>
    /// 发送 Ping 请求，测试服务端是否可达
    /// </summary>
    public async Task<AgreementEntry.PingResponse?> PingAsync(CancellationToken ct = default)
    {
        var request = new AgreementEntry.PingRequest
        {
            Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var response = await SendRequestAsync("c:ping", request, ct);
        return response as AgreementEntry.PingResponse;
    }

    /// <summary>
    /// 发送单次 Player 心跳请求
    /// </summary>
    public async Task<AgreementEntry.PlayerResponse?> SendHeartbeatAsync(CancellationToken ct = default)
    {
        var request = new AgreementEntry.PlayerRequest
        {
            ClientId = EnvironmentLabel.ClientId,
            PlayerName = _playerName
        };

        var response = await SendRequestAsync("c:player", request, ct);
        return response as AgreementEntry.PlayerResponse;
    }

    /// <summary>
    /// 启动自动心跳（每 5 秒一次），直到调用 StopHeartbeat 或 Dispose
    /// </summary>
    public void StartHeartbeat()
    {
        if (_heartbeatCts.IsCancellationRequested)
            throw new InvalidOperationException("Client is already stopped.");

        _ = Task.Run(async () =>
        {
            while (!_heartbeatCts.Token.IsCancellationRequested)
            {
                try
                {
                    var players = await SendHeartbeatAsync(_heartbeatCts.Token);
                    // 修复：服务端没有响应时 players 为 null，原实现会在这里空引用
                    if (players?.Players is { } list) OnPlayerInfoUpdated?.Invoke(list);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($@"[Heartbeat Error]: {ex.Message}");
                }

                try
                {
                    await Task.Delay(5000, _heartbeatCts.Token); // 5秒间隔
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _heartbeatCts.Token);
    }

    /// <summary>
    /// 停止自动心跳
    /// </summary>
    public void StopHeartbeat()
    {
        _heartbeatCts.Cancel();
    }

    private async Task<object?> SendRequestAsync(string ns, object requestObj, CancellationToken ct)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_serverIp, _serverPort, ct);
            using var stream = client.GetStream();

            // 构造协议数据：ns + \0 + json
            string json = JsonSerializer.Serialize(requestObj);
            byte[] nsBytes = Encoding.UTF8.GetBytes(ns);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] payload = new byte[nsBytes.Length + 1 + jsonBytes.Length];
            Buffer.BlockCopy(nsBytes, 0, payload, 0, nsBytes.Length);
            payload[nsBytes.Length] = 0; // \0 separator
            Buffer.BlockCopy(jsonBytes, 0, payload, nsBytes.Length + 1, jsonBytes.Length);

            await stream.WriteAsync(payload, 0, payload.Length, ct);
            await stream.FlushAsync(ct);

            // 修复：原实现用 stream.DataAvailable 判断读完，对端稍慢就会读到 0 字节；
            // 服务端发完响应即关闭连接，因此按「读到 EOF」才是协议的正确语义。
            using var ms = new MemoryStream();
            byte[] buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                ms.Write(buffer, 0, read);
            }

            if (ms.Length == 0)
                throw new InvalidOperationException("No response from server.");

            string responseJson = Encoding.UTF8.GetString(ms.ToArray());

            // 尝试解析错误
            if (responseJson.Contains("\"error\""))
            {
                var error = JsonSerializer.Deserialize<ErrorResponse>(responseJson);
                throw new InvalidOperationException($"Server error: {error?.Error}");
            }

            // 根据命名空间决定反序列化类型
            return ns switch
            {
                "c:ping" => JsonSerializer.Deserialize<AgreementEntry.PingResponse>(responseJson),
                "c:player" => JsonSerializer.Deserialize<AgreementEntry.PlayerResponse>(responseJson),
                _ => throw new NotSupportedException($"Unknown response namespace: {ns}")
            };
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"Failed to connect to {_serverIp}:{_serverPort}. {ex.Message}", ex);
        }
    }

    private class ErrorResponse
    {
        public string? Error { get; set; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _heartbeatCts.Cancel();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~PaperConnectClient()
    {
        Dispose();
    }

    public void Stop()
    {
        Dispose();
    }
}
