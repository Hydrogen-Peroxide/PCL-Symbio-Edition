using System.Windows;
using System.Windows.Controls;

namespace PCL;

public sealed class SymbioSelect : ComboBox
{
    public static readonly DependencyProperty HintTextProperty = DependencyProperty.Register(
        nameof(HintText), typeof(string), typeof(SymbioSelect), new PropertyMetadata(""));
    public static readonly DependencyProperty DropDownWidthSyncProperty = DependencyProperty.Register(
        nameof(DropDownWidthSync), typeof(bool), typeof(SymbioSelect), new PropertyMetadata(true));

    public string HintText { get => (string)GetValue(HintTextProperty); set => SetValue(HintTextProperty, value); }
    public bool DropDownWidthSync { get => (bool)GetValue(DropDownWidthSyncProperty); set => SetValue(DropDownWidthSyncProperty, value); }
    public event TextChangedEventHandler? TextChanged;

    private SymbioTextField? editor;

    public override void OnApplyTemplate()
    {
        if (editor is not null) editor.ValidatedTextChanged -= NotifyText;
        base.OnApplyTemplate();
        editor = GetTemplateChild("PART_EditableTextBox") as SymbioTextField;
        if (editor is not null) editor.ValidatedTextChanged += NotifyText;
    }

    private void NotifyText(object sender, RoutedEventArgs e)
    {
        if (!IsEditable || e is not TextChangedEventArgs changed) return;
        TextChanged?.Invoke(sender, changed);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (IsLoaded && !ModAnimation.AniExitsAnyControl()) ModMain.RaiseCustomEvent(this);
    }

    protected override DependencyObject GetContainerForItemOverride() => new SelectOption();
}

public sealed class SelectOption : ComboBoxItem
{
    public SelectOption() => SetResourceReference(StyleProperty, "SySelectOption");
    public override string ToString() => Content?.ToString() ?? "";
    public static implicit operator string(SelectOption option) => option.ToString();
}
