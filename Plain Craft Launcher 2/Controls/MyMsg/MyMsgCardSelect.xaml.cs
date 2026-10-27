using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PCL.Core.App.Localization;
using PCL.Core.UI.Controls;
namespace PCL;

/// <summary>
///     卡片选择框中的单个选项。
/// </summary>
public class MyMsgCardOption
{
    /// <summary>
    ///     卡片标题。
    /// </summary>
    public string Title = "";

    /// <summary>
    ///     卡片副标题说明。
    /// </summary>
    public string Description = "";

    /// <summary>
    ///     卡片图标资源路径。
    /// </summary>
    public string SvgIcon = "lucide/box";

    /// <summary>
    ///     标题旁的小标签，留空则不显示。
    /// </summary>
    public string Tag = "";
}

public partial class MyMsgCardSelect
{
    private readonly ModMain.MyMsgBoxConverter myConverter;
    private readonly int uuid = ModBase.GetUuid();
    private readonly List<MyListItem> cards = [];

    private int selectedIndex = -1;

    public MyMsgCardSelect(ModMain.MyMsgBoxConverter converter)
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
            Btn1.ColorType = MyButton.ColorState.Red;
            LabTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushRedLight");
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

    private MyListItem CreateCard(MyMsgCardOption option, int index)
    {
        var item = new MyListItem
        {
            Title = option.Title,
            Info = option.Description,
            ToolTip = option.Description,
            Tags = string.IsNullOrEmpty(option.Tag) ? Array.Empty<string>() : new[] { option.Tag },
            SvgIcon = option.SvgIcon,
            Type = MyListItem.CheckType.RadioBox,
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
            if (Btn2.IsVisible && Btn1.ColorType != MyButton.ColorState.Red)
                Btn1.ColorType = MyButton.ColorState.Highlight;
            Opacity = 0d;
            ModAnimation.AniStart(
                ModAnimation.AaColor(ModMain.frmMain.PanMsgBackground, BlurBorder.BackgroundProperty,
                    (myConverter.IsWarn
                        ? new ModBase.MyColor(140d, 80d, 0d, 0d)
                        : new ModBase.MyColor(90d, 0d, 0d, 0d)) - ModMain.frmMain.PanMsgBackground.Background, 200),
                "PanMsgBackground Background");
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaOpacity(this, 1d, 120, 60),
                    ModAnimation.AaDouble(i => TransformPos.Y += (double)i,
                        -TransformPos.Y, 300, 60, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaDouble(i => TransformRotate.Angle += (double)i,
                        -TransformRotate.Angle, 300, 60,
                        new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak))
                }, "MyMsgBox " + uuid);
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
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaCode(() =>
            {
                if (!ModMain.WaitingMyMsgBox.Any())
                    ModAnimation.AniStart(ModAnimation.AaColor(ModMain.frmMain.PanMsgBackground,
                        BlurBorder.BackgroundProperty,
                        new ModBase.MyColor(0d, 0d, 0d, 0d) - ModMain.frmMain.PanMsgBackground.Background, 200,
                        ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)));
            }, 30),
            ModAnimation.AaOpacity(this, -Opacity, 80, 20),
            ModAnimation.AaDouble(i => TransformPos.Y += (double)i, 20d - TransformPos.Y,
                150, 0, new ModAnimation.AniEaseOutFluent()),
            ModAnimation.AaDouble(i => TransformRotate.Angle += (double)i,
                6d - TransformRotate.Angle, 150, 0, new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)),
            ModAnimation.AaCode(() => ((Grid)Parent).Children.Remove(this), after: true)
        }, "MyMsgBox " + uuid);
    }

    public void Btn1_Click(object sender, MouseButtonEventArgs e)
    {
        if (myConverter.IsExited) return;
        if (selectedIndex < 0) return;
        myConverter.IsExited = true;
        myConverter.Result = selectedIndex;
        Close();
    }

    public void Btn2_Click(object sender, MouseButtonEventArgs e)
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
