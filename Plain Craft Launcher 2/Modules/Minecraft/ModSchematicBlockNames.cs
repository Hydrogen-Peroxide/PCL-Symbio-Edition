using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     方块名称本地化：优先读实例 assets 索引里的 zh_cn / en_us 语言文件，
///     取不到时退回客户端 JAR 自带的 en_us，最后退回方块 ID。
/// </summary>
public static class ModSchematicBlockNames
{
    private static readonly Dictionary<string, Dictionary<string, string>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取方块的显示名称；<paramref name="blockId" /> 为已去掉命名空间的 ID。</summary>
    public static string Get(string blockId, string? jarPath)
    {
        var id = blockId.Trim();
        if (id.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) id = id[10..];
        if (id.Length == 0) return blockId;

        var map = GetLanguage(jarPath);
        foreach (var key in CandidateKeys(id))
            if (map.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name))
                return name;
        return blockId;
    }

    private static IEnumerable<string> CandidateKeys(string id)
    {
        yield return "block.minecraft." + id;
        yield return "block." + id;
        yield return "tile." + id + ".name";
        // 墙上的变体等没有独立翻译时，退回基方块名
        if (id.Contains("_wall_", StringComparison.Ordinal))
            yield return "block.minecraft." + id.Replace("_wall_", "_", StringComparison.Ordinal);
        if (id.StartsWith("wall_", StringComparison.Ordinal))
            yield return "block.minecraft." + id[5..];
    }

    private static Dictionary<string, string> GetLanguage(string? jarPath)
    {
        var cacheKey = jarPath ?? "";
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        Dictionary<string, string> map = [];
        try
        {
            var language = Lang.IsChineseMainland ? "zh_cn" : "en_us";
            if (language != "en_us") map = LoadFromAssets(jarPath, language);
            if (map.Count == 0) map = LoadFromJar(jarPath, "en_us");
            if (map.Count == 0 && language != "en_us") map = LoadFromJar(jarPath, language);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SchematicPreview] 读取方块名称翻译失败", ModBase.LogLevel.Developer);
        }

        if (Cache.Count > 4) Cache.Clear();
        Cache[cacheKey] = map;
        return map;
    }

    #region 从 assets 索引读取（zh_cn 等非内置语言）

    private static Dictionary<string, string> LoadFromAssets(string? jarPath, string language)
    {
        if (string.IsNullOrEmpty(jarPath)) return [];
        var minecraft = GetMinecraftDirectory(jarPath);
        if (minecraft is null) return [];
        var assets = Path.Combine(minecraft, "assets");
        if (!Directory.Exists(assets)) return [];

        var entry = "minecraft/lang/" + language + ".json";
        var hash = FindHashInIndex(assets, GetAssetIndexId(jarPath), entry);
        hash ??= FindHashInAnyIndex(assets, entry);
        if (hash is null || hash.Length < 2) return [];

        var objectPath = Path.Combine(assets, "objects", hash[..2], hash);
        return File.Exists(objectPath) ? ReadLanguageFile(objectPath) : [];
    }

    /// <summary>从 <c>版本\版本.jar</c> 反推 <c>.minecraft</c> 目录。</summary>
    private static string? GetMinecraftDirectory(string jarPath)
    {
        try
        {
            var versionDir = Path.GetDirectoryName(Path.GetFullPath(jarPath));
            if (versionDir is null) return null;
            var versionsDir = Path.GetDirectoryName(versionDir);
            if (versionsDir is null) return null;
            var minecraft = Path.GetDirectoryName(versionsDir);
            return minecraft;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>版本 JSON 里记录的资源索引编号。</summary>
    private static string? GetAssetIndexId(string? jarPath)
    {
        if (string.IsNullOrEmpty(jarPath)) return null;
        try
        {
            var versionJson = Path.ChangeExtension(jarPath, ".json");
            if (!File.Exists(versionJson)) return null;
            using var document = JsonDocument.Parse(File.ReadAllBytes(versionJson));
            var root = document.RootElement;
            if (root.TryGetProperty("assetIndex", out var assetIndex) &&
                assetIndex.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } value)
                return value;
            if (root.TryGetProperty("assets", out var assets) && assets.GetString() is { Length: > 0 } legacy)
                return legacy;
        }
        catch
        {
            // 版本 JSON 缺失或损坏时退回扫描索引目录
        }

        return null;
    }

    private static string? FindHashInIndex(string assets, string? indexId, string entry)
    {
        if (string.IsNullOrEmpty(indexId)) return null;
        var path = Path.Combine(assets, "indexes", indexId + ".json");
        return File.Exists(path) ? FindHashInIndexFile(path, entry) : null;
    }

    private static string? FindHashInAnyIndex(string assets, string entry)
    {
        try
        {
            var directory = Path.Combine(assets, "indexes");
            if (!Directory.Exists(directory)) return null;
            foreach (var file in Directory.GetFiles(directory, "*.json")
                         .OrderByDescending(path => Path.GetFileNameWithoutExtension(path).Length)
                         .ThenByDescending(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal))
            {
                var hash = FindHashInIndexFile(file, entry);
                if (hash is not null) return hash;
            }
        }
        catch
        {
            // 索引目录不可读时忽略
        }

        return null;
    }

    private static string? FindHashInIndexFile(string path, string entry)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (!document.RootElement.TryGetProperty("objects", out var objects)) return null;
            if (!objects.TryGetProperty(entry, out var item)) return null;
            if (item.TryGetProperty("hash", out var hash) && hash.GetString() is { Length: > 0 } value)
                return value;
        }
        catch
        {
            // 单个索引损坏不影响其它索引
        }

        return null;
    }

    #endregion

    #region 从 JAR 读取（内置 en_us）

    private static Dictionary<string, string> LoadFromJar(string? jarPath, string language)
    {
        if (string.IsNullOrEmpty(jarPath) || !File.Exists(jarPath)) return [];
        try
        {
            using var jar = ZipFile.OpenRead(jarPath);
            var entry = jar.GetEntry("assets/minecraft/lang/" + language + ".json");
            if (entry is null) return [];
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            return ReadLanguage(document.RootElement);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SchematicPreview] 读取内置语言文件失败（{language}）", ModBase.LogLevel.Developer);
            return [];
        }
    }

    private static Dictionary<string, string> ReadLanguageFile(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return ReadLanguage(document.RootElement);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SchematicPreview] 解析语言文件失败（{path}）", ModBase.LogLevel.Developer);
            return [];
        }
    }

    private static Dictionary<string, string> ReadLanguage(JsonElement root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.StartsWith("block.", StringComparison.Ordinal) &&
                !property.Name.StartsWith("tile.", StringComparison.Ordinal))
                continue;
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value)) map[property.Name] = value;
        }

        return map;
    }

    #endregion
}
