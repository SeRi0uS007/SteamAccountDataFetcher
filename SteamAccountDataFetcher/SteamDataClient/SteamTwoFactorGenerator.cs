using SteamKit2;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamAccountDataFetcher.SteamDataClient;

internal class SteamTwoFactorGenerator
{
    private const string STEAM_TWO_FACTOR_SERVICE_INTERFACE = "ITwoFactorService";
    private const string STEAM_QUERY_TIME_METHOD = "QueryTime";

    private static byte[] STEAM_GUARD_CODE_TRANSLATIONS = new byte[] { 50, 51, 52, 53, 54, 55, 56, 57, 66, 67, 68, 70, 71, 72, 74, 75, 77, 78, 80, 81, 82, 84, 86, 87, 88, 89 };

    private static int _timeDiffirence = 0;
    private static bool _aligned = false;

    private Client _steamClient;

    internal SteamTwoFactorGenerator(Client steamClient)
    {
        _steamClient = steamClient;
    }

    internal async Task<string> GenerateSteamGuardCodeAsync(string sharedSecret)
    {
        if (string.IsNullOrWhiteSpace(sharedSecret))
            return string.Empty;

        long time = await GetSteamTimeAsync();

        string sharedSecretUnescaped = Regex.Unescape(sharedSecret);
        byte[] sharedSecretArray = Convert.FromBase64String(sharedSecretUnescaped);
        byte[] timeArray = new byte[8];

        time /= 30L;

        for (int i = 8; i > 0; i--)
        {
            timeArray[i - 1] = (byte)time;
            time >>= 8;
        }

        HMACSHA1 hmacGenerator = new HMACSHA1();
        hmacGenerator.Key = sharedSecretArray;
        byte[] hashedData = hmacGenerator.ComputeHash(timeArray);
        byte[] codeArray = new byte[5];
        byte b = (byte)(hashedData[19] & 0xF);
        int codePoint = (hashedData[b] & 0x7F) << 24 | (hashedData[b + 1] & 0xFF) << 16 | (hashedData[b + 2] & 0xFF) << 8 | hashedData[b + 3] & 0xFF;

        for (int i = 0; i < 5; ++i)
        {
            codeArray[i] = STEAM_GUARD_CODE_TRANSLATIONS[codePoint % STEAM_GUARD_CODE_TRANSLATIONS.Length];
            codePoint /= STEAM_GUARD_CODE_TRANSLATIONS.Length;
        }

        return Encoding.UTF8.GetString(codeArray);
    }
    private async Task<long> GetSteamTimeAsync()
    {
        if (!_aligned)
            await AlignTimeAsync();

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _timeDiffirence;
    }
    private async Task AlignTimeAsync()
    {
        long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        KeyValue response;
        using (var steamTwoFactorServiceInterface = _steamClient.SteamConfiguration.GetAsyncWebAPIInterface(STEAM_TWO_FACTOR_SERVICE_INTERFACE))
        {
            response = await steamTwoFactorServiceInterface.CallAsync(HttpMethod.Post, STEAM_QUERY_TIME_METHOD);
            if (response == null)
            {
                _steamClient.Log("Unable to align time.", Logger.Level.Error);
                return;
            }
        }

        var serverTime = response["server_time"].AsLong();
        if (serverTime == 0)
        {
            _steamClient.Log($"{nameof(serverTime)} is invalid.", Logger.Level.Error);
            return;
        }

        _timeDiffirence = (int)(serverTime - currentTime);
        _aligned = true;
        return;
    }
}
