using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PCL.Core.App;
using PCL.Core.App.Tools;
using PCL.Core.IO;
using PCL.Core.IO.Net;
using PCL.Core.UI;
using PCL.Core.Utils.OS;
using PCL.Core.Utils.Secret;
using PCL.Core.Utils.Validate;
using PCL.Network;
using PCL.Network.Loaders;
using PCL.Core.App.Localization;
using System.Globalization;

namespace PCL;

internal static class LauncherTools
{
    private static int cleaning;
    internal static bool IsCleaningCache => Volatile.Read(ref cleaning) != 0;
    internal static event Action? CleaningChanged;

    private static void DownloadState(ModLoader.LoaderCombo<int> loader)
    {
        try
        {
            switch (loader.State)
            {
                case ModBase.LoadState.Finished:
                {
                    HintService.Hint(Lang.Text("Tools.Test.CustomDownload.Finished", loader.name), HintType.Success);
                    Console.Beep();
                    break;
                }
                case ModBase.LoadState.Failed:
                {
                    ModBase.Log(
                        loader.Error,
                        $"{loader.name}失败",
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Tools.Test.Error.OperationFailed"));
                    Console.Beep();
                    break;
                }
                case ModBase.LoadState.Aborted:
                {
                    HintService.Hint(Lang.Text("Tools.Test.CustomDownload.Aborted", loader.name));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
        }
    }

    public static void StartCustomDownload(string url, string fileName, string folder = null, string userAgent = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = SystemDialogs.SelectSaveFile(Lang.Text("Tools.Test.CustomDownload.SelectLocation"), fileName);
                if (!folder.Contains(@"\")) return;
                if (folder.EndsWith(fileName)) folder = folder[..^fileName.Length];
            }

            folder = folder.Replace("/", @"\").TrimEnd(new[] { '\\' }) + @"\";
            try
            {
                Directory.CreateDirectory(folder);
                ModBase.CheckPermissionWithException(folder);
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    $"访问文件夹失败（{folder}）",
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Tools.Test.Error.OperationFailed"));
                return;
            }

            ModBase.Log("[Download] 自定义下载文件名：" + fileName);
            ModBase.Log("[Download] 自定义下载文件目标：" + folder);
            var uuid = ModBase.GetUuid();
            ModLoader.LoaderBase loaderdownload;
            if (new HttpValidator().Validate(url).IsValid)
                loaderdownload = new LoaderDownload(Lang.Text("Tools.Test.CustomDownload.LoaderName", fileName),
                    new List<DownloadFile> { new(new[] { url }, folder + fileName, null, true, userAgent) });
            else // UNC 路径
                loaderdownload = new LoaderDownloadUnc(Lang.Text("Tools.Test.CustomDownload.LoaderName", fileName),
                    new Tuple<string, string>(url, folder + fileName));
            var loaderCombo = new ModLoader.LoaderCombo<int>(Lang.Text("Tools.Test.CustomDownload.LoaderTitle", uuid), new[] { loaderdownload })
                { OnStateChanged = a => DownloadState((ModLoader.LoaderCombo<int>)a) };
            loaderCombo.Start();
            ModLoader.LoaderTaskbarAdd(loaderCombo);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "开始自定义下载失败",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Tools.Test.Error.OperationFailed"));
        }
    }

    public static void Jrrp()
    {
        var random = new Random(GenerateDailySeed());
        var luckValue = random.Next(0, 101);
        var rating = GetRating(luckValue);
        var title = Lang.Text("Tools.Test.Luck.MsgboxTitle", Lang.Date(DateTime.Now, "d"));

        if (luckValue >= 60)
            ModMain.MyMsgBox(Lang.Text("Tools.Test.Luck.MessageGood", luckValue, rating), title);
        else
            ModMain.MyMsgBox(Lang.Text("Tools.Test.Luck.MessageBad", luckValue, rating), title, isWarn: luckValue <= 30);
    }

    public static void RubbishClear()
    {
        if (Interlocked.CompareExchange(ref cleaning, 1, 0) != 0) return;
        ModBase.RunInUi(() => CleaningChanged?.Invoke());
        // 只有当没有运行中的Minecraft游戏且启动器不在加载状态时才能清理

        // 清理的文件数量
        // 所有 Minecraft 文件夹


        // 寻找所有 Minecraft 文件夹

        // 删除 Minecraft 的缓存
        // 删除日志和崩溃报告并计数

        // 删除 Natives 文件

        // 删除 PCL 的缓存

        ModBase.RunInNewThread(() =>
        {
            try
            {
                if (!ModWatcher.hasRunningMinecraft && ModLaunch.mcLaunchLoader.State != ModBase.LoadState.Loading)
                {
                    if (ModNet.HasDownloadingTask())
                    {
                        HintService.Hint(Lang.Text("Tools.Test.Clean.WaitForDownload"));
                        return;
                    }

                    if (!ModFolder.mcFolderList.Any()) ModFolder.mcFolderListLoader.Start();
                    if (States.Hint.CleanJunkFile <= 2)
                    {
                        if (ModMain.MyMsgBox(
                                Lang.Text("Tools.Test.Clean.ConfirmMessage"),
                                Lang.Text("Tools.Test.Clean.ConfirmTitle"),
                                Lang.Text("Common.Action.Confirm"),
                                Lang.Text("Common.Action.Cancel")
                            ) == 2) return;
                        States.Hint.CleanJunkFile += 1;
                    }

                    var num = 0;
                    var cleanMcFolderList = new List<DirectoryInfo>();
                    if (!ModFolder.mcFolderList.Any()) ModFolder.mcFolderListLoader.WaitForExit();
                    foreach (var mcFolder in ModFolder.mcFolderList)
                    {
                        cleanMcFolderList.Add(new DirectoryInfo(mcFolder.Location));
                        var dirInfo = new DirectoryInfo(mcFolder.Location + "versions");
                        if (dirInfo.Exists)
                            foreach (var item in dirInfo.EnumerateDirectories())
                                cleanMcFolderList.Add(item);
                    }

                    foreach (var dirInfo in cleanMcFolderList)
                    {
                        num += ModBase.DeleteDirectory(
                            dirInfo.FullName + (dirInfo.FullName.EndsWith(@"\") ? "" : @"\") + @"crash-reports\", true);
                        num += ModBase.DeleteDirectory(
                            dirInfo.FullName + (dirInfo.FullName.EndsWith(@"\") ? "" : @"\") + @"logs\", true);
                        foreach (var fileInfo in dirInfo.EnumerateFiles("*"))
                            if (fileInfo.Name.StartsWith("hs_err_pid") || fileInfo.Name.EndsWith(".log") ||
                                fileInfo.Name == "WailaErrorOutput.txt")
                            {
                                fileInfo.Delete();
                                num += 1;
                            }

                        foreach (var dirInfo2 in dirInfo.EnumerateDirectories())
                            if ((dirInfo2.Name ?? "") == (dirInfo2.Name + "-natives" ?? "") ||
                                dirInfo2.Name == "natives-windows-x86_64")
                                num += ModBase.DeleteDirectory(dirInfo2.FullName, true);
                    }

                    num += ModBase.DeleteDirectory(ModBase.pathTemp, true);
                    num += ModBase.DeleteDirectory(Path.Combine(SystemPaths.DriveLetter, "ProgramData", "PCL"), true);
                    if (num != 0)
                    {
                        ModMain.MyMsgBox(Lang.Text("Tools.Test.Clean.ClearedMessage", num),
                            Lang.Text("Tools.Test.Clean.Cleared"), Lang.Text("Common.Action.Confirm"), "", "", false, true, true);
                        Process.Start(new ProcessStartInfo(Basics.ExecutablePath));
                        FormMain.EndProgramForce();
                    }
                    else
                    {
                        HintService.Hint(Lang.Text("Tools.Test.Clean.NoFiles"));
                    }
                }
                else
                {
                    HintService.Hint(Lang.Text("Tools.Test.Clean.CloseGameFirst"));
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    "清理垃圾失败",
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Tools.Test.Error.OperationFailed"));
            }
            finally
            {
                Volatile.Write(ref cleaning, 0);
                ModBase.RunInUi(() => CleaningChanged?.Invoke());
            }
        }, "Rubbish Clear");
    }

    public static string GetRandomCave()
    {
        return Lang.Text("Tools.Test.CeNotice");
    }

    public static string GetRandomHint()
    {
        return Lang.Text("Tools.Test.CeNotice");
    }

    public static string GetRandomPresetHint()
    {
        return Lang.Text("Tools.Test.CeNotice");
    }

    public static int GenerateDailySeed()
    {
        var datePart = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        return DJB2Hash(datePart + Identify.LauncherId);
    }

    private static int DJB2Hash(string str)
    {
        var hash = 5381L;
        var prime = 33L;
        foreach (var c in str)
        {
            long charValue = c;
            hash = (hash * prime + charValue) % 0x100000000L;
        }

        return (int)(hash & 0x7FFFFFFFL);
    }

    public static string GetRating(int luckValue)
    {
        var key = luckValue switch
        {
            100 => "Tools.Test.Luck.Rating100",
            >= 95 => "Tools.Test.Luck.Rating95",
            >= 90 => "Tools.Test.Luck.Rating90",
            >= 60 => "Tools.Test.Luck.Rating60",
            >= 40 => "Tools.Test.Luck.Rating40",
            >= 30 => "Tools.Test.Luck.Rating30",
            >= 10 => "Tools.Test.Luck.Rating10",
            _ => "Tools.Test.Luck.Rating0"
        };

        return Lang.Text(key);
    }
}
