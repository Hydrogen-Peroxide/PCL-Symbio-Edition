using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

public partial class PageBedrockRight
{
    private bool isLoad;
    private readonly HashSet<string> _downloadingVersions = [];
    private readonly HashSet<string> _installingVersions = [];
    private readonly Dictionary<string, MyButton> _versionButtons = [];
    private readonly Dictionary<string, MyButton> _localButtons = [];

    private static string BedrockVersionsDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "bedrock", "versions");

    public PageBedrockRight()
    {
        InitializeComponent();
        Loaded += PageBedrockRight_Loaded;
    }

    private void PageBedrockRight_Loaded(object sender, RoutedEventArgs e)
    {
        if (isLoad) return;
        isLoad = true;

        DetectBedrockInstallation();
        RefreshLocalVersions();
        LoadVersionList();
    }

    #region 启动检测

    public void RefreshPage()
    {
        DetectBedrockInstallation();
        RefreshLocalVersions();
        LoadVersionList();
    }

    public void DetectBedrockInstallation()
    {
        var bedrockPath = FindBedrockInstallation();
        var bedrockVersion = GetBedrockVersion();
        if (!string.IsNullOrEmpty(bedrockPath))
        {
            LabStatus.Text = "已检测到基岩版";
            LabDetail.Text = bedrockPath;
            LabVersion.Text = string.IsNullOrEmpty(bedrockVersion)
                ? "Minecraft Bedrock Edition"
                : $"版本 {bedrockVersion}";
            LabSelectedVersion.Text = "已就绪";
            BtnLaunch.IsEnabled = true;
            BtnLaunch.Text = "启动基岩版";
        }
        else
        {
            LabStatus.Text = "未检测到基岩版";
            LabDetail.Text = "下载后请在「已下载版本」中点击安装";
            LabVersion.Text = "未安装";
            LabSelectedVersion.Text = "未检测到基岩版";
            BtnLaunch.IsEnabled = true;
            BtnLaunch.Text = "下载基岩版";
        }
    }

    private static string? GetBedrockVersion()
    {
        try
        {
            var output = ModBase.ShellAndGetOutput("powershell.exe",
                "-NoProfile -Command \"(Get-AppxPackage -Name 'Microsoft.Minecraft*').Version.ToString()\"",
                timeout: 10000);
            if (!string.IsNullOrEmpty(output) && !output.Contains("Exception"))
            {
                var trimmed = output.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    return trimmed;
            }
        }
        catch { }
        return null;
    }

    private static string? FindBedrockInstallation()
    {
        try
        {
            var output = ModBase.ShellAndGetOutput("powershell.exe",
                "-NoProfile -Command \"Get-AppxPackage -Name Microsoft.Minecraft* | Select-Object -ExpandProperty InstallLocation\"",
                timeout: 10000);
            if (!string.IsNullOrEmpty(output) && !output.Contains("Exception"))
            {
                var trimmed = output.Trim();
                if (Directory.Exists(trimmed))
                    return trimmed;
            }
        }
        catch { }

        try
        {
            var path = SearchForFile(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Minecraft.Windows.exe");
            if (!string.IsNullOrEmpty(path)) return path;

            path = SearchForFile(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Minecraft.Windows.exe");
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch { }

        try
        {
            var path = SearchForDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "com.mojang");
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch { }

        return null;
    }

    private static string? SearchForFile(string rootDir, string fileName)
    {
        try
        {
            foreach (var file in Directory.GetFiles(rootDir, fileName, SearchOption.AllDirectories))
                return file;
        }
        catch { }
        return null;
    }

    private static string? SearchForDirectory(string rootDir, string dirName)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(rootDir, dirName, SearchOption.AllDirectories))
                return dir;
        }
        catch { }
        return null;
    }

    public void BtnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        if (FindBedrockInstallation() is null)
        {
            PanVersionList.BringIntoView();
            return;
        }
        LaunchBedrock();
    }

    public void LaunchBedrock()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "minecraft://",
                UseShellExecute = true
            });

            ModBase.Log("[Bedrock] 正在启动 Minecraft Bedrock Edition...");
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 启动失败: {ex.Message}", ModBase.LogLevel.Hint, "基岩版启动错误");
            LabStatus.Text = "启动失败";
            LabDetail.Text = ex.Message;
        }
    }

    #endregion

    #region 本地版本管理

    private void RefreshLocalVersions()
    {
        PanLocalVersions.Children.Clear();
        _localButtons.Clear();

        if (!Directory.Exists(BedrockVersionsDir))
        {
            var noVerText = new TextBlock
            {
                Text = "暂无已下载的版本",
                FontSize = 13,
                Foreground = (Brush)FindResource("ColorBrushGray3"),
                Margin = new Thickness(0d, 5d, 0d, 5d)
            };
            PanLocalVersions.Children.Add(noVerText);
            return;
        }

        var dirs = Directory.GetDirectories(BedrockVersionsDir);
        if (dirs.Length == 0)
        {
            var noVerText = new TextBlock
            {
                Text = "暂无已下载的版本",
                FontSize = 13,
                Foreground = (Brush)FindResource("ColorBrushGray3"),
                Margin = new Thickness(0d, 5d, 0d, 5d)
            };
            PanLocalVersions.Children.Add(noVerText);
            return;
        }

        foreach (var dir in dirs)
        {
            var versionId = Path.GetFileName(dir);
            var msixvcFiles = Directory.GetFiles(dir, "*.msixvc");
            var appxFiles = Directory.GetFiles(dir, "*.appx");
            var allFiles = msixvcFiles.Concat(appxFiles).ToList();

            if (allFiles.Count == 0) continue;

            var filePath = allFiles[0];
            var fileName = Path.GetFileName(filePath);
            var fileSize = "";
            try
            {
                var info = new FileInfo(filePath);
                fileSize = FormatFileSize(info.Length);
            }
            catch { }

            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0d, 4d, 0d, 4d)
            };

            var nameLabel = new TextBlock
            {
                Text = versionId,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 200,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = $"{versionId}\n{fileName}\n{fileSize}"
            };
            rowPanel.Children.Add(nameLabel);

            var sizeLabel = new TextBlock
            {
                Text = fileSize,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 70,
                Foreground = (Brush)FindResource("ColorBrushGray4"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            rowPanel.Children.Add(sizeLabel);

            var installingLabel = new TextBlock
            {
                Text = "",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 80,
                Foreground = (Brush)FindResource("ColorBrushGray4")
            };
            rowPanel.Children.Add(installingLabel);

            // 安装按钮
            bool isInstalling = _installingVersions.Contains(versionId);
            var installBtn = new MyButton
            {
                Text = isInstalling ? "安装中..." : "安装",
                ColorType = MyButton.ColorState.Highlight,
                Height = 28,
                MinWidth = 60,
                Padding = new Thickness(8d, 0d, 8d, 0d),
                Tag = versionId,
                IsEnabled = !isInstalling
            };
            installBtn.Click += LocalInstall_Click;
            rowPanel.Children.Add(installBtn);

            // 删除按钮
            var deleteBtn = new MyButton
            {
                Text = "删除",
                ColorType = MyButton.ColorState.Normal,
                Height = 28,
                MinWidth = 60,
                Padding = new Thickness(8d, 0d, 8d, 0d),
                Tag = versionId,
                IsEnabled = !isInstalling
            };
            deleteBtn.Click += LocalDelete_Click;
            rowPanel.Children.Add(deleteBtn);

            _localButtons[versionId] = installBtn;

            PanLocalVersions.Children.Add(rowPanel);
        }
    }

    private void LocalInstall_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MyButton btn || btn.Tag is not string versionId)
            return;

        if (_installingVersions.Contains(versionId))
            return;

        var dir = Path.Combine(BedrockVersionsDir, versionId);
        var msixvcFiles = Directory.GetFiles(dir, "*.msixvc");
        var appxFiles = Directory.GetFiles(dir, "*.appx");
        var allFiles = msixvcFiles.Concat(appxFiles).ToList();
        if (allFiles.Count == 0) return;

        var filePath = allFiles[0];

        _installingVersions.Add(versionId);
        UpdateLocalButton(versionId, "安装中...", false);
        RefreshLocalVersions();

        LabStatus.Text = "正在安装基岩版...";
        LabDetail.Text = $"正在安装 {versionId}，请稍候...";
        BtnLaunch.IsEnabled = false;

        InstallBedrockPackage(filePath, versionId);
    }

    private void LocalDelete_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MyButton btn || btn.Tag is not string versionId)
            return;

        var dir = Path.Combine(BedrockVersionsDir, versionId);
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);

            ModBase.Log($"[Bedrock] 已删除本地版本: {versionId}",
                ModBase.LogLevel.Normal,
                userSummary: $"已删除基岩版 {versionId}");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "删除本地版本失败",
                ModBase.LogLevel.Feedback,
                userSummary: $"删除失败: {ex.Message}");
        }
        RefreshLocalVersions();
    }

    private void UpdateLocalButton(string versionId, string text, bool enabled)
    {
        if (_localButtons.TryGetValue(versionId, out var btn))
        {
            btn.Text = text;
            btn.IsEnabled = enabled;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    #endregion

    #region 在线版本下载

    private void LoadVersionList()
    {
        try
        {
            var versionListLoader = new ModLoader.LoaderTask<string, JsonObject>(
                "BedrockVersionList", ModDownload.DlBedrockListMain);
            versionListLoader.PreviewFinish += _ =>
            {
                ModBase.RunInUiWait(() => BuildVersionList(versionListLoader.output));
            };
            versionListLoader.Start();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "加载基岩版版本列表失败", ModBase.LogLevel.Feedback, userSummary: "基岩版版本列表加载失败");
        }
    }

    private void BuildVersionList(JsonObject versionData)
    {
        try
        {
            PanVersionList.Children.Clear();
            _versionButtons.Clear();

            var versions = (JsonArray)versionData["versions"];
            if (versions is null || versions.Count == 0)
            {
                var noVerText = new TextBlock
                {
                    Text = "暂无可用版本",
                    FontSize = 13,
                    Foreground = (Brush)FindResource("ColorBrushGray3"),
                    Margin = new Thickness(0d, 5d, 0d, 5d)
                };
                PanVersionList.Children.Add(noVerText);
                return;
            }

            var localVersions = new HashSet<string>();
            if (Directory.Exists(BedrockVersionsDir))
            {
                foreach (var dir in Directory.GetDirectories(BedrockVersionsDir))
                    localVersions.Add(Path.GetFileName(dir));
            }

            foreach (JsonObject version in versions)
            {
                var versionId = (string)version["id"] ?? "未知版本";
                var versionName = (string)version["name"] ?? versionId;
                var releaseDate = (string)version["releaseDate"] ?? "";

                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0d, 4d, 0d, 4d)
                };

                var nameLabel = new TextBlock
                {
                    Text = versionName,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 260,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = $"{versionName}\n发布日期: {releaseDate}"
                };
                rowPanel.Children.Add(nameLabel);

                // 已下载的显示"已下载"，否则显示"下载"
                bool isDownloaded = localVersions.Contains(versionId);
                bool isDownloading = _downloadingVersions.Contains(versionId);
                string btnText;
                bool btnEnabled;

                if (isDownloading) { btnText = "下载中..."; btnEnabled = false; }
                else if (isDownloaded) { btnText = "已下载"; btnEnabled = false; }
                else { btnText = "下载"; btnEnabled = true; }

                var downloadBtn = new MyButton
                {
                    Text = btnText,
                    ColorType = MyButton.ColorState.Highlight,
                    Height = 28,
                    MinWidth = 70,
                    Padding = new Thickness(10d, 0d, 10d, 0d),
                    Tag = versionId,
                    IsEnabled = btnEnabled
                };
                downloadBtn.Click += BedrockVersionDownload_Click;
                rowPanel.Children.Add(downloadBtn);

                _versionButtons[versionId] = downloadBtn;

                PanVersionList.Children.Add(rowPanel);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "构建基岩版版本列表失败", ModBase.LogLevel.Feedback, userSummary: "基岩版版本列表构建失败");
        }
    }

    private void UpdateVersionButton(string versionId, string text, bool enabled)
    {
        if (_versionButtons.TryGetValue(versionId, out var btn))
        {
            btn.Text = text;
            btn.IsEnabled = enabled;
        }
    }

    private void BedrockVersionDownload_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MyButton btn || btn.Tag is not string versionId)
            return;

        if (_downloadingVersions.Contains(versionId))
            return;

        var downloadInfo = ModDownload.GetBedrockDownloadUrls(versionId);
        if (downloadInfo is not null)
        {
            var (urls, md5) = downloadInfo.Value;
            if (urls.Count > 0)
            {
                StartBedrockDownload(urls, md5, versionId);
                return;
            }
        }

        var webUrl = $"https://www.mcappx.com/bedrock/{versionId}/";
        ModBase.OpenWebsite(webUrl);
        ModBase.Log(
            $"[Bedrock] 无可用 CDN 链接，打开网页: {webUrl}",
            ModBase.LogLevel.Normal,
            userSummary: $"正在打开基岩版 {versionId} 下载页面...");
    }

    private void StartBedrockDownload(List<string> urls, string? md5, string versionId)
    {
        try
        {
            var saveDir = Path.Combine(BedrockVersionsDir, versionId);
            Directory.CreateDirectory(saveDir);

            var fileName = "Minecraft_Bedrock_" + versionId + ".msixvc";
            try
            {
                var uri = new Uri(urls[0]);
                var seg = uri.Segments[^1];
                if (!string.IsNullOrEmpty(seg))
                    fileName = seg.TrimEnd('/');
            }
            catch { }

            var savePath = Path.Combine(saveDir, fileName);

            var checker = md5 is not null
                ? new ModBase.FileChecker(hash: md5, canUseExistsFile: true)
                : new ModBase.FileChecker(canUseExistsFile: true);

            var downloadFile = new DownloadFile(urls, savePath, checker);

            var downloadLoader = new LoaderDownload(
                "下载基岩版 " + versionId,
                new List<DownloadFile> { downloadFile }
            );

            var combo = new ModLoader.LoaderCombo<string>(
                "下载基岩版 " + versionId,
                new List<ModLoader.LoaderBase> { downloadLoader }
            );

            _downloadingVersions.Add(versionId);
            UpdateVersionButton(versionId, "下载中...", false);

            // 下载完成后刷新本地版本列表
            combo.OnStateChangedUi += (_, newState, _) =>
            {
                if (newState is ModBase.LoadState.Finished)
                {
                    _downloadingVersions.Remove(versionId);
                    ModBase.RunInUi(() =>
                    {
                        UpdateVersionButton(versionId, "已下载", false);
                        RefreshLocalVersions();
                        LabStatus.Text = "下载完成";
                        LabDetail.Text = $"基岩版 {versionId} 已保存到本地，可点击「安装」进行安装";
                    });
                }
                else if (newState is ModBase.LoadState.Failed or ModBase.LoadState.Aborted)
                {
                    _downloadingVersions.Remove(versionId);
                    ModBase.RunInUi(() => UpdateVersionButton(versionId, "下载", true));
                }
            };

            ModLoader.LoaderTaskbarAdd(combo);
            combo.Start();

            ModBase.Log(
                $"[Bedrock] 下载任务已添加: {versionId} ({urls.Count} 个 CDN 源)",
                ModBase.LogLevel.Normal,
                userSummary: $"基岩版 {versionId} 下载已开始 ({urls.Count} 源)");
        }
        catch (Exception ex)
        {
            _downloadingVersions.Remove(versionId);
            UpdateVersionButton(versionId, "下载", true);
            ModBase.Log(ex, "基岩版下载失败", ModBase.LogLevel.Feedback, userSummary: "基岩版下载失败: " + ex.Message);
        }
    }

    #endregion

    #region 安装

    private void InstallBedrockPackage(string filePath, string versionId)
    {
        Task.Run(() =>
        {
            try
            {
                ModBase.Log($"[Bedrock] 开始安装: {filePath}", ModBase.LogLevel.Normal);

                // 先卸载已安装的基岩版，确保可以安装旧版本（降级）
                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = "正在卸载已安装版本...";
                    LabDetail.Text = "正在移除已安装的基岩版（支持版本降级）...";
                });

                try
                {
                    var uninstallResult = ModBase.ShellAndGetOutput("powershell.exe",
                        "-NoProfile -Command \"$pkg = Get-AppxPackage -Name 'Microsoft.Minecraft*' -ErrorAction SilentlyContinue; if ($pkg) { try { Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop -Confirm:$false 2>&1; Write-Output 'Uninstalled' } catch { Write-Output ('Error:' + $_.Exception.Message) } } else { Write-Output 'None' }\"",
                        timeout: 60000);

                    ModBase.Log($"[Bedrock] 卸载结果: {uninstallResult.Trim()}", ModBase.LogLevel.Normal);

                    // 验证卸载是否成功
                    var verifyResult = ModBase.ShellAndGetOutput("powershell.exe",
                        "-NoProfile -Command \"if (Get-AppxPackage -Name 'Microsoft.Minecraft*' -ErrorAction SilentlyContinue) { Write-Output 'StillInstalled' } else { Write-Output 'Cleared' }\"",
                        timeout: 10000);

                    if (verifyResult.Trim() == "StillInstalled")
                    {
                        ModBase.Log("[Bedrock] 卸载失败，已安装版本仍存在，尝试强制移除...", ModBase.LogLevel.Normal);
                        // 强制移除：先终止相关进程再卸载
                        ModBase.ShellAndGetOutput("powershell.exe",
                            "-NoProfile -Command \"Get-Process -Name 'Minecraft.Windows' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; $pkg = Get-AppxPackage -Name 'Microsoft.Minecraft*'; if ($pkg) { Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop -Confirm:$false 2>&1 }\"",
                            timeout: 60000);

                        verifyResult = ModBase.ShellAndGetOutput("powershell.exe",
                            "-NoProfile -Command \"if (Get-AppxPackage -Name 'Microsoft.Minecraft*' -ErrorAction SilentlyContinue) { Write-Output 'StillInstalled' } else { Write-Output 'Cleared' }\"",
                            timeout: 10000);

                        if (verifyResult.Trim() == "StillInstalled")
                            throw new Exception("无法卸载已安装的基岩版，请手动卸载后再试");
                    }

                    ModBase.Log("[Bedrock] 已成功卸载已安装版本", ModBase.LogLevel.Normal);
                }
                catch (Exception ex)
                {
                    throw new Exception($"卸载已安装版本失败: {ex.Message}", ex);
                }

                // 安装新版本
                // 参考 BedrockBoot：使用开发模式注册（Add-AppxPackage -Register）
                // 开发模式注册的包不会被 Microsoft Store 自动更新
                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = "正在安装基岩版...";
                    LabDetail.Text = $"正在安装 {versionId}，请稍候...";
                });

                InstallWithRegister(filePath, versionId);

                ModBase.Log($"[Bedrock] 安装完成: {versionId}",
                    ModBase.LogLevel.Normal,
                    userSummary: $"基岩版 {versionId} 安装成功！");

                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = "基岩版安装成功！";
                    LabDetail.Text = $"已安装 {versionId}";
                    BtnLaunch.IsEnabled = true;
                    BtnLaunch.Text = "启动基岩版";
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "基岩版安装失败",
                    ModBase.LogLevel.Feedback,
                    userSummary: $"基岩版 {versionId} 安装失败：{ex.Message}");

                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = "安装失败";
                    LabDetail.Text = $"安装失败，请尝试手动安装。\n文件位置: {filePath}";
                });
            }
            finally
            {
                _installingVersions.Remove(versionId);
                ModBase.RunInUi(() =>
                {
                    RefreshLocalVersions();
                    DetectBedrockInstallation();
                });
            }
        });
    }

    /// <summary>
    ///     参考 BedrockBoot：使用开发模式注册安装基岩版包。
    ///     开发模式（Add-AppxPackage -Register）安装的包不会被 Microsoft Store 自动更新。
    ///     对所有格式（.appx / .msix / .msixvc / .appxbundle）统一处理：解压 → 删除签名 → 注册。
    /// </summary>
    /// <param name="filePath">安装包文件路径</param>
    /// <param name="versionId">版本标识</param>
    private void InstallWithRegister(string filePath, string versionId)
    {
        var extractDir = Path.Combine(BedrockVersionsDir, "extracted", versionId);

        ModBase.Log($"[Bedrock] 解压安装包到: {extractDir}", ModBase.LogLevel.Normal);

        // 清理旧解压目录
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        // 解压安装包（.appx / .msix / .msixvc / .appxbundle 本质都是 ZIP 格式）
        try
        {
            ZipFile.ExtractToDirectory(filePath, extractDir);
        }
        catch (Exception ex)
        {
            // 解压失败说明是加密格式（如加密的 .msixvc），尝试直接安装
            ModBase.Log($"[Bedrock] 解压失败，尝试直接安装: {ex.Message}", ModBase.LogLevel.Normal);
            var fallbackResult = ModBase.ShellAndGetOutput("powershell.exe",
                $"-NoProfile -Command \"Add-AppxPackage -Path '{filePath}' -ErrorAction Stop 2>&1\"",
                timeout: 300000);
            if (fallbackResult.Contains("Exception") || fallbackResult.Contains("错误"))
                throw new Exception(fallbackResult.Length > 300 ? fallbackResult[..300] : fallbackResult);
            return;
        }

        // 验证 AppxManifest.xml 存在
        var manifestPath = Path.Combine(extractDir, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
            throw new Exception("解压后的包中找不到 AppxManifest.xml");

        // 参考 BedrockBoot：删除签名文件，阻断 Microsoft Store 更新
        var sigPath = Path.Combine(extractDir, "AppxSignature.p7x");
        if (File.Exists(sigPath))
        {
            File.Delete(sigPath);
            ModBase.Log("[Bedrock] 已删除 AppxSignature.p7x，阻断 Store 自动更新", ModBase.LogLevel.Normal);
        }
        var blockMapPath = Path.Combine(extractDir, "AppxBlockMap.xml");
        if (File.Exists(blockMapPath)) File.Delete(blockMapPath);

        // 补全 .msixvc 增量更新包可能缺失的资源文件
        var splashPath = Path.Combine(extractDir, "MCSplashScreen.png");
        if (!File.Exists(splashPath))
        {
            ModBase.Log("[Bedrock] 补全缺失的 MCSplashScreen.png", ModBase.LogLevel.Normal);
            CreateMinimalPng(splashPath);
        }

        // 参考 BedrockBoot：检查开发者模式，-Register 需要开发者模式或旁加载模式
        if (!IsDeveloperModeEnabled())
        {
            ModBase.Log("[Bedrock] 开发者模式未启用，尝试自动启用...", ModBase.LogLevel.Normal);
            if (!EnableDeveloperMode())
            {
                ModBase.Log("[Bedrock] 无法启用开发者模式（需要管理员权限），回退到直接安装", ModBase.LogLevel.Normal);
                ModBase.RunInUi(() =>
                {
                    LabStatus.Text = "提示：需要开发者模式";
                    LabDetail.Text = "开发模式注册可阻止自动更新。\n请以管理员身份运行，或前往 设置 > 开发者选项 开启开发者模式。";
                });
            }
        }

        // 方式1：使用 Add-AppxPackage -Register 注册（开发模式，Store 不会自动更新）
        if (IsDeveloperModeEnabled())
        {
            var registerResult = ModBase.ShellAndGetOutput("powershell.exe",
                $"-NoProfile -Command \"Add-AppxPackage -Register '{manifestPath}' -ErrorAction Stop 2>&1\"",
                timeout: 300000);

            if (!registerResult.Contains("Exception") && !registerResult.Contains("错误"))
            {
                ModBase.Log("[Bedrock] 开发模式注册成功，Store 不会自动更新此版本", ModBase.LogLevel.Normal);
                return;
            }

            ModBase.Log($"[Bedrock] -Register 失败，尝试重新打包为 .appx: {registerResult.Trim()}", ModBase.LogLevel.Normal);
        }

        // 方式2：重新打包为 .appx 后安装（回退方案）
        var appxPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"Minecraft_Bedrock_{versionId}.appx");
        if (File.Exists(appxPath)) File.Delete(appxPath);
        ZipFile.CreateFromDirectory(extractDir, appxPath);

        var appxResult = ModBase.ShellAndGetOutput("powershell.exe",
            $"-NoProfile -Command \"Add-AppxPackage -Path '{appxPath}' -ErrorAction Stop 2>&1\"",
            timeout: 300000);

        if (appxResult.Contains("Exception") || appxResult.Contains("错误"))
            throw new Exception(appxResult.Length > 300 ? appxResult[..300] : appxResult);
    }

    #region 开发者模式检测（参考 BedrockBoot DeveloperModeHelper）

    /// <summary>
    ///     参考 BedrockBoot DeveloperModeHelper.IsDeveloperModeViaPowerShell：
    ///     检查注册表确认开发者模式或旁加载模式是否已启用。
    ///     开发者模式（AllowDevelopmentWithoutDevLicense = 1）或
    ///     旁加载模式（AllowAllTrustedApps = 1）均可支持 Add-AppxPackage -Register。
    /// </summary>
    private static bool IsDeveloperModeEnabled()
    {
        try
        {
            var result = ModBase.ShellAndGetOutput("powershell.exe",
                "-NoProfile -Command \"" +
                "$paths = @('HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock', " +
                "'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Appx'); " +
                "foreach ($p in $paths) { " +
                "  try { $v = Get-ItemProperty -Path $p -Name 'AllowDevelopmentWithoutDevLicense' -ErrorAction Stop; " +
                "    if ($v.AllowDevelopmentWithoutDevLicense -eq 1) { Write-Output 'true'; exit } " +
                "  } catch { } " +
                "}; " +
                "try { $v = Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock' -Name 'AllowAllTrustedApps' -ErrorAction Stop; " +
                "  if ($v.AllowAllTrustedApps -eq 1) { Write-Output 'true'; exit } " +
                "} catch { }; " +
                "Write-Output 'false'\"",
                timeout: 10000);

            return result.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 开发者模式检测失败: {ex.Message}", ModBase.LogLevel.Normal);
            return false;
        }
    }

    /// <summary>
    ///     尝试启用开发者模式（需要管理员权限）。
    ///     参考 BedrockBoot CoreOptions.IsAutoOpenDevelopment。
    /// </summary>
    private static bool EnableDeveloperMode()
    {
        try
        {
            var result = ModBase.ShellAndGetOutput("powershell.exe",
                "-NoProfile -Command \"" +
                "try { " +
                "  New-Item -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock' -Force -ErrorAction SilentlyContinue | Out-Null; " +
                "  Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock' -Name 'AllowDevelopmentWithoutDevLicense' -Value 1 -ErrorAction Stop; " +
                "  Write-Output 'true' " +
                "} catch { Write-Output 'false' }\"",
                timeout: 10000);

            return result.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 启用开发者模式失败: {ex.Message}", ModBase.LogLevel.Normal);
            return false;
        }
    }

    #endregion

    /// <summary>
    ///     创建一个最小的 1x1 白色 PNG 文件，用于补全 .msixvc 增量更新包缺失的启动画面。
    /// </summary>
    private static void CreateMinimalPng(string path)
    {
        // 有效的 1x1 白色 PNG（base64），使用 System.Drawing 生成
        try
        {
            // 尝试通过 PowerShell 生成（利用系统自带的 GDI+）
            var psScript = $@"
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap(1, 1)
$bmp.SetPixel(0, 0, [System.Drawing.Color]::White)
$bmp.Save('{path.Replace("'", "''")}', [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
";
            ModBase.ShellAndGetOutput("powershell.exe",
                $"-NoProfile -Command \"{psScript}\"",
                timeout: 10000);
        }
        catch
        {
            // 回退：写入硬编码的最小 PNG
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP4z8BQDwAFeAFK5RqavAAAAABJRU5ErkJggg==");
            File.WriteAllBytes(path, png);
        }
    }

    #endregion
}