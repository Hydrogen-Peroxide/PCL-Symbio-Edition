using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Bedrock;

/// <summary>
/// 基岩版构建信息（来自版本清单）。
/// </summary>
public class BedrockBuildInfo
{
    [JsonPropertyName("ID")]
    public string ID { get; set; } = string.Empty;

    [JsonPropertyName("Title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("Type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("BuildType")]
    public string BuildType { get; set; } = string.Empty;

    [JsonPropertyName("Variations")]
    public List<BedrockVariation> Variations { get; set; } = [];

    /// <summary>
    /// 是否为 UWP 版本（需要开发者模式）。
    /// </summary>
    [JsonIgnore]
    public bool IsUwp => BuildType == "UWP";

    /// <summary>
    /// 是否为 GDK 版本（可直接运行 exe）。
    /// </summary>
    [JsonIgnore]
    public bool IsGdk => BuildType == "GDK";
}

/// <summary>
/// 基岩版构建变体（包含不同架构的下载信息）。
/// </summary>
public class BedrockVariation
{
    [JsonPropertyName("MD5")]
    public string MD5 { get; set; } = string.Empty;

    [JsonPropertyName("FileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("Architecture")]
    public string Architecture { get; set; } = "x64";
}