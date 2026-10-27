using PCL.Core.App.Localization;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>跟随一次版本查询返回的不可变下载选择，不依赖更新源的可变缓存。</summary>
public sealed class UpdateDownloadSnapshot
{
    public UpdateChannel Channel { get; }
    public UpdateArch Arch { get; }
    public string VersionName { get; }
    public string Sha256 { get; }
    public string DownloadUrl { get; }

    internal UpdateDownloadSnapshot(UpdateChannel channel, UpdateArch arch, string versionName, string sha256, string downloadUrl)
    {
        Channel = channel;
        Arch = arch;
        VersionName = versionName;
        Sha256 = sha256;
        DownloadUrl = downloadUrl;
    }

    internal List<ModLoader.LoaderBase> CreateLoaders(UpdateChannel channel, UpdateArch arch, string output, VersionDataModel version)
    {
        if (channel != Channel || arch != Arch || version.VersionName != VersionName || version.Sha256 != Sha256)
            throw new InvalidOperationException("更新下载快照与所选版本、校验值或架构不一致。");
        return
        [
            new ModLoader.LoaderTask<int, List<DownloadFile>>(Lang.Text("Update.Task.GetDownloadInfo"), load =>
            {
                load.output = new List<DownloadFile> { new(new[] { DownloadUrl }, output) };
            }),
            new LoaderDownload(Lang.Text("Update.Task.DownloadUpdateFile"), new List<DownloadFile>())
        ];
    }
}
