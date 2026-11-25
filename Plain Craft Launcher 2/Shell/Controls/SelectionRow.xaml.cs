using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PCLLF.Wpf.Motion;
using SvgGlyph = PCL.Core.UI.Controls.SvgIcon.SvgIcon;

namespace PCL;

[ContentProperty(nameof(Inlines))]
public partial class SelectionRow : IMyRadio
{
    public enum CheckType { None, Clickable, RadioBox, CheckBox }

    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(nameof(Click),
        RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(SelectionRow));
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public event MouseButtonEventHandler? LogoClick;
    public event IMyRadio.CheckEventHandler? Check;
    public event IMyRadio.ChangedEventHandler? Changed;

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string),
        typeof(SelectionRow), new FrameworkPropertyMetadata("", null, (_, value) => SingleLine(value as string)));
    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(nameof(Info), typeof(string),
        typeof(SelectionRow), new FrameworkPropertyMetadata("", (d, e) => ((SelectionRow)d).UpdateInfo((string)e.NewValue),
            (_, value) => SingleLine(value as string)));
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(nameof(FontSize), typeof(double),
        typeof(SelectionRow), new PropertyMetadata(14d));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(SelectionRow));
    public static readonly DependencyProperty LogoProperty = DependencyProperty.Register(nameof(Logo), typeof(string),
        typeof(SelectionRow), new PropertyMetadata("", (d, _) => ((SelectionRow)d).UpdateLogo()));
    public static readonly DependencyProperty SvgIconProperty = DependencyProperty.Register(nameof(SvgIcon), typeof(string),
        typeof(SelectionRow), new PropertyMetadata("", (d, _) => ((SelectionRow)d).UpdateLogo()));

    private readonly ObservableCollection<SymbioIconButton> actions = [];
    private bool replacingActions;
    private bool checkedState;
    private bool logoPressed;
    private bool hoverState;

    public int Uuid = ModBase.GetUuid();
    public object? tag { get; set; }
    public bool isMouseOverAnimationEnabled = true;
    public bool IsScaleAnimationEnabled { get; set; }
    public bool IsFluentNavigation { get; set; }
    public Action<SelectionRow, EventArgs>? ContentHandler { get; set; }

    public SelectionRow()
    {
        InitializeComponent();
        SetResourceReference(ForegroundProperty, "SyText");
        PrimaryAction.Click += Activate;
        PrimaryAction.PreviewMouseLeftButtonDown += (_, _) => RefreshPressedPose();
        PrimaryAction.PreviewMouseLeftButtonUp += (_, _) => RefreshPressedPose();
        PrimaryAction.LostMouseCapture += (_, _) => RefreshPressedPose();
        MouseEnter += RefreshChrome;
        MouseLeave += RefreshChrome;
        IsKeyboardFocusWithinChanged += (_, _) => RefreshChrome(this, EventArgs.Empty);
        SizeChanged += (_, _) => UpdateLogoMetrics();
        actions.CollectionChanged += (_, _) => { if (!replacingActions) UpdateActions(); };
        Loaded += (_, _) =>
        {
            UpdateLogoMetrics();
            if (Checked && Type == CheckType.RadioBox) ClearOtherSelections(false);
            UpdateSelection(false);
            RefreshChrome(this, EventArgs.Empty);
        };
    }

    public InlineCollection Inlines => LabTitle.Inlines;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Info { get => (string)GetValue(InfoProperty); set => SetValue(InfoProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public string Logo { get => (string)GetValue(LogoProperty); set => SetValue(LogoProperty, value); }
    public string SvgIcon { get => (string)GetValue(SvgIconProperty); set => SetValue(SvgIconProperty, value); }
    public int PaddingLeft { get => (int)LeftSpace.Width.Value; set => LeftSpace.Width = new GridLength(Math.Max(0, value)); }
    public int MinPaddingRight
    {
        get => field;
        set
        {
            field = Math.Max(value, 0);
            UpdateActionWidth(hoverState, immediate: true);
        }
    } = 4;

    public bool LogoClickable
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            UpdateLogo();
        }
    }

    public double LogoScale
    {
        get => field;
        set { field = value; UpdateLogoMetrics(); }
    } = 1d;

    public CornerRadius LogoCornerRadius
    {
        get => field;
        set
        {
            field = value;
            if (LogoHost.Content is AssetImage image) image.CornerRadius = value;
        }
    }

    public CheckType Type
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            var interactive = value != CheckType.None;
            PrimaryAction.IsTabStop = interactive;
            PrimaryAction.IsHitTestVisible = interactive;
            if (value is CheckType.None or CheckType.Clickable) SetChecked(false, false, false);
            UpdateLogoMetrics();
            UpdateSelection(false);
        }
    }

    public ObservableCollection<SymbioIconButton> ButtonItems => actions;

    public IEnumerable<SymbioIconButton> Buttons
    {
        get => actions;
        set
        {
            var entries = value?.ToArray() ?? [];
            replacingActions = true;
            try
            {
                actions.Clear();
                foreach (var action in entries) actions.Add(action);
            }
            finally
            {
                replacingActions = false;
            }
            UpdateActions();
        }
    }

    public object Tags
    {
        set
        {
            PanTags.Children.Clear();
            var tags = value is string text ? text.Split('|') : value as IEnumerable<string> ?? [];
            foreach (var tag in tags.Where(tag => !string.IsNullOrEmpty(tag)))
            {
                var label = new TextBlock { Text = tag, FontSize = 11 };
                label.SetResourceReference(TextBlock.ForegroundProperty, "SyTextSecondary");
                var chip = new Border
                {
                    Child = label, CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 0, 3, 0)
                };
                chip.SetResourceReference(BackgroundProperty, "SySubtleHover");
                PanTags.Children.Add(chip);
            }
        }
    }

    private static string SingleLine(string? value) => value?.Replace("\r", "").Replace("\n", "") ?? "";

    private void UpdateInfo(string value)
    {
        LabInfo.Text = value;
        LabInfo.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    public override string ToString() => Title;

    private void UpdateLogo()
    {
        FrameworkElement? icon;
        if (!string.IsNullOrWhiteSpace(SvgIcon))
        {
            var svg = new SvgGlyph { Icon = SvgIcon, Stretch = Stretch.Uniform };
            svg.SetBinding(SvgGlyph.IconBrushProperty, new Binding(nameof(Foreground)) { Source = this });
            icon = svg;
        }
        else if (ImageCache.IsRemote(Logo) || Logo is { Length: > 0 } && IsBitmapPath(Logo))
        {
            var bitmap = new AssetImage { Address = Logo, CornerRadius = LogoCornerRadius, Stretch = Stretch.Uniform };
            bool pixelArt = Logo.Contains(ModBase.pathTemp + @"Cache\Skin\Head", StringComparison.OrdinalIgnoreCase) ||
                            Logo.Contains(ModBase.pathTemp + @"Cache\Cape", StringComparison.OrdinalIgnoreCase);
            RenderOptions.SetBitmapScalingMode(bitmap, pixelArt ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);
            icon = bitmap;
        }
        else if (!string.IsNullOrWhiteSpace(Logo))
        {
            var path = new Path { Data = Geometry.Parse(Logo), Stretch = Stretch.Uniform };
            path.SetBinding(Shape.FillProperty, new Binding(nameof(Foreground)) { Source = this });
            icon = path;
        }
        else
        {
            icon = null;
        }

        LogoHost.Content = icon;
        LogoHost.IsHitTestVisible = LogoClickable;
        if (icon is not null && LogoClickable)
        {
            icon.MouseLeave += (_, _) => logoPressed = false;
            icon.MouseLeftButtonDown += (_, _) => logoPressed = true;
            icon.MouseLeftButtonUp += (_, e) =>
            {
                if (!logoPressed) return;
                logoPressed = false;
                LogoClick?.Invoke(this, e);
            };
        }
        UpdateLogoMetrics();
    }

    private static bool IsBitmapPath(string address) => address.StartsWith("pack:", StringComparison.OrdinalIgnoreCase) ||
        new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".ico", ".gif" }.Any(ext => address.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private void UpdateLogoMetrics()
    {
        var compact = (ActualHeight > 0 ? ActualHeight : Height) < 40;
        SelectionSpace.Width = new GridLength(Type is CheckType.RadioBox or CheckType.CheckBox ? 6 : compact ? 4 : 2);
        var centered = IsFluentNavigation && string.IsNullOrEmpty(Title);
        SetColumn(LogoHost, centered ? 0 : 2);
        SetColumnSpan(LogoHost, centered ? ColumnDefinitions.Count : 1);
        LogoHost.Width = LogoHost.Content is null ? (compact ? 0 : 4) : compact ? 30 : 34;
        LabTitle.Margin = new Thickness(0, 0, 0, compact ? 0 : 2);
        if (LogoHost.Content is not FrameworkElement icon) return;
        icon.Width = icon.Height = IsFluentNavigation ? 24 : compact ? 18 : 24;
        icon.Opacity = IsFluentNavigation ? .8 : 1;
        var scale = SymbioMotion.Scale(icon);
        scale.ScaleX = scale.ScaleY = string.IsNullOrWhiteSpace(SvgIcon) ? LogoScale : 1;
    }

    private void UpdateActions()
    {
        ActionHost.Children.Clear();
        foreach (var action in actions)
        {
            if (double.IsNaN(action.Width)) action.Width = 25;
            if (double.IsNaN(action.Height)) action.Height = 25;
            action.IsTabStop = hoverState;
            ActionHost.Children.Add(action);
        }
        UpdateActionWidth(hoverState, immediate: true);
    }

    private void UpdateActionWidth(bool visible, bool immediate)
    {
        var width = visible && actions.Count > 0 ? Math.Max(MinPaddingRight, actions.Sum(action => action.Width) + 5) : MinPaddingRight;
        SymbioMotion.Spring(this, "rowActions", () => ActionSpace.Width.Value,
            value => ActionSpace.Width = new GridLength(Math.Max(0, value)), width, SymbioMotion.Exit, immediate);
    }

    private void RefreshChrome(object sender, EventArgs e)
    {
        var hovered = (IsMouseOver && isMouseOverAnimationEnabled) || IsKeyboardFocusWithin;
        if (hovered && ContentHandler is { } prepare)
        {
            ContentHandler = null;
            prepare(this, e);
        }
        if (hoverState != hovered)
        {
            hoverState = hovered;
            ActionHost.IsHitTestVisible = hovered;
            foreach (var action in actions) action.IsTabStop = hovered;
            SymbioMotion.Fade(ActionHost, hovered ? 1 : 0);
            SymbioMotion.Fade(RectBack, hovered ? 1 : 0);
            UpdateActionWidth(hovered, immediate: !IsLoaded);
        }
        UpdatePressedPose();
    }

    private void RefreshPressedPose() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(UpdatePressedPose));

    private void UpdatePressedPose()
    {
        var pressed = PrimaryAction.IsPressed && !(Type == CheckType.RadioBox && Checked);
        RectBack.SetResourceReference(BackgroundProperty, pressed ? "SySubtlePressed" : "SySubtleHover");
        if (IsScaleAnimationEnabled) SymbioMotion.SpringScale(RectBack, pressed ? .98 : 1, MotionTokens.Press);
    }

    public bool Checked { get => checkedState; set => SetChecked(value, false, value != checkedState); }

    public void SetChecked(bool value, bool user, bool anime)
    {
        if (checkedState == value) return;
        var previous = checkedState;
        checkedState = value;
        var args = new ModBase.RouteEventArgs(user);
        if (IsInitialized) Changed?.Invoke(this, args);
        if (value && !args.handled) Check?.Invoke(this, args);
        if (args.handled)
        {
            checkedState = previous;
            return;
        }

        if (Type == CheckType.RadioBox)
        {
            if (value) ClearOtherSelections(anime);
            else if (GetRadioGroup() is { Length: > 0 } siblings && !siblings.Any(row => row.Checked))
                siblings[0].SetChecked(true, false, anime);
        }
        UpdateSelection(anime);
    }

    private SelectionRow[] GetRadioGroup() => Parent is Panel panel
        ? panel.Children.OfType<SelectionRow>().Where(row => row.Type == CheckType.RadioBox).ToArray() : [];

    private void ClearOtherSelections(bool animate)
    {
        foreach (var row in GetRadioGroup())
            if (row != this && row.Checked) row.SetChecked(false, false, animate);
    }

    private void UpdateSelection(bool animate)
    {
        var selected = Checked && Type is CheckType.RadioBox or CheckType.CheckBox;
        var scale = SymbioMotion.Scale(SelectionMarker);
        SymbioMotion.Reveal(SelectionMarker, selected, value => scale.ScaleY = Math.Max(0, value), SymbioMotion.Pop,
            immediate: !animate || !IsLoaded);
        SymbioMotion.Fade(SelectedSurface, selected && IsFluentNavigation ? 1 : 0, immediate: !animate || !IsLoaded);
    }

    private void Activate(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Type == CheckType.None) return;
        var invocation = new RoutedEventArgs(ClickEvent, this);
        RaiseEvent(invocation);
        if (invocation.Handled) return;
        if (CustomEventService.GetEventType(this) != EventType.None || (GetValue(CustomEventService.EventsProperty) is CustomEventCollection events && events.Any()))
        {
            ModMain.RaiseCustomEvent(this);
            return;
        }
        if (Type == CheckType.RadioBox) SetChecked(true, true, true);
        else if (Type == CheckType.CheckBox) SetChecked(!Checked, true, true);
    }
}
