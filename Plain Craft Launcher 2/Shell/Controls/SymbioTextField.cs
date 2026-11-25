using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentValidation;
using PCL.Core.App.Localization;

namespace PCL;

public sealed class SymbioTextField : TextBox
{
    public static readonly DependencyProperty CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner(typeof(SymbioTextField));
    public static readonly DependencyProperty HintTextProperty = DependencyProperty.Register(
        nameof(HintText), typeof(string), typeof(SymbioTextField), new PropertyMetadata(""));
    public static readonly DependencyProperty HasBackgroundProperty = DependencyProperty.Register(
        nameof(HasBackground), typeof(bool), typeof(SymbioTextField), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowValidateResultProperty = DependencyProperty.Register(
        nameof(ShowValidateResult), typeof(bool), typeof(SymbioTextField),
        new PropertyMetadata(true, (d, _) => ((SymbioTextField)d).UpdateError()));
    public static readonly DependencyProperty ValidateResultProperty = DependencyProperty.Register(
        nameof(ValidateResult), typeof(string), typeof(SymbioTextField),
        new PropertyMetadata("", (d, _) => ((SymbioTextField)d).UpdateError()));
    private static readonly DependencyPropertyKey IsValidatedKey = DependencyProperty.RegisterReadOnly(
        nameof(IsValidated), typeof(bool), typeof(SymbioTextField), new PropertyMetadata(true));
    private static readonly DependencyPropertyKey ShowErrorKey = DependencyProperty.RegisterReadOnly(
        nameof(ShowError), typeof(bool), typeof(SymbioTextField), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey ShowErrorMessageKey = DependencyProperty.RegisterReadOnly(
        nameof(ShowErrorMessage), typeof(bool), typeof(SymbioTextField), new PropertyMetadata(false));
    public static readonly DependencyProperty IsValidatedProperty = IsValidatedKey.DependencyProperty;
    public static readonly DependencyProperty ShowErrorProperty = ShowErrorKey.DependencyProperty;
    public static readonly DependencyProperty ShowErrorMessageProperty = ShowErrorMessageKey.DependencyProperty;

    private bool edited;
    private bool validated;
    private bool validating;

    public SymbioTextField()
    {
        Loaded += (_, _) => Validate();
    }

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public string HintText { get => (string)GetValue(HintTextProperty); set => SetValue(HintTextProperty, value); }
    public bool HasBackground { get => (bool)GetValue(HasBackgroundProperty); set => SetValue(HasBackgroundProperty, value); }
    public bool ShowValidateResult { get => (bool)GetValue(ShowValidateResultProperty); set => SetValue(ShowValidateResultProperty, value); }
    public string ValidateResult { get => (string)GetValue(ValidateResultProperty); set => SetValue(ValidateResultProperty, value); }
    public bool IsValidated => (bool)GetValue(IsValidatedProperty);
    public bool ShowError => (bool)GetValue(ShowErrorProperty);
    public bool ShowErrorMessage => (bool)GetValue(ShowErrorMessageProperty);

    public Collection<IValidator<string>> ValidateRules
    {
        get => field;
        set
        {
            field = value ?? [];
            Validate();
        }
    } = [];

    public event EventHandler? ValidateChanged;
    public event RoutedEventHandler? ValidatedTextChanged;

    public void Validate()
    {
        if (validating) return;
        validating = true;
        try
        {
            string error = "";
            foreach (var rule in ValidateRules)
            {
                var result = rule.Validate(Text);
                if (result.IsValid) continue;
                error = result.Errors[0].ErrorMessage;
                break;
            }

            var changed = !validated || !string.Equals(ValidateResult, error, StringComparison.Ordinal);
            validated = true;
            SetCurrentValue(ValidateResultProperty, error);
            UpdateError();
            if (changed) ValidateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { validating = false; }
    }

    public void ForceShowAsSuccess()
    {
        edited = false;
        UpdateError();
        ValidateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        edited = IsLoaded;
        try
        {
            Validate();
            if (IsValidated) ValidatedTextChanged?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "输入验证失败", ModBase.LogLevel.Critical,
                userSummary: Lang.Text("Application.Control.Error.OperationFailed"));
        }
        base.OnTextChanged(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Enter) ModMain.RaiseCustomEvent(this);
    }

    private void UpdateError()
    {
        var valid = string.IsNullOrEmpty(ValidateResult);
        SetValue(IsValidatedKey, valid);
        SetValue(ShowErrorKey, edited && !valid);
        SetValue(ShowErrorMessageKey, edited && !valid && ShowValidateResult);
    }
}
