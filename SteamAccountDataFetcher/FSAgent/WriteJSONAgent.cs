using System.Collections;
using System.Text.Json;
using SteamAccountDataFetcher.SteamDataClient;

namespace SteamAccountDataFetcher.FSAgent;

internal class WriteJSONAgent
{
    public class OutMapper
    {
        public List<PackageInfo> PackagesInfo { get; set; } = new();
        public List<AccountInfo> AccountsInfo { get; set; } = new();
    }

    private string FilePath { get; init; }

    private OutMapper _outMapper;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    internal List<PackageInfo> PackageInfos { get => _outMapper.PackagesInfo; }
    internal List<AccountInfo> AccountsInfo { get => _outMapper.AccountsInfo; }

    internal WriteJSONAgent(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            var msg = $"{nameof(path)} is empty.";
            Logger.Log(msg, Logger.Level.Error);
            throw new ArgumentException(msg);
        }

        FilePath = path;

        if (!Path.Exists(path))
        {
            _outMapper = new();
            return;
        }

        try
        {
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                _outMapper = JsonSerializer.Deserialize<OutMapper?>(file) ?? new();
                return;
            }
        } catch (JsonException)
        {
            _outMapper = new();
            return;
        }
        
    }

    public void WriteCache()
    {
        using (FileStream file = new FileStream(FilePath, FileMode.Create, FileAccess.Write))
            JsonSerializer.Serialize(file, _outMapper, _jsonSerializerOptions);
    }

    public void AddAccount(AccountInfo item)
    {
        _outMapper.AccountsInfo.Add(item);
        WriteCache();
    }
}
