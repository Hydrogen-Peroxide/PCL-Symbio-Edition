using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PCL.Core.App.Localization;
using PCL.Core.UI.Controls;
namespace PCL;

public partial class CardSelectionDialog
{
    private readonly ModMain.MyMsgBoxConverter myConverter;

    private readonly List<SelectionRow> cards = [];

    private int selectedIndex = -1;

    public CardSelectionDialog(ModMain.MyMsgBoxConverter converter)
    {
        try
        {
            InitializeComponent();
            AppendUniqueNameSuffix(Btn1);
            AppendUniqueNameSuffix(Btn2);
            myConverter = converter;
            LabTitle.Text = converter.Title;
            LabCaption.Text = converter.Caption;
            LabCaption.Visibility = string.IsNullOrEmpty(converter.Caption) ? Visibility.Collapsed : Visibility.Visible;
            ShapeLine.StrokeThickness = ModBase.GetWPFSize(1d);
            ConfigurePrimaryButton(converter.Button1, converter.IsWarn);
            ConfigureSecondaryButton(converter.Button2);
            BuildCards(converter.CardOptions, converter.CardSelectedIndex);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "卡片选择弹窗初始化失败",
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

    private void BuildCards(List<MyMsgCardOption> options, int initialIndex)
    {
        cards.Clear();
        PanCards.Children.Clear();
        if (options is not null)
            for (var i = 0; i < options.Count; i++)
            {
                var visual = CreateCard(options[i], i);
                cards.Add(visual);
                PanCards.Children.Add(visual);
            }

        selectedIndex = initialIndex >= 0 && initialIndex < cards.Count ? initialIndex : -1;
        Btn1.IsEnabled = selectedIndex >= 0;
        UpdateCards();
    }

    private SelectionRow CreateCard(MyMsgCardOption option, int index)
    {
        var item = new SelectionRow
        {
            Title = option.Title,
            Info = option.Description,
            ToolTip = option.Description,
            Tags = string.IsNullOrEmpty(option.Tag) ? Array.Empty<string>() : new[] { option.Tag },
            SvgIcon = option.SvgIcon,
            Type = SelectionRow.CheckType.RadioBox,
            Height = 42
        };
        item.Check += (_, _) => SelectCard(index);
        return item;
    }

    private void SelectCard(int index)
    {
        if (selectedIndex == index) return;
        selectedIndex = index;
        Btn1.IsEnabled = true;
        UpdateCards();
    }

    private void UpdateCards()
    {
        // CE 单选列表会自行取消同组的其他项；先逐项清空会触发它的“至少选中一项”回退。
        if (selectedIndex >= 0 && selectedIndex < cards.Count)
            cards[selectedIndex].SetChecked(true, false, false);
    }

    private void Load(object sender, EventArgs e)
    {
        try
        {
            if (Btn2.IsVisible && Btn1.ColorType != SymbioButton.ColorState.Red)
                Btn1.ColorType = SymbioButton.ColorState.Highlight;
            DialogPresentation.Show(this);
            ModBase.Log("[Control] 卡片选择弹窗：" + LabTitle.Text);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "卡片选择弹窗加载失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Application.Control.MessageBox.Error.OperationFailed"));
        }
    }

    private void Close()
    {
        myConverter.WaitFrame.Continue = false;
        ComponentDispatcher.PopModal();
        DialogPresentation.Close(this);
    }

    public void Btn1_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (myConverter.IsExited) return;
        if (selectedIndex < 0) return;
        myConverter.IsExited = true;
        myConverter.Result = selectedIndex;
        Close();
    }

    public void Btn2_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (myConverter.IsExited) return;
        myConverter.IsExited = true;
        myConverter.Result = null;
        Close();
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
