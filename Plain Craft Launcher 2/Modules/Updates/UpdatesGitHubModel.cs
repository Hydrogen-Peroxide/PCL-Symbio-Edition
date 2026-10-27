using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>
///     从 GitHub Release 获取更新的更新源
/// </summary>
public class UpdatesGitHubModel : IUpdateSource
{
    private readonly string _owner;
    private readonly string _repo;
    private readonly object _cacheLock = new();
    private readonly Func<string, string> _fetchJson;
    private bool _fetched;
    private readonly Dictionary<UpdateChannel, GithubReleaseModel> _releases = [];

    public UpdatesGitHubModel(string owner, string repo, string name = "GitHub")
        : this(owner, repo, name, url => Requester.FetchString(url,
            new RequestParam { Retries = 2, Timeout = 15000, UseBrowserUserAgent = true })) { }

    internal UpdatesGitHubModel(string owner, string repo, string name, Func<string, string> fetchJson)
    {
        _owner = owner;
        _repo = repo;
        SourceName = name;
        _fetchJson = fetchJson;
    }

    public string SourceName { get; set; }

    public bool IsAvailable()
    {
        return !string.IsNullOrWhiteSpace(_owner) && !string.IsNullOrWhiteSpace(_repo);
    }

    public bool RefreshCache()
    {
        lock (_cacheLock)
        {
            var releases = FetchReleases();
            // 网络/解析失败保留各通道各自的缓存；成功的空列表则确实表示没有可用发布。
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
            return _releases.Count != 0;
        }
    }

    public VersionDataModel GetLatestVersion(UpdateChannel channel, UpdateArch arch)
    {
        var release = GetRelease(channel);
        var asset = SelectAsset(release, arch);
        // 没有目标架构的可校验附件时，不向调用方宣布一个实际无法安装的更新。
        if (release is null || asset is null || GetAssetSha256(asset) is null)
            return new VersionDataModel
            {
                Source = SourceName,
                VersionName = ModBase.versionBaseName,
                VersionCode = ModBase.versionCode,
                Changelog = ""
            };

        var versionName = NormalizeTag(release.TagName);
        return new VersionDataModel
        {
            Source = SourceName,
            VersionName = versionName,
            VersionCode = ParseVersionCode(versionName) ?? ModBase.versionCode,
            Sha256 = GetAssetSha256(asset)!,
            Changelog = release.Body ?? "",
            DownloadSnapshot = new UpdateDownloadSnapshot(channel, arch, versionName, GetAssetSha256(asset)!, asset.BrowserDownloadUrl!)
        };
    }

    public bool IsLatest(UpdateChannel channel, UpdateArch arch, SemVer currentVersion, int currentVersionCode)
    {
        var release = GetRelease(channel);
        if (release is null)
            return true;
        // 缺少 SHA256 时不进行更新，否则下载后的校验必然失败
        if (string.IsNullOrWhiteSpace(GetAssetSha256(SelectAsset(release, arch))))
            return true;
        try
        {
            return currentVersion >= SemVer.Parse(NormalizeTag(release.TagName));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Update] 无法解析 {_owner}/{_repo} 的版本号：{release.TagName}", ModBase.LogLevel.Developer);
            return true;
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

    public List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output, VersionDataModel version)
    {
        return version.DownloadSnapshot?.CreateLoaders(channel, arch, output, version)
               ?? throw new Exception(Lang.Text("Update.Error.UnableToGetUpdate"));
    }

    private GithubReleaseModel? GetRelease(UpdateChannel channel)
    {
        if (channel is not (UpdateChannel.stable or UpdateChannel.beta))
            throw new ArgumentOutOfRangeException(nameof(channel));
        lock (_cacheLock)
        {
            if (!_fetched) RefreshCache();
            return _releases.GetValueOrDefault(channel);
        }
    }

    private List<GithubReleaseModel>? FetchReleases()
    {
        try
        {
            var releases = new List<GithubReleaseModel>();
            // Release 列表含预发布；latest 端点永远不会返回 beta。
            for (var page = 1; ; page++)
            {
                var json = _fetchJson($"https://api.github.com/repos/{_owner}/{_repo}/releases?per_page=100&page={page}");
                var batch = JsonSerializer.Deserialize<List<GithubReleaseModel>>(json)
                    ?? throw new InvalidDataException("GitHub Release 列表为空响应。");
                releases.AddRange(batch);
                if (batch.Count < 100) return releases;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[Update] 无法获取 {_owner}/{_repo} 的 Release 信息");
            return null;
        }
    }

    private static GithubAssetModel? SelectAsset(GithubReleaseModel? release, UpdateArch arch)
    {
        var assets = release?.Assets?
            .Where(a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        if (assets is null || assets.Count == 0)
            return null;
        var archTag = arch switch
        {
            UpdateArch.arm64 => "arm64",
            UpdateArch.x64 => "x64",
            _ => throw new ArgumentOutOfRangeException(nameof(arch))
        };
        return assets.FirstOrDefault(asset =>
            asset.Name.Contains(archTag, StringComparison.OrdinalIgnoreCase) &&
            !asset.Name.Contains(arch == UpdateArch.arm64 ? "x64" : "arm64", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps);
    }

    private static string? GetAssetSha256(GithubAssetModel? asset)
    {
        var digest = asset?.Digest;
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var hash = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest["sha256:".Length..] : digest;
        return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash : null;
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

    private class GithubReleaseModel
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GithubAssetModel>? Assets { get; set; }
    }

    private class GithubAssetModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
