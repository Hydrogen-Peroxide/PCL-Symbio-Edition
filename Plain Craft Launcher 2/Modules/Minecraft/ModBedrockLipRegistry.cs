using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Threading;
using PCL.Core.IO.Net.Http;

namespace PCL;

/// <summary>
///     LeviLamina（DLL 模组）下载：从 Lip 官方注册表读取插件列表，并调用 lip 安装到指定实例。
///     注册表格式见 https://lipr.levimc.org/levilauncher.json。
/// </summary>
internal static class ModBedrockLipRegistry
{
    private const string RegistryUrl = "https://lipr.levimc.org/levilauncher.json";

    /// <summary>lip 在实例根目录维护的安装清单（含依赖），用来判断某个包是否已经装好。</summary>
    private const string LockFileName = "tooth_lock.json";

    private const int MaxDependencyDepth = 5;

    /// <summary>
    ///     lip 的进度输出（下载百分比、缓存命中、克隆仓库……）对错误提示没有意义，
    ///     整理异常信息时要先滤掉，否则弹窗里只剩一堆折行的 URL 片段。
    /// </summary>
    private static readonly Regex LipNoise = new(
        "^(Cache hit for key|Caching (file|directory) for key|Downloading|Downloaded|Cloning into|Cloning|Cloned|" +
        "Installing package|Installed package|Updating|Updated|Resolving|Fetching|Uninstalling|Removing|Reading)\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>lip 输出被终端宽度折行后留下的堆栈碎片，例如 <c>FileDownloader.cs:19</c>、<c>at async Task …</c>。</summary>
    private static readonly Regex StackFragment = new(
        @"^at\s|\b\w+\.(cs|go|rs|ts|py):\d+|\)\s+in\s+\S+:\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>值得原样重试一次的网络类失败。</summary>
    private static readonly Regex TransientFailure = new(
        "timeout|timed out|connection reset|connection refused|EOF|TLS|network is unreachable|temporary failure|" +
        "no such host|远程主机强迫关闭|连接超时|由于连接方|套接字",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>插件包里声明的下载地址不存在（插件作者发布的内容和清单对不上）。</summary>
    private static readonly Regex MissingAsset = new(
        @"HTTP\s*(404|410)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>lip 拒绝重复安装时的提示，例如 "... because it is already installed with version 1.0.2"。</summary>
    private static readonly Regex AlreadyInstalled = new(
        @"already installed with version\s+(\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<LipPackage>? _cache;
    private static Dictionary<string, LipPackage>? _packageMap;

    /// <summary>可以预期、直接用提示告诉用户的安装失败（不兼容、下载失效、lip 报错），不弹"反馈问题"的错误框。</summary>
    internal sealed class LipInstallException(string message) : Exception(message);

    internal sealed record LipPackage(string Id, string Name, string Description, List<string> Versions, int Stars,
        DateTime? UpdatedAt, string? AvatarUrl, Dictionary<string, Dictionary<string, string>> Dependencies,
        Dictionary<string, string> VersionVariants);

    /// <summary>安装进度：<see cref="Percent"/> 为 0~1，&lt;0 表示只有文字没有进度。</summary>
    internal readonly record struct LipInstallProgress(string Stage, double Percent);

    /// <summary>取某个版本声明的 LeviLamina 依赖约束（没有则返回 null）。</summary>
    internal static string? GetLoaderConstraint(LipPackage package, string? version = null)
    {
        version ??= package.Versions.FirstOrDefault();
        if (version is null || !package.Dependencies.TryGetValue(version, out var deps)) return null;
        return deps.FirstOrDefault(pair =>
            pair.Key.StartsWith("github.com/LiteLDev/LeviLamina", StringComparison.OrdinalIgnoreCase)).Value;
    }

    /// <summary>读取全部可用于客户端的 LeviLamina 插件。</summary>
    internal static List<LipPackage> GetAll()
    {
        if (_cache is not null) return _cache;

        var json = HttpRequest.GetStringAsync(RegistryUrl).GetAwaiter().GetResult();
        var root = JsonNode.Parse(json);
        var result = new List<LipPackage>();
        var map = new Dictionary<string, LipPackage>(StringComparer.OrdinalIgnoreCase);
        if (root?["packages"] is not JsonObject packages) return _cache = result;

        foreach (var pair in packages)
        {
            if (pair.Value is not JsonObject pkg) continue;

            var tags = (pkg["info"]?["tags"] as JsonArray)?
                .Select(node => node?.ToString() ?? "")
                .ToList() ?? [];
            if (!tags.Contains("platform:levilamina")) continue;
            if (!tags.Contains("type:mod")) continue;

            var (versions, dependencies, variants) = CollectVersions(pkg);
            if (versions.Count == 0) continue;

            var item = new LipPackage(
                pair.Key,
                ((string?)pkg["info"]?["name"] ?? pair.Key).Trim(),
                ((string?)pkg["info"]?["description"] ?? "").Trim(),
                versions,
                (int?)pkg["stargazer_count"] ?? 0,
                DateTime.TryParse((string?)pkg["updated_at"], out var updated) ? updated : null,
                NormalizeAvatar((string?)pkg["info"]?["avatar_url"]),
                dependencies,
                variants);

            result.Add(item);
            map[item.Id] = item;
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _packageMap = map;
        return _cache = result;
    }

    /// <summary>按关键词过滤（名称 / 包标识 / 简介）。</summary>
    internal static List<LipPackage> Search(string keyword)
    {
        var all = GetAll();
        if (string.IsNullOrWhiteSpace(keyword)) return all;
        var key = keyword.Trim();
        return all.Where(pkg =>
                pkg.Name.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                pkg.Id.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                pkg.Description.Contains(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>去掉包标识里的变体后缀（<c>#client</c>）：安装清单里的 tooth 只有包名。</summary>
    internal static string NormalizePackageId(string id)
    {
        var index = id.IndexOf('#');
        return index < 0 ? id : id[..index];
    }

    /// <summary>某个包版本属于哪个变体（client / 空）；空变体返回 null。</summary>
    private static string? GetVersionVariant(LipPackage package, string version)
        => package.VersionVariants.TryGetValue(version, out var variant) && !string.IsNullOrWhiteSpace(variant)
            ? variant
            : null;

    /// <summary>
    ///     拼 lip 的包规格。<b>变体标签必须带上</b>：只提供 client 变体的插件如果按
    ///     <c>id@version</c> 交出去，lip 会去找空标签的变体并抛
    ///     <c>KeyNotFoundException: No variant found for label ''</c>。
    /// </summary>
    private static string BuildSpec(string id, string? variant, string version)
        => string.IsNullOrWhiteSpace(variant) ? $"{id}@{version}" : $"{id}#{variant}@{version}";

    /// <summary>lip 安装清单里的一条记录。</summary>
    private sealed record InstalledEntry(string Id, string Version, string Variant);

    /// <summary>读取实例里 lip 的 tooth_lock.json（含依赖）；清单缺失或损坏时返回空表。</summary>
    private static List<InstalledEntry> ReadLock(string? instanceDir)
    {
        var result = new List<InstalledEntry>();
        if (string.IsNullOrWhiteSpace(instanceDir)) return result;

        try
        {
            var path = Path.Combine(instanceDir!, LockFileName);
            if (!File.Exists(path)) return result;
            if (JsonNode.Parse(File.ReadAllText(path))?["packages"] is not JsonArray packages) return result;

            foreach (var entry in packages)
            {
                var tooth = entry?["manifest"]?["tooth"]?.ToString();
                var version = entry?["manifest"]?["version"]?.ToString();
                if (string.IsNullOrWhiteSpace(tooth) || string.IsNullOrWhiteSpace(version)) continue;
                result.Add(new InstalledEntry(NormalizePackageId(tooth), version,
                    entry?["variant"]?.ToString() ?? ""));
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 读取 lip 安装清单失败，按未安装处理", ModBase.LogLevel.Debug);
        }

        return result;
    }

    /// <summary>
    ///     实例里 lip 已安装的包（包标识 → 版本）。清单缺失或损坏时返回空表，
    ///     由调用方按"没装过"处理——lip 自己会在安装时兜底。
    /// </summary>
    internal static Dictionary<string, string> GetInstalledVersions(string? instanceDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ReadLock(instanceDir)) result[entry.Id] = entry.Version;
        return result;
    }

    /// <summary>实例里已安装的变体标签（例如 client）；没装过或没有变体时返回 null。</summary>
    internal static string? GetInstalledVariant(string? instanceDir, string packageId)
    {
        var id = NormalizePackageId(packageId);
        var entry = ReadLock(instanceDir).FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(entry?.Variant) ? null : entry.Variant;
    }

    /// <summary>实例里已安装的版本；没装过返回 null。</summary>
    internal static string? GetInstalledVersion(string? instanceDir, string packageId)
        => GetInstalledVersions(instanceDir).TryGetValue(NormalizePackageId(packageId), out var version)
            ? version
            : null;

    /// <summary>
    ///     判断插件有没有适配当前 LeviLamina 的版本。列表页用它提前标注"不适用"，
    ///     <see cref="Install"/> 用同一套判断挑版本，两边结论保持一致。
    /// </summary>
    internal static bool TryResolveVersion(LipPackage package, string gameVersion, string loaderVersion,
        out string version, out string reason)
    {
        var compatible = package.Versions
            .Where(candidate => IsLoaderCompatible(package, candidate, loaderVersion))
            .ToList();

        if (compatible.Count == 0)
        {
            version = "";
            reason = $"需要 LeviLamina {FormatConstraints(package)}（当前 {loaderVersion}）";
            return false;
        }

        var series = GetSeries(gameVersion);
        if (series is not null)
            foreach (var candidate in compatible)
                if (MatchesSeries(candidate, series))
                {
                    version = candidate;
                    reason = "";
                    return true;
                }

        version = compatible[0];
        reason = "";
        return true;
    }

    /// <summary>
    ///     安装指定插件到实例目录。<br/>
    ///     注意：不能用 lip 的依赖解析——注册表里插件的依赖写的是「服务端 / 旧版 LeviLamina」
    ///     （例如 <c>github.com/LiteLDev/LeviLamina: &gt;=1.1.1</c>），交给 lip 会去装一个服务端加载器
    ///     覆盖掉客户端目录。这里统一用 <c>--no-dependencies</c> 只装本体，再由启动器按注册表里的
    ///     dependencies 自己挑版本、递归装需要的库（LeviLamina 由 EnsureInstalled 负责）。<br/>
    ///     已经装好同版本时不会重复调用 lip（lip 会以「already installed」失败退出），
    ///     但依赖仍会重新核对一遍，避免上次只装了一半。
    /// </summary>
    internal static string Install(LipPackage package, string instanceDir, string gameVersion,
        Action<LipInstallProgress>? onProgress = null)
    {
        try
        {
            return InstallCore(package, instanceDir, gameVersion, onProgress);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException
                                       or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            // 预期内的失败（版本不兼容、下载地址失效、lip 报错……）统一换成提示型异常，
            // 页面就不会再弹"是否反馈此问题"的错误框
            throw new LipInstallException(ex.Message);
        }
    }

    private static string InstallCore(LipPackage package, string instanceDir, string gameVersion,
        Action<LipInstallProgress>? onProgress)
    {
        onProgress?.Invoke(new LipInstallProgress("正在准备 LeviLamina 客户端…", -1));
        var loaderVersion = ModBedrockLeviLamina.EnsureInstalled(instanceDir, gameVersion);

        var version = PickVersion(package, gameVersion, loaderVersion);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedCount = 0;

        InstallOne(package.Id, version, 0);

        ModBase.Log($"[Bedrock] {package.Id}@{version} 安装完成（LeviLamina {loaderVersion}）", ModBase.LogLevel.Normal);
        onProgress?.Invoke(new LipInstallProgress("已安装", 1));

        if (installedCount == 0)
            return $"{package.Name} {version} 已经安装，无需重复安装";
        return installedCount == 1
            ? $"{package.Name} {version} 已安装"
            : $"{package.Name} {version} 已安装（含 {installedCount - 1} 个依赖）";

        void InstallOne(string id, string installVersion, int depth)
        {
            if (depth > MaxDependencyDepth || !visited.Add(id)) return;

            var shortName = id[(id.LastIndexOf('/') + 1)..];
            var current = GetInstalledVersion(instanceDir, id);
            if (string.Equals(current, installVersion, StringComparison.OrdinalIgnoreCase))
            {
                ModBase.Log($"[Bedrock] {id}@{installVersion} 已安装，跳过", ModBase.LogLevel.Debug);
            }
            else
            {
                // 实际装上的版本可能比要求的旧（最新版的发布文件失效时会自动回退）
                installVersion = InstallPackage(id, shortName, current,
                    current is null ? null : GetInstalledVariant(instanceDir, id), installVersion);
                installedCount++;
            }

            var info = FindPackage(id);
            if (info is null || !info.Dependencies.TryGetValue(installVersion, out var dependencies)) return;

            foreach (var (dependencyId, constraint) in dependencies)
            {
                // 加载器由启动器管理，绝不能让 lip 按注册表里的（服务端/旧版）依赖去装
                if (dependencyId.StartsWith("github.com/LiteLDev/LeviLamina", StringComparison.OrdinalIgnoreCase))
                    continue;

                var dependency = FindPackage(dependencyId);
                if (dependency is null || dependency.Versions.Count == 0)
                {
                    ModBase.Log($"[Bedrock] 注册表中没有依赖 {dependencyId}，跳过（可能需要手动安装）",
                        ModBase.LogLevel.Hint);
                    continue;
                }

                InstallOne(dependencyId,
                    ResolveDependencyVersion(dependency, constraint, gameVersion, loaderVersion), depth + 1);
            }
        }

        string InstallPackage(string id, string shortName, string? currentVersion, string? currentVariant,
            string targetVersion, bool allowFallback = true)
        {
            var packageInfo = FindPackage(id);
            var targetVariant = packageInfo is null ? null : GetVersionVariant(packageInfo, targetVersion);
            var targetSpec = BuildSpec(id, targetVariant, targetVersion);

            onProgress?.Invoke(new LipInstallProgress($"正在安装 {shortName} {targetVersion}…", 0));
            ModBase.Log($"[Bedrock] 通过 lip 安装 {targetSpec}（跳过依赖解析）", ModBase.LogLevel.Normal);

            // lip 拒绝重复安装：先卸掉旧版本，否则新版只会得到 "already installed"
            if (currentVersion is not null)
            {
                ModBase.Log($"[Bedrock] 实例里是 {id}@{currentVersion}，先卸载再装 {targetVersion}",
                    ModBase.LogLevel.Normal);
                var spec = BuildSpec(id, currentVariant, currentVersion);
                var (removeCode, removeOutput) = RunLip($"uninstall {spec} --no-dependencies", instanceDir, null);
                if (removeCode != 0 && !string.IsNullOrWhiteSpace(currentVariant))
                {
                    // 清单里的变体标签不一定和 lip 认的一致，退一步用不带变体的规格再试
                    (removeCode, removeOutput) = RunLip(
                        $"uninstall {id}@{currentVersion} --no-dependencies", instanceDir, null);
                }

                if (removeCode != 0)
                    throw new InvalidOperationException(
                        $"更新 {shortName} 前卸载旧版本 {currentVersion} 失败：{DescribeFailure(removeCode, removeOutput)}");
            }

            var (exitCode, output) = RunInstall(targetSpec);
            if (exitCode != 0 && IsTransient(exitCode, output))
            {
                ModBase.Log($"[Bedrock] lip 安装 {id} 失败（退出码 {exitCode}），5 秒后重试一次",
                    ModBase.LogLevel.Hint);
                Thread.Sleep(TimeSpan.FromSeconds(5));
                (exitCode, output) = RunInstall(targetSpec);
            }

            if (exitCode == 0) return targetVersion;

            // lip 有时文件已经落盘才报错（例如它认为"已经装好"），以安装清单为准再确认一次
            if (string.Equals(GetInstalledVersion(instanceDir, id), targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                ModBase.Log($"[Bedrock] lip 报错，但 {id}@{targetVersion} 已经在实例里，按成功处理",
                    ModBase.LogLevel.Debug);
                return targetVersion;
            }

            // 安装清单可能和实际文件不同步（例如旧版启动器留下的目录）：lip 说装的就是这个版本就放过
            var alreadyInstalled = AlreadyInstalled.Match(output);
            if (alreadyInstalled.Success &&
                string.Equals(alreadyInstalled.Groups[1].Value.Trim(), targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                ModBase.Log($"[Bedrock] lip 认为 {id}@{targetVersion} 已经装好，按成功处理", ModBase.LogLevel.Debug);
                return targetVersion;
            }

            // 插件包声明的下载地址 404（作者发布的内容和清单对不上）：退回上一个兼容版本再试
            if (allowFallback && MissingAsset.IsMatch(output))
            {
                var older = packageInfo is null ? null : FindOlderVersion(packageInfo, targetVersion, loaderVersion);
                if (older is not null)
                {
                    ModBase.Log(
                        $"[Bedrock] {id}@{targetVersion} 的发布文件下载失败（HTTP 404），改用上一个版本 {older}",
                        ModBase.LogLevel.Hint);
                    onProgress?.Invoke(new LipInstallProgress($"最新版下载失效，改用 {older}…", 0));
                    return InstallPackage(id, shortName, null, null, older, false);
                }
            }

            throw new InvalidOperationException(
                $"安装 {shortName} {targetVersion} 失败：{DescribeFailure(exitCode, output)}");

            (int ExitCode, string Output) RunInstall(string packageSpec)
                => RunLip($"install {packageSpec} --no-dependencies", instanceDir, OnLine);

            void OnLine(string line)
            {
                // lip 克隆仓库时可能几分钟没有任何进度输出，给个文字提示，避免看起来像卡死
                if (line.Contains("Cloning", StringComparison.OrdinalIgnoreCase))
                {
                    onProgress?.Invoke(new LipInstallProgress("正在克隆插件仓库（首次安装较慢）…", 0));
                    return;
                }

                // lip 把下载进度打到 stdout/stderr，形如 "Downloading https://...: 42%"
                var percent = Regex.Match(line, @"(\d{1,3})\s*%");
                if (percent.Success && double.TryParse(percent.Groups[1].Value, out var value))
                {
                    onProgress?.Invoke(new LipInstallProgress($"下载 {shortName} {value:0}%", value / 100d));
                    return;
                }

                var installing = Regex.Match(line, @"Installing package (\S+) version (\S+)");
                if (!installing.Success) return;
                var packageName = installing.Groups[1].Value;
                packageName = packageName[(packageName.LastIndexOf('/') + 1)..];
                onProgress?.Invoke(new LipInstallProgress(
                    $"安装 {packageName} {installing.Groups[2].Value}…", 0.9d));
            }
        }
    }

    private static string PickVersion(LipPackage package, string gameVersion, string loaderVersion)
    {
        if (TryResolveVersion(package, gameVersion, loaderVersion, out var version, out var reason)) return version;
        throw new LipInstallException(
            $"{package.Name} 没有适用于当前实例的版本：{reason}，请把实例切换到对应游戏版本后再安装");
    }

    private static bool IsLoaderCompatible(LipPackage package, string version, string loaderVersion)
    {
        var constraint = GetLoaderConstraint(package, version);
        if (string.IsNullOrWhiteSpace(constraint)) return true;
        return LoaderConstraint.Satisfies(constraint, loaderVersion);
    }

    /// <summary>把插件各版本声明的 LeviLamina 约束整理成一行，例如 <c>&gt;=26.60.0 / 26.51.7</c>。</summary>
    private static string FormatConstraints(LipPackage package)
    {
        var constraints = package.Versions
            .Select(version => GetLoaderConstraint(package, version))
            .Where(constraint => !string.IsNullOrWhiteSpace(constraint))
            .Select(constraint => constraint!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        return constraints.Count == 0 ? "更高的版本" : string.Join(" / ", constraints);
    }

    private static LipPackage? FindPackage(string id)
    {
        _ = GetAll();
        return _packageMap?.GetValueOrDefault(id);
    }

    /// <summary>往回找一个兼容的旧版本（仅用于最新版发布文件失效时兜底）。</summary>
    private static string? FindOlderVersion(LipPackage package, string currentVersion, string loaderVersion)
    {
        var compatible = package.Versions
            .Where(candidate => IsLoaderCompatible(package, candidate, loaderVersion))
            .ToList();
        var index = compatible.FindIndex(candidate =>
            string.Equals(candidate, currentVersion, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < compatible.Count ? compatible[index + 1] : null;
    }

    /// <summary>
    ///     为依赖挑版本：先满足上层插件声明的约束，再要求依赖自己兼容当前 LeviLamina；
    ///     同一份依赖通常也按游戏版本分别构建（GMLIB 26.40 / 26.51 / 26.60），
    ///     因此优先挑与当前游戏版本同系列的，避免把 26.60 的库塞进 26.40 的实例。
    /// </summary>
    private static string ResolveDependencyVersion(LipPackage dependency, string? constraint, string gameVersion,
        string loaderVersion)
    {
        var candidates = dependency.Versions
            .Where(version => string.IsNullOrWhiteSpace(constraint) ||
                              LoaderConstraint.Satisfies(constraint, version))
            .Where(version => IsLoaderCompatible(dependency, version, loaderVersion))
            .ToList();

        if (candidates.Count == 0)
            throw new LipInstallException(
                $"依赖 {dependency.Name} 装不上：当前 LeviLamina 是 {loaderVersion}" +
                (string.IsNullOrWhiteSpace(constraint) ? "" : $"，插件要求 {constraint.Trim()}") +
                $"（{dependency.Name} 各版本要求 LeviLamina {FormatConstraints(dependency)}），" +
                "请换一个适配当前游戏版本的插件版本");

        var series = GetSeries(gameVersion);
        if (series is not null)
        {
            foreach (var candidate in candidates)
                if (MatchesSeries(candidate, series))
                    return candidate;

            // 依赖确实按游戏版本发版（有 26.40.x 这种版本），只是上层插件要的是别的系列。
            // 这种情况装进去插件也加载不起来，直接说清楚，别留下一个坏掉的实例。
            if (dependency.Versions.Any(version => MatchesSeries(version, series)))
                throw new LipInstallException(
                    $"插件要求 {dependency.Name} {(string.IsNullOrWhiteSpace(constraint) ? "的任意版本" : constraint.Trim())}，" +
                    $"但当前 {series} 实例里只能装 {dependency.Name} {series}.x 的版本，" +
                    "这个插件版本不适配当前游戏版本，请换一个版本");

            ModBase.Log($"[Bedrock] 依赖 {dependency.Id} 没有 {series} 系列的版本，回退到 {candidates[0]}",
                ModBase.LogLevel.Hint);
        }

        return candidates[0];
    }

    private static (List<string> Versions, Dictionary<string, Dictionary<string, string>> Dependencies,
        Dictionary<string, string> Variants) CollectVersions(JsonObject package)
    {
        var versions = new List<string>();
        var dependencies = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var variants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (package["variants"] is not JsonObject variantMap) return (versions, dependencies, variants);

        // 合并所有变体：有的插件只有 client 变体带某个版本，只取第一个变体会漏掉可用版本。
        // 同一版本在多个变体里出现时，client 变体优先（先遍历 client）。
        // 变体标签必须记住：装包时要把 #client 带上，否则 lip 会去找空标签的变体并报 KeyNotFoundException。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "client", "", "server" })
        {
            if (variantMap[name] is not JsonObject variant) continue;
            if (variant["versions"] is not JsonObject list) continue;
            foreach (var entry in list)
            {
                if (!seen.Add(entry.Key)) continue;
                versions.Add(entry.Key);
                variants[entry.Key] = name;
                if (entry.Value?["dependencies"] is not JsonObject deps) continue;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dep in deps) map[dep.Key] = dep.Value?.ToString() ?? "";
                dependencies[entry.Key] = map;
            }
        }

        versions.Sort(CompareVersionsDesc);
        return (versions, dependencies, variants);
    }

    private static int CompareVersionsDesc(string a, string b)
    {
        static Version? Parse(string text)
        {
            var core = text.Split('-', '+')[0];
            return Version.TryParse(core, out var version) ? version : null;
        }

        var va = Parse(a);
        var vb = Parse(b);
        if (va is not null && vb is not null) return vb.CompareTo(va);
        return string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>游戏版本/插件版本里的「主.次」系列，例如 26.40。</summary>
    private static string? GetSeries(string? version)
    {
        var match = Regex.Match(version ?? "", @"(\d+)\.(\d+)");
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : null;
    }

    private static bool MatchesSeries(string version, string series)
    {
        var core = version.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.');
        return parts.Length >= 2 && $"{parts[0]}.{parts[1]}" == series;
    }

    private static (int ExitCode, string Output) RunLip(string arguments, string workingDirectory,
        Action<string>? onLine)
        => ModBedrockLeviLamina.RunLipCommand(arguments, workingDirectory, default, onLine);

    private static bool IsTransient(int exitCode, string output)
        => exitCode == -1 || TransientFailure.IsMatch(output);

    /// <summary>把 lip 的输出整理成一句能读懂的话，避免把满屏下载进度塞进错误弹窗。</summary>
    private static string DescribeFailure(int exitCode, string output)
    {
        if (MissingAsset.IsMatch(output))
            return "插件的下载地址失效（HTTP 404，发布页里没有清单指定的文件），这是插件包自身的问题，换个插件或等作者修复";

        var summary = Summarize(output);
        var prefix = exitCode == -1
            ? "lip 被中断（通常是克隆/下载仓库时网络超时或断开）"
            : $"lip 退出码 {exitCode}";
        return summary.Length == 0 ? prefix : $"{prefix}：{summary}";
    }

    private static string Summarize(string output, int length = 320)
    {
        var lines = output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        if (lines.Count == 0) return "";

        var meaningful = lines.Where(IsMeaningfulLine).ToList();
        if (meaningful.Count > 0)
        {
            // 优先展示异常/失败那一句，其次才是最后几条
            var key = meaningful.Where(line =>
                line.Contains("Exception:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error", StringComparison.OrdinalIgnoreCase)).ToList();
            var picked = key.Count > 0 ? key.Take(2).ToList() : meaningful.TakeLast(2).ToList();
            var text = string.Join(" / ", picked);
            return text.Length <= length ? text : "…" + text[^length..];
        }

        var last = lines[^1];
        return last.Length <= length ? last : "…" + last[^length..];
    }

    private static bool IsMeaningfulLine(string line)
    {
        // lip 折行后会把 URL 片段当成独立行输出（'https://...zip、.60.1'...），对用户没有意义
        if (line.StartsWith('\'') || line.StartsWith('"') || line.StartsWith('?')) return false;
        if (!line.Contains(' ') && (line.Contains('\'') || line.Contains("...") || line.Contains('/') ||
                                    line.Contains('\\')))
            return false;
        if (StackFragment.IsMatch(line)) return false;
        return !LipNoise.IsMatch(line);
    }

    /// <summary>
    ///     raw.githubusercontent.com 在部分地区无法访问，图标统一改走 jsDelivr。
    ///     https://raw.githubusercontent.com/owner/repo/branch/path → https://cdn.jsdelivr.net/gh/owner/repo@branch/path
    /// </summary>
    private static string? NormalizeAvatar(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        const string rawPrefix = "https://raw.githubusercontent.com/";
        if (!url.StartsWith(rawPrefix, StringComparison.OrdinalIgnoreCase)) return url;

        var segments = url[rawPrefix.Length..].Split('/');
        if (segments.Length < 4) return url;

        var owner = segments[0];
        var repo = segments[1];
        var index = 2;
        // 形如 owner/repo/refs/heads/main/logo.png
        if (segments.Length > 5 && segments[2] == "refs" && segments[3] == "heads")
            index = 4;

        var branch = segments[index];
        var path = string.Join("/", segments.Skip(index + 1));
        if (string.IsNullOrEmpty(branch) || string.IsNullOrEmpty(path)) return url;
        return $"https://cdn.jsdelivr.net/gh/{owner}/{repo}@{branch}/{path}";
    }

    /// <summary>
    ///     lip 注册表里的依赖约束是 Go 的 semver 语法：<c>&gt;=1.2.3</c>、<c>26.40.*</c>、
    ///     <c>&gt;=26.40.0 &lt;26.41.0</c>、<c>A || B</c>。这里只实现挑版本够用的部分：
    ///     比较符（&gt;= &gt; &lt;= &lt; = != ^ ~）、通配符、空格/逗号分隔的 AND 与 <c>||</c> 分隔的 OR。
    /// </summary>
    private static class LoaderConstraint
    {
        internal static bool Satisfies(string constraint, string version)
        {
            if (string.IsNullOrWhiteSpace(constraint)) return true;
            if (!TryParse(version, out var target)) return true; // 版本号认不出来就不拦

            // 逗号在 lip 的约束里当空格用（AND）
            foreach (var alternative in constraint.Replace(',', ' ')
                         .Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var comparators = alternative
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (comparators.Length == 0) continue;
                if (comparators.All(comparator => SatisfiesComparator(comparator, target))) return true;
            }

            return false;
        }

        private static bool SatisfiesComparator(string comparator, SemVer target)
        {
            var op = "";
            foreach (var candidate in new[] { ">=", "<=", "!=", "==", ">", "<", "=", "^", "~" })
            {
                if (!comparator.StartsWith(candidate, StringComparison.Ordinal)) continue;
                op = candidate;
                break;
            }

            var text = comparator[op.Length..].Trim().TrimStart('v', 'V');
            if (text.Length == 0 || text == "*") return true;

            if (text.Contains('*'))
                return MatchesPrefix(text[..text.IndexOf('*')].TrimEnd('.'), target);

            if (!TryParse(text, out var bound)) return true; // 认不出来的写法不拦

            var compare = Compare(target, bound);
            return op switch
            {
                ">=" => compare >= 0,
                ">" => compare > 0,
                "<=" => compare <= 0,
                "<" => compare < 0,
                "!=" => compare != 0,
                "^" or "~" => compare >= 0 && target.Major == bound.Major &&
                              (op == "^" || target.Minor == bound.Minor),
                _ => compare == 0
            };
        }

        private static bool MatchesPrefix(string prefix, SemVer target)
        {
            if (prefix.Length == 0) return true;
            var parts = prefix.Split('.');
            var numbers = new[] { target.Major, target.Minor, target.Patch };
            for (var i = 0; i < parts.Length && i < numbers.Length; i++)
                if (!int.TryParse(parts[i], out var value) || value != numbers[i])
                    return false;
            return true;
        }

        private static int Compare(SemVer left, SemVer right)
        {
            if (left.Major != right.Major) return left.Major.CompareTo(right.Major);
            if (left.Minor != right.Minor) return left.Minor.CompareTo(right.Minor);
            return left.Patch.CompareTo(right.Patch);
        }

        private static bool TryParse(string? text, out SemVer version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var core = text.Trim().TrimStart('v', 'V');
            var suffix = core.IndexOfAny(['-', '+']);
            if (suffix >= 0) core = core[..suffix];

            var parts = core.Split('.');
            if (parts.Length is 0 or > 3) return false;

            var numbers = new int[3];
            for (var i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out numbers[i]))
                    return false;

            version = new SemVer(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        private readonly record struct SemVer(int Major, int Minor, int Patch);
    }
}
