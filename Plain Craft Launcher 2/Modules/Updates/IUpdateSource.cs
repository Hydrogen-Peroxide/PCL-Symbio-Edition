using PCL.Core.Utils;

namespace PCL;

public interface IUpdateSource
{
    string SourceName { get; set; }

    /// <summary>
    ///     是否可用，根据本地情况判断
    /// </summary>
    /// <returns></returns>
    bool IsAvailable();

    /// <summary>
    ///     确保最新版本
    /// </summary>
    /// <returns>True 表示更新成功，False 表示没有数据更新</returns>
    bool RefreshCache();

    VersionDataModel GetLatestVersion(UpdateChannel channel, UpdateArch arch);
    bool IsLatest(UpdateChannel channel, UpdateArch arch, SemVer currentVersion, int currentVersionCode);
    VersionAnnouncementDataModel GetAnnouncementList();
    List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output);

    /// <summary>使用本次版本查询携带的下载快照；未提供快照的历史更新源继续使用原接口。</summary>
    List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output, VersionDataModel version)
    {
        return version.DownloadSnapshot?.CreateLoaders(channel, arch, output, version)
               ?? GetDownloadLoader(channel, arch, output);
    }
}
