using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic;
using PCL.Core.App;
using PCL.Core.Utils;
using PCL.Core.Utils.Exts;
using PCL.Core.App.Localization;

namespace PCL;

public partial class LocalProjectRow
{
    private string? _displayVersion;

    private string GetUpdateCompareDescription()
    {
        _displayVersion = null;
        var currentName = Entry.compFile.FileName.Replace(".jar", "");
        var newestName = Entry.UpdateFile.FileName.Replace(".jar", "");
        // 简化名称对比
        var currentSegs = currentName.Split('-').ToList();
        var newestSegs = newestName.Split('-').ToList();
        var shortened = false;
        foreach (var Seg in currentSegs.ToList())
        {
            if (!newestSegs.Contains(Seg))
                continue;
            currentSegs.Remove(Seg);
            newestSegs.Remove(Seg);
            shortened = true;
        }

        if (shortened && currentSegs.Any() && newestSegs.Any())
        {
            currentName = currentSegs.Join("-");
            newestName = newestSegs.Join("-");
            _displayVersion = currentName;
        }

        return
            Lang.Text("Instance.Resource.Item.UpdateCompare", currentName, Lang.TimeSpan(Entry.compFile.ReleaseDate - DateTime.Now), newestName, Lang.TimeSpan(Entry.UpdateFile.ReleaseDate - DateTime.Now));
    }

    public void Refresh()
    {
        Dispatcher.BeginInvoke(new Func<Task>(async () =>
        {
            // 更新
            if (Entry.CanUpdate)
            {
                BtnUpdate.Visibility = Visibility.Visible;
                BtnUpdate.ToolTip = $"{GetUpdateCompareDescription()}\r\n{Lang.Text("Instance.Resource.Item.UpdateToolTip")}";
            }
            else
            {
                BtnUpdate.Visibility = Visibility.Collapsed;
                _displayVersion = null;
            }

            // 标题与描述
            string descFileName;
            if (Entry.IsFolder)
                // 文件夹项的特殊处理
                descFileName = Entry.Name;
            else
                switch (Entry.State)
                {
                    case ModLocalComp.LocalCompFile.LocalFileStatus.Fine:
                    {
                        descFileName = ModBase.GetFileNameWithoutExtentionFromPath(Entry.path);
                        break;
                    }
                    case ModLocalComp.LocalCompFile.LocalFileStatus.Disabled:
                    {
                        descFileName =
                            ModBase.GetFileNameWithoutExtentionFromPath(Entry.path.Replace(".disabled", "")
                                .Replace(".old", "")); // McMod.McModState.Unavailable
                        break;
                    }

                    default:
                    {
                        descFileName = ModBase.GetFileNameFromPath(Entry.path);
                        break;
                    }
                }

            string newDescription;
            var displayVersion = _displayVersion ?? Entry.Version;
            var compTemp = Entry.Comp;
            if (Entry.IsFolder)
            {
                // 文件夹项的特殊显示
                Title = Entry.Name;
                newDescription = Entry.Description;
            }
            else if (Config.Download.Comp.UiCompNameSolution == 1)
            {
                // 标题显示文件名，详情显示译名
                // 标题
                Title = descFileName;
                SubTitle = "";
                // 描述
                if (Entry.Comp is null)
                {
                    newDescription = Entry.Name;
                }
                else
                {
                    var titles = await Task.Run(() => compTemp.GetControlTitle(false));
                    newDescription = titles.Key + titles.Value;
                }

                newDescription = newDescription.Replace("  |  ", " / ");
                if (displayVersion is not null)
                    newDescription += $" ({displayVersion})";
            }
            else
            {
                // 标题显示译名，详情显示文件名
                // 标题
                if (Entry.Comp is null)
                {
                    Title = Entry.Name;
                    SubTitle = displayVersion is null ? "" : "  |  " + displayVersion;
                }
                else
                {
                    var titles = await Task.Run(() => compTemp.GetControlTitle(false));
                    Title = titles.Key;
                    SubTitle = titles.Value + (displayVersion is null ? "" : "  |  " + displayVersion);
                }

                // 描述
                newDescription = descFileName;
            }

            if (Entry.Comp is not null)
                newDescription += ": " + Entry.Comp.Description.Replace("\r", "").Replace("\n", "");
            else if (Entry.Description is not null)
                newDescription += ": " + Entry.Description.Replace("\r", "").Replace("\n", "");
            else if (!Entry.IsFileAvailable) newDescription += ": " + Lang.Text("Instance.Resource.Item.InfoUnavailable");
            Description = newDescription;
            if (Checked)
                LabTitle.SetResourceReference(TextBlock.ForegroundProperty,
                    Entry.State == ModLocalComp.LocalCompFile.LocalFileStatus.Fine ? "SyAccentText" : "SyControlStrongStroke");
            else
                LabTitle.SetResourceReference(TextBlock.ForegroundProperty,
                    Entry.State == ModLocalComp.LocalCompFile.LocalFileStatus.Fine ? "SyText" : "SyTextTertiary");
            // 主 Logo
            Logo = Entry.GetLogo();

            // 图标右下角的 Logo
            if (Entry.State == ModLocalComp.LocalCompFile.LocalFileStatus.Fine)
            {
                if (imgState is not null)
                {
                    Children.Remove(imgState);
                    imgState = null;
                }
            }
            else
            {
                if (imgState is null)
                {
                    imgState = new Image
                    {
                        Width = 20d,
                        Height = 20d,
                        Margin = new Thickness(0d, 0d, -5, -3),
                        IsHitTestVisible = false,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    };
                    RenderOptions.SetBitmapScalingMode(imgState, BitmapScalingMode.HighQuality);
                    SetColumn(imgState, 1);
                    SetRow(imgState, 1);
                    SetRowSpan(imgState, 2);
                    Children.Add(imgState);
                    // <Image x:Name="ImgState" RenderOptions.BitmapScalingMode="HighQuality" Width="16" Height="16" Margin="0,0,-3,-1"
                    // Grid.Column="1" Grid.Row="1" Grid.RowSpan="2" IsHitTestVisible="False"
                    // HorizontalAlignment="Right" VerticalAlignment="Bottom"
                    // Source="/Images/Icons/Unavailable.png" />
                }

                imgState.Source = new MyBitmap(ModBase.pathImage + $"Icons/{Entry.State}.png");
            }

            // 标签
            if (Entry.IsFolder)
                // 为文件夹添加标签
                Tags = new List<string> { Lang.Text("Instance.Resource.Item.FolderTag") };
            else if (Entry.Comp is not null) Tags = Entry.Comp.Tags;
        }));
    }

    public void RefreshColor(object sender, EventArgs e)
    {
        InitLate(sender, e);
        var focused = IsMouseOver || IsKeyboardFocusWithin;
        if (buttonStack is not null)
        {
            buttonStack.IsHitTestVisible = focused;
            SymbioMotion.Fade(buttonStack, focused ? 1 : 0);
            SymbioMotion.Spring(this, "actionWidth", () => ColumnPaddingRight.Width.Value,
                v => ColumnPaddingRight.Width = new GridLength(Math.Max(0, v)),
                focused ? 5 + Buttons.Count() * 25 : 4, SymbioMotion.Exit);
        }
        RectBack.SetResourceReference(Border.BackgroundProperty, PrimaryAction.IsPressed ? "SySubtlePressed" : "SySubtleHover");
        SymbioMotion.Fade(RectBack, focused || Checked ? 1 : 0);
    }

    // 触发虚拟化内容
    private void InitLate(object sender, EventArgs e)
    {
        if (buttonHandler is not null)
        {
            var initialize = buttonHandler;
            buttonHandler = null;
            initialize(this, e);
        }
    }

    // 显示更新日志
    private void BtnUpdate_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowUpdateLog();
    }

    private void ShowUpdateLog()
    {
        if (Entry.Comp is not null)
        {
            if (!Information.IsNumeric(Entry.Comp.Id))
            {
                var modrinthUrl = Entry.changelogUrls.FirstOrDefault(x => x.Contains("modrinth.com"));
                if (modrinthUrl is not null)
                {
                    ModBase.OpenWebsite(modrinthUrl);
                    return;
                }
            }
            else
            {
                var curseForgeUrl = Entry.changelogUrls.FirstOrDefault(x => x.Contains("curseforge.com"));
                if (curseForgeUrl is not null)
                {
                    ModBase.OpenWebsite(curseForgeUrl);
                    return;
                }
            }
        }

        ModBase.Log(
            Lang.Text("Instance.Resource.Item.OpenChangelogFailed"),
            ModBase.LogLevel.Hint,
            userSummary: Lang.Text("Instance.Resource.Item.OpenChangelogFailed"));
    }

    // 触发更新
    internal Action<ModLocalComp.LocalCompFile>? UpdateRequested { get; set; }

    private void BtnUpdate_Click(object sender, EventArgs e)
    {
        switch (ModMain.MyMsgBox(
                    $"{Lang.Text("Instance.Resource.Item.UpdateConfirm.Message", Entry.Name)}\r\n\r\n{GetUpdateCompareDescription()}",
                    Lang.Text("Instance.Resource.Item.UpdateConfirm.Title"),
                    Lang.Text("Instance.Resource.Item.Update"), Lang.Text("Instance.Resource.Item.ViewChangelog"), Lang.Text("Common.Action.Cancel")))
        {
            case 1: // 更新
            {
                UpdateRequested?.Invoke(Entry);
                break;
            }
            case 2: // 查看更新日志
            {
                ShowUpdateLog();
                break;
            }
            case 3: // 取消
            {
                break;
            }
        }
    }

    // 自适应（#4465）
    private void PanTitle_SizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
    {
        // 0：全部舒展：Auto - Auto - (Auto) - 1*
        // 1：压缩 Subtitle：Auto - 1* - (Auto) - 0
        // 2：继续压缩 Title：1* - 0 - (Auto) - 0
        var currentCompressLevel =
            ColumnExtend.Width.IsStar ? 0 : ColumnTitle.Width.IsStar ? 2 : 1; // Subtitle 可能是 Collapsed
        var newCompressLevel = default(int);
        switch (currentCompressLevel)
        {
            case 0:
            {
                if (ColumnExtend.ActualWidth < 0.5d)
                    newCompressLevel = LabSubtitle.Visibility == Visibility.Collapsed ? 2 : 1;
                else
                    return;

                break;
            }
            case 1:
            {
                if (ColumnSubtitle.ActualWidth < 0.5d)
                    newCompressLevel = 2;
                else if (!LabSubtitle.IsTextTrimmed())
                    newCompressLevel = 0;
                else
                    return;

                break;
            }
            case 2:
            {
                if (!LabTitle.IsTextTrimmed())
                    newCompressLevel = LabSubtitle.Visibility == Visibility.Collapsed ? 0 : 1;
                else
                    return;

                break;
            }
        }

        switch (newCompressLevel)
        {
            case 0:
            {
                // 全部舒展：Auto - Auto - (Auto) - 1*
                ColumnTitle.Width = GridLength.Auto;
                ColumnSubtitle.Width = GridLength.Auto;
                ColumnExtend.Width = new GridLength(1d, GridUnitType.Star);
                break;
            }
            case 1:
            {
                // 压缩 Subtitle：Auto - 1* - (Auto) - 0
                ColumnTitle.Width = GridLength.Auto;
                ColumnSubtitle.Width = new GridLength(1d, GridUnitType.Star);
                ColumnExtend.Width = new GridLength(0d, GridUnitType.Pixel);
                break;
            }
            case 2:
            {
                // 继续压缩 Title：1* - 0 - (Auto) - 0
                ColumnTitle.Width = new GridLength(1d, GridUnitType.Star);
                ColumnSubtitle.Width = new GridLength(0d, GridUnitType.Pixel);
                ColumnExtend.Width = new GridLength(0d, GridUnitType.Pixel);
                break;
            }
        }
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
        get => field;
        set
        {
            var rawValue = value;
            switch (Entry.State)
            {
                case ModLocalComp.LocalCompFile.LocalFileStatus.Fine:
                {
                    LabTitle.TextDecorations = null;
                    break;
                }
                case ModLocalComp.LocalCompFile.LocalFileStatus.Disabled:
                {
                    LabTitle.TextDecorations = TextDecorations.Strikethrough;
                    break;
                }
                case ModLocalComp.LocalCompFile.LocalFileStatus.Unavailable:
                {
                    LabTitle.TextDecorations = TextDecorations.Strikethrough;
                    value += Lang.Text("Instance.Resource.Item.ErrorSuffix");
                    break;
                }
            }

            if ((LabTitle.Text ?? "") == (value ?? ""))
                return;
            LabTitle.Text = value;
            field = rawValue;
        }
    }

    // 副标题
    public string SubTitle
    {
        get => LabSubtitle?.Text ?? "";
        set
        {
            if ((LabSubtitle.Text ?? "") == (value ?? ""))
                return;
            LabSubtitle.Text = value;
            LabSubtitle.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
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

    // Tag
    public List<string> Tags
    {
        set
        {
            PanTags.Children.Clear();
            PanTags.Visibility = value.Any() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var TagText in value)
            {
                var newTag = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(12, 0, 0, 0)),
                    Padding = new Thickness(3d, 1d, 3d, 1d),
                    CornerRadius = new CornerRadius(3d),
                    Margin = new Thickness(0d, 0d, 3d, 0d),
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = false
                };
                var tagTextBlock = new TextBlock
                {
                    Text = TagText,
                    Foreground = new SolidColorBrush(ThemeManager.IsDarkMode
                        ? Color.FromArgb(88, 255, 255, 255)
                        : Color.FromArgb(88, 136, 136, 136)),
                    FontSize = 11d
                };
                newTag.Child = tagTextBlock;
                PanTags.Children.Add(newTag);
            }
        }
    }

    // 相关联的 Mod
    public ModLocalComp.LocalCompFile Entry
    {
        get => (ModLocalComp.LocalCompFile)Tag;
        set => Tag = value;
    }

    #endregion

    #region 点击与勾选

    // 触发点击事件
    public event RoutedEventHandler? Click;

    public LocalProjectRow()
    {
        InitializeComponent();
        PrimaryAction.Click += Activate;
        PrimaryAction.PreviewMouseLeftButtonDown += BeginSwipe;
        PrimaryAction.PreviewMouseMove += UpdateSwipe;
        PrimaryAction.PreviewMouseLeftButtonUp += EndSwipe;
        PrimaryAction.LostMouseCapture += (_, _) => { if (CurrentSwipe is not null) CurrentSwipe.Swiping = false; };
        Loaded += (_, _) =>
        {
            if (Tag is not ModLocalComp.LocalCompFile entry) return;
            entry.OnCompUpdate += EntryUpdated;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (Tag is ModLocalComp.LocalCompFile entry) entry.OnCompUpdate -= EntryUpdated;
        };
        MouseEnter += RefreshColor;
        MouseLeave += RefreshColor;
        MouseLeftButtonDown += RefreshColor;
        MouseLeftButtonUp += RefreshColor;
        IsKeyboardFocusWithinChanged += (_, _) => RefreshColor(this, EventArgs.Empty);
        Changed += RefreshColor;
        BtnUpdate.PreviewMouseRightButtonUp += BtnUpdate_PreviewMouseRightButtonUp;
        BtnUpdate.Click += BtnUpdate_Click;
        PanTitle.SizeChanged += PanTitle_SizeChanged;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != TagProperty || !IsLoaded) return;
        if (e.OldValue is ModLocalComp.LocalCompFile oldEntry) oldEntry.OnCompUpdate -= EntryUpdated;
        if (e.NewValue is ModLocalComp.LocalCompFile newEntry)
        {
            newEntry.OnCompUpdate += EntryUpdated;
            Refresh();
        }
    }

    private void EntryUpdated(ModLocalComp.LocalCompFile entry) => Refresh();

    private void Activate(object sender, RoutedEventArgs e)
    {
        InitLate(this, e);
        Click?.Invoke(this, e);
        if (!e.Handled) ModBase.Log("[Control] 按下本地资源列表项：" + LabTitle.Text);
        e.Handled = true;
    }

    public sealed class SwipeSelect
    {
        public int Start { get; set; }
        public int End { get; set; }
        public bool SwipeToState { get; set; }

        public bool Swiping
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                ActivityChanged?.Invoke(value);
            }
        }

        public event Action<bool>? ActivityChanged;
    }

    public SwipeSelect? CurrentSwipe { get; set; }

    private void BeginSwipe(object sender, MouseButtonEventArgs e)
    {
        if (CurrentSwipe is not { } swipe || Parent is not Panel panel) return;
        swipe.Start = swipe.End = panel.Children.IndexOf(this);
        swipe.SwipeToState = !Checked;
        swipe.Swiping = true;
    }

    private void UpdateSwipe(object sender, MouseEventArgs e)
    {
        if (CurrentSwipe is not { Swiping: true } swipe || Parent is not Panel panel) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            swipe.Swiping = false;
            return;
        }

        var hit = panel.InputHitTest(e.GetPosition(panel)) as DependencyObject;
        while (hit is not null && hit is not LocalProjectRow && hit != panel)
            hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
        if (hit is not LocalProjectRow target || target.Parent != panel) return;

        ExtendSwipe(panel, swipe, panel.Children.IndexOf(target), e);
    }

    private static void ExtendSwipe(Panel panel, SwipeSelect swipe, int index, EventArgs e)
    {
        var start = Math.Min(swipe.Start, index);
        var end = Math.Max(swipe.End, index);
        if (start == swipe.Start && end == swipe.End) return;
        swipe.Start = start;
        swipe.End = end;
        var rows = panel.Children.Cast<UIElement>().Skip(start).Take(end - start + 1).OfType<LocalProjectRow>().ToArray();
        foreach (var row in rows)
        {
            row.InitLate(row, e);
            row.Checked = swipe.SwipeToState;
        }
    }

    private void EndSwipe(object sender, MouseButtonEventArgs e)
    {
        if (CurrentSwipe is not { Swiping: true } swipe) return;
        swipe.Swiping = false;
        if (swipe.Start == swipe.End) return;
        PrimaryAction.ReleaseMouseCapture();
        e.Handled = true;
    }

    // 勾选状态
    public event CheckEventHandler? Check;

    public delegate void CheckEventHandler(object sender, ModBase.RouteEventArgs e);

    public event ChangedEventHandler? Changed;

    public delegate void ChangedEventHandler(object sender, ModBase.RouteEventArgs e);

    public bool Checked
    {
        get => field;
        set
        {
            try
            {
                // 触发属性值修改
                var rawValue = field;
                if (value == field)
                    return;
                field = value;
                var ChangedEventArgs = new ModBase.RouteEventArgs();
                if (IsInitialized)
                {
                    Changed?.Invoke(this, ChangedEventArgs);
                    if (ChangedEventArgs.handled)
                    {
                        field = rawValue;
                        return;
                    }
                }

                if (value)
                {
                    var checkEventArgs = new ModBase.RouteEventArgs();
                    Check?.Invoke(this, checkEventArgs);
                    if (checkEventArgs.handled)
                        return;
                }

                RectCheck.Height = 32;
                RectCheck.VerticalAlignment = VerticalAlignment.Center;
                RectCheck.Margin = new Thickness(-3, 0, 0, 0);
                var marker = SymbioMotion.Scale(RectCheck);
                SymbioMotion.Reveal(RectCheck, Checked, v => marker.ScaleY = Math.Max(0, v),
                    SymbioMotion.Pop, immediate: !this.IsVisibleInWindow(ModMain.frmMain));
                LabTitle.SetResourceReference(TextBlock.ForegroundProperty, Checked
                    ? Entry.State == ModLocalComp.LocalCompFile.LocalFileStatus.Fine ? "SyAccentText" : "SyControlStrongStroke"
                    : LabTitle.TextDecorations is null ? "SyText" : "SyTextTertiary");
                SymbioMotion.Fade(RectBack, IsMouseOver || Checked ? 1 : 0);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "设置 Checked 失败");
            }
        }
    }

    #endregion

    #region 后加载内容

    // 右下角状态指示图标
    private Image imgState;

    // 指向背景
    public Border RectBack
    {
        get
        {
            if (field is null)
            {
                var rect = new Border
                {
                    Name = "RectBack",
                    CornerRadius = new CornerRadius(3d),
                    RenderTransformOrigin = new Point(0.5d, 0.5d),
                    BorderThickness = new Thickness(ModBase.GetWPFSize(1d)),
                    SnapsToDevicePixels = true,
                    IsHitTestVisible = false,
                    Opacity = 0d
                };
                rect.SetResourceReference(Border.BackgroundProperty, "SySubtleHover");
                rect.SetResourceReference(Border.BorderBrushProperty, "SyControlStroke");
                SetColumnSpan(rect, 999);
                SetRowSpan(rect, 999);
                Children.Insert(0, rect);
                field = rect;
                // <!--<corelocal:BlurBorder x:Name = "RectBack" CornerRadius="3" RenderTransformOrigin="0.5,0.5" SnapsToDevicePixels="True" 
                // IsHitTestVisible = "False" Opacity="0" BorderThickness="1" 
                // Grid.ColumnSpan = "4" Background="{DynamicResource ColorBrush7}" BorderBrush="{DynamicResource ColorBrush6}"/>-->
            }

            return field;
        }
    }

    // 按钮
    public Action<LocalProjectRow, EventArgs> buttonHandler;
    public FrameworkElement buttonStack;
    public IEnumerable<SymbioIconButton> Buttons
    {
        get => field;
        set
        {
            field = value;
            // 移除原 Stack
            if (buttonStack is not null)
            {
                Children.Remove(buttonStack);
                buttonStack = null;
            }

            if (!value.Any())
                return;
            // 添加新 Stack
            buttonStack = new StackPanel
            {
                Opacity = 0d,
                Margin = new Thickness(0d, 0d, 5d, 0d),
                SnapsToDevicePixels = false,
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = false
            };
            SetColumnSpan(buttonStack, 10);
            SetRowSpan(buttonStack, 10);
            // 构造按钮
            foreach (var Btn in value)
            {
                if (Btn.Height.Equals(double.NaN))
                    Btn.Height = 25d;
                if (Btn.Width.Equals(double.NaN))
                    Btn.Width = 25d;
                ((StackPanel)buttonStack).Children.Add(Btn);
            }

            Children.Add(buttonStack);
        }
    }

    // 勾选条
    public Border RectCheck
    {
        get
        {
            if (field is null)
            {
                field = new Border
                {
                    Width = 5d,
                    Height = Checked ? double.NaN : 0d,
                    CornerRadius = new CornerRadius(2d, 2d, 2d, 2d),
                    VerticalAlignment = Checked ? VerticalAlignment.Stretch : VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    UseLayoutRounding = false,
                    SnapsToDevicePixels = false,
                    Margin = Checked ? new Thickness(-3, 6d, 0d, 6d) : new Thickness(-3, 0d, 0d, 0d)
                };
                field.SetResourceReference(Border.BackgroundProperty, "SyAccentFill");
                SetRowSpan(field, 10);
                Children.Add(field);
            }

            return field;
        }
    }

    #endregion
}
