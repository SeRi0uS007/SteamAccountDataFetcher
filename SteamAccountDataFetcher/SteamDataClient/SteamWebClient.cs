using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace SteamAccountDataFetcher.SteamDataClient;

internal class SteamWebClient: IDisposable
{
    private static readonly Uri API_KEY_URI = new("https://steamcommunity.com/dev/apikey");
    private static readonly Uri REGISTER_API_KEY_URI = new("https://steamcommunity.com/dev/registerkey");
    private const string API_GROUP_KEY = "apiKey";
    private static readonly Regex API_KEY_EXPRESSION = new($@"<p>.*:*.(?'{API_GROUP_KEY}'[0-9A-F]{{32}})</p>", RegexOptions.Compiled);

    private HttpClient? _httpClient;
    private Client _steamClient;
    private string _webAPIKey = string.Empty;
    private SemaphoreSlim _webAPISemaphore = new(1);
    internal string SessionID { get; private set; } = string.Empty;

    private static DateTime _lastRequestTime = DateTime.MinValue;

    internal SteamWebClient(Client steamClient)
    {
        _steamClient = steamClient;
    }

    internal async Task<(bool, string)> GetWebApiKeyAsync()
    {
        if (_httpClient == null)
        {
            _steamClient.Log($"{nameof(_httpClient)} is not initialized.", Logger.Level.Error);
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
            _steamClient.Log($"Unable to load API page with status code {e.StatusCode}.", Logger.Level.Error);
            _webAPISemaphore.Release();
            return (false, string.Empty);
        }
        using (var responseStream = await response.Content.ReadAsStreamAsync())
        {
            var cleanHtml = CleanHtml(responseStream);
            if (string.IsNullOrEmpty(cleanHtml))
            {
                _steamClient.Log($"{nameof(responseStream)} is empty.", Logger.Level.Error);
                _webAPISemaphore.Release();
                return (false, string.Empty);
            }

            var webApiKey = MatchApiKey(cleanHtml);
            if (string.IsNullOrEmpty(webApiKey))
            {
                _steamClient.Log("API key is missing. Generating.");
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

    private async Task<(bool, string)> RegisterWebApiKeyAsync()
    {
        string result = string.Empty;

        if (_httpClient == null)
        {
            _steamClient.Log($"{nameof(_httpClient)} is not initialized.", Logger.Level.Error);
            return (false, result);
        }

        if (string.IsNullOrEmpty(SessionID))
        {
            _steamClient.Log($"{nameof(SessionID)} is null.", Logger.Level.Error);
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
            _steamClient.Log($"Unable to load API page with status code {e.StatusCode}.", Logger.Level.Error);
            return (false, result);
        }

        using (var responseStream = await response.Content.ReadAsStreamAsync())
        {
            var cleanHtml = CleanHtml(responseStream);
            if (string.IsNullOrEmpty(cleanHtml))
            {
                _steamClient.Log($"{nameof(responseStream)} is empty.", Logger.Level.Error);
                return (false, result);
            }    

            result = MatchApiKey(cleanHtml);
            if (string.IsNullOrEmpty(result))
            {
                _steamClient.Log("Unable to generate API key.", Logger.Level.Error);
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
