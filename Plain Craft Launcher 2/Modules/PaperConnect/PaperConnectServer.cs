using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PaperConnect.Core.Entry;
using PaperConnect.Core.Interface;
using PaperConnect.Core.Module.Global;

namespace PaperConnect.Core;

public class PaperConnectServer : IPaperConnect
{
    private readonly int _gamePort;
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, AgreementEntry.PlayerEntry> _players = new();
    private readonly CancellationTokenSource _cts = new();

    public int ServerPort { get; private set; } = new Random().Next(1024, 65535);
    public Action<List<AgreementEntry.PlayerEntry>> OnPlayerInfoUpdated { get; set; }

    // 房主信息（本服务端自身）
    private readonly AgreementEntry.PlayerEntry _hostInfo;

    public PaperConnectServer(string hostName, int gamePort)
    {
        if (gamePort <= 1024 || gamePort > 65535)
            throw new ArgumentOutOfRangeException(nameof(gamePort), "Game port must be between 1025 and 65535.");

        _gamePort = gamePort;
        _listener = new TcpListener(IPAddress.Any, ServerPort);
        _hostInfo = new AgreementEntry.PlayerEntry
        {
            PlayerName = hostName,
            ClientId = EnvironmentLabel.ClientId,
            IsRoomHost = true,
            LastHeartbeat = DateTime.UtcNow
        };
        _players[_hostInfo.PlayerName] = _hostInfo;
    }

    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine($@"[PaperConnect] Server listening on port {ServerPort}");

        OnPlayerInfoUpdated.Invoke(_players.Values.ToList());

        // 启动心跳清理任务
        _ = Task.Run(CleanupInactivePlayers, _cts.Token);

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleClientAsync(client);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"[Error] Accepting client: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        _listener.Stop();
        Console.WriteLine(@"[PaperConnect] Server stopped.");
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        using var memory = new MemoryStream();
        byte[] buffer = new byte[1024];
        int totalRead = 0;

        try
        {
            // 读取直到遇到 \0 或超时
            while (totalRead < 4096)
            {
                if (!stream.DataAvailable)
                {
                    await Task.Delay(10, _cts.Token);
                    continue;
                }

                int read = await stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (read == 0) break;

                memory.Write(buffer, 0, read);
                totalRead += read;

                // 检查是否包含 \0
                if (memory.ToArray().Take(totalRead).Contains((byte)0))
                    break;
            }

            var rawData = memory.ToArray();
            int nullIndex = Array.IndexOf(rawData, (byte)0);
            if (nullIndex == -1)
            {
                SendErrorResponse(stream, "Missing null separator");
                return;
            }

            string namespacePart = Encoding.UTF8.GetString(rawData, 0, nullIndex);
            string jsonPart = Encoding.UTF8.GetString(rawData, nullIndex + 1, rawData.Length - nullIndex - 1);

            await ProcessRequestAsync(namespacePart, jsonPart, stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[Client Error]: {ex.Message}");
            SendErrorResponse(stream, "Invalid request");
        }
        finally
        {
            client.Close();
        }
    }

    private async Task ProcessRequestAsync(string ns, string json, NetworkStream stream)
    {
        try
        {
            if (ns == "c:ping")
            {
                var request = JsonSerializer.Deserialize<AgreementEntry.PingRequest>(json);
                var response = new AgreementEntry.PingResponse
                {
                    Time = request.Time,
                    ReturnTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    GameType = "MinecraftBedrock",
                    GameProtocolType = "UDP",
                    GamePort = _gamePort
                };
                await SendJsonResponse(stream, response);
            }
            else if (ns == "c:player")
            {
                var request = JsonSerializer.Deserialize<AgreementEntry.PlayerRequest>(json);
                if (string.IsNullOrWhiteSpace(request?.PlayerName) || string.IsNullOrWhiteSpace(request.ClientId))
                {
                    SendErrorResponse(stream, "Missing playerName or clientId");
                    return;
                }

                _players[request.PlayerName] = new AgreementEntry.PlayerEntry
                {
                    PlayerName = request.PlayerName,
                    ClientId = request.ClientId,
                    IsRoomHost = false,
                    LastHeartbeat = DateTime.UtcNow
                };

                var activePlayers = _players.Values
                    .Where(p => p.IsRoomHost || (DateTime.UtcNow - p.LastHeartbeat).TotalSeconds <= 10)
                    .ToList();

                OnPlayerInfoUpdated?.Invoke(activePlayers);

                // 返回给客户端的响应也要包含房主
                var response = new AgreementEntry.PlayerResponse
                {
                    ReturnTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Players = activePlayers.Select(p => new AgreementEntry.PlayerEntry
                    {
                        PlayerName = p.PlayerName,
                        ClientId = p.ClientId,
                        IsRoomHost = p.IsRoomHost
                    }).ToList()
                };

                await SendJsonResponse(stream, response);
            }
            else
            {
                SendErrorResponse(stream, $"Unknown namespace: {ns}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[Process Error]: {ex.Message}");
            SendErrorResponse(stream, "Malformed JSON");
        }
    }

    private async Task SendJsonResponse<T>(NetworkStream stream, T obj)
    {
        var json = JsonSerializer.Serialize(obj);
        byte[] data = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(data, 0, data.Length, _cts.Token);
        await stream.FlushAsync(_cts.Token);
    }

    private void SendErrorResponse(NetworkStream stream, string message)
    {
        var error = new { error = message };
        var json = JsonSerializer.Serialize(error);
        var data = Encoding.UTF8.GetBytes(json);
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    private async Task CleanupInactivePlayers()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-10);
                var toRemove = _players.Keys
                    .Where(name => !_players[name].IsRoomHost && _players[name].LastHeartbeat < cutoff)
                    .ToList();

                foreach (var name in toRemove)
                {
                    _players.TryRemove(name, out _);
                    OnPlayerInfoUpdated?.Invoke(_players.Values.ToList());
                    Console.WriteLine($@"[Cleanup] Removed inactive player: {name}");
                }

                await Task.Delay(10000, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
