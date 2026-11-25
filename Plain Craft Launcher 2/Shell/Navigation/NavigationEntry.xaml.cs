using System.Windows;
using System.Windows.Controls;

namespace PCL;

public partial class NavigationEntry
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavigationEntry));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(NavigationEntry));
    public static readonly DependencyProperty ActionHintProperty = DependencyProperty.Register(nameof(ActionHint), typeof(string), typeof(NavigationEntry));
    public static readonly DependencyProperty ActionIconProperty = DependencyProperty.Register(nameof(ActionIcon), typeof(string), typeof(NavigationEntry),
        new PropertyMetadata(null, (d,e) => ((NavigationEntry)d).Secondary.Visibility = string.IsNullOrEmpty(e.NewValue as string) ? Visibility.Collapsed : Visibility.Visible));
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string ActionHint { get => (string)GetValue(ActionHintProperty); set => SetValue(ActionHintProperty, value); }
    public string ActionIcon { get => (string)GetValue(ActionIconProperty); set => SetValue(ActionIconProperty, value); }
    public object? ActionTag { get; set; }
    public bool IsDestination { get; set; } = true;
    public bool Selected { get => isSelected; set => Select(value); }
    public event Action<object, ModBase.RouteEventArgs>? Chosen;
    public event RoutedEventHandler? Invoked;
    public event RoutedEventHandler? ActionRequested;
    public NavigationEntry() => InitializeComponent();

    public void Select(bool selected, bool notify = true)
    {
        if (Selected == selected) return;
        SetSelectionVisual(selected);
        if (!selected) return;
        if (Parent is Panel group)
            foreach (var row in group.Children.OfType<NavigationEntry>())
                if (!ReferenceEquals(this, row) && row.Selected) row.SetSelectionVisual(false);
        if (notify && Tag is not null && IsInitialized) Chosen?.Invoke(this, new ModBase.RouteEventArgs());
    }
    private void SetSelectionVisual(bool selected)
    {
        isSelected = selected;
        Primary.Tag = selected ? "Selected" : null;
    }
    private bool isSelected;
    private void Choose(object sender, RoutedEventArgs e)
    {
        if (IsDestination) Select(true);
        Invoked?.Invoke(this, e);
    }
    private void ExecuteSecondary(object sender, RoutedEventArgs e)
    {
        Secondary.Tag = ActionTag ?? Tag;
        ActionRequested?.Invoke(Secondary, e);
        e.Handled = true;
    }
}
