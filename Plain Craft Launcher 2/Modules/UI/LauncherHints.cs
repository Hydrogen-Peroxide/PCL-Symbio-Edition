using System.IO;
using System.Windows;
using PCL.Core.App.Localization;

namespace PCL;

internal static class LauncherHints
{
    public static string GetRandomHint(bool enableLengthLimit = false, bool raw = false)
    {
        string[]? lines = null;

        // 外部文件
        var externalPath = Path.Combine(ModBase.exePath, "PCL", "hints.txt");
        if (File.Exists(externalPath))
        {
            try
            {
                lines = File.ReadAllLines(externalPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l.Trim())
                    .ToArray();
            }
            catch
            {
                ModBase.Log(
                    Lang.Text("Launch.Homepage.Error.ExternalFile", externalPath),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Homepage.Error.ExternalFile", externalPath));
            }
        }

        // 嵌入式资源
        if (lines is null || lines.Length == 0)
        {
            var langCode = LocalizationService.CurrentLanguage.Code;
            lines = _LoadEmbeddedHints(langCode)
                ?? _LoadEmbeddedHints(LocalizationService.DefaultLanguageCode);
        }

        if (lines is not { Length: > 0 }) return "";
        // 长度限制
        if (enableLengthLimit)
        {
            var shortLines = lines.Where(l => l.Length < 50).ToArray();
            if (shortLines.Length > 0) lines = shortLines;
        }

        // 随机返回
        var hint = lines[Random.Shared.Next(lines.Length)];
        return raw ? hint : hint.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private static string[]? _LoadEmbeddedHints(string langCode)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Plain Craft Launcher 2;component/Resources/hints/{langCode}.txt", UriKind.Absolute);
            using var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd()
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToArray();
        }
        catch
        {
            return null;
        }
    }
}
