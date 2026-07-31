using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PCL.Core.Minecraft.Bedrock;

/// <summary>
/// 基岩版版本清单服务，负责获取和缓存基岩版版本列表。
/// </summary>
public static class BedrockVersionManifest
{
    private static readonly HttpClient _httpClient = new();
    private static List<BedrockBuildInfo>? _cachedVersions;
    private static readonly string CacheFilePath;
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);

    private const string DefaultManifestUrl = "https://data.mcappx.com/v2/bedrock.json";

    static BedrockVersionManifest()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PCL-Symbio/1.0");
        CacheFilePath = Path.Combine(Path.GetTempPath(), "PCL-Symbio", "bedrock_version_cache.json");
    }

    /// <summary>
    /// 获取版本列表（优先使用缓存）。
    /// </summary>
    public static async Task<List<BedrockBuildInfo>> GetVersionsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedVersions != null)
            return _cachedVersions;

        // 尝试从本地缓存加载
        if (!forceRefresh && TryLoadFromCache(out var cached))
        {
            _cachedVersions = cached;
            return _cachedVersions;
        }

        // 从网络获取
        try
        {
            var versions = await FetchFromNetworkAsync();
            if (versions.Count > 0)
            {
                _cachedVersions = versions;
                SaveToCache(versions);
                return _cachedVersions;
            }
        }
        catch
        {
            // 网络失败，忽略
        }

        // 降级到过期缓存
        if (TryLoadFromCache(out var fallback, ignoreExpiry: true))
        {
            _cachedVersions = fallback;
            return _cachedVersions;
        }

        return [];
    }

    /// <summary>
    /// 强制刷新版本列表。
    /// </summary>
    public static async Task<List<BedrockBuildInfo>> RefreshVersionsAsync()
    {
        _cachedVersions = null;
        return await GetVersionsAsync(forceRefresh: true);
    }

    private static async Task<List<BedrockBuildInfo>> FetchFromNetworkAsync()
    {
        var json = await _httpClient.GetStringAsync(DefaultManifestUrl);
        return ParseVersionJson(json);
    }

    private static List<BedrockBuildInfo> ParseVersionJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 尝试获取 "From_mcappx.com" 属性（mcappx 源格式）
        JsonElement versionsDict;
        if (root.TryGetProperty("From_mcappx.com", out var fromProp))
            versionsDict = fromProp;
        else
        {
            // 动态查找第一个非 CreationTime 的属性
            var first = root.EnumerateObject().FirstOrDefault(p => p.Name != "CreationTime");
            if (first.Value.ValueKind == JsonValueKind.Undefined)
                return [];
            versionsDict = first.Value;
        }

        if (versionsDict.ValueKind != JsonValueKind.Object)
            return [];

        var result = new List<BedrockBuildInfo>();
        foreach (var prop in versionsDict.EnumerateObject())
        {
            try
            {
                var buildInfo = JsonSerializer.Deserialize<BedrockBuildInfo>(prop.Value.GetRawText());
                if (buildInfo == null) continue;

                if (string.IsNullOrEmpty(buildInfo.ID))
                    buildInfo.ID = prop.Name;

                if (string.IsNullOrEmpty(buildInfo.ID)) continue;
                if (buildInfo.Variations.Count == 0) continue;

                result.Add(buildInfo);
            }
            catch
            {
                // 跳过解析失败的条目
            }
        }

        return result;
    }

    private static bool TryLoadFromCache(out List<BedrockBuildInfo> versions, bool ignoreExpiry = false)
    {
        versions = [];
        try
        {
            if (!File.Exists(CacheFilePath)) return false;

            if (!ignoreExpiry)
            {
                var lastWrite = File.GetLastWriteTime(CacheFilePath);
                if (DateTime.Now - lastWrite > CacheMaxAge)
                    return false;
            }

            var json = File.ReadAllText(CacheFilePath);
            var cached = JsonSerializer.Deserialize<List<BedrockBuildInfo>>(json);
            if (cached != null && cached.Count > 0)
            {
                versions = cached;
                return true;
            }
        }
        catch
        {
            // 缓存损坏，忽略
        }
        return false;
    }

    private static void SaveToCache(List<BedrockBuildInfo> versions)
    {
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(versions);
            File.WriteAllText(CacheFilePath, json);
        }
        catch
        {
            // 缓存写入失败不影响主流程
        }
    }
}