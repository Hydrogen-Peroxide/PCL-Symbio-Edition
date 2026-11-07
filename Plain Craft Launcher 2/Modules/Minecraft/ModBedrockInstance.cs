using System;
using System.IO;
using System.Text.Json.Nodes;
using PCL.Core.App;

namespace PCL;

/// <summary>
///     基岩版本地实例的显示名称、目录定位与复制。
///     文件夹名（版本标识）保持不变，重命名只修改卡片上显示的别名，避免破坏注册、隔离与加载器配置。
/// </summary>
public static class ModBedrockInstance
{
    private const string MetadataFileName = ".pcl-instance.json";

    /// <summary>
    ///     基岩版数据根目录。默认在启动器目录下的 .minecraft\bedrock，可在基岩版页面改为其它磁盘。
    /// </summary>
    public static string DataRoot
    {
        get
        {
            var custom = Config.Launch.BedrockFolder;
            if (!string.IsNullOrWhiteSpace(custom))
            {
                try
                {
                    return Path.GetFullPath(custom);
                }
                catch (Exception ex)
                {
                    ModBase.Log($"[Bedrock] 自定义下载路径无效（{custom}）：{ex.Message}", ModBase.LogLevel.Hint);
                }
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "bedrock");
        }
    }

    /// <summary>下载、导入的安装包与解包产物所在的 versions 目录。</summary>
    public static string VersionsDirectory => Path.Combine(DataRoot, "versions");

    private static string GetMetadataPath(string versionsDirectory, string versionId) =>
        Path.Combine(versionsDirectory, versionId, MetadataFileName);

    /// <summary>读取实例显示名称；没有设置或读取失败时回退到版本标识。</summary>
    public static string GetDisplayName(string versionsDirectory, string versionId)
    {
        try
        {
            var path = GetMetadataPath(versionsDirectory, versionId);
            if (!File.Exists(path)) return versionId;

            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var name = node?["displayName"]?.GetValue<string>()?.Trim();
            return string.IsNullOrEmpty(name) ? versionId : name;
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 读取实例显示名失败（{versionId}）: {ex.Message}", ModBase.LogLevel.Normal);
            return versionId;
        }
    }

    /// <summary>设置实例显示名称；传入空值或与版本标识相同的名称时删除别名。</summary>
    public static void SetDisplayName(string versionsDirectory, string versionId, string? displayName)
    {
        var path = GetMetadataPath(versionsDirectory, versionId);
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Equals(versionId, StringComparison.Ordinal))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new JsonObject { ["displayName"] = name }.ToJsonString());
    }

    /// <summary>返回实例所在目录；已解包时优先返回解包目录。</summary>
    public static string GetInstanceFolder(string versionsDirectory, string versionId)
    {
        var extracted = ModBedrockUwp.GetVersionDirectory(Path.Combine(versionsDirectory, "extracted"), versionId);
        if (Directory.Exists(extracted)) return extracted;
        return ModBedrockUwp.GetVersionDirectory(versionsDirectory, versionId);
    }

    /// <summary>为新副本生成不冲突的版本标识，例如「26.45 副本」「26.45 副本 2」。</summary>
    public static string CreateCopyId(string versionsDirectory, string versionId)
    {
        var baseName = $"{versionId} 副本";
        var candidate = baseName;
        var index = 2;
        while (Directory.Exists(Path.Combine(versionsDirectory, candidate)) ||
               Directory.Exists(Path.Combine(versionsDirectory, "extracted", candidate)))
            candidate = $"{baseName} {index++}";
        return candidate;
    }

    /// <summary>复制便携版实例的安装包目录与解包目录，并继承显示名称与加载器配置由调用方处理。</summary>
    public static void CopyPortableInstance(string versionsDirectory, string sourceId, string targetId,
        Action<string>? onProgress = null)
    {
        var sourcePackage = ModBedrockUwp.GetVersionDirectory(versionsDirectory, sourceId);
        var targetPackage = ModBedrockUwp.GetVersionDirectory(versionsDirectory, targetId);
        if (!Directory.Exists(sourcePackage))
            throw new DirectoryNotFoundException($"找不到源实例目录：{sourcePackage}");
        if (Directory.Exists(targetPackage))
            throw new IOException($"目标实例目录已存在：{targetPackage}");

        var copied = 0;
        var targetExtracted = ModBedrockUwp.GetVersionDirectory(
            Path.Combine(versionsDirectory, "extracted"), targetId);
        try
        {
            CopyDirectory(sourcePackage, targetPackage, ref copied, onProgress);

            var sourceExtracted = ModBedrockUwp.GetVersionDirectory(
                Path.Combine(versionsDirectory, "extracted"), sourceId);
            if (Directory.Exists(sourceExtracted))
                CopyDirectory(sourceExtracted, targetExtracted, ref copied, onProgress);

            SetDisplayName(versionsDirectory, targetId, GetDisplayName(versionsDirectory, sourceId) + " 副本");
        }
        catch
        {
            // 复制失败时清理半成品，避免列表里出现无法启动的残缺实例
            try { if (Directory.Exists(targetPackage)) Directory.Delete(targetPackage, true); } catch { }
            try { if (Directory.Exists(targetExtracted)) Directory.Delete(targetExtracted, true); } catch { }
            throw;
        }

        ModBase.Log($"[Bedrock] 已复制实例 {sourceId} -> {targetId}（{copied} 个文件）", ModBase.LogLevel.Normal);
    }

    private static void CopyDirectory(string source, string target, ref int copied, Action<string>? onProgress)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file);
            // 别名由目标实例单独写入，避免复制成源实例的显示名
            if (name.Equals(MetadataFileName, StringComparison.OrdinalIgnoreCase)) continue;

            File.Copy(file, Path.Combine(target, name), true);
            copied++;
            if (copied % 200 == 0) onProgress?.Invoke($"已复制 {copied} 个文件…");
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), ref copied, onProgress);
        }
    }
}
