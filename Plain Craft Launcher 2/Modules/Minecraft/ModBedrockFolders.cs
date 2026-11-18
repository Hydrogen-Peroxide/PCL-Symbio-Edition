using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     基岩版数据目录（DataRoot）的探测与记录。
///     自动列出常见位置（启动器目录、各磁盘的 .minecraft\bedrock 等），并持久化手动添加的目录。
/// </summary>
public static class ModBedrockFolders
{
    private const string IndexFileName = "bedrock-folders.json";

    /// <summary>一个可选的数据目录。</summary>
    public sealed class Folder
    {
        /// <summary>目录完整路径。</summary>
        public string Path = "";

        /// <summary>手动添加或探测得到的显示名称。</summary>
        public string Alias = "";

        /// <summary>是否为启动器默认目录（不可移除）。</summary>
        public bool IsDefault;

        /// <summary>该目录下的实例数量。</summary>
        public int InstanceCount;

        /// <summary>列表里显示的名字：与 Java 版一致，直接用文件夹本身的名字。</summary>
        public string Title
        {
            get
            {
                var trimmed = (Path ?? "").TrimEnd(System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar);
                var name = System.IO.Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(name) ? Path : name;
            }
        }

        public override string ToString() => Path;
    }

    private static string IndexPath =>
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft", IndexFileName);

    private static string Normalize(string path)
    {
        var full = System.IO.Path.GetFullPath(path.Trim());
        return full.TrimEnd(System.IO.Path.DirectorySeparatorChar);
    }

    public static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启动器默认目录：&lt;启动器目录&gt;\.minecraft\bedrock。</summary>
    public static string DefaultPath =>
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "bedrock");

    /// <summary>读取手动添加的目录（路径 + 别名）。</summary>
    private static List<Folder> LoadManual()
    {
        var result = new List<Folder>();
        try
        {
            if (!File.Exists(IndexPath)) return result;
            var root = JsonNode.Parse(File.ReadAllText(IndexPath)) as JsonObject;
            var list = root?["folders"] as JsonArray;
            if (list is null) return result;
            foreach (var item in list)
            {
                if (item is not JsonObject obj) continue;
                var path = obj["path"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(path)) continue;
                try
                {
                    result.Add(new Folder { Path = Normalize(path), Alias = obj["alias"]?.GetValue<string>() ?? "" });
                }
                catch (Exception ex)
                {
                    ModBase.Log($"[Bedrock] 忽略无效的数据目录记录（{path}）：{ex.Message}", ModBase.LogLevel.Debug);
                }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 读取数据目录列表失败：{ex.Message}", ModBase.LogLevel.Normal);
        }

        return result;
    }

    private static void SaveManual(IEnumerable<Folder> folders)
    {
        try
        {
            var array = new JsonArray();
            foreach (var folder in folders)
                array.Add(new JsonObject { ["path"] = folder.Path, ["alias"] = folder.Alias });
            var directory = System.IO.Path.GetDirectoryName(IndexPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(IndexPath, new JsonObject { ["folders"] = array }.ToJsonString(
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 保存数据目录列表失败：{ex.Message}", ModBase.LogLevel.Hint);
        }
    }

    /// <summary>统计目录下的实例数量（不含解包缓存 extracted）。</summary>
    private static int CountInstances(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return 0;
            // 既支持 <root>\versions 布局，也支持用户直接选中版本目录本身
            var nested = System.IO.Path.Combine(root, "versions");
            var versions = Directory.Exists(nested) ? nested : root;
            return Directory.GetDirectories(versions)
                .Count(dir => !System.IO.Path.GetFileName(dir).Equals("extracted", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>猜测一个易读的别名，例如「D 盘用户目录」。</summary>
    private static string GuessAlias(string path)
    {
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        var parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(trimmed) ?? "");
        if (string.IsNullOrEmpty(name)) return trimmed;
        return string.IsNullOrEmpty(parent) ? name : parent + @"\" + name;
    }

    /// <summary>
    ///     列出全部可选目录。
    ///     顺序固定为：启动器默认目录 → 手动添加的目录 → Java 版旁边的 bedrock → 各磁盘探测 → 用户目录 → 当前配置目录。
    ///     当前配置目录放到最后兜底，这样切换目录时列表位置不会跳动（它会留在原来的分组里）。
    /// </summary>
    public static List<Folder> Enumerate()
    {
        var result = new List<Folder>();
        var seen = new List<string>();

        void Push(string path, string alias, bool isDefault)
        {
            try
            {
                var full = Normalize(path);
                if (seen.Any(item => SamePath(item, full))) return;
                seen.Add(full);
                result.Add(new Folder
                {
                    Path = full,
                    Alias = string.IsNullOrWhiteSpace(alias) ? GuessAlias(full) : alias,
                    IsDefault = isDefault,
                    InstanceCount = CountInstances(full)
                });
            }
            catch (Exception ex)
            {
                ModBase.Log($"[Bedrock] 跳过无效的数据目录（{path}）：{ex.Message}", ModBase.LogLevel.Debug);
            }
        }

        // 1. 启动器默认目录
        Push(DefaultPath, "", true);

        // 2. 手动添加过的目录（保持添加顺序）
        foreach (var folder in LoadManual()) Push(folder.Path, folder.Alias, false);

        // 3. 启动器里 Java 版当前选择的 .minecraft 旁的 bedrock（换台机器、换启动器副本时最常见的位置）
        try
        {
            var javaFolder = ModFolder.mcFolderSelected;
            if (!string.IsNullOrWhiteSpace(javaFolder))
            {
                var candidate = System.IO.Path.Combine(javaFolder, "bedrock");
                if (Directory.Exists(candidate)) Push(candidate, Lang.Text("Bedrock.Folder.Alongside"), false);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 读取 Java 版目录失败：{ex.Message}", ModBase.LogLevel.Debug);
        }

        // 4. 各磁盘上的 .minecraft\bedrock
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                if (!drive.IsReady) continue;
                var candidate = System.IO.Path.Combine(drive.RootDirectory.FullName, ".minecraft", "bedrock");
                if (Directory.Exists(candidate)) Push(candidate, Lang.Text("Bedrock.Folder.Detected", drive.Name.TrimEnd('\\')), false);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 探测磁盘目录失败：{ex.Message}", ModBase.LogLevel.Debug);
        }

        // 5. 用户目录下的 .minecraft\bedrock
        foreach (var variable in new[] { "APPDATA", "USERPROFILE" })
        {
            var basePath = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(basePath)) continue;
            var candidate = System.IO.Path.Combine(basePath, ".minecraft", "bedrock");
            if (Directory.Exists(candidate)) Push(candidate, Lang.Text("Bedrock.Folder.User", variable), false);
        }

        // 6. 当前配置的目录（通常已经出现在上面某一组里；不在时补在最后，保证列表顺序稳定）
        var configured = Config.Launch.BedrockFolder;
        if (!string.IsNullOrWhiteSpace(configured)) Push(configured, Lang.Text("Bedrock.Folder.Custom"), false);

        return result;
    }

    /// <summary>判断目录是否为当前正在使用的数据目录。</summary>
    public static bool IsCurrent(Folder folder) => SamePath(folder.Path, ModBedrockInstance.DataRoot);

    /// <summary>将指定目录设为当前数据目录（默认目录会清空配置值）。</summary>
    public static void Switch(Folder folder)
    {
        Config.Launch.BedrockFolder = folder.IsDefault ? "" : folder.Path;
        ModBase.Log($"[Bedrock] 切换数据目录：{folder.Path}", ModBase.LogLevel.Normal);
    }

    /// <summary>添加一个已有目录；返回添加后的条目，已存在时返回 null。</summary>
    public static Folder? Add(string path)
    {
        var full = Normalize(path);
        if (Enumerate().Any(folder => SamePath(folder.Path, full))) return null;

        var manual = LoadManual();
        var alias = GuessAlias(full);
        manual.Add(new Folder { Path = full, Alias = alias });
        SaveManual(manual);
        ModBase.Log($"[Bedrock] 已添加数据目录：{full}", ModBase.LogLevel.Normal);
        return new Folder { Path = full, Alias = alias, InstanceCount = CountInstances(full) };
    }

    /// <summary>检查目录是否像一个基岩版数据目录（存在 versions 子目录或为空目录）。</summary>
    public static bool LooksValid(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            // 标准布局：<path>\versions
            if (Directory.Exists(System.IO.Path.Combine(path, "versions"))) return true;
            // 空目录允许，用户可能想先把目录挂上再装版本
            if (!Directory.EnumerateFileSystemEntries(path).Any()) return true;
            // 直接选中版本目录本身：里面就是各个版本文件夹
            return Directory.EnumerateDirectories(path)
                .Any(dir => !System.IO.Path.GetFileName(dir).Equals("extracted", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
