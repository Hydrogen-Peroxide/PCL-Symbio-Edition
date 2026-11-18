using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>
///     从 CNB（cnb.cool）获取更新的更新源。
///     优先读取 updates 分支上的公开清单（匿名可读、可携带 SHA256），
///     清单不可用时退回 Releases API，两者都失败时抛异常交由上层回退到其他源。
/// </summary>
public class UpdatesCnbModel : IUpdateSource
{
    private const int PageSize = 100;

    /// <summary>正式版清单，位于 updates 分支。</summary>
    private static readonly string[] StableManifestNames = ["latest.json"];

    /// <summary>预发布版清单；README 约定与正式版清单分开保存。</summary>
    private static readonly string[] BetaManifestNames = ["latest-beta.json", "beta.json"];

    private readonly object _cacheLock = new();
    private readonly Func<string, string> _fetch;
    private readonly Dictionary<UpdateChannel, CnbManifestVersion> _manifests = [];
    private readonly Dictionary<UpdateChannel, CnbReleaseModel> _releases = [];
    private readonly string _repo;
    private bool _fetched;

    public UpdatesCnbModel(string repo, string name = "CNB")
        : this(repo, name, url => Requester.FetchString(url,
            CreateRequestParam(url.Contains("api.cnb.cool", StringComparison.OrdinalIgnoreCase))))
    {
    }

    /// <summary>
    ///     构造请求参数。只有 CNB 的 API 需要 OpenAPI 的 Accept，
    ///     也只有它需要鉴权头——清单走 git raw，匿名即可读。
    /// </summary>
    internal static RequestParam CreateRequestParam(bool apiRequest)
    {
        var param = new RequestParam
        {
            Retries = 2,
            Timeout = 15000,
            UseBrowserUserAgent = true
        };
        if (apiRequest) param.Accept = "application/vnd.cnb.api+json";
        // 只有列举接口需要鉴权；清单走 git raw，匿名即可读，不发令牌以减少暴露面
        var token = Secrets.CnbToken;
        if (apiRequest && !string.IsNullOrWhiteSpace(token))
            param.Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token };
        return param;
    }

    internal UpdatesCnbModel(string repo, string name, Func<string, string> fetch)
    {
        _repo = repo;
        SourceName = name;
        _fetch = fetch;
    }

    public string SourceName { get; set; }

    public bool IsAvailable()
    {
        return !string.IsNullOrWhiteSpace(_repo);
    }

    public bool RefreshCache()
    {
        lock (_cacheLock)
        {
            _manifests.Clear();
            FetchManifests();
            var releases = FetchReleases();
            // 网络或解析失败时保留各通道原有缓存；成功但为空列表才表示确实没有可用发布。
            if (releases is not null)
            {
                foreach (var channel in new[] { UpdateChannel.stable, UpdateChannel.beta })
                {
                    var release = releases
                        .Where(item => !item.Draft && SemVer.TryParse(NormalizeTag(item.TagName), out _))
                        .Where(item => channel == UpdateChannel.beta ||
                            (!item.Prerelease && string.IsNullOrEmpty(SemVer.Parse(NormalizeTag(item.TagName)).Prerelease)))
                        .OrderByDescending(item => SemVer.Parse(NormalizeTag(item.TagName)))
                        .FirstOrDefault();
                    if (release is null) _releases.Remove(channel);
                    else _releases[channel] = release;
                }
            }

            _fetched = true;
            return _manifests.Count != 0 || _releases.Count != 0;
        }
    }

    /// <summary>
    ///     取不到可用安装包时直接抛异常，交由 UpdatesWrapperModel 回退到下一个更新源。
    /// </summary>
    public VersionDataModel GetLatestVersion(UpdateChannel channel, UpdateArch arch)
    {
        EnsureFetched();
        lock (_cacheLock)
        {
            if (_manifests.TryGetValue(channel, out var manifest) &&
                manifest.Urls.TryGetValue(arch, out var manifestUrl))
                return new VersionDataModel
                {
                    Source = SourceName,
                    VersionName = manifest.VersionName,
                    VersionCode = ParseVersionCode(manifest.VersionName) ?? ModBase.versionCode,
                    Sha256 = manifest.Sha256 ?? "",
                    Changelog = manifest.Changelog ?? "",
                    DownloadSnapshot = new UpdateDownloadSnapshot(channel, arch, manifest.VersionName,
                        manifest.Sha256 ?? "", manifestUrl)
                };

            var release = _releases.GetValueOrDefault(channel);
            if (release is null)
                throw new InvalidDataException($"{SourceName} 上没有 {channel} 通道的可用版本。");
            var asset = SelectAsset(release, arch);
            if (asset is null)
                throw new InvalidDataException(
                    $"{SourceName} 的 {NormalizeTag(release.TagName)} 缺少 {arch} 安装包附件。");

            var versionName = NormalizeTag(release.TagName);
            var sha256 = ResolveSha256(release, asset) ?? "";
            return new VersionDataModel
            {
                Source = SourceName,
                VersionName = versionName,
                VersionCode = ParseVersionCode(versionName) ?? ModBase.versionCode,
                Sha256 = sha256,
                Changelog = release.Body ?? "",
                DownloadSnapshot = new UpdateDownloadSnapshot(channel, arch, versionName, sha256, AssetUrl(asset)!)
            };
        }
    }

    public bool IsLatest(UpdateChannel channel, UpdateArch arch, SemVer currentVersion, int currentVersionCode)
    {
        EnsureFetched();
        lock (_cacheLock)
        {
            if (_manifests.TryGetValue(channel, out var manifest) && manifest.Urls.ContainsKey(arch) &&
                SemVer.TryParse(manifest.VersionName, out var manifestVersion))
                return currentVersion >= manifestVersion;

            // 没有可用附件时同样视为“本源不可用”，否则会把更新错误地判定为已是最新而阻断回退。
            var release = _releases.GetValueOrDefault(channel);
            if (release is null || SelectAsset(release, arch) is null)
                throw new InvalidDataException($"{SourceName} 上没有可用于 {channel}/{arch} 的安装包。");
            return currentVersion >= SemVer.Parse(NormalizeTag(release.TagName));
        }
    }

    public VersionAnnouncementDataModel GetAnnouncementList()
    {
        return new VersionAnnouncementDataModel { Content = new List<VersionAnnouncementContentModel>() };
    }

    public List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output)
    {
        return GetDownloadLoader(channel, arch, output, GetLatestVersion(channel, arch));
    }

    public List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output,
        VersionDataModel version)
    {
        return version.DownloadSnapshot?.CreateLoaders(channel, arch, output, version)
               ?? throw new Exception(Lang.Text("Update.Error.UnableToGetUpdate"));
    }

    private void EnsureFetched()
    {
        lock (_cacheLock)
        {
            if (_fetched) return;
        }

        RefreshCache();
    }

    private void FetchManifests()
    {
        foreach (var channel in new[] { UpdateChannel.stable, UpdateChannel.beta })
        {
            var names = channel == UpdateChannel.stable ? StableManifestNames : BetaManifestNames;
            foreach (var name in names)
            {
                var manifest = TryFetchManifest(channel, name);
                if (manifest is null) continue;
                _manifests[channel] = manifest;
                ModBase.Log($"[Update] 已从 {SourceName} 清单 {name} 读取版本 {manifest.VersionName}");
                break;
            }
        }
    }

    private CnbManifestVersion? TryFetchManifest(UpdateChannel channel, string fileName)
    {
        var url = $"https://cnb.cool/{_repo}/-/git/raw/updates/{fileName}";
        try
        {
            return ParseManifest(_fetch(url));
        }
        catch (Exception ex)
        {
            // 清单尚未发布时是 404 页面而非 JSON，属正常情况，安静退回 Releases API
            ModBase.Log(ex, $"[Update] 无法读取 {SourceName} 清单 {fileName}", ModBase.LogLevel.Developer);
            return null;
        }
    }

    /// <summary>
    ///     容错解析更新清单。仓库侧可能写入不同字段名，这里兼容常见写法；
    ///     解析不出可用下载地址时返回 null，让调用方退回 Releases API。
    /// </summary>
    private static CnbManifestVersion? ParseManifest(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (node is not JsonObject root) return null;
        // 允许 { "stable": { ... } } / { "latest": { ... } } 这类多包一层的写法
        if (root["stable"] is JsonObject stable) root = stable;
        else if (root["latest"] is JsonObject latest) root = latest;

        var versionName = NormalizeTag(FirstString(root, "version", "name", "tag_name", "version_name"));
        if (string.IsNullOrWhiteSpace(versionName)) return null;

        var sha256 = NormalizeSha(FirstString(root, "sha256", "hash", "digest"));
        var changelog = FirstString(root, "changelog", "body", "notes", "release_notes");
        var urls = new Dictionary<UpdateArch, string>();

        foreach (var key in new[] { "downloads", "urls", "assets", "files" })
        {
            switch (root[key])
            {
                case JsonObject map:
                    foreach (var (name, value) in map)
                    {
                        var arch = MapArch(name);
                        if (arch is null) continue;
                        var url = value is JsonValue jv && jv.TryGetValue<string>(out var text)
                            ? text
                            : FirstString(value, "url", "download_url");
                        if (!string.IsNullOrWhiteSpace(url) && IsHttps(url)) urls[arch.Value] = url;
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array.OfType<JsonObject>())
                    {
                        var name = FirstString(item, "name", "file_name", "arch");
                        var url = FirstString(item, "url", "download_url", "browser_download_url", "brower_download_url");
                        if (string.IsNullOrWhiteSpace(url) || !IsHttps(url)) continue;
                        var arch = MapArch(name) ?? UpdateArch.x64;
                        urls[arch] = url;
                        sha256 ??= NormalizeSha(FirstString(item, "sha256", "hash", "digest"));
                    }

                    break;
            }
        }

        if (urls.Count == 0)
        {
            var url = FirstString(root, "url", "download_url", "download", "exe");
            if (!string.IsNullOrWhiteSpace(url) && IsHttps(url)) urls[UpdateArch.x64] = url;
        }

        return urls.Count == 0
            ? null
            : new CnbManifestVersion(versionName, sha256, changelog, urls);
    }

    private static string? FirstString(JsonNode? node, params string[] names)
    {
        if (node is not JsonObject obj) return null;
        foreach (var name in names)
            foreach (var (key, value) in obj)
            {
                if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (value is JsonValue jv && jv.TryGetValue<string>(out var text) &&
                    !string.IsNullOrWhiteSpace(text))
                    return text;
            }

        return null;
    }

    private static string? NormalizeSha(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var hash = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..]
            : digest;
        hash = hash.Trim();
        return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash.ToLowerInvariant() : null;
    }

    private static UpdateArch? MapArch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains("arm64", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("aarch64", StringComparison.OrdinalIgnoreCase))
            return UpdateArch.arm64;
        if (text.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("amd64", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("x86_64", StringComparison.OrdinalIgnoreCase))
            return UpdateArch.x64;
        return null;
    }

    private static bool IsHttps(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    private List<CnbReleaseModel>? FetchReleases()
    {
        try
        {
            var releases = new List<CnbReleaseModel>();
            for (var page = 1; ; page++)
            {
                var json = _fetch(
                    $"https://api.cnb.cool/{_repo}/-/releases?page={page}&page_size={PageSize}");
                var batch = JsonSerializer.Deserialize<List<CnbReleaseModel>>(json)
                            ?? throw new InvalidDataException("CNB Release 列表为空响应。");
                releases.AddRange(batch);
                if (batch.Count < PageSize) return releases;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Update] 无法获取 {_repo} 的 Release 信息");
            return null;
        }
    }

    /// <summary>
    ///     挑选与目标架构匹配的 exe 附件。CNB 的附件没有 digest 字段，只能按文件名判断。
    /// </summary>
    private static CnbAssetModel? SelectAsset(CnbReleaseModel? release, UpdateArch arch)
    {
        var assets = release?.Assets?
            .Where(a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            .Where(a => AssetUrl(a) is not null)
            .ToList();
        if (assets is null || assets.Count == 0)
            return null;
        var archTag = arch switch
        {
            UpdateArch.arm64 => "arm64",
            UpdateArch.x64 => "x64",
            _ => throw new ArgumentOutOfRangeException(nameof(arch))
        };
        var otherTag = arch == UpdateArch.arm64 ? "x64" : "arm64";
        return assets.FirstOrDefault(asset =>
            asset.Name!.Contains(archTag, StringComparison.OrdinalIgnoreCase) &&
            !asset.Name.Contains(otherTag, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Releases API 的附件不带 SHA256，若仓库另外提供了 .sha256 / .sha256sum 校验文件则用其校验；
    ///     没有校验文件时返回 null，由更新流程跳过校验（否则永远无法安装）。
    /// </summary>
    private string? ResolveSha256(CnbReleaseModel release, CnbAssetModel asset)
    {
        var checksumAsset = release.Assets?.FirstOrDefault(a =>
            a.Name is not null &&
            (a.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".sha256sum", StringComparison.OrdinalIgnoreCase)));
        var url = AssetUrl(checksumAsset);
        if (url is null) return null;
        try
        {
            var text = _fetch(url);
            // 优先取提到目标文件的那一行的哈希，否则退回文件中第一个 SHA256
            var line = text
                .Split('\n')
                .FirstOrDefault(item => item.Contains(asset.Name!, StringComparison.OrdinalIgnoreCase)) ?? text;
            var match = Regex.Match(line, "[0-9a-fA-F]{64}");
            if (!match.Success) match = Regex.Match(text, "[0-9a-fA-F]{64}");
            return match.Success ? match.Value.ToLowerInvariant() : null;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Update] 无法获取 {SourceName} 的校验文件");
            return null;
        }
    }

    private static string? AssetUrl(CnbAssetModel? asset)
    {
        if (asset is null) return null;
        if (!string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
            Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var browserUrl) &&
            browserUrl.Scheme == Uri.UriSchemeHttps)
            return asset.BrowserDownloadUrl;
        if (!string.IsNullOrWhiteSpace(asset.ApiUrl) &&
            Uri.TryCreate(asset.ApiUrl, UriKind.Absolute, out var apiUrl) &&
            apiUrl.Scheme == Uri.UriSchemeHttps)
            return asset.ApiUrl;
        return null;
    }

    private static string NormalizeTag(string? tag)
    {
        return (tag ?? "").Trim().TrimStart('v', 'V');
    }

    private static int? ParseVersionCode(string versionName)
    {
        var parts = versionName.Split('.', '-', '+');
        if (parts.Length < 3
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var patch))
            return null;
        return major * 10000 + minor * 100 + patch;
    }

    private sealed record CnbManifestVersion(
        string VersionName,
        string? Sha256,
        string? Changelog,
        Dictionary<UpdateArch, string> Urls);

    private class CnbReleaseModel
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }

        [JsonPropertyName("body")] public string? Body { get; set; }

        [JsonPropertyName("draft")] public bool Draft { get; set; }

        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }

        [JsonPropertyName("assets")] public List<CnbAssetModel>? Assets { get; set; }
    }

    private class CnbAssetModel
    {
        [JsonPropertyName("name")] public string? Name { get; set; }

        // CNB OpenAPI 里该字段拼写就是 brower_download_url，按原样映射
        [JsonPropertyName("brower_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("url")] public string? ApiUrl { get; set; }

        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
