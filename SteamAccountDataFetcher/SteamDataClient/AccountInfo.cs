namespace SteamAccountDataFetcher.SteamDataClient;

public class AccountInfo
{
    public class PackageInfo
    {
        public uint PackageId { get; set; } = 0;
        public long RegistrationTime { get; set; } = 0;
    }
    public string Username { get; set; } = string.Empty;
    public ulong SteamId { get; set; } = 0;
    public List<PackageInfo> Packages { get; set; } = new();
    public bool IsLimited { get; set; }
    public bool IsBanned { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}
