using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PCL.Core.UI.Controls;

using PCL.Core.App.Localization;
namespace PCL;

public partial class InputDialog
{
    private readonly ModMain.MyMsgBoxConverter myConverter;


    public InputDialog(ModMain.MyMsgBoxConverter converter)
    {
        try
        {
            InitializeComponent();
            AppendUniqueNameSuffix(Btn1);
            AppendUniqueNameSuffix(Btn2);
            myConverter = converter;
            LabTitle.Text = converter.Title;
            LabText.Text = converter.Text;
            PanText.Visibility = string.IsNullOrEmpty(converter.Text) ? Visibility.Collapsed : Visibility.Visible;
            TextArea.Text = (string)converter.Content;
            TextArea.HintText = converter.HintText;
            TextArea.ValidateRules = converter.ValidateRules;
            ConfigurePrimaryButton(converter.Button1, converter.IsWarn);
            ConfigureSecondaryButton(converter.Button2);
            ShapeLine.StrokeThickness = ModBase.GetWPFSize(1d);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "输入弹窗初始化失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Application.Control.MessageBox.Error.OperationFailed"));
        }

        Loaded += Load;
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

    private void ConfigureSecondaryButton(string text)
    {
        Btn2.Text = text;
        Btn2.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Load(object sender, EventArgs e)
    {
        try
        {
            // UI 初始化
            if (Btn2.IsVisible && !(Btn1.ColorType == SymbioButton.ColorState.Red))
                Btn1.ColorType = SymbioButton.ColorState.Highlight;
            TextArea.Focus();
            TextArea.SelectionStart = TextArea.Text.Length;
            // 动画
            DialogPresentation.Show(this);
            // 记录日志
            ModBase.Log("[Control] 输入弹窗：" + LabTitle.Text);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "输入弹窗加载失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Application.Control.MessageBox.Error.OperationFailed"));
        }
    }

    private void Close()
    {
        // 结束线程阻塞
        myConverter.WaitFrame.Continue = false;
        ComponentDispatcher.PopModal();
        // 动画
        DialogPresentation.Close(this);
    }

    public void Btn1_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        TextArea.Validate(); // #5773
        if (myConverter.IsExited || !TextArea.IsValidated)
            return;
        myConverter.IsExited = true;
        myConverter.Result = TextArea.Text;
        Close();
    }

    public void Btn2_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (myConverter.IsExited)
            return;
        myConverter.IsExited = true;
        myConverter.Result = null;
        Close();
    }

    private void TextCaption_ValidateChanged(object sender, EventArgs e)
    {
        Btn1.IsEnabled = TextArea.IsValidated;
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
