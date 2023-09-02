using SteamKit2;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamAccountDataFetcher.SteamDataClient;

internal class SteamWebClient: IDisposable
{
    private const string STEAM_USER_AUTH_INTERFACE = "ISteamUserAuth";
    private const string STEAM_COMMUNITY_SERVICE_INTERFACE = "ICommunityService";

    private const string STEAM_AUTH_USER_METHOD = "AuthenticateUser";
    private const string STEAM_GET_APPS_METHOD = "GetApps";

    private static readonly Uri API_KEY_URI = new("https://steamcommunity.com/dev/apikey");
    private static readonly Uri REGISTER_API_KEY_URI = new("https://steamcommunity.com/dev/registerkey");
    private const string API_GROUP_KEY = "apiKey";
    private static readonly Regex API_KEY_EXPRESSION = new($@"<p>.*:*.(?'{API_GROUP_KEY}'[0-9A-F]{{32}})</p>", RegexOptions.Compiled);

    private HttpClient? _httpClient;
    private SteamDataClient _steamDataClient;
    private string _webAPIKey = string.Empty;
    private SemaphoreSlim _webAPISemaphore = new(1);
    internal string SessionID { get; private set; } = string.Empty;

    private static DateTime _lastRequestTime = DateTime.MinValue;

    internal SteamWebClient(SteamDataClient steamDataClient)
    {
        _steamDataClient = steamDataClient;
    }

    internal async Task<(bool, string)> GetWebApiKeyAsync()
    {
        if (_httpClient == null)
        {
            _steamDataClient.Log($"{nameof(_httpClient)} is not initialized.", Logger.Level.Error);
            return (false, string.Empty);
        }

        await RunOrSleep();

        await _webAPISemaphore.WaitAsync();

        if (!string.IsNullOrEmpty(_webAPIKey))
        {
            _webAPISemaphore.Release();
            return (true, _webAPIKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(API_KEY_URI);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            _steamDataClient.Log($"Unable to load API page with status code {e.StatusCode}.", Logger.Level.Error);
            _webAPISemaphore.Release();
            return (false, string.Empty);
        }
        using (var responseStream = await response.Content.ReadAsStreamAsync())
        {
            var cleanHtml = CleanHtml(responseStream);
            if (string.IsNullOrEmpty(cleanHtml))
            {
                _steamDataClient.Log($"{nameof(responseStream)} is empty.", Logger.Level.Error);
                _webAPISemaphore.Release();
                return (false, string.Empty);
            }

            var webApiKey = MatchApiKey(cleanHtml);
            if (string.IsNullOrEmpty(webApiKey))
            {
                _steamDataClient.Log("API key is missing. Generating.");
                await Task.Delay(Configuration.DefaultWebRequestTimeout);
                (bool success, webApiKey) = await RegisterWebApiKeyAsync();
                
                if (success)
                    _webAPIKey = webApiKey;

                _webAPISemaphore.Release();
                return (success, _webAPIKey);
            }
            else
            {
                _webAPIKey = webApiKey;
                _webAPISemaphore.Release();
                return (true, _webAPIKey);
            }
        }
    }

    internal async Task<(bool, IReadOnlyDictionary<uint, string>?)> GetAppNamesAsync(uint[] apps)
    {
        if (apps.Length == 0)
        {
            var msg = $"{nameof(apps)} is invalid.";
            _steamDataClient.Log(msg, Logger.Level.Error);
            throw new ArgumentOutOfRangeException(msg);
        }

        await RunOrSleep();

        Dictionary<string, object?> postData = new(apps.Length, StringComparer.Ordinal);
        for (int i = 0; i < apps.Length; i++)
            postData.Add($"appids[{i}]", apps[i]);

        KeyValue response;
        using (var steamUserAuthInterface = _steamDataClient.SteamConfiguration.GetAsyncWebAPIInterface(STEAM_COMMUNITY_SERVICE_INTERFACE))
        {
            response = await steamUserAuthInterface.CallAsync(HttpMethod.Get, STEAM_GET_APPS_METHOD, args: postData);
            if (response == null || response.Children.Count == 0)
            {
                _steamDataClient.Log("Unable to get information about Steam Apps.", Logger.Level.Error);
                return (false, null);
            }
        }

        Dictionary<uint, string> appNames = new();
        var responseApps = response.Children[0].Children;
        foreach (var responseApp in responseApps)
        {
            var appId = responseApp["appid"].AsUnsignedInteger();
            var appName = responseApp["name"].AsString() ?? string.Empty;

            appNames.Add(appId, appName);
        }

        return (true, appNames);
    }

    private async Task<(bool, string)> RegisterWebApiKeyAsync()
    {
        string result = string.Empty;

        if (_httpClient == null)
        {
            _steamDataClient.Log($"{nameof(_httpClient)} is not initialized.", Logger.Level.Error);
            return (false, result);
        }

        if (string.IsNullOrEmpty(SessionID))
        {
            _steamDataClient.Log($"{nameof(SessionID)} is null.", Logger.Level.Error);
            return (false, result);
        }

        await RunOrSleep();

        var postData = new FormUrlEncodedContent(new Dictionary<string, string>()
        {
            { "domain", Configuration.ApiDomain },
            { "agreeToTerms", "agreed" },
            { "sessionid", SessionID }
        });
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(REGISTER_API_KEY_URI, postData);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            _steamDataClient.Log($"Unable to load API page with status code {e.StatusCode}.", Logger.Level.Error);
            return (false, result);
        }

        using (var responseStream = await response.Content.ReadAsStreamAsync())
        {
            var cleanHtml = CleanHtml(responseStream);
            if (string.IsNullOrEmpty(cleanHtml))
            {
                _steamDataClient.Log($"{nameof(responseStream)} is empty.", Logger.Level.Error);
                return (false, result);
            }    

            result = MatchApiKey(cleanHtml);
            if (string.IsNullOrEmpty(result))
            {
                _steamDataClient.Log("Unable to generate API key.", Logger.Level.Error);
                return (false, result);
            }

            return (true, result);
        }
    }

    private string CleanHtml(Stream htmlContent)
    {
        StringBuilder clearHtml = new();
        using (StreamReader reader = new(htmlContent))
        {
            string? line;
            do
            {
                line = reader.ReadLine();
                if (string.IsNullOrEmpty(line))
                    continue;
                clearHtml.Append(line.Trim(' ', '\n', '\r', '\t'));
            } while (!reader.EndOfStream);
            return clearHtml.ToString();
        }
    }

    private string MatchApiKey(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var match = API_KEY_EXPRESSION.Match(html);
        if (match == null || !match.Success || !match.Groups.ContainsKey(API_GROUP_KEY))
            return string.Empty;

        var apiKey = match.Groups[API_GROUP_KEY].Value;
        return apiKey;
    }

    internal async Task<bool> InitAsync(string webApiUserNonce)
    {
        if (_steamDataClient.SteamID == null || !_steamDataClient.SteamID.IsValid ||  !_steamDataClient.SteamID.IsIndividualAccount)
        {
            _steamDataClient.Log($"{nameof(_steamDataClient.SteamID)} is invalid.", Logger.Level.Error);
            return false;
        }

        if (string.IsNullOrEmpty(webApiUserNonce))
        {
            _steamDataClient.Log($"{nameof(webApiUserNonce)} is empty.", Logger.Level.Error);
            return false;
        }

        await RunOrSleep();

        var publicKey = KeyDictionary.GetPublicKey(EUniverse.Public);
        if (publicKey == null || publicKey.Length == 0)
        {
            _steamDataClient.Log($"{nameof(KeyDictionary)} is empty.", Logger.Level.Error);
            return false;
        }

        var sessionKey = CryptoHelper.GenerateRandomBlock(32);
        byte[] encryptedSessionKey;

        using (RSACrypto rsa = new(publicKey))
            encryptedSessionKey = rsa.Encrypt(sessionKey);

        var loginKey = Encoding.UTF8.GetBytes(webApiUserNonce);
        var encryptedLoginKey = CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

        Dictionary<string, object?> postData = new(3, StringComparer.Ordinal)
        {
            { "encrypted_loginkey", encryptedLoginKey },
            { "sessionkey", encryptedSessionKey },
            { "steamid", _steamDataClient.SteamID.ConvertToUInt64() }
        };

        KeyValue response;
        using (var steamUserAuthInterface = _steamDataClient.SteamConfiguration.GetAsyncWebAPIInterface(STEAM_USER_AUTH_INTERFACE))
        {
            response = await steamUserAuthInterface.CallAsync(HttpMethod.Post, STEAM_AUTH_USER_METHOD, args: postData);
            if (response == null)
            {
                _steamDataClient.Log("Unable to authenticate user to web.", Logger.Level.Error);
                return false;
            }
        }

        string? steamLogin = response["token"].AsString();
        if (string.IsNullOrWhiteSpace(steamLogin))
        {
            _steamDataClient.Log($"{nameof(steamLogin)} is empty.", Logger.Level.Error);
            return false;
        }

        string? steamLoginSecure = response["tokensecure"].AsString();
        if (string.IsNullOrWhiteSpace(steamLoginSecure))
        {
            _steamDataClient.Log($"{nameof(steamLoginSecure)} is empty.", Logger.Level.Error);
            return false;
        }

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
            new Cookie("steamLogin", steamLogin, "/", ".checkout.steampowered.com"),
            new Cookie("steamLogin", steamLogin, "/", ".steamcommunity.com"),
            new Cookie("steamLogin", steamLogin, "/", ".help.steampowered.com"),
            new Cookie("steamLogin", steamLogin, "/", ".store.steampowered.com"),
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

        return true;
    }

    private async Task RunOrSleep()
    {
        while (DateTime.Now - _lastRequestTime < Configuration.DefaultWebRequestTimeout)
            await Task.Delay(Configuration.DefaultWebRequestTimeout);

        _lastRequestTime = DateTime.Now;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _webAPISemaphore.Dispose();
    }
}
