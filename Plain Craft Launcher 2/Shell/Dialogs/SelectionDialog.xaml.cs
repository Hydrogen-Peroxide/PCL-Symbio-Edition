using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PCL.Core.UI.Controls;

using PCL.Core.App.Localization;
namespace PCL;

public partial class SelectionDialog
{
    private readonly ModMain.MyMsgBoxConverter myConverter;


    private int selectedIndex = -1;

    public SelectionDialog(ModMain.MyMsgBoxConverter converter)
    {
        try
        {
            InitializeComponent();
            AppendUniqueNameSuffix(Btn1);
            AppendUniqueNameSuffix(Btn2);
            myConverter = converter;
            LabTitle.Text = converter.Title;
            ConfigurePrimaryButton(converter.Button1, converter.IsWarn);
            ConfigureSecondaryButton(converter.Button2);
            ShapeLine.StrokeThickness = ModBase.GetWPFSize(1d);
            InitializeSelectionList(converter.Content);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "选择弹窗初始化失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Application.Control.MessageBox.Error.OperationFailed"));
        }

        Loaded += Load;
        Btn1.Click += Btn1_Click;
        Btn2.Click += Btn2_Click;
        LabTitle.MouseLeftButtonDown += Drag;
        PanBorder.MouseLeftButtonDown += Drag;
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

    private void InitializeSelectionList(object content)
    {
        // 添加选择控件
        Btn1.IsEnabled = false;
        foreach (var rawContent in (IEnumerable)content)
        {
            // 1. Initialize and get the actual element
            // Note: We use a new variable because 'foreach' variables are read-only
            var selectionContent = DeferredView.TryInit((FrameworkElement)rawContent);

            // 2. Interface casting and event subscription
            if (selectionContent is IMyRadio selection)
            {
                PanSelection.Children.Add((UIElement)selection);
                selection.Check += (sender, e) => OnChecked((IMyRadio)sender, e);
                selection.Changed += (_, _) => UpdateCheckedState();

                // 3. Property configuration based on specific type
                if (selection is SelectionRow listItem)
                {
                    listItem.Type = myConverter.MultiSelect ? SelectionRow.CheckType.CheckBox : SelectionRow.CheckType.RadioBox;
                    listItem.MinHeight = 24.0;
                }
                else if (selection is SymbioRadio radioBox)
                {
                    radioBox.MinHeight = 24.0;
                }
            }
        }

        // 预选项不会触发 Check 事件，这里补上选中索引，否则确定按钮会一直不可用
        if (myConverter.MultiSelect)
        {
            UpdateCheckedState();
        }
        else
        {
            for (var i = 0; i < PanSelection.Children.Count; i++)
            {
                var isChecked = PanSelection.Children[i] switch
                {
                    SelectionRow listItem => listItem.Checked,
                    SymbioRadio radioBox => radioBox.Checked,
                    _ => false
                };
                if (!isChecked) continue;
                selectedIndex = i;
                Btn1.IsEnabled = true;
                break;
            }
        }
    }

    private void Load(object sender, EventArgs e)
    {
        try
        {
            // UI 初始化
            if (Btn2.IsVisible && !(Btn1.ColorType == SymbioButton.ColorState.Red))
                Btn1.ColorType = SymbioButton.ColorState.Highlight;
            // 动画
            DialogPresentation.Show(this);
            // 记录日志
            ModBase.Log("[Control] 选择弹窗：" + LabTitle.Text);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "选择弹窗加载失败",
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
        if (myConverter.IsExited)
            return;
        if (myConverter.MultiSelect)
        {
            var indices = new List<int>();
            for (var i = 0; i < PanSelection.Children.Count; i++)
                if (PanSelection.Children[i] is SelectionRow { Checked: true })
                    indices.Add(i);
            if (indices.Count == 0)
                return;
            myConverter.IsExited = true;
            myConverter.Result = indices;
        }
        else
        {
            if (selectedIndex == -1)
                return;
            myConverter.IsExited = true;
            myConverter.Result = selectedIndex;
        }

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

    private void OnChecked(IMyRadio sender, EventArgs e)
    {
        if (myConverter.MultiSelect)
            return;
        Btn1.IsEnabled = true;
        selectedIndex = PanSelection.Children.IndexOf((UIElement)sender);
    }

    private void UpdateCheckedState()
    {
        // 多选
        if (!myConverter.MultiSelect)
            return;
        var checkedCount = 0;
        foreach (var child in PanSelection.Children)
            if (child is SelectionRow { Checked: true })
                checkedCount += 1;
        Btn1.IsEnabled = checkedCount > 0;
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
