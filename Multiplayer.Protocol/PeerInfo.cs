using System.Text.Json.Serialization;

namespace PaperConnect.Core.Entry.Easytier;

public class PeerInfo
{
    [JsonPropertyName("cidr")] public string Cidr { get; set; } = string.Empty;
    [JsonPropertyName("ipv4")] public string Ipv4 { get; set; } = string.Empty;
    [JsonPropertyName("hostname")] public string Hostname { get; set; } = string.Empty;
    [JsonPropertyName("cost")] public string Cost { get; set; } = string.Empty;
    [JsonPropertyName("lat_ms")] public string LatMs { get; set; } = string.Empty;
    [JsonPropertyName("loss_rate")] public string LossRate { get; set; } = string.Empty;
    [JsonPropertyName("rx_bytes")] public string RxBytes { get; set; } = string.Empty;
    [JsonPropertyName("tx_bytes")] public string TxBytes { get; set; } = string.Empty;
    [JsonPropertyName("tunnel_proto")] public string TunnelProto { get; set; } = string.Empty;
    [JsonPropertyName("nat_type")] public string NatType { get; set; } = string.Empty;
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
}
