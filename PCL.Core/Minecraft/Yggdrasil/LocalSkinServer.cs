using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Minecraft.Yggdrasil;

/// <summary>
///     本地 Yggdrasil 皮肤服务。<br/>
///     配合 authlib-injector 使用：启动器把游戏原本发往 Mojang 的会话请求劫持到这里，
///     本地服务直接读取皮肤库里的 PNG，按 Mojang 的格式签名后返回，从而实现纯离线的皮肤加载。<br/>
///     只在 127.0.0.1 上监听，端口由系统动态分配，路径带一次性随机前缀。
/// </summary>
public sealed class LocalSkinServer : IDisposable
{
    private const int RsaKeySize = 2048;

    /// <summary>同时最多缓存的材质数量，防止长时间运行后无限增长。</summary>
    private const int MaxCachedTextures = 64;

    private const string ProfilePrefix = "/sessionserver/session/minecraft/profile/";
    private const string TexturePrefix = "/textures/";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly RSA _rsa;
    private readonly string _pathToken;
    private readonly ConcurrentDictionary<string, ProfileEntry> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>join 记录：serverId → 档案，供同一台机器上的验证服务端查询。</summary>
    private readonly ConcurrentDictionary<string, JoinRecord> _joins = new(StringComparer.Ordinal);

    private readonly object _signLock = new();
    private int _disposed;

    /// <summary>join 记录的有效期，与规范建议的 30 秒保持在同一量级。</summary>
    private static readonly TimeSpan JoinLifetime = TimeSpan.FromSeconds(60);

    /// <summary>服务监听的本地端口。</summary>
    public int Port { get; }

    /// <summary>Yggdrasil API 根地址，形如 <c>http://127.0.0.1:12345/abcdef/</c>。</summary>
    public string ApiRoot { get; }

    /// <summary>API 元数据 JSON（含 <c>signaturePublickey</c>），用于 authlib-injector 的预获取。</summary>
    public string MetadataJson { get; }

    /// <summary>API 元数据的 Base64 编码，直接填入 <c>-Dauthlibinjector.yggdrasil.prefetched</c>。</summary>
    public string PrefetchedMetadata { get; }

    /// <param name="dataFolder">存放签名密钥的目录。</param>
    public LocalSkinServer(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        _rsa = LoadOrCreateKey(Path.Combine(dataFolder, "service-key.pem"));

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _pathToken = Guid.NewGuid().ToString("N")[..16];
        ApiRoot = $"http://127.0.0.1:{Port}/{_pathToken}/";

        MetadataJson = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["serverName"] = "PCL SE 本地皮肤服务",
                ["implementationName"] = "PCL Symbio Edition",
                ["implementationVersion"] = "1"
            },
            ["skinDomains"] = new JsonArray { "127.0.0.1", "localhost" },
            ["signaturePublickey"] = _rsa.ExportSubjectPublicKeyInfoPem()
        }.ToJsonString();

        PrefetchedMetadata = Convert.ToBase64String(Encoding.UTF8.GetBytes(MetadataJson));
        _ = AcceptLoopAsync();
    }

    /// <summary>
    ///     注册（或更新）一个档案的材质。签名与材质地址只在这里计算一次，后续请求直接命中缓存。
    /// </summary>
    /// <param name="uuid">档案 UUID，可带连字符。</param>
    /// <param name="name">玩家 ID。</param>
    /// <param name="skinPath">皮肤 PNG 路径，为空表示不使用皮肤。</param>
    /// <param name="slim">是否使用纤细模型。</param>
    /// <param name="capePath">披风 PNG 路径，可为空。</param>
    public void Register(string uuid, string name, string? skinPath, bool slim, string? capePath = null)
    {
        var id = uuid.Replace("-", "").ToLowerInvariant();
        var textures = new JsonObject();
        if (!string.IsNullOrEmpty(skinPath) && File.Exists(skinPath))
            textures["SKIN"] = BuildTextureNode(skinPath, slim);
        if (!string.IsNullOrEmpty(capePath) && File.Exists(capePath))
            textures["CAPE"] = BuildTextureNode(capePath, false);
        if (textures.Count == 0)
            throw new ArgumentException("至少需要提供皮肤或披风", nameof(skinPath));

        var payload = new JsonObject
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["profileId"] = id,
            ["profileName"] = name,
            ["textures"] = textures
        }.ToJsonString();

        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        string signature;
        lock (_signLock)
            signature = Convert.ToBase64String(
                _rsa.SignData(Encoding.UTF8.GetBytes(value), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1));

        var profile = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["properties"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "textures",
                    ["value"] = value,
                    ["signature"] = signature
                }
            }
        };
        _profiles[id] = new ProfileEntry { Json = profile.ToJsonString(), Name = name };
    }

    /// <summary>移除某个档案的材质（关闭离线皮肤时调用）。</summary>
    public void Unregister(string uuid)
    {
        _profiles.TryRemove(uuid.Replace("-", "").ToLowerInvariant(), out _);
    }

    private JsonObject BuildTextureNode(string path, bool slim)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (_textures.Count >= MaxCachedTextures) _textures.Clear();
        _textures[hash] = path;

        var node = new JsonObject { ["url"] = $"{ApiRoot}{TexturePrefix.TrimStart('/')}{hash}" };
        if (slim) node["metadata"] = new JsonObject { ["model"] = "slim" };
        return node;
    }

    #region HTTP 服务

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                if (request is null) return;
                var response = RouteAsync(request.Value.Method, request.Value.Target, request.Value.Body);
                await WriteResponseAsync(stream, response).ConfigureAwait(false);
            }
            catch
            {
                // 客户端提前断开、连接被重置等情况无需处理
            }
        }
    }

    private static async Task<(string Method, string Target, byte[] Body)?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[4096];
        using var head = new MemoryStream();
        while (head.Length <= 32 * 1024)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read <= 0) return null;
            head.Write(buffer, 0, read);

            var text = Encoding.ASCII.GetString(head.GetBuffer(), 0, (int)head.Length);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) continue;

            var lines = text[..end].Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;

            var contentLength = 0;
            foreach (var line in lines)
            {
                if (!line.StartsWith("content-length:", StringComparison.OrdinalIgnoreCase)) continue;
                int.TryParse(line["content-length:".Length..].Trim(), out contentLength);
            }

            // 只有 join 等少数接口带请求体，限制大小避免被塞爆内存
            if (contentLength is < 0 or > 64 * 1024) return null;

            var headerEnd = end + 4;
            var body = new byte[contentLength];
            var buffered = Math.Min((int)head.Length - headerEnd, contentLength);
            if (buffered > 0) Array.Copy(head.GetBuffer(), headerEnd, body, 0, buffered);

            var received = buffered;
            while (received < contentLength)
            {
                var readBody = await stream
                    .ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, contentLength - received))).ConfigureAwait(false);
                if (readBody <= 0) break;
                Array.Copy(buffer, 0, body, received, readBody);
                received += readBody;
            }

            return (parts[0], parts[1], body);
        }

        return null;
    }

    private (int Status, string ContentType, byte[] Body) RouteAsync(string method, string target, byte[] body)
    {
        var path = target;
        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute)) path = absolute.PathAndQuery;
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0) path = path[..queryIndex];

        var prefix = "/" + _pathToken;
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return NotFound();

        var route = path[prefix.Length..];
        if (route is "" or "/") return Json(MetadataJson);

        if (route.StartsWith(ProfilePrefix, StringComparison.Ordinal))
        {
            var uuid = route[ProfilePrefix.Length..].Replace("-", "").ToLowerInvariant();
            return _profiles.TryGetValue(uuid, out var profile) ? Json(profile.Json) : NoContent();
        }

        if (route.StartsWith(TexturePrefix, StringComparison.Ordinal))
        {
            var hash = route[TexturePrefix.Length..];
            if (!_textures.TryGetValue(hash, out var file)) return NotFound();
            try
            {
                return (200, "image/png", File.ReadAllBytes(file));
            }
            catch
            {
                return NotFound();
            }
        }

        // 离线启动时游戏不会真的走这些接口，但补上能避免个别整合包/模组探测时出错
        if (method is "POST" or "GET")
            switch (route)
            {
                case "/authserver/validate":
                    return NoContent();
                case "/authserver/authenticate":
                case "/authserver/refresh":
                    return AuthenticateResponse();
                case "/sessionserver/session/minecraft/join":
                    return HandleJoin(body);
                case "/sessionserver/session/minecraft/hasJoined":
                    return HandleHasJoined(target);
                case "/api/profiles/minecraft":
                    return Json(ProfilesJson());
            }

        return NotFound();
    }

    /// <summary>
    ///     记录一次进服验证。只有同一台机器上的服务端才能访问到这里，因此只校验档案是否存在。
    /// </summary>
    private (int, string, byte[]) HandleJoin(byte[] body)
    {
        try
        {
            var json = JsonNode.Parse(Encoding.UTF8.GetString(body));
            var uuid = (json?["selectedProfile"]?.GetValue<string>() ?? "").Replace("-", "").ToLowerInvariant();
            var serverId = json?["serverId"]?.GetValue<string>() ?? "";
            var accessToken = json?["accessToken"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(serverId) || string.IsNullOrEmpty(accessToken) ||
                !_profiles.TryGetValue(uuid, out var profile))
                return Forbidden();

            if (_joins.Count > 64) PruneJoins();
            _joins[serverId] = new JoinRecord
            {
                Uuid = uuid,
                Name = profile.Name,
                Expires = DateTimeOffset.UtcNow + JoinLifetime
            };
            return NoContent();
        }
        catch
        {
            return BadRequest();
        }
    }

    /// <summary>
    ///     供服务端核对客户端会话，返回带材质的完整档案。<br/>
    ///     记录在有效期内可重复查询，以容忍验证服务端因网络抖动而重试。
    /// </summary>
    private (int, string, byte[]) HandleHasJoined(string target)
    {
        var serverId = GetQueryValue(target, "serverId");
        var username = GetQueryValue(target, "username");
        if (string.IsNullOrEmpty(serverId) || string.IsNullOrEmpty(username)) return NoContent();
        if (!_joins.TryGetValue(serverId, out var record)) return NoContent();
        if (record.Expires < DateTimeOffset.UtcNow)
        {
            _joins.TryRemove(serverId, out _);
            return NoContent();
        }

        if (!string.Equals(record.Name, username, StringComparison.Ordinal)) return NoContent();

        return _profiles.TryGetValue(record.Uuid, out var profile) ? Json(profile.Json) : NoContent();
    }

    private void PruneJoins()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (serverId, record) in _joins)
            if (record.Expires < now)
                _joins.TryRemove(serverId, out _);
    }

    private static string GetQueryValue(string target, string key)
    {
        var queryIndex = target.IndexOf('?');
        if (queryIndex < 0) return "";
        foreach (var pair in target[(queryIndex + 1)..].Split('&'))
        {
            var equalIndex = pair.IndexOf('=');
            var name = equalIndex < 0 ? pair : pair[..equalIndex];
            if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase)) continue;
            return equalIndex < 0 ? "" : Uri.UnescapeDataString(pair[(equalIndex + 1)..]);
        }

        return "";
    }

    private string ProfilesJson()
    {
        var array = new JsonArray();
        foreach (var (id, profile) in _profiles)
            array.Add(new JsonObject { ["id"] = id, ["name"] = profile.Name });

        return array.ToJsonString();
    }

    private (int, string, byte[]) AuthenticateResponse()
    {
        if (_profiles.IsEmpty) return NoContent();

        var (id, profile) = _profiles.First();
        var body = new JsonObject
        {
            ["accessToken"] = id,
            ["clientToken"] = id,
            ["availableProfiles"] = new JsonArray { new JsonObject { ["id"] = id, ["name"] = profile.Name } },
            ["selectedProfile"] = new JsonObject { ["id"] = id, ["name"] = profile.Name }
        };
        return Json(body.ToJsonString());
    }

    private static (int, string, byte[]) Json(string json) =>
        (200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));

    private static (int, string, byte[]) NoContent() => (204, "text/plain", Array.Empty<byte>());

    private static (int, string, byte[]) NotFound() =>
        (404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"));

    private static (int, string, byte[]) Forbidden() =>
        (403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Forbidden"));

    private static (int, string, byte[]) BadRequest() =>
        (400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Bad Request"));

    private static async Task WriteResponseAsync(NetworkStream stream, (int Status, string ContentType, byte[] Body) response)
    {
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(ReasonPhrase(response.Status))
            .Append("\r\n");
        if (response.Body.Length > 0)
            head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        head.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
        head.Append("Connection: close\r\n");
        head.Append("Cache-Control: no-cache\r\n");
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString())).ConfigureAwait(false);
        if (response.Body.Length > 0) await stream.WriteAsync(response.Body).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
        try
        {
            stream.Socket.Shutdown(SocketShutdown.Send);
        }
        catch
        {
            // 已关闭
        }
    }

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        _ => "Unknown"
    };

    #endregion

    #region 数据结构

    /// <summary>已注册的档案。</summary>
    private sealed class ProfileEntry
    {
        public required string Json { get; init; }
        public required string Name { get; init; }
    }

    /// <summary>一次进服验证记录。</summary>
    private sealed class JoinRecord
    {
        public required string Uuid { get; init; }
        public required string Name { get; init; }
        public required DateTimeOffset Expires { get; init; }
    }

    #endregion

    #region 密钥

    private static RSA LoadOrCreateKey(string path)
    {
        if (File.Exists(path))
            try
            {
                var existing = RSA.Create();
                existing.ImportFromPem(File.ReadAllText(path));
                if (existing.KeySize >= 1024) return existing;
                existing.Dispose();
            }
            catch
            {
                // 密钥文件损坏时重新生成
            }

        var rsa = RSA.Create(RsaKeySize);
        File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        return rsa;
    }

    #endregion

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // 已停止
        }

        _cts.Dispose();
        _rsa.Dispose();
    }
}
