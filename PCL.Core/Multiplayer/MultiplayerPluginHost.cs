using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using PCL.Core.App;

namespace PCL.Core.Multiplayer;

/// <summary>
///     联机插件加载器。
///     插件目录：<c>%AppData%\PCLSE\Plugins\&lt;模块 id&gt;\&lt;版本&gt;\</c>，同目录必须有 <c>manifest.json</c>：
///     <code>{ "id": "bedrock.paperconnect", "version": "1.0.0", "file": "Multiplayer.Bedrock.dll", "sha256": "…" }</c>
///     加载流程：读清单 → 校验 SHA256 → <c>Assembly.Load(byte[])</c>（不走 LoadFrom，避免锁文件）→
///     在独立可回收 <see cref="AssemblyLoadContext" /> 里实例化 <see cref="IMultiplayerModule" />。
///     校验失败、缺依赖、类型不匹配都只跳过该插件，不影响启动。
/// </summary>
public static class MultiplayerPluginHost
{
    private const string ManifestName = "manifest.json";

    /// <summary>插件根目录。默认 <c>%AppData%\PCLSE\Plugins</c>，可显式指定（便携版 / 测试）。</summary>
    public static string PluginRoot
    {
        get => _pluginRoot ?? Path.Combine(Paths.SharedData, "Plugins");
        set => _pluginRoot = value;
    }

    private static string? _pluginRoot;

    /// <summary>最近一次加载过程中的错误，供 UI 的"查看日志"用。</summary>
    public static IReadOnlyList<string> LastErrors => _errors;

    private static readonly List<string> _errors = [];

    /// <summary>扫描并加载全部插件，返回成功的模块。</summary>
    public static IReadOnlyList<IMultiplayerModule> LoadAll()
    {
        var loaded = new List<IMultiplayerModule>();
        _errors.Clear();
        if (!Directory.Exists(PluginRoot)) return loaded;

        foreach (var moduleDir in Directory.EnumerateDirectories(PluginRoot))
        foreach (var versionDir in Directory.EnumerateDirectories(moduleDir).OrderByDescending(Path.GetFileName))
        {
            var manifestPath = Path.Combine(versionDir, ManifestName);
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var module = LoadOne(versionDir, manifestPath);
                if (module is not null) loaded.Add(module);
            }
            catch (Exception ex)
            {
                _errors.Add($"{Path.GetFileName(moduleDir)}/{Path.GetFileName(versionDir)}: {ex.Message}");
            }
        }

        return loaded;
    }

    private static IMultiplayerModule? LoadOne(string versionDir, string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.File))
            throw new InvalidDataException("manifest.json 缺少 id 或 file");

        var dllPath = Path.Combine(versionDir, manifest.File);
        if (!File.Exists(dllPath)) throw new FileNotFoundException($"插件文件不存在：{manifest.File}");

        var bytes = File.ReadAllBytes(dllPath);
        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            var actual = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(actual, manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA256 校验失败（期望 {manifest.Sha256}，实际 {actual}）");
        }

        var context = new PluginLoadContext(dllPath);
        var assembly = context.LoadFromStream(new MemoryStream(bytes));
        var type = assembly.GetTypes().FirstOrDefault(t =>
            t is { IsAbstract: false, IsInterface: false } && typeof(IMultiplayerModule).IsAssignableFrom(t));
        if (type is null) throw new InvalidDataException("插件里没有实现 IMultiplayerModule 的类型");

        var module = (IMultiplayerModule?)Activator.CreateInstance(type)
                     ?? throw new InvalidOperationException("插件模块无法实例化");
        if (!string.Equals(module.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"模块 Id 与清单不一致（{module.Id} ≠ {manifest.Id}）");

        return module;
    }

    /// <summary>
    ///     每个插件一个可回收 ALC；契约程序集（PCL.Core）必须回落到宿主已加载的那份，
    ///     否则插件里的 <see cref="IMultiplayerModule" /> 与主程序不是同一个类型，会强制转换失败。
    /// </summary>
    private sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, typeof(IMultiplayerModule).Assembly.GetName().Name,
                    StringComparison.OrdinalIgnoreCase))
                return typeof(IMultiplayerModule).Assembly;

            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolved is null ? null : LoadFromAssemblyPath(resolved);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    private sealed class PluginManifest
    {
        public string Id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
    }
}
