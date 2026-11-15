// SPDX-License-Identifier: MPL-2.0
// 百宝箱彩蛋的查找、下载与启动流程改写自 PCL Living Fork（MPL-2.0）：
// https://github.com/DDZmumo/PCL-Living-Fork
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using PCL.Core.App.Localization;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>
///     百宝箱的「千万别点」彩蛋：找到或下载 PCL Living Fork，启动成功后关闭当前启动器。
///     流程参考 PCL Living Fork（MPL-2.0）的 SymbioLauncherSwitch.cs 改写，下载方向反过来。
/// </summary>
public static class ModLivingForkEgg
{
    private const string Repository = "https://github.com/DDZmumo/PCL-Living-Fork";
    private const string LatestApi = "https://api.github.com/repos/DDZmumo/PCL-Living-Fork/releases/latest";

    /// <summary>下载后保存的文件名；与当前启动器放在同一目录。</summary>
    private const string LocalName = "PCLLF.exe";

    private static int _running;

    /// <summary>尝试占用彩蛋，避免重复点击同时执行多次。</summary>
    public static bool TryBegin() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    public static void End() => Interlocked.Exchange(ref _running, 0);

    /// <summary>在后台线程执行：查找本地程序，没有就下载最新正式版，然后启动并退出当前启动器。</summary>
    public static void Run()
    {
        try
        {
            var executable = Process.GetCurrentProcess().MainModule?.FileName ??
                             throw new IOException("无法定位当前程序。");
            var folder = Path.GetDirectoryName(executable) ?? throw new IOException("无法定位当前目录。");

            LivingForkAsset? downloaded = null;
            var target = FindLocal(folder, executable);
            if (target is null)
            {
                HintService.Hint(Lang.Text("Tools.Test.Surprise.Preparing"));
                downloaded = SelectAsset((JsonObject)Requester.FetchJson(LatestApi, new RequestParam
                {
                    Accept = "application/json",
                    Timeout = 15000,
                    Retries = 2,
                    UseBrowserUserAgent = true
                }));

                var stage = Path.Combine(folder, ".pcl-surprise-" + Guid.NewGuid().ToString("N") + ".tmp");
                target = Path.Combine(folder, LocalName);
                try
                {
                    HintService.Hint(Lang.Text("Tools.Test.Surprise.Fetching"));
                    Download(downloaded, stage);
                    using (var input = new FileStream(stage, FileMode.Open, FileAccess.Read, FileShare.Read))
                        VerifyDownload(input, downloaded);
                    // 已存在同名文件时不覆盖用户自己的程序
                    File.Move(stage, target);
                }
                finally
                {
                    if (File.Exists(stage)) File.Delete(stage);
                }
            }
            else
            {
                HintService.Hint(Lang.Text("Tools.Test.Surprise.Exists"));
            }

            using (var program = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (downloaded is null) VerifyHeader(program);
                else VerifyDownload(program, downloaded);
                HintService.Hint(Lang.Text("Tools.Test.Surprise.Ready"));
                Start(target);
                ModBase.RunInUiWait(() => ModMain.frmMain.EndProgram(false));
            }
        }
        catch (ThreadInterruptedException)
        {
            HintService.Hint(Lang.Text("Tools.Test.Surprise.Cancelled"));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "工具百宝箱彩蛋未完成，已保留当前启动器", ModBase.LogLevel.Hint);
            HintService.Hint(Lang.Text("Tools.Test.Surprise.Failed"), HintType.Error);
        }
        finally
        {
            End();
        }
    }

    /// <summary>查找当前目录里已有的 PCL Living Fork 程序（跳过当前正在运行的启动器）。</summary>
    private static string? FindLocal(string folder, string currentExecutable)
    {
        return Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(currentExecutable),
                StringComparison.OrdinalIgnoreCase))
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .Where(path => IsLauncherFile(Path.GetFileName(path)))
            .OrderBy(path => string.Equals(Path.GetFileName(path), LocalName, StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1)
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsLauncherFile(string name) =>
        string.Equals(name, LocalName, StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(name, @"\APCL-Living-Fork-\d+\.\d+\.\d+(?:\.\d+)?(?:-[A-Za-z0-9.]+)?(?:-win-x64)?\.exe\z",
            RegexOptions.IgnoreCase);

    /// <summary>从 GitHub Release 中挑出唯一可用的 x64 程序，并校验地址、大小与 SHA256 摘要。</summary>
    private static LivingForkAsset SelectAsset(JsonObject release)
    {
        var tag = release["tag_name"]?.ToString() ?? "";
        var isDraft = release["draft"] is JsonValue draft && draft.TryGetValue<bool>(out var draftValue) && draftValue;
        var isPrerelease = release["prerelease"] is JsonValue pre &&
                           pre.TryGetValue<bool>(out var preValue) && preValue;
        if (isDraft || isPrerelease || !Regex.IsMatch(tag, @"\Av?\d+\.\d+\.\d+(?:\.\d+)?\z"))
            throw new IOException("发布记录无效。");

        var name = "PCL-Living-Fork-" + tag.TrimStart('v') + ".exe";
        var matches = (release["assets"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(asset => asset["name"]?.ToString() == name && asset["state"]?.ToString() == "uploaded")
            .ToArray();
        if (matches.Length != 1) throw new IOException("发布中没有唯一可用的程序。");

        var asset = matches[0];
        var url = asset["browser_download_url"]?.ToString() ?? "";
        var digest = asset["digest"]?.ToString() ?? "";
        var expected = $"{Repository}/releases/download/{tag}/{name}";
        long.TryParse(asset["size"]?.ToString(), out var size);
        if (url != expected || !Regex.IsMatch(digest, @"\Asha256:[a-fA-F0-9]{64}\z") ||
            size < 1024 || size > 256L * 1024 * 1024)
            throw new IOException("发布附件的地址、大小或校验信息无效。");

        return new LivingForkAsset { Url = url, Sha256 = digest[7..], Size = size };
    }

    /// <summary>用启动器自带的多线程下载引擎把程序下载到临时文件。</summary>
    private static void Download(LivingForkAsset asset, string target)
    {
        var checker = new ModBase.FileChecker(actualSize: asset.Size, hash: asset.Sha256);
        var file = new DownloadFile(new[] { asset.Url }, target, checker, true);
        var download = new LoaderDownload(Lang.Text("Tools.Test.Surprise.DownloadName"),
            new List<DownloadFile> { file });
        var combo = new ModLoader.LoaderCombo<int>(Lang.Text("Tools.Test.Surprise.DownloadTitle"),
            new[] { download });
        ModBase.RunInUiWait(() =>
        {
            combo.Start();
            ModLoader.LoaderTaskbarAdd(combo);
            ModMain.frmMain.BtnExtraDownload.ShowRefresh();
            ModMain.frmMain.BtnExtraDownload.Ribble();
        });
        combo.WaitForExit();
    }

    private static void VerifyHeader(Stream input)
    {
        if (input.Length < 2 || input.ReadByte() != 'M' || input.ReadByte() != 'Z')
            throw new IOException("程序文件无效。");
        input.Position = 0;
    }

    private static void VerifyDownload(Stream input, LivingForkAsset asset)
    {
        VerifyHeader(input);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var actual = Convert.ToHexString(sha.ComputeHash(input));
        if (input.Length != asset.Size || !string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("下载文件校验失败。");
        input.Position = 0;
    }

    private static void Start(string target)
    {
        using var process = Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(target)
        });
        if (process is null || (process.WaitForExit(750) && process.ExitCode != 0))
            throw new IOException("彩蛋程序没能正常启动。");
    }

    private sealed class LivingForkAsset
    {
        public string Url = "";
        public string Sha256 = "";
        public long Size;
    }
}
