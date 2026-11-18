using System.Text;

namespace PCL.Network;

public struct RequestParam
{
    public Encoding? Encoding { get; set; }
    public string? Accept { get; set; }
    public string? FallbackUrl { get; set; }

    /// <summary>
    ///     附加请求头，用于需要鉴权的接口（如 CNB 的 Authorization）。
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }
    public bool UseBrowserUserAgent { get; set; }
    public int Timeout { get; set; }
    public int Retries { get; set; }

    public static RequestParam WithRetry => new()
    {
        Timeout = 30000,
        Retries = 3
    };
}
