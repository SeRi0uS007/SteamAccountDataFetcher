using System.Text.Json.Serialization;

namespace SteamAccountDataFetcher.SteamDataClient;

public class PackageInfo
{
    [JsonPropertyName("packageid")]
    public uint PackageID { get; set; } = 0;
    [JsonExtensionData]
    public Dictionary<string, object?>? ExtensionData { get; set; }
}
