using System.Windows;
using System.Windows.Controls;
using PCL.Core.App.Localization;

namespace PCL;

public partial class LanguageSettingsView
{
    private bool loading;
    public LanguageSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => { Scroll.ScrollToHome(); Reload(); };
    }

    public void Reload()
    {
        loading = true;
        try
        {
            Languages.ItemsSource = LanguageSettings.Languages().ToArray();
            Cultures.ItemsSource = LanguageSettings.Cultures().ToArray();
            Select(Languages, LanguageSettings.Language);
            Select(Cultures, LanguageSettings.Format);
        }
        finally { loading = false; }
    }

    private static void Select(ComboBox box, string? code) => box.SelectedItem =
        box.Items.OfType<LanguageChoice>().FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? box.Items[0];

    private void ChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || !IsLoaded || sender is not ComboBox { SelectedItem: LanguageChoice choice } box) return;
        var format = ReferenceEquals(box, Cultures);
        if (choice.Code == (format ? LanguageSettings.Format : LanguageSettings.Language)) return;
        LanguageSettings.Select(choice.Code, format);
        HintService.Hint(Lang.Text("Setup.Language.Changed"), HintType.Success, false);
        Reload();
    }

    public void Reset()
    {
        LanguageSettings.Reset();
        HintService.Hint(Lang.Text("Setup.Language.Reset.Success"), HintType.Success, false);
        Reload();
    }

    private void OpenTranslations(object sender, RoutedEventArgs e) =>
        ModBase.OpenWebsite("https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/pulls");
}
