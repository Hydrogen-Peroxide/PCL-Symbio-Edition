using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using PCLLF.Wpf.Glass;
using PCLLF.Wpf.Motion;
using SvgIconControl = PCL.Core.UI.Controls.SvgIcon.SvgIcon;

namespace PCL;

/// <summary>
///     Fluent 2 settings card (Windows 11 Settings / WinUI SettingsCard): icon, header and description on the left,
///     the setting's control on the right. Cards stack with a 4 px gap inside a settings section.
///     The card is an LF-style frosted glass pane: it blurs the wallpaper directly behind it under a light tint.
/// </summary>
[ContentProperty(nameof(Action))]
public class SettingsCard : GlassSurface
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string),
        typeof(SettingsCard), new PropertyMetadata("", (d, e) => ((SettingsCard)d).header.Text = (string)e.NewValue));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string),
        typeof(SettingsCard), new PropertyMetadata("", (d, e) => ((SettingsCard)d).ApplyDescription()));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string),
        typeof(SettingsCard), new PropertyMetadata("", (d, e) => ((SettingsCard)d).ApplyIcon()));
    public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(nameof(Action), typeof(object),
        typeof(SettingsCard), new PropertyMetadata(null, (d, e) => ((SettingsCard)d).action.Content = e.NewValue));
    public static readonly DependencyProperty ActionWidthProperty = DependencyProperty.Register(nameof(ActionWidth), typeof(double),
        typeof(SettingsCard), new PropertyMetadata(double.NaN, (d, e) => ((SettingsCard)d).action.Width = (double)e.NewValue));
    public static readonly DependencyProperty StackedProperty = DependencyProperty.Register(nameof(Stacked), typeof(bool),
        typeof(SettingsCard), new PropertyMetadata(false, (d, e) => ((SettingsCard)d).ApplyLayout()));

    private readonly Grid grid = new();
    private readonly SvgIconControl icon = new() { Width = 20, Height = 20, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock header = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    // Fluent descriptions stay short: at most two lines, the full text is in the tooltip.
    private readonly TextBlock description = new()
    {
        FontSize = 12, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34,
        Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed
    };
    private readonly StackPanel texts = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentPresenter action = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    /// <summary>A lucide icon name such as <c>lucide/gauge</c>.</summary>
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    /// <summary>Fixed width for the trailing control; NaN keeps its natural size.</summary>
    public double ActionWidth { get => (double)GetValue(ActionWidthProperty); set => SetValue(ActionWidthProperty, value); }
    /// <summary>Puts the control on its own row under the text (wide editors such as text boxes or lists).</summary>
    public bool Stacked { get => (bool)GetValue(StackedProperty); set => SetValue(StackedProperty, value); }

    public SettingsCard()
    {
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(16, 12, 16, 12);
        MinHeight = 68;
        Margin = new Thickness(0, 0, 0, 4);
        BlurRadius = 24;
        TintOpacity = 1;
        HighlightOpacity = 0;
        SetResourceReference(BackgroundProperty, "SyFrostFill");
        SetResourceReference(BorderBrushProperty, "SyFrostEdge");
        header.SetResourceReference(TextBlock.ForegroundProperty, "SyText");
        description.SetResourceReference(TextBlock.ForegroundProperty, "SyTextSecondary");
        icon.SetResourceReference(SvgIconControl.IconBrushProperty, "SyText");

        texts.Children.Add(header);
        texts.Children.Add(description);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(icon);
        grid.Children.Add(texts);
        grid.Children.Add(action);
        Child = grid;
        ApplyLayout();
    }

    private void ApplyIcon()
    {
        icon.Icon = Icon;
        icon.Visibility = string.IsNullOrWhiteSpace(Icon) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyDescription()
    {
        description.Text = Description;
        description.ToolTip = string.IsNullOrWhiteSpace(Description) ? null : Description;
        description.Visibility = string.IsNullOrWhiteSpace(Description) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyLayout()
    {
        // Single-line cards centre their content inside the 68 px minimum height, like WinUI SettingsCard.
        grid.VerticalAlignment = Stacked ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (Stacked)
        {
            Grid.SetRow(action, 1);
            Grid.SetColumn(action, 1);
            Grid.SetColumnSpan(action, 2);
            action.Margin = new Thickness(0, 12, 0, 0);
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            Grid.SetRow(action, 0);
            Grid.SetColumn(action, 2);
            Grid.SetColumnSpan(action, 1);
            action.Margin = new Thickness(16, 0, 0, 0);
            action.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }
}

/// <summary>A row inside a <see cref="SettingsExpander" />: same layout as a card, separated by a hairline.</summary>
public class SettingsRow : SettingsCard
{
    public SettingsRow()
    {
        CornerRadius = new CornerRadius(0);
        BorderThickness = new Thickness(0, 1, 0, 0);
        Margin = new Thickness(0);
        Padding = new Thickness(58, 10, 16, 10);
        MinHeight = 52;
        PaintsWallpaper = false; // rows sit on their expander's glass
        Background = Brushes.Transparent;
        SetResourceReference(BorderBrushProperty, "SyDivider");
    }
}

/// <summary>
///     Fluent settings expander: a settings card whose chevron reveals related rows underneath.
///     The header can still carry its own control (e.g. a master switch).
/// </summary>
[ContentProperty(nameof(Items))]
public class SettingsExpander : StackPanel
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool),
        typeof(SettingsExpander), new PropertyMetadata(false, (d, e) => ((SettingsExpander)d).ApplyExpanded(true)));

    private readonly SettingsCard card = new() { Margin = new Thickness(0), Cursor = Cursors.Hand };
    private readonly GlassSurface body = new()
    {
        BorderThickness = new Thickness(1, 0, 1, 1), CornerRadius = new CornerRadius(0, 0, 4, 4), Visibility = Visibility.Collapsed,
        BlurRadius = 24, TintOpacity = 1, HighlightOpacity = 0
    };
    private readonly StackPanel rows = new();
    private readonly Path chevron = new()
    {
        Data = Geometry.Parse("M0,0 L5,5 L10,0"), Width = 11, Height = 6, Stretch = Stretch.Uniform, StrokeThickness = 1.3,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        RenderTransformOrigin = new Point(.5, .5), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ContentPresenter headerAction = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly RotateTransform chevronRotation = new();
    private readonly SpringMotion chevronMotion;

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string),
        typeof(SettingsExpander), new PropertyMetadata("", (d, e) => ((SettingsExpander)d).card.Header = (string)e.NewValue));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string),
        typeof(SettingsExpander), new PropertyMetadata("", (d, e) => ((SettingsExpander)d).card.Description = (string)e.NewValue));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string),
        typeof(SettingsExpander), new PropertyMetadata("", (d, e) => ((SettingsExpander)d).card.Icon = (string)e.NewValue));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    /// <summary>Optional control shown in the header, left of the chevron.</summary>
    public object? HeaderAction { get => headerAction.Content; set => headerAction.Content = value; }
    public UIElementCollection Items => rows.Children;
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

    public SettingsExpander()
    {
        Margin = new Thickness(0, 0, 0, 4);
        chevron.SetResourceReference(Shape.StrokeProperty, "SyTextSecondary");
        chevron.RenderTransform = chevronRotation;
        chevronMotion = new SpringMotion(chevron, 0, v => chevronRotation.Angle = v, new SpringParameters(.32, .78));

        var trailing = new StackPanel { Orientation = Orientation.Horizontal };
        trailing.Children.Add(headerAction);
        trailing.Children.Add(chevron);
        card.Action = trailing;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInside(source, headerAction)) return;
            IsExpanded = !IsExpanded;
        };
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "SyFrostHover");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "SyFrostFill");

        body.SetResourceReference(Border.BackgroundProperty, "SyFrostFill");
        body.SetResourceReference(Border.BorderBrushProperty, "SyFrostEdge");
        body.Child = rows;
        Children.Add(card);
        Children.Add(body);
        ApplyExpanded(false);
    }

    private void ApplyExpanded(bool animate)
    {
        card.CornerRadius = IsExpanded ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(4);
        chevronMotion.Retarget(IsExpanded ? 180 : 0, !animate || !IsLoaded);
        if (IsExpanded)
        {
            body.Visibility = Visibility.Visible;
            if (animate && IsLoaded) SymbioMotion.Stagger(rows.Children.OfType<UIElement>());
        }
        else body.Visibility = Visibility.Collapsed;
    }

    private static bool IsInside(DependencyObject source, DependencyObject ancestor)
    {
        for (var item = source; item is not null; item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
            if (ReferenceEquals(item, ancestor)) return true;
        return false;
    }
}
