using System.Windows.Media;
using System.Windows;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

public partial class AboutSettingsView
{
    // 彩蛋
    private int clickCount;

    private new bool isLoaded;

    public AboutSettingsView()
    {
        InitializeComponent();
        Loaded += PageOtherAbout_Loaded;
    }

    private void PageOtherAbout_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();

        // 非重复加载部分
        if (isLoaded)
            return;
        isLoaded = true;

        ItemAboutPcl.Info = ItemAboutPcl.Info.Replace("%VERSION%", ModBase.versionBaseName)
            .Replace("%VERSIONCODE%", ModBase.versionCode.ToString()).Replace("%BRANCH%", ModBase.versionBranchName)
            .Replace("%COMMIT_HASH%", ModBase.commitHashShort);

        if (!Lang.IsChineseMainland)
        {
            ItemMcmod.Visibility = Visibility.Collapsed;
            BtnMcmod.Visibility = Visibility.Collapsed;
            ImgMcmod.Visibility = Visibility.Collapsed;
            RowMcmod.Height = new GridLength(0);
        }
    }

    private void ImgPCLCommunity_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement logo)
        {
            var angle = logo.RenderTransform is RotateTransform rotation ? rotation.Angle : 0;
            SymbioMotion.SpringRotate(logo, angle + 360, SymbioMotion.Depth);
        }
    }

    private void ImgPCLLogo_Click(object sender, MouseButtonEventArgs e)
    {
        if (clickCount < 200)
        {
            clickCount += 1;
            switch (clickCount)
            {
                case 5:
                {
                    HintService.Hint(Lang.Text("Setup.About.EasterEgg.NiceClick"));
                    break;
                }
                case 15:
                {
                    HintService.Hint(Lang.Text("Setup.About.EasterEgg.StillClicking"));
                    break;
                }
                case 25:
                {
                    switch (ModMain.MyMsgBox(Lang.Text("Setup.About.EasterEgg.Bored.Message"), Lang.Text("Setup.About.EasterEgg.Bored.Title"), Lang.Text("Setup.About.EasterEgg.Bored.Yes"), Lang.Text("Setup.About.EasterEgg.Bored.No")))
                    {
                        case 2:
                        {
                            HintService.Hint(Lang.Text("Setup.About.EasterEgg.Bored.Response"));
                            break;
                        }
                    }

                    break;
                }
                case 50:
                {
                    HintService.Hint(Lang.Text("Setup.About.EasterEgg.Encouragement"));
                    break;
                }
                case 75:
                {
                    HintService.Hint(Lang.Text("Setup.About.EasterEgg.HiddenTheme"));
                    break;
                }
                case 100:
                {
                    HintService.Hint(Lang.Text("Setup.About.EasterEgg.StillStaring"));
                    break;
                }
                case 130:
                {
                    HintService.Hint(Lang.Text("Setup.About.EasterEgg.NothingBehind"));
                    break;
                }
                case 150:
                {
                    switch (ModMain.MyMsgBox(Lang.Text("Setup.About.EasterEgg.Tired.Message1"), Lang.Text("Setup.About.EasterEgg.Tired.Title1"), Lang.Text("Setup.About.EasterEgg.Tired.Exhausted"), Lang.Text("Setup.About.EasterEgg.Tired.NotTired")))
                    {
                        case 1:
                        {
                            HintService.Hint(Lang.Text("Setup.About.EasterEgg.Tired.StopClicking"));
                            break;
                        }
                        case 2:
                        {
                            switch (ModMain.MyMsgBox(Lang.Text("Setup.About.EasterEgg.Tired.Message2"), Lang.Text("Setup.About.EasterEgg.Tired.Title2"), Lang.Text("Setup.About.EasterEgg.Tired.Exhausted"), Lang.Text("Setup.About.EasterEgg.Tired.NotTired")))
                            {
                                case 1:
                                {
                                    HintService.Hint(Lang.Text("Setup.About.EasterEgg.Tired.StopClicking"));
                                    break;
                                }
                                case 2:
                                {
                                    switch (ModMain.MyMsgBox(Lang.Text("Setup.About.EasterEgg.Tired.Message3"), Lang.Text("Setup.About.EasterEgg.Tired.Title3"), Lang.Text("Setup.About.EasterEgg.Tired.Exhausted"), Lang.Text("Setup.About.EasterEgg.Tired.ReallyNotTired")))
                                    {
                                        case 1:
                                        {
                                            HintService.Hint(Lang.Text("Setup.About.EasterEgg.Tired.StopClicking"));
                                            break;
                                        }
                                        case 2:
                                        {
                                            HintService.Hint(Lang.Text("Setup.About.EasterEgg.Tired.FinallyGiveUp"));
                                            break;
                                        }
                                    }

                                    break;
                                }
                            }

                            break;
                        }
                    }

                    break;
                }
                case 200:
                {
                    HintService.Hint(Lang.Text("Setup.About.EasterEgg.ClickDisabled"));
                    ImgPCLLogo.IsHitTestVisible = false;
                    return;
                }
            }

            if (sender is FrameworkElement logo && PCLLF.Wpf.Motion.MotionPreferences.AllowsAnimation)
            {
                var offset = SymbioMotion.Translate(logo);
                SymbioMotion.Spring(logo, "logoNudge", () => offset.X, v => offset.X = v,
                    Random.Shared.Next(2) == 0 ? -2 : 2, SymbioMotion.Pop, immediate: true);
                SymbioMotion.Spring(logo, "logoNudge", () => offset.X, v => offset.X = v, 0, SymbioMotion.Pop);
            }
        }
    }
}
