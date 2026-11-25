using System.IO;
using System.Security;
using System.Text.Json;

namespace PCL;

internal sealed record HelpContent(string SourcePath, string Title, string Xaml);

internal static class HelpDocuments
{
    /// <summary>
    ///     从帮助 JSON 及同名 XAML 文件读取详情页数据。失败会抛出异常。
    /// </summary>
    internal static HelpContent LoadContent(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("未找到帮助 JSON 文件", jsonPath);

        var json = ModMain.ArgumentReplace(File.ReadAllText(jsonPath), SecurityElement.Escape);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Title", out var titleElement) ||
            titleElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(titleElement.GetString()))
            throw new ArgumentException("帮助 JSON 中未找到有效的 Title 项", nameof(jsonPath));

        var xamlPath = Path.ChangeExtension(jsonPath, ".xaml");
        if (!File.Exists(xamlPath))
            throw new FileNotFoundException("未找到帮助 JSON 对应的 XAML 文件", xamlPath);

        var xaml = File.ReadAllText(xamlPath);
        if (string.IsNullOrWhiteSpace(xaml))
            throw new InvalidDataException("帮助 XAML 文件为空");

        return new HelpContent(jsonPath, titleElement.GetString()!, xaml);
    }
}
