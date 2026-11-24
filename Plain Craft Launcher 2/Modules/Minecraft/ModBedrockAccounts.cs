using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Core.Utils.Secret;

namespace PCL;

/// <summary>
///     基岩版 Xbox 账号的本地存档：多账号列表 + 当前选中的账号。
///     Xbox 的访问令牌只有几小时有效期，因此真正保存的是 refresh_token，
///     需要时才续期。这样重启启动器后仍能把同一个账号注入游戏进程，
///     而不会像只有内存会话时那样退回系统 Xbox 账号。
/// </summary>
internal static class ModBedrockAccounts
{
    private static readonly object Sync = new();
    private static List<Entry>? accounts;
    private static string? activeXuid;

    /// <summary>单个基岩版账号。令牌一律加密后落盘。</summary>
    internal sealed class Entry
    {
        [JsonPropertyName("xuid")] public string Xuid { get; set; } = "";

        [JsonPropertyName("gamertag")] public string Gamertag { get; set; } = "";

        /// <summary>EncryptHelper 加密后的 refresh_token；为空表示只能使用缓存会话。</summary>
        [JsonPropertyName("refresh_token")]
        public string ProtectedRefreshToken { get; set; } = "";

        /// <summary>加密后的缓存会话，命中时可以直接启动，不必每次都续期。</summary>
        [JsonPropertyName("session")]
        public string ProtectedSession { get; set; } = "";

        /// <summary>缓存会话的过期时间（Unix 秒）。</summary>
        [JsonPropertyName("session_expires_at")]
        public long SessionExpiresAt { get; set; }

        /// <summary>
        ///     加密后的 SISU 中间凭证。它是「只用 Minecraft for Android 凭证」这条链路下
        ///     唯一还能免登录续期的东西：凭它可以直接重签 XSTS，不需要 MSA 令牌。
        /// </summary>
        [JsonPropertyName("credential")]
        public string ProtectedCredential { get; set; } = "";

        /// <summary>中间凭证的过期时间（Unix 秒）。</summary>
        [JsonPropertyName("credential_expires_at")]
        public long CredentialExpiresAt { get; set; }
    }

    private sealed class Store
    {
        [JsonPropertyName("active")] public string? Active { get; set; }

        [JsonPropertyName("accounts")] public List<Entry> Accounts { get; set; } = [];
    }

    private static string StorePath =>
        Path.Combine(ModBase.pathAppdataConfig, "bedrock-accounts.json");

    /// <summary>全部已保存账号（按添加顺序）。</summary>
    internal static IReadOnlyList<Entry> All
    {
        get
        {
            EnsureLoaded();
            return accounts!;
        }
    }

    internal static string? ActiveXuid
    {
        get
        {
            EnsureLoaded();
            return activeXuid;
        }
    }

    internal static Entry? Active
    {
        get
        {
            EnsureLoaded();
            return accounts!.FirstOrDefault(x => x.Xuid == activeXuid) ?? accounts!.FirstOrDefault();
        }
    }

    internal static bool IsActive(Entry entry) => entry.Xuid == Active?.Xuid;

    private static void EnsureLoaded()
    {
        if (accounts is not null) return;
        lock (Sync)
        {
            if (accounts is not null) return;
            try
            {
                if (File.Exists(StorePath))
                {
                    var store = JsonSerializer.Deserialize<Store>(File.ReadAllText(StorePath));
                    accounts = store?.Accounts ?? [];
                    activeXuid = store?.Active;
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "读取基岩版账号存档失败，本次按无账号启动", ModBase.LogLevel.Debug);
            }

            accounts ??= [];
            // 去掉没有 XUID 的脏数据，避免生成无名卡片
            accounts.RemoveAll(x => string.IsNullOrWhiteSpace(x.Xuid));
            if (accounts.All(x => x.Xuid != activeXuid))
                activeXuid = accounts.FirstOrDefault()?.Xuid;
        }
    }

    private static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var store = new Store { Active = activeXuid, Accounts = accounts ?? [] };
            File.WriteAllText(StorePath,
                JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "保存基岩版账号存档失败", ModBase.LogLevel.Hint,
                userSummary: "基岩版账号保存失败，下次启动可能需要重新登录。");
        }
    }

    /// <summary>登录成功后登记账号，并设为当前账号。</summary>
    internal static Entry SaveLogin(BLoaderXUserLogin.LoginResult result)
    {
        EnsureLoaded();
        var session = result.Session;
        Entry entry;
        lock (Sync)
        {
            entry = accounts!.FirstOrDefault(x => x.Xuid == session.XblXuid);
            if (entry is null)
            {
                entry = new Entry { Xuid = session.XblXuid };
                accounts.Add(entry);
            }

            entry.Gamertag = session.XblGamertag;
            if (!string.IsNullOrWhiteSpace(result.RefreshToken))
                entry.ProtectedRefreshToken = EncryptHelper.SecretEncrypt(result.RefreshToken);
            CacheSession(entry, session);
            CacheCredential(entry, result.Credential);
            activeXuid = entry.Xuid;
            Save();
        }

        return entry;
    }

    /// <summary>切换当前使用的账号；返回该账号可用的会话（必要时自动续期）。</summary>
    internal static async Task<BLoaderXUserBridge.Session> SetActiveAsync(string xuid,
        CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        lock (Sync)
        {
            var entry = accounts!.FirstOrDefault(x => x.Xuid == xuid)
                        ?? throw new InvalidOperationException("该基岩版账号已不存在，请重新添加。");
            activeXuid = entry.Xuid;
            Save();
        }

        return await EnsureActiveSessionAsync(cancellationToken);
    }

    internal static void Remove(string xuid)
    {
        EnsureLoaded();
        lock (Sync)
        {
            accounts!.RemoveAll(x => x.Xuid == xuid);
            if (activeXuid == xuid)
                activeXuid = accounts.FirstOrDefault()?.Xuid;
            Save();
        }

        // 被删掉的正好是当前注入游戏的账号时，必须一起清掉内存会话，
        // 否则下一次启动仍会把已删除的账号喂给游戏。
        if (BLoaderXUserBridge.CurrentSession?.XblXuid == xuid)
            BLoaderXUserBridge.SetSession(null);
    }

    internal static void ForgetSession()
    {
        foreach (var entry in All)
        {
            entry.ProtectedSession = "";
            entry.SessionExpiresAt = 0;
        }

        lock (Sync) Save();
    }

    /// <summary>
    ///     取得当前选中账号的可用会话，依次尝试：
    ///     内存会话 → 磁盘缓存会话 → refresh_token 续期 → 用 SISU 中间凭证重签 XSTS。
    ///     没有任何账号时返回 null（此时按系统 Xbox 账号启动）。
    /// </summary>
    internal static async Task<BLoaderXUserBridge.Session?> EnsureActiveSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var entry = Active;
        if (entry is null) return null;

        if (BLoaderXUserBridge.CurrentSession is { } live &&
            live.XblXuid == entry.Xuid && IsUsable(live))
            return live;

        var cached = ReadCachedSession(entry);
        if (cached is not null && IsUsable(cached))
        {
            BLoaderXUserBridge.SetSession(cached);
            return cached;
        }

        // refresh_token 跨度最长，但它需要 offline_access，该客户端在授权阶段就会拒绝；
        // 只有令牌响应真的下发过才走这条。
        var refreshToken = EncryptHelper.SecretDecrypt(entry.ProtectedRefreshToken);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var refreshed = await BLoaderXUserLogin.RefreshAsync(refreshToken, cancellationToken);
            SaveLogin(refreshed);
            return refreshed.Session;
        }

        // 用缓存的 SISU 票据重签 XSTS：不需要 MSA 令牌，也不需要用户再登录一次。
        var credential = ReadCredential(entry);
        if (credential is not null)
        {
            var renewed = await BLoaderXUserLogin.RenewXstsAsync(credential, cancellationToken);
            SaveLogin(renewed);
            return renewed.Session;
        }

        throw new InvalidOperationException(
            $"账号 {entry.Gamertag} 的登录凭据已失效，请重新登录该账号。");
    }

    private static BLoaderXUserBridge.Session? ReadCachedSession(Entry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ProtectedSession)) return null;
        if (entry.SessionExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
        try
        {
            var json = EncryptHelper.SecretDecrypt(entry.ProtectedSession);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<BLoaderXUserBridge.Session>(json);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "解析基岩版缓存会话失败，将重新续期", ModBase.LogLevel.Debug);
            return null;
        }
    }

    private static void CacheSession(Entry entry, BLoaderXUserBridge.Session session)
    {
        try
        {
            entry.ProtectedSession = EncryptHelper.SecretEncrypt(JsonSerializer.Serialize(session));
            entry.SessionExpiresAt = MinExpiry(session);
            var minutes = Math.Max(0, (entry.SessionExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) / 60);
            ModBase.Log($"[Bedrock] 已缓存账号 {session.XblGamertag} 的会话，有效期约 {minutes} 分钟",
                ModBase.LogLevel.Normal);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "缓存基岩版会话失败", ModBase.LogLevel.Debug);
            entry.ProtectedSession = "";
            entry.SessionExpiresAt = 0;
        }
    }

    private static bool IsUsable(BLoaderXUserBridge.Session session) =>
        MinExpiry(session) > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120;

    private static void CacheCredential(Entry entry, BLoaderXUserLogin.SisuCredential? credential)
    {
        if (credential is null) return;
        try
        {
            entry.ProtectedCredential = EncryptHelper.SecretEncrypt(JsonSerializer.Serialize(credential));
            entry.CredentialExpiresAt =
                long.TryParse(credential.ExpiresAt, out var expiry) ? expiry : 0;
            var minutes = Math.Max(0, (entry.CredentialExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) / 60);
            ModBase.Log($"[Bedrock] 已缓存账号 {credential.Gamertag} 的 SISU 凭证，可免登录续期约 {minutes} 分钟",
                ModBase.LogLevel.Normal);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "缓存基岩版 SISU 凭证失败", ModBase.LogLevel.Debug);
            entry.ProtectedCredential = "";
            entry.CredentialExpiresAt = 0;
        }
    }

    private static BLoaderXUserLogin.SisuCredential? ReadCredential(Entry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ProtectedCredential)) return null;
        // 留 2 分钟余量，避免拿到票据后刚发出去就过期
        if (entry.CredentialExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120) return null;
        try
        {
            var json = EncryptHelper.SecretDecrypt(entry.ProtectedCredential);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<BLoaderXUserLogin.SisuCredential>(json);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "解析基岩版 SISU 凭证失败，需要重新登录", ModBase.LogLevel.Debug);
            return null;
        }
    }

    /// <summary>会话里所有票据的最早过期时间，取最小值才不会用到已经过期的票据。</summary>
    private static long MinExpiry(BLoaderXUserBridge.Session session)
    {
        var values = new[]
        {
            session.XblTokenExpiryEpoch,
            session.SisuExpiryEpoch,
            session.MultiplayerExpiryEpoch,
            session.RealmsExpiryEpoch,
            session.LicensingExpiryEpoch
        };
        var min = long.MaxValue;
        foreach (var value in values)
        {
            if (!long.TryParse(value, out var parsed) || parsed <= 0) continue;
            if (parsed < min) min = parsed;
        }

        return min == long.MaxValue ? 0 : min;
    }
}
