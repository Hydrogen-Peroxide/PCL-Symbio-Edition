using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
    private Popup? popup;
    private FrameworkElement? dropDown;

    public override void OnApplyTemplate()
    {
        if (editor is not null) editor.ValidatedTextChanged -= NotifyText;
        if (popup is not null) popup.Opened -= Popup_Opened;
        base.OnApplyTemplate();
        editor = GetTemplateChild("PART_EditableTextBox") as SymbioTextField;
        if (editor is not null) editor.ValidatedTextChanged += NotifyText;

        // 下拉以前是纯 IsOpen 绑定，弹出没有任何过渡；这里接上统一的 EnterDepth
        popup = GetTemplateChild("PART_Popup") as Popup;
        dropDown = popup?.Child as FrameworkElement;
        if (popup is not null) popup.Opened += Popup_Opened;
    }

    private void Popup_Opened(object? sender, EventArgs e)
    {
        if (dropDown is null) return;
        dropDown.RenderTransformOrigin = new Point(0.5, 0);
        SymbioMotion.EnterDepth(dropDown, 6, .97, SymbioMotion.Pop);
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
