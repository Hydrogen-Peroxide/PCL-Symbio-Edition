using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PCL;

public sealed class SymbioSearchField : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(SymbioSearchField),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((SymbioSearchField)d).SyncText()));
    public static readonly DependencyProperty HintTextProperty = SymbioTextField.HintTextProperty.AddOwner(typeof(SymbioSearchField));
    public static readonly DependencyProperty SearchButtonVisibilityProperty = DependencyProperty.Register(
        nameof(SearchButtonVisibility), typeof(Visibility), typeof(SymbioSearchField), new PropertyMetadata(Visibility.Collapsed));

    private SymbioTextField? editor;
    private Button? clear;
    private Button? search;

    public SymbioSearchField()
    {
        Loaded += (_, _) => editor?.Focus();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string HintText { get => (string)GetValue(HintTextProperty); set => SetValue(HintTextProperty, value); }
    public Visibility SearchButtonVisibility { get => (Visibility)GetValue(SearchButtonVisibilityProperty); set => SetValue(SearchButtonVisibilityProperty, value); }

    public event TextChangedEventHandler? TextChanged;
    public event EventHandler? Search;

    public override void OnApplyTemplate()
    {
        if (editor is not null) editor.TextChanged -= Edit;
        if (clear is not null) clear.Click -= Clear;
        if (search is not null) search.Click -= Submit;
        base.OnApplyTemplate();
        editor = GetTemplateChild("PART_Query") as SymbioTextField;
        clear = GetTemplateChild("PART_Clear") as Button;
        search = GetTemplateChild("PART_Search") as Button;
        SyncText();
        UpdateClear();
        if (editor is not null) editor.TextChanged += Edit;
        if (clear is not null) clear.Click += Clear;
        if (search is not null) search.Click += Submit;
    }

    private void SyncText()
    {
        if (editor is not null && editor.Text != Text) editor.SetCurrentValue(TextBox.TextProperty, Text ?? "");
    }

    private void Edit(object sender, TextChangedEventArgs e)
    {
        if (editor is null) return;
        SetCurrentValue(TextProperty, editor.Text);
        UpdateClear();
        TextChanged?.Invoke(sender, e);
    }

    private void UpdateClear()
    {
        if (clear is null) return;
        var hasText = !string.IsNullOrEmpty(Text);
        clear.IsHitTestVisible = hasText;
        clear.IsTabStop = hasText;
        SymbioMotion.Fade(clear, hasText ? 1 : 0, immediate: !IsLoaded);
    }

    private void Clear(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(TextProperty, "");
        editor?.Focus();
    }

    private void Submit(object sender, RoutedEventArgs e) => Search?.Invoke(sender, e);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Enter || editor?.IsKeyboardFocusWithin != true || Search is null) return;
        e.Handled = true;
        Search(this, e);
    }
}
