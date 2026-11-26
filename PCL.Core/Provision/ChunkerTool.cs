using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Provision;

/// <summary>存档相关功能的可用状态。UI 只按这个枚举渲染，不再各处硬编码文案。</summary>
public enum SaveFeatureState
{
    /// <summary>工具就绪，可直接用。</summary>
    Ready = 0,

    /// <summary>外部工具未就绪，点一下会自动下载。</summary>
    ToolMissing = 1,

    /// <summary>下载中，按钮显示进度。</summary>
    ToolDownloading = 2,

    /// <summary>下载或校验失败，需要人介入。</summary>
    ToolFailed = 3,

    /// <summary>格式本身不兼容（跟工具无关）。</summary>
    FormatUnsupported = 4,

    /// <summary>当前平台能力限制。</summary>
    PlatformLimited = 5
}

/// <summary>
///     Chunker（https://github.com/HiveGamesOSS/Chunker）的版本解析。
///     只负责"查最新 CLI jar 的地址"，下载/校验/落盘全部交给 <see cref="ToolProvisionService" />。
/// </summary>
public static class ChunkerTool
{
    public const string Id = "Chunker";

    private const string LatestApi = "https://api.github.com/repos/HiveGamesOSS/Chunker/releases/latest";

    public static async Task<ToolDefinition> ResolveLatestAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Symbio-Edition");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var json = await http.GetStringAsync(LatestApi, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "latest" : "latest";

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!name.StartsWith("chunker-cli-", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;

            var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(url)) continue;
            return new ToolDefinition(Id, tag, new Uri(url), null, name);
        }

        throw new InvalidOperationException("Chunker 的最新发行里没有找到 CLI jar");
    }
}
