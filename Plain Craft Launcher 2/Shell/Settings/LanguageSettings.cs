using System.Globalization;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

internal sealed record LanguageChoice(string Code, string Label, FontFamily? Font = null);
internal static class LanguageSettings
{
    internal static IEnumerable<LanguageChoice> Languages()
    {
        var automatic = LocalizationService.ResolveLanguage(LocalizationService.Auto);
        yield return new(LocalizationService.Auto, Lang.Text("Setup.Language.UiLanguage.Auto", automatic.NativeName),
            LocalizationFontService.BuildRepresentativeFontFamily(automatic));
        foreach (var language in LocalizationService.SupportedLanguages)
            yield return new(language.Code, language.NativeName, LocalizationFontService.BuildRepresentativeFontFamily(language));
    }

    internal static IEnumerable<LanguageChoice> Cultures()
    {
        yield return new(LocalizationService.Auto, Lang.Text("Setup.Language.FormatCulture.Auto"));
        yield return new(LocalizationService.FormatCultureFollowLanguage, Lang.Text("Setup.Language.FormatCulture.FollowLanguage"));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in LocalizationService.SupportedLanguages.Select(l => l.CultureName)
                     .Append(Config.Preference.Localization.FormatCulture))
        {
            if (string.IsNullOrWhiteSpace(code) || code == LocalizationService.Auto ||
                code == LocalizationService.FormatCultureFollowLanguage) continue;
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(code); }
            catch (CultureNotFoundException) { continue; }
            if (used.Add(culture.Name)) yield return new(culture.Name, culture.NativeName);
        }
    }

    internal static string Language => LocalizationService.IsLanguageSupported(Config.Preference.Localization.Language)
        && !string.Equals(Config.Preference.Localization.Language, LocalizationService.Auto, StringComparison.OrdinalIgnoreCase)
        ? LocalizationService.ResolveLanguage(Config.Preference.Localization.Language).Code : LocalizationService.Auto;
    internal static string Format => Config.Preference.Localization.FormatCulture;

    internal static void Select(string code, bool format)
    {
        if (format) Config.Preference.Localization.FormatCulture = code;
        else Config.Preference.Localization.Language = code;
    }

    internal static void Reset()
    {
        Config.Preference.Localization.LanguageConfig.SetDefaultValue();
        Config.Preference.Localization.FormatCultureConfig.SetDefaultValue();
        LocalizationService.ApplyFromConfig();
    }
}
