namespace PCL;

internal static class BedrockInstanceWorkspace
{
    internal static string? CurrentVersion { get; set; }

    /// <summary>把过长的路径折叠成「头…尾」，保持行高不变。</summary>
    internal static string FoldPath(string? path, int max = 46)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (path.Length <= max) return path;
        var head = max / 2 - 2;
        var tail = max - head - 1;
        return path[..head] + "…" + path[^tail..];
    }
}
