using System.IO;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;

namespace PCL;

internal enum JavaLaunchAction { Loading, Unavailable, Download, Launch }

internal static class JavaLaunchSession
{
    private static readonly System.Text.StringBuilder log = new();
    internal static event Action? LogChanged;
    internal static string LogText => log.ToString();
    internal static void AppendLog(string message) { log.AppendLine(message); LogChanged?.Invoke(); }
    internal static void ClearLog() { log.Clear(); LogChanged?.Invoke(); }
    private static bool started;
    internal static bool Ready { get; private set; }
    internal static event Action? Changed;

    internal static void Initialize()
    {
        if (started) return;
        started = true;
        ModInstanceList.mcInstanceListLoader.LoadingStateChanged += (_, _) => ModBase.RunInUi(() => Changed?.Invoke());
        ModFolder.mcFolderListLoader.LoadingStateChanged += (_, _) => ModBase.RunInUi(() => Changed?.Invoke());
        // 初始化档案
        ModProfile.GetProfile();
        if (!(ModProfile.profileList.Count == 0) && ModProfile.lastUsedProfile >= 0 &&
            ModProfile.lastUsedProfile < ModProfile.profileList.Count)
            ModProfile.selectedProfile = ModProfile.profileList[ModProfile.lastUsedProfile];

        // 加载实例
        ModBase.RunInNewThread(() =>
        {
            // 自动整合包安装：准备
            string packInstallPath = null;
            if (File.Exists(Path.Combine(ModBase.exePath, "modpack.zip")))
                packInstallPath = Path.Combine(ModBase.exePath, "modpack.zip");
            if (File.Exists(Path.Combine(ModBase.exePath, "modpack.mrpack")))
                packInstallPath = Path.Combine(ModBase.exePath, "modpack.mrpack");
            if (packInstallPath is not null)
            {
                ModBase.Log("[Launch] 需自动安装整合包：" + packInstallPath, ModBase.LogLevel.Debug);
                States.Game.SelectedFolder = @"$.minecraft\";
                if (!Directory.Exists(ModBase.exePath + @".minecraft\"))
                {
                    Directory.CreateDirectory(ModBase.exePath + @".minecraft\");
                    Directory.CreateDirectory(ModBase.exePath + @".minecraft\versions\");
                    ModFolder.McFolderLauncherProfilesJsonCreate(ModBase.exePath + @".minecraft\");
                }

                ModFolder.AddFolder(ModBase.exePath + @".minecraft\",
                    ModBase.GetFolderNameFromPath(ModBase.exePath), false);
                ModFolder.mcFolderListLoader.WaitForExit();
            }

            // 确认 Minecraft 文件夹存在
            ModFolder.mcFolderSelected =
                States.Game.SelectedFolder.ToString().Replace("$", ModBase.exePath);
            if (string.IsNullOrEmpty(ModFolder.mcFolderSelected) || !Directory.Exists(ModFolder.mcFolderSelected))
            {
                // 无效的文件夹
                if (string.IsNullOrEmpty(ModFolder.mcFolderSelected))
                    ModBase.Log("[Launch] 没有已储存的 Minecraft 文件夹");
                else
                    ModBase.Log("[Launch] Minecraft 文件夹无效，该文件夹已不存在：" + ModFolder.mcFolderSelected,
                        ModBase.LogLevel.Debug);
                ModFolder.mcFolderListLoader.WaitForExit(isForceRestart: true);
                States.Game.SelectedFolder = ModFolder.mcFolderList[0].Location.Replace(ModBase.exePath, "$");
            }

            ModBase.Log("[Launch] Minecraft 文件夹：" + ModFolder.mcFolderSelected);
            if (Config.Debug.AddRandomDelay)
                Thread.Sleep(RandomUtils.NextInt(500, 3000));
            // 自动整合包安装
            if (packInstallPath is not null)
                try
                {
                    var installLoader = ModModpack.ModpackInstall(packInstallPath);
                    ModBase.Log("[Launch] 自动安装整合包已开始：" + packInstallPath);
                    installLoader.WaitForExit();
                    if (installLoader.State == ModBase.LoadState.Finished)
                    {
                        ModBase.Log("[Launch] 自动安装整合包成功，清理安装包：" + packInstallPath);
                        if (File.Exists(packInstallPath))
                            File.Delete(packInstallPath);
                    }
                }
                catch (ModBase.CancelledException ex)
                {
                    ModBase.Log(ex, "自动安装整合包被用户取消：" + packInstallPath);
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Select.Folder.Error.InstallPack", packInstallPath),
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Select.Folder.Error.InstallPack", packInstallPath));
                }

            // 确认 Minecraft 版本实例
            var selection = States.Game.SelectedInstance;
            var instance = selection == "" ? null : new McInstance(selection);
            if (instance is null || !instance.PathInstance.StartsWithF(ModFolder.mcFolderSelected) ||
                !instance.Check())
            {
                // 无效的实例
                ModBase.Log("[Launch] 当前选择的 Minecraft 实例无效：" + (instance is null ? "null" : instance.PathInstance),
                    instance is null ? ModBase.LogLevel.Normal : ModBase.LogLevel.Debug);
                if (ModInstanceList.mcInstanceListLoader.State != ModBase.LoadState.Finished)
                    ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                        ModLoader.LoaderFolderRunType.ForceRun, 1, @"versions\", true);
                if (ModInstanceList.mcInstanceList.Count == 0 ||
                    ModInstanceList.mcInstanceList.First().Value[0].Logo.Contains("RedstoneBlock"))
                {
                    instance = null;
                    States.Game.SelectedInstance = "";
                    ModBase.Log("[Launch] 无可用 Minecraft 实例");
                }
                else
                {
                    instance = ModInstanceList.mcInstanceList.First().Value[0];
                    States.Game.SelectedInstance = instance.Name;
                    ModBase.Log("[Launch] 自动选择 Minecraft 实例：" + instance.PathInstance);
                }
            }

            ModBase.RunInUi(() =>
            {
                ModInstanceList.McMcInstanceSelected = instance; // 绕这一圈是为了避免 McInstanceCheck 触发第二次实例改变
                Ready = true;
                ProfileSkins.LoadSelected();
                Changed?.Invoke();
            });
        }, "Instance Check", ThreadPriority.AboveNormal);

    }

    internal static JavaLaunchAction Action(bool allowDownload)
    {
        if (!Ready || ModInstanceList.mcInstanceListLoader.State == ModBase.LoadState.Loading ||
            ModFolder.mcFolderListLoader.State == ModBase.LoadState.Loading) return JavaLaunchAction.Loading;
        if (ModInstanceList.McMcInstanceSelected is null)
            return allowDownload ? JavaLaunchAction.Download : JavaLaunchAction.Unavailable;
        return JavaLaunchAction.Launch;
    }

    internal static bool CanExecute(JavaLaunchAction action) =>
        action == JavaLaunchAction.Download || action == JavaLaunchAction.Launch && !AccountSignIn.IsActive && ModProfile.selectedProfile is not null;

    internal static bool TryLaunch()
    {
        if (ModLaunch.mcLaunchLoader.State == ModBase.LoadState.Loading ||
            !CanExecute(Action(false)) || ModInstanceList.McMcInstanceSelected is not { } instance) return false;
        if (File.Exists(Path.Combine(instance.PathInstance, ".pclignore")))
        {
            HintService.Hint(Lang.Text("Launch.Home.Instance.InstallingCannotLaunch"), HintType.Error);
            return false;
        }
        return ModLaunch.McLaunchStart();
    }

    internal static void Cancel()
    {
        ModLaunch.mcLaunchLoader.Abort();
        if (ModLaunch.mcLaunchLoaderReal is not null)
        {
            ModLaunch.mcLaunchLoaderReal.Abort();
            ModLaunch.McLaunchLog("已取消启动");
            try
            {
                if (ModLaunch.mcLaunchWatcher is not null)
                    ModLaunch.mcLaunchWatcher.Kill();
                else if (ModLaunch.mcLaunchProcess is not null)
                    if (!ModLaunch.mcLaunchProcess.HasExited)
                        ModLaunch.mcLaunchProcess.Kill();
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Minecraft.Launch.Error.CancelProcess"),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Launch.Error.CancelProcess"));
            }
        }
    }

    internal static (string Stage, double? Progress) ReadProgress()
    {
        var loader = ModLaunch.mcLaunchLoaderReal;
        if (loader is null || loader.State != ModBase.LoadState.Loading) return ("正在准备启动", null);
        try
        {
            var stage = loader.GetLoaderList(false).FirstOrDefault(task =>
                task.State is ModBase.LoadState.Loading or ModBase.LoadState.Waiting);
            return (stage?.name ?? Lang.Text("Launch.Status.Completed"), Math.Clamp(loader.Progress, 0, 1));
        }
        catch (InvalidOperationException) { return ("正在准备启动", null); }
    }

}
