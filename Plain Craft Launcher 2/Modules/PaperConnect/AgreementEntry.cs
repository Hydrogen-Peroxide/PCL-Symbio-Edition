using System.Text.Json.Serialization;

namespace PaperConnect.Core.Entry;

public class AgreementEntry
{
    public class PingRequest
    {
        [JsonPropertyName("time")] public long Time { get; set; }
    }

    public class PingResponse
    {
        [JsonPropertyName("time")] public long Time { get; set; }
        [JsonPropertyName("returnTime")] public long ReturnTime { get; set; }
        [JsonPropertyName("gameType")] public string GameType { get; set; } = "MinecraftBedrock";
        [JsonPropertyName("gameProtocolType")] public string GameProtocolType { get; set; } = "UDP";
        [JsonPropertyName("gamePort")] public int GamePort { get; set; }
    }

    public class PlayerRequest
    {
        [JsonPropertyName("clientId")] public string? ClientId { get; set; }
        [JsonPropertyName("playerName")] public string? PlayerName { get; set; }
    }

    public class PlayerResponse
    {
        [JsonPropertyName("returnTime")] public long ReturnTime { get; set; }
        [JsonPropertyName("players")] public List<PlayerEntry> Players { get; set; } = new();
    }

    public class PlayerEntry
    {
        [JsonPropertyName("player")] public string PlayerName { get; set; } = "";
        [JsonPropertyName("clientId")] public string ClientId { get; set; } = "";
        [JsonPropertyName("isRoomHost")] public bool IsRoomHost { get; set; }
        public DateTime LastHeartbeat { get; set; }
    }
}
