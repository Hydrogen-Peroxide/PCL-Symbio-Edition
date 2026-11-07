using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

public partial class PageBedrockLeft
{
    // Xbox 应用的族名与固定 App Id；实际 AUMID 需要按当前用户已注册的包拼出来
    private const string XboxAppFamilyName = "Microsoft.GamingApp_8wekyb3d8bbwe";
    private const string XboxAppPackagePrefix = "Microsoft.GamingApp_";
    private const string XboxAppApplicationId = "Microsoft.Xbox.App";

    // 当前用户已注册的 UWP 包列表；用它判断 Xbox 应用是否可用，而不是把固定 AUMID 直接交给 explorer
    private const string PackageRepositoryKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    // 未安装 Xbox 应用时引导用户前往商店，商店不可用时回退到网页版商店
    private const string XboxAppStoreUri = "ms-windows-store://pdp/?ProductId=9MV0B5HZVK9Z";
    private const string XboxAppStoreWebsite = "https://apps.microsoft.com/detail/9MV0B5HZVK9Z";

    // 登录等待的轮询间隔与总时长：Xbox 登录可能包含二步验证，留足时间
    private static readonly TimeSpan SignInWatchInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SignInWatchTimeout = TimeSpan.FromMinutes(5);

    private static readonly string[] BedrockPackageFamilies =
    [
        "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
        "Microsoft.Minecraft_8wekyb3d8bbwe"
    ];

    private bool isLoad;

    // 状态行展示「点击复制安装路径」时保存可复制的路径
    private string? _detailCopyPath;

    // 等待 Xbox 登录时的轮询任务；重复点击、手动刷新或离开页面时取消
    private CancellationTokenSource? _signInWatch;

    // 宿主窗口，用于在从 Xbox 应用切回时刷新账号
    private Window? _hostWindow;

    public PageBedrockLeft()
    {
        InitializeComponent();
        Loaded += PageBedrockLeft_Loaded;
        Unloaded += PageBedrockLeft_Unloaded;
    }

    private void PageBedrockLeft_Loaded(object sender, RoutedEventArgs e)
    {
        // 每次回到该页面时都重新读取账号与状态，切换账号 / 安装版本后能及时反映
        RefreshAccount();
        RefreshStatus();

        AttachHostWindow();

        if (isLoad) return;
        isLoad = true;

        // 首次进入时通知右侧页面执行一次完整检测
        if (ModMain.frmBedrockRight is not null)
            ModMain.frmBedrockRight.RefreshPage();

        RefreshStatus();
    }

    private void PageBedrockLeft_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelSignInWatch();
        if (_hostWindow is null) return;
        _hostWindow.Activated -= HostWindow_Activated;
        _hostWindow = null;
    }

    /// <summary>
    ///     从 Xbox 应用切回启动器时立即刷新账号，不必只依赖轮询或手动点刷新。
    /// </summary>
    private void HostWindow_Activated(object? sender, EventArgs e)
    {
        RefreshAccount();
        RefreshStatus();

        // 仍在等待登录时保留等待提示，账号变化后由轮询收尾
        if (_signInWatch is null) return;
        BtnSwitchAccount.Text = Lang.Text("Bedrock.Account.Waiting");
        LabAccountHint.Text = Lang.Text("Bedrock.Account.WaitingSignIn");
    }

    private void AttachHostWindow()
    {
        if (_hostWindow is not null) return;
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null) _hostWindow.Activated += HostWindow_Activated;
    }

    private void BtnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModMain.frmBedrockRight is not null)
            ModMain.frmBedrockRight.BtnLaunch_Click(sender, e);
    }

    #region 账号

    // Xbox 账号所在的根注册表位置，按优先级排列；账号也可能写在它们的子键里，检测时会一并枚举
    private static readonly string[] XboxAccountRoots =
    [
        @"Software\Microsoft\XboxLive",
        @"Software\Microsoft\Xbox"
    ];

    private sealed record XboxAccountInfo(string? Gamertag, string? Mail, string? Xuid, string Source);

    /// <summary>
    ///     读取当前 Windows 登录的 Xbox 账号信息。
    /// </summary>
    private void RefreshAccount()
    {
        var account = DetectXboxAccount();
        if (account is null)
        {
            // 没有 Xbox 账号时，再看 Windows 是否至少登录了 Microsoft 账号
            var msa = DetectMicrosoftAccount();
            if (msa is not null)
            {
                LabAvatar.Text = GetAvatarText(msa);
                LabGamertag.Text = Lang.Text("Bedrock.Account.MsaOnly");
                LabAccountMail.Text = msa;
                BtnSwitchAccount.Text = Lang.Text("Bedrock.Account.SignIn");
                LabAccountHint.Text = Lang.Text("Bedrock.Account.MsaOnlyHint", msa);
                return;
            }

            LabAvatar.Text = "?";
            LabGamertag.Text = Lang.Text("Bedrock.Account.NotLoggedIn");
            LabAccountMail.Text = Lang.Text("Bedrock.Account.SignInHint");
            BtnSwitchAccount.Text = Lang.Text("Bedrock.Account.SignIn");
            LabAccountHint.Text = GetInstalledXboxAppAumid() is null
                ? Lang.Text("Bedrock.Account.XboxNotInstalledHint")
                : Lang.Text("Bedrock.Account.Hint");
            return;
        }

        var name = account.Gamertag ?? account.Mail;
        if (name is null)
        {
            // 系统只写入了 Xbox 身份标识（Xuid 等），没有缓存玩家代号
            LabAvatar.Text = "X";
            LabGamertag.Text = Lang.Text("Bedrock.Account.XboxOnly");
            LabAccountMail.Text = account.Xuid is null
                ? Lang.Text("Bedrock.Account.MicrosoftAccount")
                : $"XUID {account.Xuid}";
            BtnSwitchAccount.Text = Lang.Text("Bedrock.Account.Switch");
            LabAccountHint.Text = Lang.Text("Bedrock.Account.XboxOnlyHint");
            return;
        }

        LabAvatar.Text = GetAvatarText(name);
        LabGamertag.Text = name;
        LabAccountMail.Text = string.IsNullOrWhiteSpace(account.Mail)
            ? Lang.Text("Bedrock.Account.MicrosoftAccount")
            : account.Mail;
        BtnSwitchAccount.Text = Lang.Text("Bedrock.Account.Switch");
        LabAccountHint.Text = Lang.Text("Bedrock.Account.SwitchHint");
    }

    /// <summary>
    ///     依次尝试各个注册表位置读取 Xbox 账号，全部读不到时返回 null。
    ///     除根键外还会枚举子键，兼容 DefaultUser、Identity、Authentication 等不同系统与 Xbox 应用的写法。
    /// </summary>
    private static XboxAccountInfo? DetectXboxAccount(bool writeLog = true)
    {
        foreach (var path in GetXboxAccountKeyPaths(writeLog))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(path);
                if (key is null) continue;

                // 只记录值名，不记录账号内容，避免日志泄露
                if (writeLog)
                    ModBase.Log($"[Bedrock] 账号检测：注册表 {path} 的值 = {string.Join(", ", key.GetValueNames())}",
                        ModBase.LogLevel.Debug);

                var gamertag = FirstNonEmpty(
                    ReadRegistryValue(key, "ModernGamertag"),
                    ReadRegistryValue(key, "UniqueModernGamertag"),
                    ReadRegistryValue(key, "Gamertag"),
                    ReadRegistryValue(key, "DisplayName"),
                    ReadRegistryValue(key, "PlayerName"));
                var suffix = ReadRegistryValue(key, "ModernGamertagSuffix");
                if (gamertag is not null && suffix is not null && !gamertag.Contains('#'))
                    gamertag = $"{gamertag}#{suffix}";

                var mail = FirstNonEmpty(
                    ReadRegistryValue(key, "UserName"),
                    ReadRegistryValue(key, "Email"),
                    ReadRegistryValue(key, "EmailAddress"),
                    ReadRegistryValue(key, "AccountName"));
                var xuid = FirstNonEmpty(
                    ReadRegistryValue(key, "Xuid"),
                    ReadRegistryValue(key, "UserXUID"),
                    ReadRegistryValue(key, "XboxUserId"));

                // 部分系统只写入身份标识（Xuid、AccountId），不缓存昵称与邮箱，此时同样视为已登录
                var hasIdentity = xuid is not null ||
                                  ReadRegistryValue(key, "AccountId") is not null ||
                                  ReadRegistryValue(key, "UserId") is not null;

                // Gamertag、邮箱与身份标识都没有时视为该位置没有可用账号，继续尝试下一个
                if (gamertag is null && mail is null && !hasIdentity) continue;
                if (writeLog) ModBase.Log($"[Bedrock] 账号检测：命中注册表 {path}", ModBase.LogLevel.Normal);
                return new XboxAccountInfo(gamertag, mail, xuid, path);
            }
            catch (Exception ex)
            {
                if (writeLog)
                    ModBase.Log($"[Bedrock] 账号检测：读取 {path} 失败：{ex.Message}", ModBase.LogLevel.Debug);
            }
        }

        if (writeLog) ModBase.Log("[Bedrock] 账号检测：所有 Xbox 注册表位置均未找到账号", ModBase.LogLevel.Normal);
        return null;
    }

    /// <summary>
    ///     列出需要尝试的注册表路径：先根键，再枚举其子键。
    /// </summary>
    private static List<string> GetXboxAccountKeyPaths(bool writeLog = true)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in XboxAccountRoots)
        {
            if (seen.Add(root)) paths.Add(root);

            string[] subKeys;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(root);
                subKeys = key?.GetSubKeyNames() ?? [];
            }
            catch (Exception ex)
            {
                if (writeLog)
                    ModBase.Log($"[Bedrock] 账号检测：枚举 {root} 的子键失败：{ex.Message}", ModBase.LogLevel.Debug);
                continue;
            }

            foreach (var subKey in subKeys)
            {
                var path = $@"{root}\{subKey}";
                if (seen.Add(path)) paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>读取字符串值；值名不区分大小写，非字符串类型（如令牌二进制）直接忽略。</summary>
    private static string? ReadRegistryValue(RegistryKey key, string name) => key.GetValue(name) switch
    {
        string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
        string[] parts => FirstNonEmpty(parts),
        _ => null
    };

    /// <summary>
    ///     读取 Windows 已登录的 Microsoft 账号（IdentityCRL）。
    ///     系统会保留多个历史账号，无法判断 Xbox 当前使用的是哪一个，此时取第一个，
    ///     由界面标注为仅 Microsoft 账号，而不是直接判定为未登录。
    /// </summary>
    private static string? DetectMicrosoftAccount()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\IdentityCRL\UserExtendedProperties");
            var identities = key?.GetSubKeyNames();
            if (identities is not { Length: > 0 })
            {
                ModBase.Log("[Bedrock] 账号检测：IdentityCRL 中没有 Microsoft 账号", ModBase.LogLevel.Normal);
                return null;
            }

            ModBase.Log($"[Bedrock] 账号检测：使用 IdentityCRL 中的 Microsoft 账号（共 {identities.Length} 个）",
                ModBase.LogLevel.Normal);
            return identities[0];
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 账号检测：读取 Microsoft 账号失败：{ex.Message}", ModBase.LogLevel.Debug);
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return null;
    }

    private static string GetAvatarText(string name) =>
        name.Length > 1 && char.IsHighSurrogate(name[0]) ? name[..2] : name[..1].ToUpperInvariant();

    private void RefreshAccount_Click(object sender, EventArgs e)
    {
        CancelSignInWatch();
        RefreshAccount();
        RefreshStatus();
    }

    private void BtnSwitchAccount_Click(object sender, MouseButtonEventArgs e)
    {
        // 打开应用前先记下当前账号，避免 Xbox 应用瞬间完成登录时被当成“没有变化”
        var baseline = AccountSignature(DetectXboxAccount());

        if (!OpenXboxApp())
        {
            // 未安装 Xbox 应用时不能再交给 explorer，否则它会把“文档”文件夹打开，看起来像跳转到了文档
            LabAccountHint.Text = Lang.Text("Bedrock.Account.XboxNotInstalledHint");
            PromptInstallXboxApp();
            return;
        }

        BeginSignInWatch(baseline);
    }

    /// <summary>
    ///     打开 Xbox 应用。先确认当前用户注册了该应用，未安装时直接返回 false，
    ///     避免 explorer 找不到目标后转而打开“文档”文件夹。
    /// </summary>
    private static bool OpenXboxApp()
    {
        var aumid = GetInstalledXboxAppAumid();
        if (aumid is null)
        {
            ModBase.Log("[Bedrock] 未检测到可用的 Xbox 应用，已取消打开", ModBase.LogLevel.Normal);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\{aumid}",
                UseShellExecute = true
            });
            ModBase.Log($"[Bedrock] 已打开 Xbox 应用：{aumid}");
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 打开 Xbox 应用失败：{ex.Message}", ModBase.LogLevel.Debug);
            return false;
        }
    }

    /// <summary>
    ///     调起 Xbox 应用后开始等待登录：轮询注册表，检测到账号变化立即刷新页面。
    /// </summary>
    private void BeginSignInWatch(string baseline)
    {
        CancelSignInWatch();

        var cts = new CancellationTokenSource();
        _signInWatch = cts;

        BtnSwitchAccount.Text = Lang.Text("Bedrock.Account.Waiting");
        LabAccountHint.Text = Lang.Text("Bedrock.Account.WaitingSignIn");
        ModBase.Log("[Bedrock] 已打开 Xbox 应用，开始等待账号变化");

        _ = WatchSignInAsync(baseline, cts);
    }

    private void CancelSignInWatch()
    {
        var watch = _signInWatch;
        if (watch is null) return;
        _signInWatch = null;
        watch.Cancel();
    }

    /// <summary>
    ///     轮询 Xbox 账号信息，发生变化时自动刷新；超时或离开页面则恢复普通提示。
    /// </summary>
    private async Task WatchSignInAsync(string baseline, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && stopwatch.Elapsed < SignInWatchTimeout)
            {
                await Task.Delay(SignInWatchInterval, token).ConfigureAwait(false);

                if (AccountSignature(DetectXboxAccount(false)) == baseline) continue;

                ModBase.Log("[Bedrock] 检测到 Xbox 账号变化，自动刷新账号信息");
                ModBase.RunInUi(() =>
                {
                    if (!ReferenceEquals(_signInWatch, cts)) return;
                    CancelSignInWatch();
                    RefreshAccount();
                    HintService.Hint(Lang.Text("Bedrock.Account.AutoRefreshed"), HintType.Success);
                });
                return;
            }

            if (token.IsCancellationRequested) return;

            ModBase.Log("[Bedrock] 等待 Xbox 登录超时，恢复普通提示", ModBase.LogLevel.Normal);
            ModBase.RunInUi(() =>
            {
                if (!ReferenceEquals(_signInWatch, cts)) return;
                CancelSignInWatch();
                RefreshAccount();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 等待 Xbox 登录失败：{ex.Message}", ModBase.LogLevel.Debug);
        }
    }

    /// <summary>判断账号是否变化的特征值，不包含令牌等敏感内容。</summary>
    private static string AccountSignature(XboxAccountInfo? account) =>
        account is null ? "" : $"{account.Source}|{account.Gamertag}|{account.Mail}|{account.Xuid}";

    /// <summary>
    ///     读取当前用户已注册的 Xbox 应用 AUMID，未安装时返回 null。
    /// </summary>
    private static string? GetInstalledXboxAppAumid()
    {
        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(PackageRepositoryKey);
            if (packages is null) return null;

            foreach (var package in packages.GetSubKeyNames())
            {
                if (!package.StartsWith(XboxAppPackagePrefix, StringComparison.OrdinalIgnoreCase)) continue;

                using var packageKey = packages.OpenSubKey(package);
                if (packageKey is null) continue;

                var appIds = packageKey.GetSubKeyNames();
                var appId = appIds.FirstOrDefault(id =>
                                string.Equals(id, XboxAppApplicationId, StringComparison.OrdinalIgnoreCase))
                            ?? appIds.FirstOrDefault(id =>
                                id.StartsWith(XboxAppApplicationId, StringComparison.OrdinalIgnoreCase));
                if (appId is null) continue;

                var aumid = $"{XboxAppFamilyName}!{appId}";
                ModBase.Log($"[Bedrock] 检测到 Xbox 应用：{aumid}", ModBase.LogLevel.Debug);
                return aumid;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 检测 Xbox 应用失败：{ex.Message}", ModBase.LogLevel.Debug);
        }

        return null;
    }

    /// <summary>
    ///     未能打开 Xbox 应用时提示安装，用户确认后前往 Microsoft Store。
    /// </summary>
    private static void PromptInstallXboxApp()
    {
        if (ModMain.MyMsgBox(Lang.Text("Bedrock.Account.OpenXboxFailed"),
                Lang.Text("Bedrock.Account.OpenXboxFailedTitle"),
                Lang.Text("Bedrock.Account.InstallXbox"), Lang.Text("Common.Action.Cancel")) != 1)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = XboxAppStoreUri,
                UseShellExecute = true
            });
            ModBase.Log("[Bedrock] 已打开 Microsoft Store 的 Xbox 应用页面");
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 打开 Microsoft Store 失败：{ex.Message}", ModBase.LogLevel.Normal);
            ModBase.OpenWebsite(XboxAppStoreWebsite);
        }
    }

    #endregion

    #region 快捷操作

    private void OpenEditor_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModMain.frmBedrockRight is not { IsBedrockInstalled: true })
        {
            ModMain.MyMsgBox(Lang.Text("Bedrock.Editor.NotInstalled"), Lang.Text("Bedrock.Editor.Title"));
            return;
        }

        if (ModBedrockUwp.LaunchEditor()) return;
        ModMain.MyMsgBox(Lang.Text("Bedrock.Editor.Failed"), Lang.Text("Bedrock.Editor.Title"));
    }

    private void BackupWorlds_Click(object sender, MouseButtonEventArgs e)
    {
        var worlds = ModBedrockWorldBackup.GetWorlds();
        if (worlds.Count == 0)
        {
            HintService.Hint(Lang.Text("Bedrock.Backup.None"), HintType.Error);
            return;
        }

        var path = SystemDialogs.SelectSaveFile(
            Lang.Text("Bedrock.Backup.Title"),
            $"Bedrock-Worlds-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Lang.Text("Bedrock.Backup.Filter"));
        if (string.IsNullOrEmpty(path)) return;

        Task.Run(() =>
        {
            try
            {
                var count = ModBedrockWorldBackup.BackupAll(path,
                    name => ModBase.RunInUi(() => HintService.Hint(Lang.Text("Bedrock.Backup.Running", name), log: false)));
                ModBase.Log($"[Bedrock] 已备份 {count} 个世界到 {path}", ModBase.LogLevel.Normal);
                HintService.Hint(Lang.Text("Bedrock.Backup.Success", count));
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "备份基岩版世界失败", ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Bedrock.Backup.Failed", ex.Message));
            }
        });
    }

    private void OpenSavesFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dataDir = FindBedrockDataDir();
        if (dataDir is null)
        {
            ModMain.MyMsgBox(Lang.Text("Bedrock.Folder.NotFound"), Lang.Text("Bedrock.Shortcuts.OpenSaves"));
            return;
        }

        var saves = Path.Combine(dataDir, "games", "com.mojang", "minecraftWorlds");
        OpenFolder(Directory.Exists(saves) ? saves : dataDir, Lang.Text("Bedrock.Folder.Saves"));
    }

    private void OpenPackageFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dir = ModBedrockInstance.VersionsDirectory;
        if (!Directory.Exists(dir))
        {
            ModMain.MyMsgBox(Lang.Text("Bedrock.Folder.NoPackages"), Lang.Text("Bedrock.Shortcuts.OpenPackages"));
            return;
        }

        OpenFolder(dir, Lang.Text("Bedrock.Folder.Packages"));
    }

    private void OpenScreenshotFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dataDir = FindBedrockDataDir();
        var shots = dataDir is null ? null : Path.Combine(dataDir, "screenshots");
        if (shots is not null && Directory.Exists(shots))
        {
            OpenFolder(shots, Lang.Text("Bedrock.Folder.Screenshots"));
            return;
        }

        // 回退到 Xbox Game Bar 的截图目录
        var gameBarShots = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        if (Directory.Exists(gameBarShots))
        {
            OpenFolder(gameBarShots, Lang.Text("Bedrock.Folder.Screenshots"));
            return;
        }

        ModMain.MyMsgBox(Lang.Text("Bedrock.Folder.NoScreenshots"), Lang.Text("Bedrock.Shortcuts.OpenScreenshots"));
    }

    private void OpenXboxApp_Click(object sender, MouseButtonEventArgs e)
    {
        if (OpenXboxApp()) return;
        PromptInstallXboxApp();
    }

    private static string? FindBedrockDataDir()
    {
        var packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        foreach (var family in BedrockPackageFamilies)
        {
            var dir = Path.Combine(packages, family, "LocalState");
            if (Directory.Exists(dir)) return dir;
        }

        return null;
    }

    private static void OpenFolder(string path, string name)
    {
        if (!Directory.Exists(path))
        {
            ModMain.MyMsgBox(Lang.Text("Bedrock.Folder.NotFoundPath", name, path),
                Lang.Text("Bedrock.Folder.OpenTitle", name));
            return;
        }

        ModBase.OpenExplorer(path.TrimEnd('\\') + "\\");
    }

    #endregion

    #region 状态

    public void RefreshStatus()
    {
        var right = ModMain.frmBedrockRight;
        if (right is null)
        {
            // 右侧页面尚未创建时也要把状态重置为“未检测到”，避免显示上一次的残留信息
            ShapeStatusDot.Fill = (Brush)FindResource("ColorBrushGray4");
            LabStatus.Text = Lang.Text("Bedrock.Status.NotFound");
            SetDetailPath(null);
            LabVersionText.Text = Lang.Text("Bedrock.Status.NotFound");
            return;
        }

        var installed = right.IsBedrockInstalled;
        ShapeStatusDot.Fill = (Brush)FindResource(installed ? "ColorBrush3" : "ColorBrushGray4");
        LabStatus.Text = installed ? Lang.Text("Bedrock.Status.Detected") : Lang.Text("Bedrock.Status.NotFound");
        SetDetailPath(installed ? right.BedrockInstallPath : null);
        LabVersionText.Text = installed
            ? string.IsNullOrEmpty(right.BedrockVersion)
                ? Lang.Text("Bedrock.Status.Ready")
                : Lang.Text("Bedrock.Status.Version", right.BedrockVersion)
            : Lang.Text("Bedrock.Status.NotFound");
    }

    /// <summary>路径不直接展示，悬停查看、点击复制。</summary>
    private void SetDetailPath(string? path)
    {
        _detailCopyPath = string.IsNullOrEmpty(path) ? null : path;
        LabDetail.Text = _detailCopyPath is null
            ? Lang.Text("Bedrock.Status.AvailableToDownload")
            : Lang.Text("Bedrock.Status.CopyPathHint");
        LabDetail.ToolTip = _detailCopyPath;
        LabDetail.Cursor = _detailCopyPath is null ? null : Cursors.Hand;
    }

    private void LabDetail_Click(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_detailCopyPath)) return;

        try
        {
            Clipboard.SetText(_detailCopyPath);
            HintService.Hint(Lang.Text("Bedrock.Status.PathCopied"), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 复制安装路径失败");
            HintService.Hint(Lang.Text("Bedrock.Status.PathCopyFailed"), HintType.Error);
        }
    }

    #endregion
}
