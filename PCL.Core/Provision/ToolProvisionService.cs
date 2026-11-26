using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;

namespace PCL.Core.Provision;

/// <summary>外部工具/运行依赖的声明。<see cref="Sha256" /> 为空表示上游不提供摘要。</summary>
public sealed record ToolDefinition(
    string Id,
    string Version,
    Uri Url,
    string? Sha256 = null,
    string? EntryFile = null,
    bool IsArchive = false);

/// <summary>
///     工具与依赖的统一供给层：查本地清单 → 命中直接用 → 未命中走 <see cref="DownloadService" /> → 写清单 → 返回路径。
///     目录约定：<c>%AppData%\PCLSE\Tools\&lt;id&gt;\&lt;version&gt;\</c>，多版本可共存；
///     清单：<c>%AppData%\PCLSE\Tools\manifest.json</c>。
///     存档转换器（Chunker）、后续 AMULET 等都走这里，不要各写一套下载。
/// </summary>
public static class ToolProvisionService
{
    private const string ManifestFileName = "manifest.json";

    private static readonly object Sync = new();
    private static Dictionary<string, ToolManifestEntry>? _manifest;

    /// <summary>工具根目录。默认 <c>%AppData%\PCLSE\Tools</c>，可显式指定（便携版 / 测试）。</summary>
    public static string ToolsRoot
    {
        get => _toolsRoot ?? Path.Combine(Paths.SharedData, "Tools");
        set => _toolsRoot = value;
    }

    private static string? _toolsRoot;

    private static string ManifestPath => Path.Combine(ToolsRoot, ManifestFileName);

    /// <summary>某工具某版本的安装目录。</summary>
    public static string DirectoryFor(string id, string version) => Path.Combine(ToolsRoot, id, version);

    /// <summary>清单里记录的可执行/入口文件全路径；没有记录时返回 null。</summary>
    public static string? Resolve(string id)
    {
        var manifest = LoadManifest();
        if (!manifest.TryGetValue(id, out var entry)) return null;
        var directory = DirectoryFor(id, entry.Version);
        if (!Directory.Exists(directory)) return null;
        var file = Path.Combine(directory, entry.EntryFile);
        return File.Exists(file) ? file : null;
    }

    /// <summary>
    ///     确保工具就绪并返回入口文件路径。
    ///     已命中清单且文件存在则直接返回（不联网、不校验）；否则下载 → 校验 → 解压 → 写清单。
    /// </summary>
    public static async Task<string> EnsureAsync(ToolDefinition definition, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var existing = Resolve(definition.Id);
        if (existing is not null) return existing;

        var directory = DirectoryFor(definition.Id, definition.Version);
        System.IO.Directory.CreateDirectory(directory);

        var fileName = Path.GetFileName(definition.Url.LocalPath);
        var target = Path.Combine(directory, string.IsNullOrWhiteSpace(fileName) ? "download.bin" : fileName);

        using (var downloader = new DownloadService())
        {
            await downloader.DownloadAsync(definition.Url, target, definition.Sha256, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        if (definition.IsArchive) ExtractArchive(target, directory);

        var entryFile = definition.EntryFile ?? Path.GetFileName(target);
        // 发行包常常把 exe 放在子目录里（例如 easytier-windows-x86_64/），
        // 先按根层找，找不到就按文件名在解压目录里递归找，避免"明明下载成功却报找不到入口"。
        var entryPath = Path.Combine(directory, entryFile);
        if (!File.Exists(entryPath))
        {
            var entryName = Path.GetFileName(entryFile);
            var found = System.IO.Directory
                .EnumerateFiles(directory, entryName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found is not null)
            {
                entryFile = Path.GetRelativePath(directory, found);
                entryPath = found;
            }
        }

        if (!File.Exists(entryPath))
            throw new FileNotFoundException($"工具 {definition.Id} 解压后找不到入口文件 {entryFile}", entryPath);

        WriteManifestEntry(definition.Id, new ToolManifestEntry(definition.Version, entryFile, definition.Sha256));
        return entryPath;
    }

    /// <summary>把任意已解压/已存在的入口登记进清单（"手动指定本地路径"用）。</summary>
    public static void RegisterManual(string id, string entryPath, string? version = null)
    {
        if (!File.Exists(entryPath)) throw new FileNotFoundException("指定的文件不存在", entryPath);
        var actualVersion = string.IsNullOrWhiteSpace(version) ? "manual" : version;
        var directory = DirectoryFor(id, actualVersion);
        System.IO.Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, Path.GetFileName(entryPath));
        if (!string.Equals(Path.GetFullPath(entryPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(entryPath, target, overwrite: true);

        WriteManifestEntry(id, new ToolManifestEntry(actualVersion, Path.GetFileName(target), null));
    }

    private static void ExtractArchive(string archivePath, string directory)
    {
        if (!archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // 目录项
            var targetPath = Path.Combine(directory, entry.FullName);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static Dictionary<string, ToolManifestEntry> LoadManifest()
    {
        lock (Sync)
        {
            if (_manifest is not null) return _manifest;
            try
            {
                _manifest = File.Exists(ManifestPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, ToolManifestEntry>>(File.ReadAllText(ManifestPath)) ?? []
                    : [];
            }
            catch
            {
                _manifest = [];
            }

            return _manifest;
        }
    }

    private static void WriteManifestEntry(string id, ToolManifestEntry entry)
    {
        lock (Sync)
        {
            var manifest = LoadManifest();
            manifest[id] = entry;
            System.IO.Directory.CreateDirectory(ToolsRoot);
            File.WriteAllText(ManifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>清单项。EntryFile 相对工具版本目录。</summary>
    public sealed record ToolManifestEntry(string Version, string EntryFile, string? Sha256);
}
