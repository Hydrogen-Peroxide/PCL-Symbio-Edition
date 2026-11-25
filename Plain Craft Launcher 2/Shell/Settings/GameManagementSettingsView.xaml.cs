using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.Localization;
using PCL.Core.Utils;

namespace PCL;

public partial class GameManagementSettingsView
{
    private new bool isLoaded;

    public GameManagementSettingsView()
    {
        InitializeComponent();
        Loaded += PageSetupSystem_Loaded;
    }

    private void PageSetupSystem_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();

        // 非重复加载部分
        if (isLoaded)
            return;
        isLoaded = true;

        ModAnimation.AniBeginControl();
        Reload();
        SliderLoad();

        if (!Lang.IsChineseMainland)
        {
            TextFilenameFormat.Visibility = Visibility.Collapsed;
            ComboDownloadTranslateV2.Visibility = Visibility.Collapsed;
            TextModManageStyle.Visibility = Visibility.Collapsed;
            ComboModLocalNameStyle.Visibility = Visibility.Collapsed;
        }

        ModAnimation.AniEndControl();
    }

    public void Reload()
    {
        // 下载
        SliderDownloadThread.StepValue = Config.Download.ThreadLimit;
        SliderDownloadSpeed.StepValue = Config.Download.SpeedLimit;
        ComboDownloadSource.SelectedIndex = Config.Download.FileSource;
        ComboDownloadVersion.SelectedIndex = Config.Download.VersionListSource;
        CheckDownloadAutoSelectVersion.Checked = Config.Download.AutoSelectInstance;
        CheckFixAuthlib.Checked = Config.Download.FixAuthLib;

        // Mod 与整合包
        ComboDownloadTranslateV2.SelectedIndex = Config.Download.Comp.NameFormatV2;
        ComboDownloadMod.SelectedIndex = Config.Download.Comp.CompSourceSolution;
        ComboModLocalNameStyle.SelectedIndex = Config.Download.Comp.UiCompNameSolution;
        ComboDownloadQuickBehavior.SelectedIndex = Config.Download.Comp.QuickDownloadBehavior;
        CheckDownloadIgnoreQuilt.Checked = Config.Download.Comp.IgnoreQuilt;
        CheckDownloadAutoInstallDependencies.Checked = Config.Download.Comp.AutoInstallDependencies;
        CheckDownloadClipboard.Checked = Config.Download.Comp.ReadClipboard;

        // Minecraft 更新提示
        CheckUpdateRelease.Checked = Config.Tool.ReleaseNotification;
        CheckUpdateSnapshot.Checked = Config.Tool.SnapshotNotification;

        // 辅助设置
        CheckHelpLauncherLanguage.Checked = Config.Tool.AutoChangeLanguage;
    }

    // 初始化
    // 将控件改变路由到设置改变
    private void CheckBoxChange(object senderRaw, bool user)
    {
        var sender = (SymbioCheck)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.Checked);
    }

    private void SliderChange(object senderRaw, bool user)
    {
        var sender = (SymbioSlider)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.StepValue);
    }

    private void ComboChange(object senderRaw, SelectionChangedEventArgs e)
    {
        var sender = (SymbioSelect)senderRaw;
        if (!ModAnimation.AniExitsAnyControl())
            SetByTag(sender.Tag?.ToString(), sender.SelectedIndex);
    }

    private static void SetByTag(string tag, object value)
        => ConfigService.TrySetValue(tag, value);

    // 滑动条
    private void SliderLoad()
    {
        SliderDownloadThread.getHintText = new Func<object, object>(v => (int)v + 1);
        SliderDownloadSpeed.getHintText = new Func<object, object>(v =>
        {
            int value = (int)v;
            switch (value)
            {
                case <= 14:
                    return Lang.Number((value + 1) * 0.1d, "N1") + " MiB/s";
                case <= 31:
                    return Lang.Number((value - 11) * 0.5d, "N1") + " MiB/s";
                case <= 41:
                    return Lang.Number(value - 21, "N0") + " MiB/s";
                default:
                    return Lang.Text("Setup.GameManage.Download.Unlimited");
            }
        });
    }

    private void SliderDownloadThread_PreviewChange(object sender, ModBase.RouteEventArgs e)
    {
        if (SliderDownloadThread.StepValue < 100)
            return;
        if (!States.Hint.LargeDownloadThread)
        {
            States.Hint.LargeDownloadThread = true;
            ModMain.MyMsgBox(
                Lang.Text("Setup.GameManage.Download.Threads.TooManyWarning.Message"),
                Lang.Text("Common.Dialog.Warning"),
                Lang.Text("Setup.GameManage.Download.Threads.TooManyWarning.Confirm"), isWarn: true);
        }
    }
}
