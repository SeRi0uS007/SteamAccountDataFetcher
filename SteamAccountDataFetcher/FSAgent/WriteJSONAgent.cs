using System.Collections;
using System.Text.Json;

namespace SteamAccountDataFetcher.FSAgent;

internal class WriteJSONAgent : IList<SteamDataClient.SteamDataClient.ResponseData>
{
    private string FilePath { get; init; }

    private List<SteamDataClient.SteamDataClient.ResponseData> _accountDataList;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

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
            _accountDataList = new();
            return;
        }

        try
        {
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var cachedType = JsonSerializer.Deserialize<List<SteamDataClient.SteamDataClient.ResponseData>?>(file);
                if (cachedType == null)
                {
                    _accountDataList = new();
                    return;
                }
                _accountDataList = cachedType;
                return;
            }
        } catch (JsonException)
        {
            _accountDataList = new();
            return;
        }
        
    }

    private void WriteCache()
    {
        using (FileStream file = new FileStream(FilePath, FileMode.Create, FileAccess.Write))
            JsonSerializer.Serialize(file, _accountDataList, _jsonSerializerOptions);
    }

    public SteamDataClient.SteamDataClient.ResponseData this[int index]
    { 
        get => _accountDataList[index];
        set => _accountDataList[index] = value; 
    }

    public int Count => _accountDataList.Count;

    public bool IsReadOnly => 
        ((IList<SteamDataClient.SteamDataClient.ResponseData>)_accountDataList).IsReadOnly;

    public int IndexOf(SteamDataClient.SteamDataClient.ResponseData item) => _accountDataList.IndexOf(item);

    public void Insert(int index, SteamDataClient.SteamDataClient.ResponseData item)
    {
        _accountDataList.Insert(index, item);
        WriteCache();
    }

    public void RemoveAt(int index)
    {
        _accountDataList.RemoveAt(index);
        WriteCache();
    }

    public void Add(SteamDataClient.SteamDataClient.ResponseData item)
    {
        _accountDataList.Add(item);
        WriteCache();
    }

    public void Clear()
    {
        _accountDataList.Clear();
        WriteCache();
    }

    public bool Contains(SteamDataClient.SteamDataClient.ResponseData item) => _accountDataList.Contains(item);

    public void CopyTo(SteamDataClient.SteamDataClient.ResponseData[] array, int arrayIndex) => _accountDataList.CopyTo(array, arrayIndex);

    public bool Remove(SteamDataClient.SteamDataClient.ResponseData item)
    {
        var success = _accountDataList.Remove(item);
        if (success)
            WriteCache();

        return success;
    }

    public IEnumerator<SteamDataClient.SteamDataClient.ResponseData> GetEnumerator() => _accountDataList.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
