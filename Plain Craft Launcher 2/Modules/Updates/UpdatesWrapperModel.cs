using PCL.Core.App.Localization;
using PCL.Core.Utils;

namespace PCL;

public class UpdatesWrapperModel : IUpdateSource
{
    private readonly IEnumerable<IUpdateSource> _sources;
    private IUpdateSource? _announcementSource;
    private IUpdateSource? _versionSource;

    public UpdatesWrapperModel(IEnumerable<IUpdateSource> sources)
    {
        _sources = sources;
    }

    public string SourceName
    {
        get => _versionSource?.SourceName ?? "";
        set
        {
            if (_versionSource is null)
                return;
            _versionSource.SourceName = value;
        }
    }

    public bool IsAvailable()
    {
        return _sources.Any(x => x.IsAvailable());
    }

    public bool RefreshCache()
    {
        foreach (var item in _sources)
            try
            {
                // 只有真正取到数据的源才作为缓存源；空来源（如缺清单又无权限的源）
                // 若被缓存，后续每次查询都会先撞它一次。
                if (!item.RefreshCache()) continue;
                _versionSource = item;
                return true;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[Update] {item.SourceName} 暂不可用");
            }

        return _versionSource is not null;
    }

    /// <summary>
    ///     按“上次成功的源优先”的顺序枚举，且保证每个源在一次查询中只被尝试一次。
    /// </summary>
    private IEnumerable<IUpdateSource> OrderedSources(IUpdateSource? cached)
    {
        if (cached is not null && _sources.Contains(cached))
            yield return cached;
        foreach (var item in _sources)
            if (!ReferenceEquals(item, cached))
                yield return item;
    }

    /// <summary>
    ///     查询失败的源不再保留为缓存源，避免后续查询继续命中它。
    /// </summary>
    private void DropCache(IUpdateSource failed)
    {
        if (ReferenceEquals(_versionSource, failed)) _versionSource = null;
        if (ReferenceEquals(_announcementSource, failed)) _announcementSource = null;
    }

    public VersionDataModel GetLatestVersion(UpdateChannel channel, UpdateArch arch)
    {
        foreach (var item in OrderedSources(_versionSource))
            try
            {
                var ret = item.GetLatestVersion(channel, arch);
                _versionSource = item;
                return ret;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[Update] {item.SourceName} 无法获取最新版本信息");
                DropCache(item);
            }

        ModBase.Log("[Update] 错误！所有的版本源都无法使用！");
        throw new Exception(Lang.Text("Update.Task.GetVersionInfoFailed"));
    }

    public bool IsLatest(UpdateChannel channel, UpdateArch arch, SemVer currentVersion, int currentVersionCode)
    {
        foreach (var item in OrderedSources(_versionSource))
            try
            {
                var ret = item.IsLatest(channel, arch, currentVersion, currentVersionCode);
                _versionSource = item;
                return ret;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[Update] {item.SourceName} 无法获取最新版本信息");
                DropCache(item);
            }

        ModBase.Log("[Update] 错误！所有的版本源都无法使用！");
        throw new Exception(Lang.Text("Update.Task.GetVersionInfoFailed"));
    }

    public VersionAnnouncementDataModel GetAnnouncementList()
    {
        foreach (var item in OrderedSources(_announcementSource))
            try
            {
                var ret = item.GetAnnouncementList();
                _announcementSource = item;
                return ret;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[Update] {item.SourceName} 无法获取最新公告信息");
                DropCache(item);
            }

        ModBase.Log("[Update] 错误！所有的公告源都无法使用！");
        throw new Exception(Lang.Text("Update.Task.GetAnnouncementFailed"));
    }

    public List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output)
    {
        foreach (var item in OrderedSources(_versionSource))
            try
            {
                var ret = item.GetDownloadLoader(channel, arch, output);
                _versionSource = item;
                return ret;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[Update] {item.SourceName} 无法获取最新版本信息");
                DropCache(item);
            }

        ModBase.Log("[Update] 错误！所有的版本源都无法使用！");
        throw new Exception(Lang.Text("Update.Task.GetVersionInfoFailed"));
    }

    public List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output, VersionDataModel version)
    {
        // 快照已含确定的附件，即使其他查询更换 _versionSource 也不会串用另一个版本。
        return version.DownloadSnapshot?.CreateLoaders(channel, arch, output, version)
               ?? GetDownloadLoader(channel, arch, output);
    }

    public async Task<bool> IsLatestAsync(UpdateChannel channel, UpdateArch arch, SemVer currentVersion,
        int currentVersionCode)
    {
        return await Task.Run(() => IsLatest(channel, arch, currentVersion, currentVersionCode));
    }
}
