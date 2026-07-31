using System;
using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Bedrock;

/// <summary>
/// 基岩版实例配置。
/// </summary>
public class BedrockInstance
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("buildType")]
    public string BuildType { get; set; } = "UWP";

    [JsonPropertyName("versionName")]
    public string VersionName { get; set; } = string.Empty;

    [JsonPropertyName("versionType")]
    public string VersionType { get; set; } = "Release";

    [JsonPropertyName("installPath")]
    public string InstallPath { get; set; } = string.Empty;

    [JsonPropertyName("isVersionIsolated")]
    public bool IsVersionIsolated { get; set; } = true;

    [JsonPropertyName("otherCommand")]
    public string OtherCommand { get; set; } = string.Empty;

    [JsonPropertyName("totalPlayTime")]
    public long TotalPlayTime { get; set; }

    [JsonPropertyName("lastPlayTime")]
    public DateTime? LastPlayTime { get; set; }

    [JsonPropertyName("totalSessions")]
    public int TotalSessions { get; set; }

    [JsonPropertyName("firstPlayTime")]
    public DateTime? FirstPlayTime { get; set; }

    [JsonIgnore]
    public bool IsUwp => BuildType == "UWP";

    [JsonIgnore]
    public bool IsGdk => BuildType == "GDK";

    /// <summary>
    /// 获取游戏可执行文件路径（仅 GDK 版本）。
    /// </summary>
    [JsonIgnore]
    public string? ExecutablePath => IsGdk
        ? System.IO.Path.Combine(InstallPath, "Minecraft.Windows.exe")
        : null;
}