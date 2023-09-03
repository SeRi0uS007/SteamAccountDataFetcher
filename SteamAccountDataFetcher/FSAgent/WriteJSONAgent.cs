using System.Collections;
using System.Text.Json;
using SteamAccountDataFetcher.SteamDataClient;

namespace SteamAccountDataFetcher.FSAgent;

internal class WriteJSONAgent : IList<ResponseData>
{
    private string FilePath { get; init; }

    private List<ResponseData> _accountDataList;
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
                var cachedType = JsonSerializer.Deserialize<List<ResponseData>?>(file);
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

    public ResponseData this[int index]
    { 
        get => _accountDataList[index];
        set => _accountDataList[index] = value; 
    }

    public int Count => _accountDataList.Count;

    public bool IsReadOnly => 
        ((IList<ResponseData>)_accountDataList).IsReadOnly;

    public int IndexOf(ResponseData item) => _accountDataList.IndexOf(item);

    public void Insert(int index, ResponseData item)
    {
        _accountDataList.Insert(index, item);
        WriteCache();
    }

    public void RemoveAt(int index)
    {
        _accountDataList.RemoveAt(index);
        WriteCache();
    }

    public void Add(ResponseData item)
    {
        _accountDataList.Add(item);
        WriteCache();
    }

    public void Clear()
    {
        _accountDataList.Clear();
        WriteCache();
    }

    public bool Contains(ResponseData item) => _accountDataList.Contains(item);

    public void CopyTo(ResponseData[] array, int arrayIndex) => _accountDataList.CopyTo(array, arrayIndex);

    public bool Remove(ResponseData item)
    {
        var success = _accountDataList.Remove(item);
        if (success)
            WriteCache();

        return success;
    }

    public IEnumerator<ResponseData> GetEnumerator() => _accountDataList.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
