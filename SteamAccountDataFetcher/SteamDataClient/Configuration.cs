using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamAccountDataFetcher.SteamDataClient;

internal static class Configuration
{
    internal static string ReadCSVFilePath { get; } = "SteamAccountsLogin.txt";
    internal static string WriteJSONFilePath { get; } = "SteamAccounts.json";
    internal static string ApiDomain { get; } = "serious-re.com"; //"sadf.localhost";
    internal static TimeSpan DefaultLoginTimeout { get; } = TimeSpan.FromSeconds(30);
    internal static TimeSpan DefaultRateLimitTimeout { get; } = TimeSpan.FromMinutes(30);
    internal static TimeSpan DefaultWebRequestTimeout { get; } = TimeSpan.FromSeconds(3);
}
