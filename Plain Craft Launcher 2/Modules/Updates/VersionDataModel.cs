using System.Text.Json.Serialization;

namespace PCL;

public class VersionDataModel
{
    [JsonIgnore]
    public UpdateDownloadSnapshot? DownloadSnapshot { get; init; }
    public string Changelog { get; set; }
    public string Sha256 { get; set; }
    public string Source { get; set; }
    public int VersionCode { get; set; }
    public string VersionName { get; set; }
}
