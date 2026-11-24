using System.Buffers.Binary;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Core.App;

namespace PCL;

/// <summary>使用 Minecraft for Android 的 Xbox/SISU 链路建立 GDK 会话。</summary>
public static class BLoaderXUserLogin
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string AndroidClientId = "0000000048183522";

    // 该客户端 ID 在「设备码申请」阶段接受 offline_access，但用户在浏览器完成登录后
    // 的令牌交换会被拒（unauthorized scopes），因此拿不到 refresh_token。
    // 账号持久化改为按 tickets 自身有效期缓存会话，见 ModBedrockAccounts。
    private const string AndroidScope = "service::user.auth.xboxlive.com::MBI_SSL";
    private const string AndroidUserAgent = "XAL Android 2025.04.20250326.000";
    private const string DeviceType = "Android";
    private const string DeviceVersion = "13";
    private const string Sandbox = "RETAIL";
    private const string XboxLiveRelyingParty = "http://xboxlive.com";
    private const string PlayFabRelyingParty = "https://b980a380.minecraft.playfabapi.com/";
    private const string MultiplayerRelyingParty = "https://multiplayer.minecraft.net/";
    private const string RealmsRelyingParty = "https://pocket.realms.minecraft.net/";
    private const string LicensingRelyingParty = "http://licensing.xboxlive.com";

    /// <summary>
    ///     可在没有 MSA 令牌的情况下重签 XSTS 的中间凭证。
    ///     Minecraft for Android 的客户端 ID 不接受 offline_access（授权阶段直接拒绝），
    ///     拿不到 refresh_token，因此账号持久化只能依赖 SISU 票据自身的有效期：
    ///     只要它还活着，凭证明密钥就能重新向 xsts.auth.xboxlive.com 换服务票据。
    /// </summary>
    public sealed record SisuCredential(
        string ProofPrivateBlobBase64,
        string DeviceToken,
        string SisuUserToken,
        string SisuTitleToken,
        string Xuid,
        string Gamertag,
        string IdentityToken,
        string IdentityUhs,
        string IdentityExpiry,
        string? AgeGroup,
        string? Privileges,
        string ExpiresAt);

    /// <summary>一次登录的结果：可用于启动游戏的会话，以及后续恢复会话所需的凭证。</summary>
    public sealed record LoginResult(
        BLoaderXUserBridge.Session Session,
        string? RefreshToken,
        SisuCredential Credential);

    public static async Task<LoginResult> LoginAsync(
        Action<string, string>? onDeviceCode = null,
        CancellationToken cancellationToken = default)
    {
        var device = await StartDeviceCodeAsync(cancellationToken);
        onDeviceCode?.Invoke(device.UserCode, device.VerificationUri);

        var msa = await WaitForMsaTokenAsync(device, cancellationToken);
        var (session, credential) = await BuildSessionAsync(msa.AccessToken, cancellationToken);
        BLoaderXUserBridge.SetSession(session);
        ModBase.Log("[Bedrock] Android Xbox 会话已准备完成", ModBase.LogLevel.Normal);
        return new LoginResult(session, msa.RefreshToken, credential);
    }

    /// <summary>
    ///     用保存的 refresh_token 重新建立会话。当前客户端通常拿不到 refresh_token，
    ///     保留这条路径以便令牌响应里出现时自动升级为长期续期。
    /// </summary>
    public static async Task<LoginResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("缺少 refresh_token，无法恢复基岩版账号，请重新登录。");

        var msa = await ExchangeRefreshTokenAsync(refreshToken, cancellationToken);
        var (session, credential) = await BuildSessionAsync(msa.AccessToken, cancellationToken);
        BLoaderXUserBridge.SetSession(session);
        ModBase.Log("[Bedrock] 已用保存的凭据恢复 Xbox 会话", ModBase.LogLevel.Normal);
        return new LoginResult(session, msa.RefreshToken ?? refreshToken, credential);
    }

    /// <summary>
    ///     用缓存的 SISU 票据重新签发 XSTS 服务票据。不需要 MSA 令牌，也不需要用户再登录一次，
    ///     是「只能用 Minecraft for Android 凭证」这条链路下能拿到的最长有效期。
    /// </summary>
    public static async Task<LoginResult> RenewXstsAsync(
        SisuCredential credential,
        CancellationToken cancellationToken = default)
    {
        using var proofKey = RestoreProofKey(credential.ProofPrivateBlobBase64);
        var session = await BuildServiceTokensAsync(credential, proofKey, cancellationToken);
        BLoaderXUserBridge.SetSession(session);
        ModBase.Log("[Bedrock] 已用缓存的 SISU 票据重新签发服务票据", ModBase.LogLevel.Normal);
        return new LoginResult(session, null, credential);
    }

    private static async Task<(BLoaderXUserBridge.Session Session, SisuCredential Credential)> BuildSessionAsync(
        string msaToken,
        CancellationToken cancellationToken)
    {
        using var proofKey = CreateProofKey();
        var deviceToken = await GetDeviceTokenAsync(proofKey, cancellationToken);
        var sisu = await GetSisuAuthorizationAsync(msaToken, deviceToken.Token, proofKey, cancellationToken);

        var identity = ParseXboxToken(
            sisu["AuthorizationToken"]?.AsObject()
            ?? throw new InvalidOperationException("SISU 响应缺少 Xbox 身份票据。"),
            "SISU 身份票据");
        if (string.IsNullOrWhiteSpace(identity.Xuid))
            throw new InvalidOperationException("SISU 身份票据中没有 XUID。");

        var userToken = ParseSisuTicket(sisu, "UserToken");
        var titleToken = ParseSisuTicket(sisu, "TitleToken");

        // 这张凭证能活多久，取决于设备票据和 SISU 三张票据里最早到期的那张
        var credential = new SisuCredential(
            Convert.ToBase64String(proofKey.PrivateBlob),
            deviceToken.Token,
            userToken.Token,
            titleToken.Token,
            identity.Xuid,
            string.IsNullOrWhiteSpace(identity.Gamertag) ? identity.Xuid : identity.Gamertag,
            identity.Token,
            identity.Uhs,
            identity.Expiry,
            identity.AgeGroup,
            identity.Privileges,
            MinEpoch(deviceToken.Expiry, userToken.Expiry, titleToken.Expiry, identity.Expiry));

        // 这几个时间点决定账号能免登录用多久，出问题时先看这一行
        ModBase.Log($"[Bedrock] SISU 凭证有效期：设备 {FormatEpoch(deviceToken.Expiry)}，" +
                    $"用户 {FormatEpoch(userToken.Expiry)}，标题 {FormatEpoch(titleToken.Expiry)}，" +
                    $"身份 {FormatEpoch(identity.Expiry)}", ModBase.LogLevel.Normal);

        var session = await BuildServiceTokensAsync(credential, proofKey, cancellationToken);
        return (session, credential);
    }

    private static async Task<BLoaderXUserBridge.Session> BuildServiceTokensAsync(
        SisuCredential credential,
        ProofKeyMaterial proofKey,
        CancellationToken cancellationToken)
    {
        ModBase.Log("[Bedrock] 正在获取服务票据", ModBase.LogLevel.Debug);
        var sisuToken = await GetXstsAsync(credential.DeviceToken, credential.SisuTitleToken, credential.SisuUserToken,
            PlayFabRelyingParty, proofKey, cancellationToken);
        var multiplayer = await GetXstsAsync(credential.DeviceToken, credential.SisuTitleToken, credential.SisuUserToken,
            MultiplayerRelyingParty, proofKey, cancellationToken);
        var realms = await GetXstsAsync(credential.DeviceToken, credential.SisuTitleToken, credential.SisuUserToken,
            RealmsRelyingParty, proofKey, cancellationToken);
        XboxToken? licensing = null;
        try
        {
            licensing = await GetXstsAsync(credential.DeviceToken, credential.SisuTitleToken, credential.SisuUserToken,
                LicensingRelyingParty, proofKey, cancellationToken);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "Xbox licensing token 获取失败，将继续使用基础基岩登录会话", ModBase.LogLevel.Debug);
        }

        ModBase.Log($"[Bedrock] 票据有效期：身份 {FormatEpoch(credential.IdentityExpiry)}，" +
                    $"SISU 凭证 {FormatEpoch(credential.ExpiresAt)}，XSTS {FormatEpoch(sisuToken.Expiry)}",
            ModBase.LogLevel.Debug);

        return new BLoaderXUserBridge.Session(
            credential.ProofPrivateBlobBase64,
            credential.Xuid,
            credential.Gamertag,
            credential.IdentityToken,
            credential.IdentityUhs,
            credential.IdentityExpiry,
            sisuToken.Token,
            sisuToken.Uhs,
            PlayFabRelyingParty,
            sisuToken.Expiry,
            multiplayer.Token,
            multiplayer.Uhs,
            MultiplayerRelyingParty,
            multiplayer.Expiry,
            realms.Token,
            realms.Uhs,
            RealmsRelyingParty,
            realms.Expiry,
            licensing?.Token,
            licensing?.Uhs,
            licensing is null ? null : LicensingRelyingParty,
            licensing?.Expiry,
            credential.AgeGroup,
            credential.Privileges);
    }

    private static async Task<DeviceCodeData> StartDeviceCodeAsync(CancellationToken cancellationToken)
    {
        var response = await RequestDeviceCodeAsync(AndroidScope, cancellationToken);
        if (response["error"] is not null || string.IsNullOrWhiteSpace(response["device_code"]?.ToString()))
            throw new InvalidOperationException(response["error_description"]?.ToString()
                                                ?? response["error"]?.ToString()
                                                ?? "Microsoft 没有返回 Android 设备码。");

        var interval = response["interval"]?.GetValue<int>() ?? 5;
        if (interval is < 1 or > 60) interval = 5;
        var expiresIn = response["expires_in"]?.GetValue<int>() ?? 900;
        return new DeviceCodeData(
            Required(response, "device_code"),
            response["user_code"]?.ToString() ?? throw new InvalidOperationException("Microsoft 没有返回用户验证码。"),
            response["verification_uri"]?.ToString() ?? "https://www.microsoft.com/link",
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)),
            interval);
    }

    private static Task<JsonObject> RequestDeviceCodeAsync(string scope, CancellationToken cancellationToken)
    {
        // 这里不能用 using：本方法没有 await，using 会在返回 Task 的同时就把 content 释放掉，
        // 真正发送时就会抛 ObjectDisposedException。content 由 PostFormAsync 负责释放。
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = AndroidClientId,
            ["scope"] = scope,
            ["response_type"] = "device_code"
        });
        return PostFormAsync("https://login.live.com/oauth20_connect.srf", request, cancellationToken);
    }

    private static async Task<MsaToken> WaitForMsaTokenAsync(DeviceCodeData device, CancellationToken cancellationToken)
    {
        var interval = device.Interval;
        while (DateTimeOffset.UtcNow < device.ExpiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            using var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = AndroidClientId,
                ["device_code"] = device.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });
            var response = await PostFormAsync(
                "https://login.live.com/oauth20_token.srf", request, cancellationToken);
            var error = response["error"]?.ToString();
            if (error == "authorization_pending") continue;
            if (error == "slow_down")
            {
                interval = Math.Min(60, interval + 5);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException(response["error_description"]?.ToString() ?? error);
            return ReadMsaToken(response);
        }
        throw new TimeoutException("Microsoft Android 设备码已过期，请重新登录。");
    }

    /// <summary>用 refresh_token 换新的 MSA 访问令牌，不打扰用户。</summary>
    private static async Task<MsaToken> ExchangeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = AndroidClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = AndroidScope
        });
        var response = await PostFormAsync(
            "https://login.live.com/oauth20_token.srf", request, cancellationToken);
        var error = response["error"]?.ToString();
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException(response["error_description"]?.ToString() ?? error);
        return ReadMsaToken(response);
    }

    private static MsaToken ReadMsaToken(JsonObject response)
    {
        var accessToken = Required(response, "access_token");
        // 未申请 offline_access 或服务端未下发时允许为空，此时账号只能用缓存的会话。
        var refreshToken = response["refresh_token"]?.ToString();
        return new MsaToken(accessToken, string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken);
    }

    private static async Task<(string Token, string Expiry)> GetDeviceTokenAsync(
        ProofKeyMaterial proofKey,
        CancellationToken cancellationToken)
    {
        var response = await PostSignedJsonAsync(
            "https://device.auth.xboxlive.com/device/authenticate",
            new
            {
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT",
                Properties = new
                {
                    AuthMethod = "ProofOfPossession",
                    Id = Guid.NewGuid().ToString(),
                    DeviceType,
                    Version = DeviceVersion,
                    ProofKey = proofKey.Jwk
                }
            }, proofKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(response["Token"]?.ToString()))
            throw new InvalidOperationException("Xbox 设备认证没有返回设备票据。");
        return (Required(response, "Token", "Xbox 设备票据"), ParseExpiry(response["NotAfter"]?.ToString()));
    }

    /// <summary>读出 SISU 票据的 Token 与它自己的过期时间。</summary>
    private static (string Token, string Expiry) ParseSisuTicket(JsonObject sisu, string name)
    {
        var ticket = sisu[name]?.AsObject() ?? throw new InvalidOperationException($"SISU 响应缺少 {name}。");
        return (Required(ticket, "Token", name), ParseExpiry(ticket["NotAfter"]?.ToString()));
    }

    /// <summary>取多个 Unix 秒时间戳里最早的一个，任一项不可解析就忽略。</summary>
    private static string MinEpoch(params string?[] values)
    {
        var min = long.MaxValue;
        foreach (var value in values)
        {
            if (!long.TryParse(value, out var parsed) || parsed <= 0) continue;
            if (parsed < min) min = parsed;
        }

        return min == long.MaxValue ? "0" : min.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatEpoch(string? epoch) =>
        long.TryParse(epoch, out var parsed) && parsed > 0
            ? DateTimeOffset.FromUnixTimeSeconds(parsed).ToLocalTime().ToString("MM-dd HH:mm:ss")
            : "未知";

    private static Task<JsonObject> GetSisuAuthorizationAsync(
        string msaToken,
        string deviceToken,
        ProofKeyMaterial proofKey,
        CancellationToken cancellationToken) =>
        PostSignedJsonAsync(
            "https://sisu.xboxlive.com/authorize",
            new
            {
                AccessToken = $"t={msaToken}",
                AppId = AndroidClientId,
                DeviceToken = deviceToken,
                ProofKey = proofKey.Jwk,
                RelyingParty = XboxLiveRelyingParty,
                Sandbox,
                SiteName = "user.auth.xboxlive.com",
                UseModernGamertag = true
            }, proofKey, cancellationToken);

    private static async Task<XboxToken> GetXstsAsync(
        string deviceToken,
        string titleToken,
        string userToken,
        string relyingParty,
        ProofKeyMaterial proofKey,
        CancellationToken cancellationToken)
    {
        var response = await PostSignedJsonAsync(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            new
            {
                RelyingParty = relyingParty,
                TokenType = "JWT",
                Properties = new
                {
                    SandboxId = Sandbox,
                    DeviceToken = deviceToken,
                    TitleToken = titleToken,
                    UserTokens = new[] { userToken }
                }
            }, proofKey, cancellationToken);
        return ParseXboxToken(response, relyingParty);
    }

    private static XboxToken ParseXboxToken(JsonObject response, string serviceName)
    {
        var claims = response["DisplayClaims"]?["xui"]?[0]?.AsObject()
                     ?? throw new InvalidOperationException($"{serviceName} 响应缺少 XUser 信息。");
        return new XboxToken(
            Required(response, "Token", serviceName),
            Required(claims, "uhs", serviceName),
            claims["xid"]?.ToString() ?? claims["xuid"]?.ToString() ?? "",
            claims["gtg"]?.ToString() ?? claims["gamertag"]?.ToString() ?? "",
            claims["agg"]?.ToString(),
            claims["prv"]?.ToString(),
            ParseExpiry(response["NotAfter"]?.ToString()));
    }

    private static ProofKeyMaterial CreateProofKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        if (parameters.Q.X is not { Length: 32 } x
            || parameters.Q.Y is not { Length: 32 } y
            || parameters.D is not { Length: 32 } d)
        {
            key.Dispose();
            throw new CryptographicException("无法导出 Android P-256 证明密钥。");
        }

        var privateBlob = new byte[8 + 32 * 3];
        BinaryPrimitives.WriteUInt32LittleEndian(privateBlob.AsSpan(0, 4), 0x32534345);
        BinaryPrimitives.WriteUInt32LittleEndian(privateBlob.AsSpan(4, 4), 32);
        Buffer.BlockCopy(x, 0, privateBlob, 8, 32);
        Buffer.BlockCopy(y, 0, privateBlob, 40, 32);
        Buffer.BlockCopy(d, 0, privateBlob, 72, 32);

        var jwk = new JsonObject
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["alg"] = "ES256",
            ["use"] = "sig",
            ["x"] = Base64Url(x),
            ["y"] = Base64Url(y)
        };
        return new ProofKeyMaterial(key, privateBlob, jwk);
    }

    /// <summary>
    ///     从保存的证明密钥字节还原出可签名的 ECDsa。
    ///     重签 XSTS 时 xsts.auth.xboxlive.com 会校验签名是否出自签发 SISU 时的那把密钥，
    ///     所以必须复用原密钥而不是新生成一把。
    /// </summary>
    private static ProofKeyMaterial RestoreProofKey(string base64Blob)
    {
        var blob = Convert.FromBase64String(base64Blob);
        if (blob.Length < 8 + 32 * 3)
            throw new InvalidOperationException("保存的证明密钥长度不正确，请重新登录该账号。");

        var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = blob.AsSpan(8, 32).ToArray(),
                Y = blob.AsSpan(40, 32).ToArray()
            },
            D = blob.AsSpan(72, 32).ToArray()
        });
        return new ProofKeyMaterial(key, blob, null);
    }

    private static async Task<JsonObject> PostSignedJsonAsync(
        string url,
        object body,
        ProofKeyMaterial proofKey,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", AndroidUserAgent);
        request.Headers.TryAddWithoutValidation("X-Xbl-Contract-Version", "1");
        request.Headers.TryAddWithoutValidation("Signature", CreateSignature(request.RequestUri!, bytes, proofKey.Key));

        using var response = await Http.SendAsync(request, cancellationToken);
        var bodyJson = await ReadObjectAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var xErr = response.Headers.TryGetValues("X-Err", out var values)
                ? values.FirstOrDefault()
                : null;
            var message = bodyJson["Message"]?.ToString()
                          ?? bodyJson["message"]?.ToString()
                          ?? bodyJson["error_description"]?.ToString()
                          ?? bodyJson["error"]?.ToString()
                          ?? bodyJson["XErr"]?.ToString()
                          ?? bodyJson["xerr"]?.ToString()
                          ?? xErr;
            throw new InvalidOperationException(message is { Length: > 0 }
                ? $"Android Xbox 请求失败：{(int)response.StatusCode} {message}"
                : $"Android Xbox 请求失败：{(int)response.StatusCode}");
        }
        return bodyJson;
    }

    private static string CreateSignature(Uri uri, byte[] body, ECDsa key)
    {
        var timestamp = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10_000
                                + 116444736000000000L);
        var prefix = new byte[14];
        BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt64BigEndian(prefix.AsSpan(5, 8), timestamp);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prefix);
        hash.AppendData(Encoding.UTF8.GetBytes("POST"));
        hash.AppendData(new byte[] { 0 });
        hash.AppendData(Encoding.UTF8.GetBytes(uri.PathAndQuery));
        hash.AppendData(new byte[] { 0 });
        hash.AppendData(new byte[] { 0 });
        hash.AppendData(body);
        hash.AppendData(new byte[] { 0 });

        var signature = key.SignHash(hash.GetHashAndReset(), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (signature.Length != 64) throw new CryptographicException("Android Xbox 签名长度错误。");
        var output = new byte[76];
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(4, 8), timestamp);
        Buffer.BlockCopy(signature, 0, output, 12, signature.Length);
        return Convert.ToBase64String(output);
    }

    private static async Task<JsonObject> PostFormAsync(string url, HttpContent content, CancellationToken cancellationToken)
    {
        using (content)
        {
            using var response = await Http.PostAsync(url, content, cancellationToken);
            return await ReadObjectAsync(response, cancellationToken);
        }
    }

    private static async Task<JsonObject> ReadObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?.AsObject() ?? new JsonObject();

    private static string Required(JsonObject value, string name, string context = "登录响应") =>
        value[name]?.ToString() is { Length: > 0 } result
            ? result
            : throw new InvalidOperationException($"{context}缺少字段：{name}");

    private static string RequiredObjectField(JsonObject? value, string name, string context)
    {
        if (value is null) throw new InvalidOperationException($"{context}缺少对象。");
        return Required(value, name, context);
    }

    private static string ParseExpiry(string? value)
    {
        if (DateTimeOffset.TryParse(value, out var parsed))
            return parsed.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private sealed record DeviceCodeData(
        string DeviceCode,
        string UserCode,
        string VerificationUri,
        DateTimeOffset ExpiresAt,
        int Interval);

    private sealed record MsaToken(string AccessToken, string? RefreshToken);

    private sealed record XboxToken(
        string Token,
        string Uhs,
        string Xuid,
        string Gamertag,
        string? AgeGroup,
        string? Privileges,
        string Expiry);

    private sealed class ProofKeyMaterial : IDisposable
    {
        public ProofKeyMaterial(ECDsa key, byte[] privateBlob, JsonObject? jwk)
        {
            Key = key;
            PrivateBlob = privateBlob;
            Jwk = jwk;
        }

        public ECDsa Key { get; }
        public byte[] PrivateBlob { get; }

        /// <summary>仅设备认证请求需要带回 JWK；从保存的密钥还原时为 null。</summary>
        public JsonObject? Jwk { get; }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(PrivateBlob);
            Key.Dispose();
        }
    }
}
