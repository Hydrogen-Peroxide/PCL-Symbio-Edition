using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

public partial class ResourceProjectRow
{
    internal ResourceSearchOrigin? SearchOrigin { get; init; }

    public bool CanInteraction
    {
        get => field;
        set
        {
            field = value;
            PrimaryAction.Focusable = PrimaryAction.IsTabStop = value;
            RefreshColor(this, EventArgs.Empty);
        }
    } = true;

    public void RefreshColor(object sender, EventArgs e)
    {
        var show = CanInteraction && (IsMouseOver || IsKeyboardFocusWithin);
        PanButtons.IsHitTestVisible = show && _HasActionButtons;
        BtnDelete.IsTabStop = BtnDownload.IsTabStop = show;
        SymbioMotion.Fade(HoverSurface, show ? 1 : 0);
        SymbioMotion.Fade(PanButtons, show && _HasActionButtons ? 1 : 0);
    }

    #region 基础属性

    public int Uuid = ModBase.GetUuid();

    // Logo
    public string Logo
    {
        get => PathLogo.Address;
        set => PathLogo.Address = value;
    }

    // 标题
    public string Title
    {
        get => LabTitle.Text;
        set
        {
            if ((LabTitle.Text ?? "") == (value ?? ""))
                return;
            LabTitle.Text = value;
        }
    }

    // 副标题
    public string SubTitle
    {
        get => LabTitleRaw?.Text ?? "";
        set
        {
            if ((LabTitleRaw.Text ?? "") == (value ?? ""))
                return;
            LabTitleRaw.Text = value;
            LabTitleRaw.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // 描述
    public string Description
    {
        get => LabInfo.Text;
        set
        {
            if ((LabInfo.Text ?? "") == (value ?? ""))
                return;
            LabInfo.Text = value;
        }
    }

    public ResourceProjectRow()
    {
        InitializeComponent();
        Click += (_, _) => OpenProject();
        PrimaryAction.Click += (_, args) =>
        {
            args.Handled = true;
            if (CanInteraction) Click?.Invoke(this, args);
        };
        PrimaryAction.MouseEnter += ShowDescriptionToolTip;
        MouseEnter += RefreshColor;
        MouseLeave += RefreshColor;
        IsKeyboardFocusWithinChanged += (_, _) => RefreshColor(this, EventArgs.Empty);
        BtnDelete.Click += BtnDelete_Click;
        BtnDownload.Click += _BtnDownload_Click;
    }

    private void ShowDescriptionToolTip(object sender, MouseEventArgs e)
    {
        if (IsTextTrimmed(LabInfo))
        {
            ToolTipInfo.Content = LabInfo.Text;
            ToolTipInfo.Width = LabInfo.ActualWidth + 25d;
            PrimaryAction.ToolTip = ToolTipInfo;
        }
        else PrimaryAction.ToolTip = null;
    }

    private bool IsTextTrimmed(TextBlock textBlock)
    {
        var typeface = new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight,
            textBlock.FontStretch);
        var formattedText = new FormattedText(textBlock.Text, Thread.CurrentThread.CurrentCulture,
            textBlock.FlowDirection, typeface, textBlock.FontSize, textBlock.Foreground, ModBase.dpi);
        return formattedText.Width > textBlock.ActualWidth;
    }

    // Tag
    public List<string> Tags
    {
        set
        {
            PanTags.Children.Clear();
            PanTags.Visibility = value.Any() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var tagText in value)
            {
                var newTag = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(17, 0, 0, 0)),
                    Padding = new Thickness(3d, 1d, 3d, 1d),
                    CornerRadius = new CornerRadius(3d),
                    Margin = new Thickness(0d, 0d, 3d, 0d),
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = false
                };
                var tagTextBlock = new TextBlock
                {
                    Text = tagText,
                    Foreground = new SolidColorBrush(Color.FromRgb(134, 134, 134)),
                    FontSize = 11d
                };
                newTag.Child = tagTextBlock;
                PanTags.Children.Add(newTag);
            }
        }
    }

    // ‘收藏按钮
    public bool ShowFavoriteBtn
    {
        get => BtnDelete.Visibility == Visibility.Visible;
        set
        {
            BtnDelete.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _UpdatePanButtons();
        }
    }

    // 快速下载按钮
    public bool ShowDownloadBtn
    {
        get => BtnDownload.Visibility == Visibility.Visible;
        set
        {
            BtnDownload.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _UpdatePanButtons();
        }
    }

    /// <summary>右侧是否存在任意可见的操作按钮（收藏 / 下载），用于决定悬停时是否淡入按钮区。</summary>
    private bool _HasActionButtons => ShowFavoriteBtn || ShowDownloadBtn;

    private void _UpdatePanButtons()
    {
        if (PanButtons is null) return;
        PanButtons.Visibility = _HasActionButtons ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    ///     刷新收藏状态（仅更新图标，不影响 hover 展示逻辑）
    /// </summary>
    public void RefreshFavoriteStatus()
    {
        if (Tag is not ModComp.CompProject project) return;

        var isFavourite = ModComp.CompFavorites.IsFavourite(project.Id);
        BtnDelete.SvgIcon = isFavourite ? "lucide/heart-filled" : "lucide/heart";
    }

    #endregion

    #region 点击

    // 触发点击事件
    public event RoutedEventHandler? Click;

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (PanButtons.Opacity > 0d && Tag is ModComp.CompProject)
        {
            var project = (ModComp.CompProject)Tag;
            ModComp.CompFavorites.ShowMenu(project, (UIElement)sender, () => RefreshFavoriteStatus());
        }
    }

    private void _BtnDownload_Click(object sender, EventArgs e)
    {
        if (Tag is not ModComp.CompProject project)
            return;
        ModBase.Log("[Comp] 快速下载按钮被点击：" + project.RawName);
        ModComp.QuickDownload(project);
    }

    private void OpenProject()
    {
        if (Tag is not ModComp.CompProject project) return;
        // 打开详情页
        var targetType = default(ModComp.CompType);
        string targetVersion = null;
        var targetLoader = ModComp.CompLoaderType.Any;
        if (ModMain.frmMain.pageCurrent.page == FormMain.PageType.Download)
        {
            if (ModMain.frmMain.PageCurrentSub == FormMain.PageSubType.DownloadCompFavorites)
            {
                targetVersion = "";
                targetLoader = ModComp.CompLoaderType.Any;
            }
            else if (SearchOrigin is { } origin)
            {
                targetType = origin.Type;
                targetVersion = origin.Version;
                targetLoader = origin.Loader;
            }
        }
        else if (ModMain.frmMain.pageCurrent.page == FormMain.PageType.InstanceSetup)
        {
            // 从实例设置页进入（查看整合包信息）
            targetType = ModComp.CompType.ModPack;
        }
        else if (ModMain.frmMain.pageCurrent.additional is { } context)
        {
            targetType = ModComp.CompType.Any;
            targetVersion = context.TargetVersion;
            targetLoader = context.TargetLoader;
        }

        ModMain.frmMain.PageChange(new FormMain.PageStackData
        {
            page = FormMain.PageType.CompDetail,
            additional = (project, new List<string>(), targetVersion, targetLoader, targetType, null)
        });
    }

    #endregion
}
