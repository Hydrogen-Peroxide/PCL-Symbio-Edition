using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using PCL.Core.IO.Net;
using PCL.Core.IO.Net.Http;
using PCL.Core.Minecraft.IdentityModel.Extensions.YggdrasilConnect;
using PCL.Core.Minecraft.IdentityModel.OAuth;

namespace PCL;

/// <summary>
///     Yggdrasil Connect（Yggdrasil 的 OpenID Connect 扩展）外置登录支持。
///     移植自上游 PCL CE 的 <c>PCL.Core/Minecraft/Profile/Authentication/YggdrasilConnectProvider.cs</c>，
///     改为沿用本项目以 <see cref="ModProfile" /> 为中心的同步启动流程。
///     <para>流程：读取验证服务器根节点的 <c>feature.openid_configuration_url</c> → 解析 OpenID Discovery →
///     用设备代码流（device code）在浏览器里完成授权 → 拿到令牌并写入档案。</para>
/// </summary>
public static class ModYggdrasilConnect
{
    /// <summary>
    ///     验证服务器根节点里声明 OpenID Discovery 地址的字段名
    /// </summary>
    private const string DiscoveryFeatureKey = "feature.openid_configuration_url";

    /// <summary>
    ///     Yggdrasil Connect 官方公共客户端 ID 的内置表（按域名），与上游保持一致
    /// </summary>
    private static readonly Dictionary<string, string> BuiltInClientIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["littleskin.cn"] = "1514"
    };

    /// <summary>
    ///     请求的授权范围。缺少 <c>offline_access</c> 时拿不到 refresh_token，档案过期后就只能重新登录
    /// </summary>
    private static readonly string[] RequestedScopes =
    [
        "openid",
        "offline_access",
        "Yggdrasil.PlayerProfiles.Select",
        "Yggdrasil.Server.Join"
    ];

    /// <summary>
    ///     验证服务器信息
    /// </summary>
    /// <param name="AuthServer">authlib 验证服务器地址，以 <c>/authserver</c> 结尾</param>
    /// <param name="Root">验证服务器根地址，用于读取 meta</param>
    /// <param name="Discovery">OpenID Discovery 地址</param>
    /// <param name="ClientId">OAuth 客户端 ID</param>
    public sealed record ServerInfo(string AuthServer, string Root, string Discovery, string ClientId);

    /// <summary>
    ///     登录结果
    /// </summary>
    public sealed record LoginResult(string UserName, string Uuid, string AccessToken, string RefreshToken,
        string IdToken, long Expires);

    #region 地址处理

    /// <summary>
    ///     把用户输入的地址规范化为 authlib 验证服务器地址
    /// </summary>
    public static string GetAuthServer(string server)
    {
        var value = (server ?? "").Trim().TrimEnd('/');
        return value.EndsWith("/authserver", StringComparison.OrdinalIgnoreCase) ? value : value + "/authserver";
    }

    /// <summary>
    ///     把用户输入的地址规范化为验证服务器根地址
    /// </summary>
    public static string GetRoot(string server)
    {
        var value = (server ?? "").Trim().TrimEnd('/');
        return value.EndsWith("/authserver", StringComparison.OrdinalIgnoreCase)
            ? value[..^"/authserver".Length]
            : value;
    }

    /// <summary>
    ///     取得内置的公共客户端 ID
    /// </summary>
    public static string? GetBuiltInClientId(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        return BuiltInClientIds.GetValueOrDefault(host.Trim().TrimEnd('.'));
    }

    #endregion

    #region 探测

    /// <summary>
    ///     探测该验证服务器是否支持 Yggdrasil Connect。不支持时返回 <see langword="null" />。
    /// </summary>
    /// <param name="server">验证服务器地址，可带或不带 <c>/authserver</c></param>
    /// <param name="token">取消令牌</param>
    public static async Task<ServerInfo?> DiscoverAsync(string server, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(server)) return null;
        try
        {
            var root = GetRoot(server);
            if (!Uri.TryCreate(root, UriKind.Absolute, out var rootUri)) return null;

            string? discovery;
            string? serverName = null;
            using (var response = await HttpRequest.Create(root).SendAsync(cancellationToken: token)
                       .ConfigureAwait(false))
            {
                if (!response.IsSuccess) return null;
                var body = await response.AsStringAsync().ConfigureAwait(false);
                var json = ModBase.GetJson(body) as JsonObject;
                if (json is null) return null;
                serverName = json["meta"]?["serverName"]?.ToString();
                discovery = json["meta"]?[DiscoveryFeatureKey]?.ToString()
                            ?? json[DiscoveryFeatureKey]?.ToString();
            }

            if (string.IsNullOrWhiteSpace(discovery)) return null;
            if (!Uri.TryCreate(discovery, UriKind.Absolute, out var discoveryUri)) return null;

            var clientId = GetBuiltInClientId(rootUri.Host);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                using var response = await HttpRequest.Create(discoveryUri.ToString())
                    .SendAsync(cancellationToken: token).ConfigureAwait(false);
                if (!response.IsSuccess) return null;
                var body = await response.AsStringAsync().ConfigureAwait(false);
                var json = ModBase.GetJson(body) as JsonObject;
                clientId = json?["shared_client_id"]?.ToString();
            }

            if (string.IsNullOrWhiteSpace(clientId)) return null;

            ModBase.Log($"[Auth] {rootUri.Host} 支持 Yggdrasil Connect（serverName = {serverName ?? "未知"}）");
            return new ServerInfo(GetAuthServer(server), root, discoveryUri.ToString(), clientId!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Auth] 探测 Yggdrasil Connect 支持失败");
            return null;
        }
    }

    #endregion

    #region 登录

    /// <summary>
    ///     用设备代码流完成一次 Yggdrasil Connect 登录。
    /// </summary>
    /// <param name="info">验证服务器信息</param>
    /// <param name="onDeviceCode">拿到设备代码后的回调，用于提示用户在网页输入授权码并展示验证地址</param>
    /// <param name="token">取消令牌</param>
    public static async Task<LoginResult> LoginAsync(ServerInfo info, Action<DeviceCodeData> onDeviceCode,
        CancellationToken token = default)
    {
        var client = await CreateClientAsync(info, token).ConfigureAwait(false);
        var code = await client.GetCodePairAsync(RequestedScopes, token).ConfigureAwait(false);
        if (code is null) throw new Exception("验证服务器没有返回设备授权信息");
        if (code.IsError) throw new Exception(code.ErrorDescription ?? code.Error ?? "验证服务器拒绝了设备授权请求");
        if (string.IsNullOrWhiteSpace(code.DeviceCode)) throw new Exception("验证服务器没有返回设备代码");

        onDeviceCode(code);

        var interval = Math.Clamp(code.Interval ?? 5, 1, 60);
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(30, code.ExpiresIn ?? 300));
        while (true)
        {
            if (DateTime.UtcNow >= deadline) throw new Exception("授权超时，请重新发起网页登录");
            await Task.Delay(TimeSpan.FromSeconds(interval), token).ConfigureAwait(false);

            var result = await client.AuthorizeWithDeviceAsync(code, token).ConfigureAwait(false);
            if (result is null) continue;
            if (result.IsError)
            {
                switch (result.Error)
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        interval = Math.Min(interval + 5, 60);
                        continue;
                    case "access_denied":
                        throw new Exception("你取消了网页授权");
                    case "expired_token":
                        throw new Exception("授权码已过期，请重新发起网页登录");
                    default:
                        ModBase.Log($"[Auth] 设备代码轮询返回 {result.Error}：{result.ErrorDescription}");
                        continue;
                }
            }

            if (string.IsNullOrWhiteSpace(result.AccessToken)) throw new Exception("验证服务器没有返回访问令牌");
            var (name, uuid) = ReadProfileFromIdToken(result.IdToken);
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                throw new Exception("验证服务器没有返回游戏角色信息");
            ModBase.Log($"[Auth] Yggdrasil Connect 授权成功（{name}）");
            return new LoginResult(name!, uuid!, result.AccessToken!,
                result.RefreshToken ?? "",
                result.IdToken ?? "",
                DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn ?? 3600).ToUnixTimeMilliseconds());
        }
    }

    /// <summary>
    ///     用保存的 refresh_token 静默续期。失败时返回 <see langword="null" />，由调用方回退或提示重新登录。
    /// </summary>
    /// <param name="info">验证服务器信息</param>
    /// <param name="refreshToken">档案里保存的 refresh_token</param>
    /// <param name="fallbackName">令牌里没有角色信息时使用的角色名</param>
    /// <param name="fallbackUuid">令牌里没有角色信息时使用的 UUID</param>
    /// <param name="token">取消令牌</param>
    public static async Task<LoginResult?> TryRefreshAsync(ServerInfo info, string refreshToken,
        string? fallbackName, string? fallbackUuid, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        try
        {
            var client = await CreateClientAsync(info, token).ConfigureAwait(false);
            var result = await client.AuthorizeWithSilentAsync(new AuthorizeResult { RefreshToken = refreshToken },
                token).ConfigureAwait(false);
            if (result is null || result.IsError || string.IsNullOrWhiteSpace(result.AccessToken)) return null;

            var (name, uuid) = ReadProfileFromIdToken(result.IdToken);
            if (string.IsNullOrWhiteSpace(uuid)) uuid = fallbackUuid;
            if (string.IsNullOrWhiteSpace(name)) name = fallbackName;
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name)) return null;

            ModBase.Log($"[Auth] Yggdrasil Connect 静默续期成功（{name}）");
            return new LoginResult(name!, uuid!, result.AccessToken!,
                string.IsNullOrWhiteSpace(result.RefreshToken) ? refreshToken : result.RefreshToken,
                result.IdToken ?? "",
                DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn ?? 3600).ToUnixTimeMilliseconds());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Auth] Yggdrasil Connect 静默续期失败");
            return null;
        }
    }

    private static async Task<YggdrasilClient> CreateClientAsync(ServerInfo info, CancellationToken token)
    {
        var options = new YggdrasilOptions
        {
            OpenIdDiscoveryAddress = info.Discovery,
            ClientId = info.ClientId,
            OnlyDeviceAuthorize = true,
            GetClient = () => NetworkService.GetClient()
        };
        var client = new YggdrasilClient(options);
        await client.InitializeAsync(token).ConfigureAwait(false);
        return client;
    }

    #endregion

    #region 角色信息

    /// <summary>
    ///     从 id_token 的载荷里读取角色信息。载荷只做 Base64 解码、不校验签名：
    ///     令牌是通过 TLS 直接从上一步的 token 端点拿到的，不经过第三方转发。
    /// </summary>
    private static (string? name, string? uuid) ReadProfileFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return (null, null);
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return (null, null);
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))) as JsonObject;
            if (json is null) return (null, null);

            var selected = json["selectedProfile"];
            if (selected is not null)
                return (selected["name"]?.ToString(), selected["id"]?.ToString());

            if (json["availableProfiles"] is JsonArray { Count: > 0 } available)
                return (available[0]?["name"]?.ToString(), available[0]?["id"]?.ToString());

            return (null, null);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Auth] 解析 id_token 失败");
            return (null, null);
        }
    }

    #endregion
}
