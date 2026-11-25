using System.IO;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Core.Utils;

namespace PCL;

internal static class LauncherBackground
{
    public static int ItemCount { get; private set; }
    public static event Action<int>? CatalogChanged;

    private static void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not MediaElement { Source: { } address }) return;
        ModVideoBack.VideoStop();
        if (e.ErrorException.Message.Contains("0xC00D109B", StringComparison.Ordinal))
        {
            ModBase.Log($"背景视频可能并非 H.264（AVC）格式。文件：{address}", ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Ui.Error.BackgroundVideoUnsupported"));
            return;
        }

        ModBase.Log(e.ErrorException, $"刷新背景内容失败（{address}）", ModBase.LogLevel.Msgbox,
            userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
    }

    public static void Refresh(bool isHint, bool refresh)
    {
        try
        {
            // 获取可用的图片文件
            Directory.CreateDirectory(ModBase.exePath + @"PCL\Pictures\");
            var pic = ModBase.EnumerateFiles(ModBase.exePath + @"PCL\Pictures\").Where(file =>
                    !(file.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) ||
                      file.Extension.Equals(".db", StringComparison.OrdinalIgnoreCase))).Select(file => file.FullName)
                .ToList();

            var video = ModMain.frmMain.VideoBack;
            if (refresh) video.MediaFailed -= OnMediaFailed;
            if (!Config.Preference.Background.AutoPauseVideo)
                ModVideoBack.ForcePlay = true;
            // 加载
            if (pic.Count == 0)
            {
                if (refresh)
                {
                    ModVideoBack.VideoStop();
                    FluentAppearance.UpdateWallpaperAccent(null);
                    if (ModMain.frmMain.ImgBack.Visibility == Visibility.Collapsed)
                    {
                        if (isHint)
                            HintService.Hint(Lang.Text("Setup.Ui.Background.NoAvailableContent"), HintType.Error);
                    }
                    else
                    {
                        ModMain.frmMain.ImgBack.Visibility = Visibility.Collapsed;
                        if (isHint)
                            HintService.Hint(Lang.Text("Setup.Ui.Background.Cleared"), HintType.Success);
                    }
                }

            }
            else
            {
                if (refresh)
                {
                    var address = RandomUtils.PickRandom(pic);
                    try
                    {
                        ModMain.frmMain.ImgBack.Background = null;
                        ModVideoBack.VideoStop();
                        ModBase.Log("[UI] 加载背景内容：" + address);
                        ModMain.frmMain.ImgBack.Background = new MyBitmap(address);
                        _ = Config.Preference.Background.WallpaperSuitMode;
                        ModMain.frmMain.ImgBack.Visibility = Visibility.Visible;
                        FluentAppearance.UpdateWallpaperAccent(ModMain.frmMain.ImgBack.Background);
                        if (isHint)
                                HintService.Hint(Lang.Text("Setup.Ui.Background.Refresh.Success", ModBase.GetFileNameFromPath(address)), HintType.Success,
                                false);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            video.MediaFailed += OnMediaFailed;
                            FluentAppearance.UpdateWallpaperAccent(null);
                            ModBase.Log(ex, "[UI] 加载背景图片失败" + address);
                            if (ModBase.modeDebug)
                                HintService.Hint(Lang.Text("Setup.Ui.Background.ImageLoadFailed", address));
                            ModMain.frmMain.ImgBack.Visibility = Visibility.Visible;
                            ModMain.frmMain.VideoBack.Source = new Uri(address, UriKind.Absolute);
                            ModVideoBack.VideoPlay();
                            if (isHint)
                            HintService.Hint(Lang.Text("Setup.Ui.Background.Refresh.Success", ModBase.GetFileNameFromPath(address)), HintType.Success,
                                    false);
                        }
                        catch (Exception playEx)
                        {
                            ModBase.Log(playEx, "播放背景内容时出现未知错误：");
                        }
                    }
                }

            }

            ItemCount = pic.Count;
            if (refresh) ThemeManager.ThemeRefreshMain();
            CatalogChanged?.Invoke(ItemCount);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "刷新背景内容时出现未知错误",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Setup.Ui.Error.OperationFailed"));
        }
    }
}
