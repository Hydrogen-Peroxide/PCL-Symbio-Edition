using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PCL;

/// <summary>
///     基岩版世界目录探测与备份。
///     基岩版世界使用 LevelDB 存储，当前地图编辑器只支持 Java 版 Anvil 区块；
///     这里提供世界备份，避免在删除/迁移前丢档。
/// </summary>
public static class ModBedrockWorldBackup
{
    public static List<string> GetWorldsRoots()
    {
        var roots = new List<string>();
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        void Add(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) &&
                !roots.Contains(path, StringComparer.OrdinalIgnoreCase))
                roots.Add(path);
        }

        Add(Path.Combine(roaming, "Minecraft Bedrock", "minecraftWorlds"));
        Add(Path.Combine(roaming, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang",
            "minecraftWorlds"));

        var users = Path.Combine(roaming, "Minecraft Bedrock", "Users");
        if (Directory.Exists(users))
            foreach (var user in Directory.GetDirectories(users))
                Add(Path.Combine(user, "games", "com.mojang", "minecraftWorlds"));

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(Path.Combine(local, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState",
            "games", "com.mojang", "minecraftWorlds"));

        return roots;
    }

    /// <summary>返回基岩版世界名称与目录。</summary>
    public static List<(string Name, string Path)> GetWorlds()
    {
        var result = new List<(string Name, string Path)>();
        var index = 0;
        foreach (var root in GetWorldsRoots())
        foreach (var directory in Directory.GetDirectories(root))
        {
            if (!File.Exists(Path.Combine(directory, "level.dat"))) continue;
            var name = $"world-{++index}-{Path.GetFileName(directory)}";
            result.Add((name, directory));
        }

        return result;
    }

    /// <summary>把所有基岩版世界压缩到一个 zip，返回备份的世界数量。</summary>
    public static int BackupAll(string zipPath, Action<string>? onProgress = null)
    {
        var worlds = GetWorlds();
        if (worlds.Count == 0) return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, path) in worlds)
        {
            onProgress?.Invoke(name);
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(path, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, $"worlds/{name}/{relative}", CompressionLevel.Optimal);
            }
        }

        return worlds.Count;
    }
}
