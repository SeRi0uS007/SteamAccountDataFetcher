using System.Collections;

namespace SteamAccountDataFetcher.FSAgent;

internal record AccountLoginInfo(string Username, string Password, string SharedSecret);

internal class ReadCSVAgent : IList<AccountLoginInfo>, IDisposable
{
    private const string HEADER = "AccountLogin;AccountPassword;SharedSecret";

    private string FilePath { get; init; }

    private List<AccountLoginInfo> _accountLoginList;

    internal ReadCSVAgent(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            var msg = $"{nameof(path)} is empty.";
            Logger.Log(msg, Logger.Level.Error);
            throw new ArgumentException(msg);
        }

        if (!Path.Exists(path))
        {
            var msg = $"{nameof(path)} file not exists.";
            Logger.Log(msg, Logger.Level.Error);
            throw new ArgumentException(msg);
        }

        _accountLoginList = new();
        FilePath = path;

        using (StreamReader reader = new StreamReader(path))
        {
            if (reader.EndOfStream)
            {
                var msg = $"{nameof(path)} file is empty.";
                Logger.Log(msg, Logger.Level.Error);
                throw new ArgumentException(msg);
            }

            string? header = reader.ReadLine();
            if (string.IsNullOrEmpty(header) || header != HEADER)
            {
                var msg = $"{nameof(path)} file is invalid.";
                Logger.Log(msg, Logger.Level.Error);
                throw new ArgumentException(msg);
            }

            int lineIndex = 1;
            while (!reader.EndOfStream)
            {
                var dataLine = reader.ReadLine();
                if (string.IsNullOrEmpty(dataLine))
                {
                    Logger.Log($"{nameof(path)} file in line {lineIndex} is empty.");
                    ++lineIndex;
                    continue;
                }

                var data = dataLine.Split(';');
                if (data == null ||  data.Length != 3)
                {
                    Logger.Log($"{nameof(path)} file in line {lineIndex} is invalid.");
                    ++lineIndex;
                    continue;
                }

                var username = data[0];
                var password = data[1];
                var sharedSecret = data[2];

                if (string.IsNullOrEmpty(username))
                {
                    Logger.Log($"{nameof(path)} file in line {lineIndex} component {nameof(username)} is invalid.");
                    ++lineIndex;
                    continue;
                }
                if (string.IsNullOrEmpty(password))
                {
                    Logger.Log($"{nameof(path)} file in line {lineIndex} component {nameof(password)} is invalid.");
                    ++lineIndex;
                    continue;
                }
                if (string.IsNullOrEmpty(sharedSecret))
                {
                    Logger.Log($"{nameof(path)} file in line {lineIndex} component {nameof(sharedSecret)} is invalid.");
                    ++lineIndex;
                    continue;
                }

                _accountLoginList.Add(new(username, password, sharedSecret));
                ++lineIndex;
            }

            if (Count == 0)
            {
                var msg = $"{nameof(path)} all data lines are invalid.";
                Logger.Log(msg, Logger.Level.Error);
                throw new ArgumentException(msg);
            }
        }
    }

    internal void WriteCache()
    {
        using (StreamWriter writer = new(FilePath, false))
        {
            writer.WriteLine(HEADER);

            foreach(AccountLoginInfo accountInfo in _accountLoginList)
                writer.WriteLine($"{accountInfo.Username};{accountInfo.Password};{accountInfo.SharedSecret}");
        }
    }

    public AccountLoginInfo this[int index] 
    { 
        get => _accountLoginList[index]; 
        set
        {
            _accountLoginList[index] = value;
            WriteCache();
        }
    }

    public int Count => _accountLoginList.Count;

    public bool IsReadOnly => ((IList<AccountLoginInfo>)_accountLoginList).IsReadOnly;

    public void Add(AccountLoginInfo item)
    {
        _accountLoginList.Add(item);
        WriteCache();
    }

    public void Clear()
    {
        _accountLoginList.Clear();
        WriteCache();
    }

    public bool Contains(AccountLoginInfo item) => _accountLoginList.Contains(item);

    public void CopyTo(AccountLoginInfo[] array, int arrayIndex) => _accountLoginList.CopyTo(array, arrayIndex);

    public IEnumerator<AccountLoginInfo> GetEnumerator() => _accountLoginList.GetEnumerator();

    public int IndexOf(AccountLoginInfo item) => _accountLoginList.IndexOf(item);

    public void Insert(int index, AccountLoginInfo item)
    {
        _accountLoginList.Insert(index, item);
        WriteCache();
    }

    public bool Remove(AccountLoginInfo item)
    {
        var success = _accountLoginList.Remove(item);
        if (success)
            WriteCache();

        return success;
    }

    public void RemoveAt(int index)
    {
        _accountLoginList.RemoveAt(index);
        WriteCache();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose() => WriteCache();
}
