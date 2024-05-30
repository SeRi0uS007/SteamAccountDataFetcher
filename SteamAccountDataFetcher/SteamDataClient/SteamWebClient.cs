using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace SteamAccountDataFetcher.SteamDataClient;

class SteamWebClient: IDisposable
{
    static readonly Uri API_KEY_URI = new("https://steamcommunity.com/dev/apikey");
    static readonly Uri REGISTER_API_KEY_URI = new("https://steamcommunity.com/dev/registerkey");
    const string API_GROUP_KEY = "apiKey";
    static readonly Regex API_KEY_EXPRESSION = new($@"<p>.*:*.(?'{API_GROUP_KEY}'[0-9A-F]{{32}})</p>", RegexOptions.Compiled);

    HttpClient? _httpClient;
    Client _steamClient;

    internal string SessionID { get; private set; } = string.Empty;

    static bool _isFirstRequestBefore = false;

    internal SteamWebClient(Client steamClient)
    {
        _steamClient = steamClient;
    }

    internal void InitAsync()
    {
        if (_steamClient.SteamID == null || !_steamClient.SteamID.IsValid ||  !_steamClient.SteamID.IsIndividualAccount)
        {
            var msg = $"{nameof(_steamClient.SteamID)} is invalid.";
            _steamClient.Log(msg, Logger.Level.Error);
            throw new InvalidOperationException(msg);
        }

        if (string.IsNullOrEmpty(_steamClient.AccessToken))
        {
            var msg = $"{nameof(_steamClient.AccessToken)} is empty.";
            _steamClient.Log(msg, Logger.Level.Error);
            throw new InvalidOperationException(msg);
        }

        string steamLoginSecure = HttpUtility.UrlEncode($"{_steamClient.SteamID.ConvertToUInt64()}||{_steamClient.AccessToken}");

        Random rnd = new Random();
        byte[] sessionBytes = new byte[12];
        rnd.NextBytes(sessionBytes);
        SessionID = Convert.ToHexString(sessionBytes);

        string timeZoneOffset = $"{(int)DateTimeOffset.Now.Offset.TotalSeconds}{Uri.EscapeDataString(",")}0";
        CookieCollection cookieCollection = new()
        {
            new Cookie("sessionid", SessionID, "/", ".checkout.steampowered.com"),
            new Cookie("sessionid", SessionID, "/", ".steamcommunity.com"),
            new Cookie("sessionid", SessionID, "/", ".help.steampowered.com"),
            new Cookie("sessionid", SessionID, "/", ".store.steampowered.com"),
            new Cookie("steamLoginSecure", steamLoginSecure, "/", ".checkout.steampowered.com"),
            new Cookie("steamLoginSecure", steamLoginSecure, "/", ".steamcommunity.com"),
            new Cookie("steamLoginSecure", steamLoginSecure, "/", ".help.steampowered.com"),
            new Cookie("steamLoginSecure", steamLoginSecure, "/", ".store.steampowered.com"),
            new Cookie("timezoneOffset", timeZoneOffset, "/", ".checkout.steampowered.com"),
            new Cookie("timezoneOffset", timeZoneOffset, "/", ".steamcommunity.com"),
            new Cookie("timezoneOffset", timeZoneOffset, "/", ".help.steampowered.com"),
            new Cookie("timezoneOffset", timeZoneOffset, "/", ".store.steampowered.com"),
        };
        HttpClientHandler httpClientHandler = new();
        httpClientHandler.UseCookies = true;
        httpClientHandler.CookieContainer.Add(cookieCollection);

        _httpClient = new HttpClient(httpClientHandler, true);

        return;
    }

    async Task RunOrSleep()
    {
        if (_isFirstRequestBefore)
        {
            await Task.Delay(Configuration.DefaultWebRequestTimeout);
            return;
        }
        _isFirstRequestBefore = true;
    }

    public void Dispose() =>
        _httpClient?.Dispose();
}
