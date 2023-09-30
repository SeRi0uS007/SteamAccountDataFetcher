using SteamKit2;

namespace SteamAccountDataFetcher.SteamDataClient;

public static class Extensions
{
    public enum KeyValueType
    {
        KeyValuePair,
        KeyValuePairNullable,
        List,
        Dictionary
    }

    public static KeyValueType GetKeyValueType(this KeyValue kv)
    {
        if (kv.Value != null)
            return KeyValueType.KeyValuePair;

        if (kv.Children.Count == 0)
            return KeyValueType.KeyValuePairNullable;

        for (int i = 0; i < kv.Children.Count; ++i)
        {
            if (!int.TryParse(kv.Children[i].Name, out int kvName))
                return KeyValueType.Dictionary;
            if (kvName != i)
                return KeyValueType.Dictionary;
        }

        return KeyValueType.List;
    }

    public static Dictionary<string, object?> ToDictionary(this KeyValue kv)
    {
        Dictionary<string, object?> dictValue = new();

        foreach(var child in kv.Children)
        {
            var pair = child.ToKeyValuePair();
            if (pair == null)
                continue;

            dictValue.Add(pair.Value.Key, pair.Value.Value);
        }
        return dictValue;
    }

    public static List<object> ToList(this KeyValue kv)
    {
        List<object> list = new(kv.Children.Count);

        foreach (var child in kv.Children)
        {
            if (child.Value == null)
                continue;

            if (long.TryParse(child.Value, out var value))
                list.Add(value);
            else
                list.Add(child.Value);
        }

        return list;
    }

    public static KeyValuePair<string, object?>? ToKeyValuePair(this KeyValue kv)
    {

        // Yes, I know that the response from the network already has a PackageID.
        // But it's easier for me to have a ready-made .NET object.
        if (string.IsNullOrEmpty(kv.Name) || kv.Name == "packageid")
            return null;

        switch (kv.GetKeyValueType())
        {
            case KeyValueType.KeyValuePair:
                if (long.TryParse(kv.Name, out var value))
                    return new(kv.Name, value);
                return new(kv.Name, kv.Value);

            case KeyValueType.KeyValuePairNullable:
                return new(kv.Name, null);

            case KeyValueType.List:
                return new(kv.Name, kv.ToList());

            case KeyValueType.Dictionary:
            default:
                return new(kv.Name, kv.ToDictionary());
        }
    }
}
