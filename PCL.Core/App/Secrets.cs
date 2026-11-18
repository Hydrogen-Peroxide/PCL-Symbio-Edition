using PCL.Core.Utils.Exts;
using PCL.Core.Utils.OS;

namespace PCL.Core.App;

// ReSharper disable InconsistentNaming
public static class Secrets
{
    /// <summary>
    /// 微软 OAuth 的 Client ID
    /// </summary>
    public static string MSOAuthClientId { get; } = EnvironmentInterop.GetSecret("MS_CLIENT_ID", readEnvDebugOnly: true).ReplaceNullOrEmpty("d4915fd6-8245-4bd4-a150-396834b705b5");

    /// <summary>
    /// CurseForge API 的 Client ID
    /// </summary>
    public static string CurseForgeAPIKey { get; } = EnvironmentInterop.GetSecret("CURSEFORGE_API_KEY", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// 遥测密钥
    /// </summary>
    public static string TelemetryKey { get; } = EnvironmentInterop.GetSecret("TELEMETRY_KEY", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// 基岩版 GDK Release 版内容密钥（CIK，48 字节）。构建时由 PCL_BEDROCK_RELEASE_CIK 注入，
    /// 以字节数组形式编译进程序集，不以十六进制字符串形式出现
    /// </summary>
    public static byte[]? BedrockReleaseCik { get; } = EnvironmentInterop.GetSecretBytes("BEDROCK_RELEASE_CIK");

    /// <summary>
    /// 基岩版 GDK Preview / Beta 版内容密钥（CIK，48 字节）
    /// </summary>
    public static byte[]? BedrockPreviewCik { get; } = EnvironmentInterop.GetSecretBytes("BEDROCK_PREVIEW_CIK");

    /// <summary>
    /// CNB 访问令牌。CNB 的 Release 列举接口不开放匿名访问，
    /// 构建时由 PCL_CNB_TOKEN 注入（建议使用仅具备读取权限的令牌）。
    /// </summary>
    public static string CnbToken { get; } = EnvironmentInterop.GetSecret("CNB_TOKEN", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// 当前版本的 Git 提交 SHA
    /// </summary>
    public static string CommitHash { get; } = EnvironmentInterop.GetSecret("GITHUB_SHA", readEnvDebugOnly: true).ReplaceNullOrEmpty();
}
