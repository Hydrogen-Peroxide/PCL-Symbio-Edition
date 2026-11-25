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

public partial class LauncherToolsView
{
    /// <summary>MCBEUnlock++ 官网。</summary>
    private const string McbeUnlockUrl = "https://coneric.dpdns.org/";

    private Bitmap currentSkinBitmap;
    private Bitmap generatedHeadBitmap;

    private int headSize = 64;
    private string skinPath = "";

    public LauncherToolsView()
    {
        InitializeComponent();
        BtnSelectSkin.Click += BtnSelectSkin_Click;
        CmbHeadSize.SelectionChanged += CmbHeadSize_SelectionChanged;
        Loaded += (_, _) =>
        {
            LauncherTools.CleaningChanged += RefreshCacheButton;
            RefreshCacheButton();
            MeLoaded();
        };
        Unloaded += (_, _) => LauncherTools.CleaningChanged -= RefreshCacheButton;
        #if DEBUG
        BtnCrash.Visibility = Visibility.Visible;
        #endif
    }

    private void RefreshCacheButton() => BtnClear.IsEnabled = !LauncherTools.IsCleaningCache;

    private void MeLoaded()
    {
        BtnDownloadStart.IsEnabled = false;

        TextDownloadFolder.Text = States.Tool.DownloadFolder;
        TextDownloadFolder.Validate();

        if (!string.IsNullOrEmpty(TextDownloadFolder.ValidateResult) || string.IsNullOrEmpty(TextDownloadFolder.Text))
            TextDownloadFolder.Text = ModBase.exePath + @"PCL\MyDownload\";

        TextDownloadFolder.Validate();
        TextDownloadName.Validate();
        TextDownloadUrl.Validate();
        StartButtonRefresh();
    }

    private void StartButtonRefresh()
    {
        BtnDownloadStart.IsEnabled = string.IsNullOrEmpty(TextDownloadFolder.ValidateResult) &&
                                     string.IsNullOrEmpty(TextDownloadUrl.ValidateResult) &&
                                     string.IsNullOrEmpty(TextDownloadName.ValidateResult);

        BtnDownloadOpen.IsEnabled = string.IsNullOrEmpty(TextDownloadFolder.ValidateResult);

        BtnAchievementPreview.IsEnabled = string.IsNullOrEmpty(AchievementBlockTextBox.ValidateResult) &&
                                          string.IsNullOrEmpty(AchievementTitleTextBox.ValidateResult) &&
                                          string.IsNullOrEmpty(AchievementString1TextBox.ValidateResult);

        BtnAchievementSave.IsEnabled = string.IsNullOrEmpty(AchievementBlockTextBox.ValidateResult) &&
                                       string.IsNullOrEmpty(AchievementTitleTextBox.ValidateResult) &&
                                       string.IsNullOrEmpty(AchievementString1TextBox.ValidateResult);
    }

    private void SaveCacheDownloadFolder(object sender, RoutedEventArgs e)
    {
        States.Tool.DownloadFolder = TextDownloadFolder.Text;
        TextDownloadName.Validate();
    }








    private void TextDownloadUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(TextDownloadName.Text) || string.IsNullOrEmpty(TextDownloadUrl.Text)) return;
            TextDownloadName.Text = ModBase.GetFileNameFromPath(WebUtility.UrlDecode(TextDownloadUrl.Text));
            StartButtonRefresh();
        }
        catch
        {
        }
    }

    private void MyTextButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var text = SystemDialogs.SelectFolder();
        if (!string.IsNullOrEmpty(text)) TextDownloadFolder.Text = text;
    }

    private void BtnDownloadOpen_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            var text = TextDownloadFolder.Text;
            Directory.CreateDirectory(text);
            Basics.OpenPath(text);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开下载文件夹失败");
        }
    }

    private void BtnDownloadStart_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        LauncherTools.StartCustomDownload(TextDownloadUrl.Text, TextDownloadName.Text, TextDownloadFolder.Text);
        TextDownloadUrl.Text = "";
        TextDownloadUrl.Validate();
        TextDownloadUrl.ForceShowAsSuccess();
        TextDownloadName.Text = "";
        TextDownloadName.Validate();
        TextDownloadName.ForceShowAsSuccess();
        StartButtonRefresh();
    }

    private void TextDownloadUrl_ValidateChanged(object sender, RoutedEventArgs e)
    {
        StartButtonRefresh();
    }

    private void TextDownloadFolder_ValidateChanged(object sender, EventArgs e)
    {
        StartButtonRefresh();
    }

    private void TextDownloadName_ValidateChanged(object sender, EventArgs e)
    {
        StartButtonRefresh();
    }

    private void BtnClear_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        LauncherTools.RubbishClear();
    }

    // 下载正版玩家皮肤
    private void BtnSkinSave_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var id = TextSkinID.Text;
        HintService.Hint(Lang.Text("Tools.Test.Skin.Fetching"));
        ModBase.RunInNewThread(() =>
        {
            try
            {
                if (id.Length < 3)
                {
                    HintService.Hint(Lang.Text("Tools.Test.Skin.InvalidId"));
                }
                else
                {
                    var result = (string)ModProfile.McLoginMojangUuid(id, true);
                    result = ModSkin.McSkinGetAddress(result, "Mojang");
                    result = ModSkin.McSkinDownload(result);
                    ModBase.RunInUi(() =>
                    {
                        var path = SystemDialogs.SelectSaveFile(Lang.Text("Tools.Test.Skin.Save"), $"{id}.png", Lang.Text("Tools.Test.Skin.FileFilter"));
                        ModBase.CopyFile(result, path);
                        HintService.Hint(Lang.Text("Tools.Test.Skin.Saved", id), HintType.Success);
                    });
                }
            }
            catch (Exception ex)
            {
                if (ex.ToString().Contains("429"))
                {
                    HintService.Hint(Lang.Text("Tools.Test.Skin.TooFrequent"), HintType.Error);
                    ModBase.Log($"获取正版皮肤失败（{id}）：获取皮肤太过频繁，请 5 分钟后再试！");
                }
                else
                {
                    ModBase.Log(ex, $"获取正版皮肤失败（{id}）");
                }
            }
        });
    }

    // 下载 MCBEUnlock++
    private void BtnUnlockDownload_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 先展示正版购买提示，玩家确认后才打开 MCBEUnlock++ 的官网
        ModMain.MyMsgBox(Lang.Text("Tools.Test.Unlock.Notice"), Lang.Text("Tools.Test.Unlock.Title"),
            Lang.Text("Common.Action.Continue"), Lang.Text("Common.Action.Cancel"),
            button1Action: () => ModBase.OpenWebsite(McbeUnlockUrl));
    }

    // 今日人品
    private void BtnLuck_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        LauncherTools.Jrrp();
    }




    private void BtnCreateShortcut_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var shortcutName = Lang.Text("Tools.Test.Shortcut.FileName", ".lnk");
        var desktopName = Lang.Text("Tools.Test.Shortcut.Desktop");
        var startName = Lang.Text("Tools.Test.Shortcut.StartMenu");
        var desktop = Paths.GetSpecialPath(Environment.SpecialFolder.Desktop, shortcutName);
        var start = Paths.GetSpecialPath(Environment.SpecialFolder.StartMenu, @"Programs\" + shortcutName);
        var choice =
            ModMain.MyMsgBox(
                Lang.Text("Tools.Test.Shortcut.ConfirmMessage", desktopName, desktop, startName, start),
                Lang.Text("Tools.Test.Shortcut.SelectLocation"), Lang.Text("Common.Action.Cancel"), desktopName, startName);
        if (choice == 1)
            return;
        var shortcutPath = choice == 2 ? desktop : start;
        var locationName = choice == 2 ? desktopName : startName;
        Files.CreateShortcut(shortcutPath, Basics.ExecutablePath);
        HintService.Hint(Lang.Text("Tools.Test.Shortcut.Created", locationName), HintType.Success);
    }

    // 启动计数显示
    private void BtnLaunchCount_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModMain.MyMsgBox(Lang.Text("Tools.Test.LaunchCount.Message", States.System.LaunchCount), Lang.Text("Tools.Test.LaunchCount.Title"));
    }

    // 千万别点
    private void BtnSurprise_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!ModLivingForkEgg.TryBegin())
        {
            HintService.Hint(Lang.Text("Tools.Test.Surprise.Busy"));
            return;
        }

        if (ModMain.MyMsgBox(Lang.Text("Tools.Test.Surprise.Warning"), Lang.Text("Tools.Test.Surprise.Title"),
                Lang.Text("Common.Action.Cancel"), Lang.Text("Common.Action.Continue"),
                isWarn: true, highLight: false) != 2)
        {
            ModLivingForkEgg.End();
            return;
        }

        ModBase.RunInNewThread(ModLivingForkEgg.Run, "Toolbox Surprise");
    }

    private async void BtnAchievementPreview_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var url = GetAchievementUrl();
        ModBase.Log("[Net] 获取网络结果" + url);
        await LoadImageAsync(url);
    }

    private async Task LoadImageAsync(string imageUrl)
    {
        var client = NetworkService.GetClient();
        try
        {
            var response = await client.GetAsync(imageUrl);
            if (response.IsSuccessStatusCode)
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    Dispatcher.Invoke(() =>
                    {
                        AchievementImage.Source = bitmapImage;
                        AchievementImage.Visibility = Visibility.Visible;
                    });
                }
            else if (response.StatusCode == HttpStatusCode.NotFound)
                Dispatcher.Invoke(() =>
                {
                    ModBase.Log("获取成就图片失败（404）");
                    HintService.Hint(Lang.Text("Tools.Test.Achievement.FetchFailed"), HintType.Error);
                });

            else
                Dispatcher.Invoke(() => ModBase.Log("获取成就图片失败（" + (int)response.StatusCode + "）"));
        }

        catch (Exception ex)
        {
            Dispatcher.Invoke(() => ModBase.Log(ex, "获取成就图片失败"));
        }
    }

    private async void BtnAchievementSave_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var url = GetAchievementUrl();
        await DownloadImageToLocalAsync(url);
    }

    private async Task DownloadImageToLocalAsync(string imageUrl)
    {
        var savePath = ModBase.pathTemp + @"Download\" + ModBase.GetHash(imageUrl) + ".png";
        var client = NetworkService.GetClient();
        try
        {
            // 异步发送 GET 请求
            var response = await client.GetAsync(imageUrl);

            // 如果响应状态码是成功的，则继续
            if (response.IsSuccessStatusCode)
            {
                // 异步读取响应内容为字节流
                var imageBytes = await response.Content.ReadAsByteArrayAsync();

                // 将字节写入本地文件
                File.WriteAllBytes(savePath, imageBytes);

                var path =
                    SystemDialogs.SelectSaveFile(Lang.Text("Tools.Test.Achievement.Save"), AchievementTitleTextBox.Text + ".png", Lang.Text("Tools.Test.Achievement.FileFilter"));
                if (string.IsNullOrEmpty(path))
                {
                    ModBase.Log("用户取消了保存操作");
                    File.Delete(savePath);
                    return;
                }

                ModBase.CopyFile(savePath, path);
                File.Delete(savePath);
                HintService.Hint(Lang.Text("Tools.Test.Achievement.Saved"), HintType.Success);
            }
            // 下载成功，返回 True
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // 捕获 404 错误
                ModBase.Log("获取成就图片失败（404）");
                HintService.Hint(Lang.Text("Tools.Test.Achievement.FetchFailed"), HintType.Error);
            }
            else
            {
                // 处理其他非成功状态码
                ModBase.Log("获取成就图片失败（" + (int)response.StatusCode + "）");
            }
        }

        catch (Exception ex)
        {
            // 捕获所有其他异常（如网络连接问题）
            ModBase.Log(ex, "获取成就图片失败");
        }
    }

    private string GetAchievementUrl()
    {
        var block = AchievementBlockTextBox.Text.Trim();
        var title = AchievementTitleTextBox.Text.Replace(" ", "..");
        var str1 = AchievementString1TextBox.Text.Replace(" ", "..");
        var str2 = AchievementString2TextBox.Text.Replace(" ", "..");
        var url = $"https://minecraft-api.com/api/achivements/{block}/{title}/{str1}";
        if (!string.IsNullOrEmpty(str2)) url += $"/{str2}";
        return url;
    }

    private void BtnCrash_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        throw new Exception(Lang.Text("Tools.Test.Crash.ManualCrash"));
    }

    private int GetHeadSize() => CmbHeadSize.SelectedIndex switch
    {
        0 => 64,
        1 => 96,
        2 => 128,
        _ => 64
    };

    private void BtnSelectSkin_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SystemDialogs.SelectFile(Lang.Text("Tools.Test.Avatar.FileFilter"),
            Lang.Text("Tools.Test.Avatar.SelectSkinFile"));
        if (!string.IsNullOrEmpty(filePath)) LoadAndGenerateHead(filePath);
    }

    private void LoadAndGenerateHead(string skinPath)
    {
        try
        {
            using (var stream = new FileStream(skinPath, FileMode.Open, FileAccess.Read))
            {
                currentSkinBitmap = new Bitmap(stream);
            }

            this.skinPath = skinPath;

            if (currentSkinBitmap.Width != currentSkinBitmap.Height)
            {
                HintService.Hint(Lang.Text("Tools.Test.Avatar.InvalidSize"), HintType.Error);
                SkinPreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            generatedHeadBitmap = GenerateHeadFromSkin(currentSkinBitmap);

            ImgFace.Source = BitmapToBitmapImage(generatedHeadBitmap);
            ImgHair.Source = null;

            SkinPreviewBorder.Visibility = Visibility.Visible;
            HintService.Hint(Lang.Text("Tools.Test.Avatar.Generated"), HintType.Success);
        }

        catch (Exception ex)
        {
            ModBase.Log(ex, "生成头像失败");
            HintService.Hint(Lang.Text("Tools.Test.Avatar.GenerateFailed", ex.Message), HintType.Error);
            SkinPreviewBorder.Visibility = Visibility.Collapsed;
        }
    }

    private Bitmap GenerateHeadFromSkin(Bitmap skinBitmap)
    {
        headSize = GetHeadSize();
        return ModSkinAvatar.Generate(skinBitmap, headSize);
    }

    private void BtnSaveHead_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (generatedHeadBitmap is null)
        {
            HintService.Hint(Lang.Text("Tools.Test.Avatar.SelectFirst"), HintType.Error);
            return;
        }

        var savePath = SystemDialogs.SelectSaveFile(Lang.Text("Tools.Test.Avatar.Save"), "Head.png");

        if (string.IsNullOrEmpty(savePath))
            return;

        generatedHeadBitmap.Save(savePath, ImageFormat.Png);
        HintService.Hint(Lang.Text("Tools.Test.Avatar.Saved"), HintType.Success);
    }

    private void CmbHeadSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (currentSkinBitmap is not null && skinPath is not null) LoadAndGenerateHead(skinPath);
    }

    private BitmapImage BitmapToBitmapImage(Bitmap bitmap)
    {
        using (var memoryStream = new MemoryStream())
        {
            bitmap.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0L;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = memoryStream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            return bitmapImage;
        }
    }

    private void TextDownloadFolder_OnValidatedTextChanged(object sender, RoutedEventArgs e)
    {
        SaveCacheDownloadFolder(sender, e);
        TextDownloadName_ValidateChanged(sender, e);
    }
}
