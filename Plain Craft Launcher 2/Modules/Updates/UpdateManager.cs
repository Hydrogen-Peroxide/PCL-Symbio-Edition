using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;

namespace PCL;

/// <summary>
/// PCL CE 更新系统 - 在 PCL Symbio Edition 中已禁用。
/// </summary>
public static class UpdateManager
{
    public static bool isUpdateWaitingRestart;

    /// <summary>
    /// 在 Symbio Edition 中不连接任何 PCL CE 更新服务器。
    /// </summary>
    public static UpdatesWrapperModel remoteServer = new(new List<IUpdateSource>());

    public static bool IsCurrentVersionBeta => false;

    public static UpdateEnums.VersionStatus GetVersionStatus()
    {
        return UpdateEnums.VersionStatus.Latest;
    }

    public static ModLoader.LoaderCombo<JsonObject> updateLoader;

    public static void UpdateStart(UpdateEnums.UpdateType type, string receivedKey = null, bool forceValidated = false)
    {
        // 已禁用：PCL Symbio Edition 不使用 PCL CE 更新
    }

    public static void UpdateRestart(bool triggerRestartAndByEnd, bool triggerRestart = true)
    {
        // 已禁用：PCL Symbio Edition 不使用 PCL CE 更新
    }

    /// <summary>
    ///     确保 PathTemp 下的 Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ///     如果不是，则下载一个。
    /// </summary>
    internal static void DownloadLatestPCL(ModLoader.LoaderBase loaderToSyncProgress = null)
    {
        // 已禁用：PCL Symbio Edition 不使用 PCL CE 更新
    }

    public static ModLoader.LoaderTask<int, int> serverLoader =
        new("PCL CE 更新服务（已禁用）",
            _ => { },
            priority: ThreadPriority.BelowNormal);

    /// <summary>
    ///     展示社区版提示 - 在 Symbio Edition 中已禁用。
    /// </summary>
    public static void ShowCEAnnounce()
    {
        // 已禁用：PCL Symbio Edition 不使用 PCL CE 社区版提示
    }
}