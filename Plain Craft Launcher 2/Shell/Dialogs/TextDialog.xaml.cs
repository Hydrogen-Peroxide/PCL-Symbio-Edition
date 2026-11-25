using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using PCL.Core.UI.Controls;

using PCL.Core.App.Localization;
namespace PCL;

public partial class TextDialog
{
    private readonly ModMain.MyMsgBoxConverter myConverter;


    public TextDialog(ModMain.MyMsgBoxConverter converter)
    {
        try
        {
            InitializeComponent();
            AppendUniqueNameSuffix(Btn1);
            AppendUniqueNameSuffix(Btn2);
            AppendUniqueNameSuffix(Btn3);
            myConverter = converter;
            LabTitle.Text = converter.Title;
            LabCaption.Text = converter.Text;
            if (converter.ShowCrashBanner)
                LoadCrashBanner();
            ConfigurePrimaryButton(converter.Button1, converter.IsWarn);
            ConfigureSecondaryButton(Btn2, converter.Button2);
            ConfigureSecondaryButton(Btn3, converter.Button3);
            ShapeLine.StrokeThickness = ModBase.GetWPFSize(1d);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "普通弹窗初始化失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Application.Control.MessageBox.Error.OperationFailed"));
        }

        Loaded += Load;
    }

    private void LoadCrashBanner()
    {
        var variant = LocalizationService.CurrentLanguage.Code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase)
            ? "zh" : "en";
        try
        {
            CrashBannerIcon.Source = LoadBannerLayer(variant, "Icon");
            CrashBannerKicker.Source = LoadBannerLayer(variant, "Kicker");
            CrashBannerHeadline.Source = LoadBannerLayer(variant, "Headline");
            LabTitle.Visibility = Visibility.Collapsed;
            ShapeLine.Visibility = Visibility.Collapsed;
            CrashBanner.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "加载游戏崩溃横幅失败", ModBase.LogLevel.Debug);
        }
    }

    private static BitmapImage LoadBannerLayer(string variant, string layer)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/Images/Crash/Banner-{variant}-{layer}.png");
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void AppendUniqueNameSuffix(FrameworkElement element)
    {
        element.Name += ModBase.GetUuid();
    }

    private void ConfigurePrimaryButton(string text, bool isWarn)
    {
        Btn1.Text = text;
        if (isWarn)
        {
            Btn1.ColorType = SymbioButton.ColorState.Red;
            // Fluent dialogs keep a neutral title; the destructive action is marked on its button instead.
        }
    }

    private static void ConfigureSecondaryButton(SymbioButton button, string text)
    {
        button.Text = text;
        button.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Load(object sender, RoutedEventArgs e)
    {
        try
        {
            // UI 初始化
            if (Btn2.IsVisible && !(Btn1.ColorType == SymbioButton.ColorState.Red))
                Btn1.ColorType = SymbioButton.ColorState.Highlight;
            Btn1.Focus();
            // 动画
            DialogPresentation.Show(this);
            // 记录日志
            ModBase.Log("[Control] 普通弹窗：" + LabTitle.Text + "\r\n" + LabCaption.Text);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "普通弹窗加载失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Application.Control.MessageBox.Error.OperationFailed"));
        }
    }

    private void Close()
    {
        // 结束线程阻塞
        if (myConverter.ForceWait || !string.IsNullOrEmpty(myConverter.Button2))
            myConverter.WaitFrame.Continue = false;
        ComponentDispatcher.PopModal();
        // 动画
        DialogPresentation.Close(this);
    }

    public void Btn1_Click(object? sender = null, RoutedEventArgs? e = null)
    {
        if (myConverter.IsExited)
            return;
        if (myConverter.Button1Action is not null)
        {
            myConverter.Button1Action();
        }
        else
        {
            myConverter.IsExited = true;
            myConverter.Result = 1;
            Close();
        }
    }

    public void Btn2_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (myConverter.IsExited)
            return;
        if (myConverter.Button2Action is not null)
        {
            myConverter.Button2Action();
        }
        else
        {
            myConverter.IsExited = true;
            myConverter.Result = 2;
            Close();
        }
    }

    public void Btn3_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (myConverter.IsExited)
            return;
        if (myConverter.Button3Action is not null)
        {
            myConverter.Button3Action();
        }
        else
        {
            myConverter.IsExited = true;
            myConverter.Result = 3;
            Close();
        }
    }

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                if (e.GetPosition(ShapeLine).Y <= 2d)
                    ModMain.frmMain.DragMove();
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "拖拽移动失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Application.Control.MessageBox.Error.OperationFailed"));
        }
    }
}
