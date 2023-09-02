using System.Runtime.CompilerServices;
using SteamKit2;

namespace SteamAccountDataFetcher.SteamDataClient;

internal class SteamDataClient
{
    private SteamClient _steamClient;
    private SteamTwoFactorGenerator _steamTwoFactorGenerator;
    private SteamWebClient _steamWebClient;
    private CallbackManager _callbackManager;
    private SteamUser _steamUser;
    private SteamApps _steamApps;

    private bool _isRunning = false;
    private bool _isLicensesProcessed = false;

    internal SteamConfiguration SteamConfiguration 
    {
        get => _steamClient.Configuration;
    }
    internal SteamID? SteamID
    {
        get => _steamClient.SteamID;
    }
    internal string Username { get; private set; }
    internal string Password { get; private set; }
    private string SharedSecret { get; set; }

    private static uint _instance = 0;
    private static DateTime _lastConnectionTime = DateTime.MinValue;

    public class ResponseData
    {
        public class AppData
        {
            public uint AppId { get; set; } = 0;
            public long RegistrationTime { get; set; } = 0;
            public string AppName { get; set; } = string.Empty;
        }
        public bool Success
        {
            get => SteamId != 0 && !string.IsNullOrEmpty(ApiKey);
        }
        public string Username { get; set; } = string.Empty;
        public ulong SteamId { get; set; } = 0;
        public List<AppData> Apps { get; set; } = new();
        public bool IsLimited { get; set; }
        public bool IsBanned { get; set; }
        public string ApiKey { get; set; } = string.Empty;
    }
    private ResponseData _responseData;

    internal SteamDataClient(string username, string password, string sharedSecret)
    {
        ++_instance;

        Username = username;
        Password = password;
        SharedSecret = sharedSecret;

        _responseData = new()
        {
            Username = Username
        };

        _steamClient = new();
        _steamClient.AddHandler(new DataFetcher());

        _callbackManager = new(_steamClient);
        var steamUser = _steamClient.GetHandler<SteamUser>();
        if (steamUser == null)
        {
            var msg = $"{nameof(steamUser)} is null.";
            Log(msg, Logger.Level.Error);
            throw new InvalidOperationException(msg);
        }
        _steamUser = steamUser;

        var steamApps = _steamClient.GetHandler<SteamApps>();
        if (steamApps == null)
        {
            var msg = $"{nameof(steamApps)} is null.";
            Log(msg, Logger.Level.Error);
            throw new InvalidOperationException(msg);
        }
        _steamApps = steamApps;

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnectedAsync);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnectedAsync);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOnAsync);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOffAsync);
        _callbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseListAsync);

        _callbackManager.Subscribe<DataFetcher.IsLimitedAccountCallback>(OnIsLimitedAccount);

        _steamTwoFactorGenerator = new(this);
        _steamWebClient = new(this);
    }

    internal async Task<ResponseData> GetResponseDataAsync()
    {
        await RunAsync();
        return _responseData;
    }

    internal async Task RunAsync()
    {
        var secondsBetweenLastConnect = DateTime.Now - _lastConnectionTime;
        if (secondsBetweenLastConnect < Configuration.DefaultLoginTimeout)
        {
            var timeToWait = Configuration.DefaultLoginTimeout - secondsBetweenLastConnect;
            Log($"Last connection was {secondsBetweenLastConnect.Seconds} seconds ago, wait {timeToWait.Seconds} seconds.");
            await Task.Delay(timeToWait);
        }

        _isRunning = true;
        _steamClient.Connect();

        while (_isRunning)
        {
            _callbackManager.RunCallbacks();

            if (_responseData.Success && _isLicensesProcessed)
                _steamClient.Disconnect();
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private async Task LoginAsync()
    {
        string twoFactorCode = await _steamTwoFactorGenerator.GenerateSteamGuardCodeAsync(SharedSecret);
        _steamUser.LogOn(new()
        {
            Username = Username,
            Password = Password,
            TwoFactorCode = twoFactorCode
        });
    }

    private async void OnConnectedAsync(SteamClient.ConnectedCallback callback)
    {
        _lastConnectionTime = DateTime.Now;

        if (_steamClient.CurrentEndPoint == null)
        {
            Log($"{nameof(_steamClient.CurrentEndPoint)} is null.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }

        string? ipAddr = _steamClient.CurrentEndPoint.ToString();
        if (string.IsNullOrEmpty(ipAddr))
        {
            Log($"{nameof(ipAddr)} is null.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }

        Log($"Connected to Steam Server {ipAddr}. Log-in.");
        await LoginAsync();
    }

    private async void OnDisconnectedAsync(SteamClient.DisconnectedCallback callback)
    {
        if (callback.UserInitiated)
        {
            Log("Disconnected from Steam Network by user.");

            if (!_responseData.Success || !_isLicensesProcessed)
            {
                _responseData.SteamId = 0;
                _responseData.Apps.Clear();
                _responseData.IsLimited = false;
                _responseData.IsBanned = false;
                _responseData.ApiKey = string.Empty;
            }

            _isRunning = false;
            return;
        }

        Log("Disconnected from Steam Network.", Logger.Level.Error);
        var secondsBetweenLastConnect = DateTime.Now - _lastConnectionTime;
        if (secondsBetweenLastConnect < Configuration.DefaultLoginTimeout)
        {
            var timeToWait = Configuration.DefaultLoginTimeout - secondsBetweenLastConnect;
            Log($"Last connection was {secondsBetweenLastConnect.Seconds} seconds ago, wait {timeToWait.Seconds} seconds.");
            await Task.Delay(timeToWait);
        }
        _steamClient.Connect();
    }

    private async void OnLoggedOnAsync(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            Log($"Unable to logon with reason {callback.Result}.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }

        if (string.IsNullOrEmpty(callback.WebAPIUserNonce)) 
        {
            Log($"{nameof(callback.WebAPIUserNonce)} is empty.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }

        if (callback.ClientSteamID == null)
        {
            Log($"{nameof(callback.ClientSteamID)} is empty.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }
        _responseData.SteamId = callback.ClientSteamID.ConvertToUInt64();

        Log("Logged into Steam. Log-in into Web Api.");
        var webLoginResult = await _steamWebClient.InitAsync(callback.WebAPIUserNonce);
        if (!webLoginResult)
        {
            Log("Unable to log-in into Web Api.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }
        Log("Logged into Web Api. Retrieving Web API Key.");
        await Task.Delay(Configuration.DefaultWebRequestTimeout);

        (bool success, string webApiKey) = await _steamWebClient.GetWebApiKeyAsync();
        if (!success)
        {
            Log("Unable to retrieve Web Api Key.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }
        Log("Retrieved Web API Key.");

        _responseData.ApiKey = webApiKey;
    }

    private void OnIsLimitedAccount(DataFetcher.IsLimitedAccountCallback callback)
    {
        if (callback.Locked)
        {
            Log("Account is locked.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }

        if (callback.CommunityBanned)
            Log("Account is banned.", Logger.Level.Warning);

        _responseData.IsBanned = callback.CommunityBanned;
        _responseData.IsLimited = callback.Limited;
    }

    private async void OnLoggedOffAsync(SteamUser.LoggedOffCallback callback)
    {
        if (callback.Result == EResult.OK)
        {
            Log("Logged off from Steam.");
            _steamClient.Disconnect();
            return;
        }

        if (callback.Result == EResult.RateLimitExceeded)
        {
            Log($"Logged off from Steam with code {callback.Result}. Sleep for one hour.");
            await Task.Delay(TimeSpan.FromHours(1));
            await LoginAsync();
            return;
        }

        Log($"Logged off from Steam with code {callback.Result}.");
        return;
    }

    private async void OnLicenseListAsync(SteamApps.LicenseListCallback callback)
    {
        var licenseCount = callback.LicenseList.Count - 1;
        if (licenseCount > 0)
        {
            Log($"Account has {licenseCount} packages.");

            foreach (SteamApps.LicenseListCallback.License package in callback.LicenseList)
            {
                if (package.PackageID == 0)
                    continue;

                Log($"Retrieving PICS info for package {package.PackageID}");

                List<SteamApps.PICSRequest> packages = new()
                {
                    new(package.PackageID, package.AccessToken)
                };

                var productInfo = await _steamApps.PICSGetProductInfo(new List<SteamApps.PICSRequest>(0), packages);
                if (productInfo == null || !productInfo.Complete || productInfo.Failed || productInfo.Results == null)
                {
                    Log($"Unable to receive PICS info", Logger.Level.Error);
                    _steamClient.Disconnect();
                    return;
                }

                foreach (SteamApps.PICSProductInfoCallback product in productInfo.Results)
                {
                    foreach (var packageResponse in product.Packages.Values)
                    {
                        List<uint> apps = GetAppsInKeyValues(packageResponse.KeyValues);
                        if (apps.Count == 0)
                            continue;

                        (var success, var appNames) = await _steamWebClient.GetAppNamesAsync(apps.ToArray());

                        if (!success || appNames == null)
                        {
                            Log($"Unable to receive Apps info", Logger.Level.Error);
                            _steamClient.Disconnect();
                            return;
                        }

                        foreach (var app in appNames)
                        {
                            var offset = new DateTimeOffset(package.TimeCreated);
                            _responseData.Apps.Add(new()
                            {
                                AppId = app.Key,
                                RegistrationTime = offset.ToUnixTimeMilliseconds(),
                                AppName = app.Value
                            });
                        }
                    }
                }

                // No spam delay
                await Task.Delay(1000);
            }

            Log($"Totally account has {_responseData.Apps.Count} licenses.");
        }
        else
        {
            Log("Account has no packages.");
        }

        _isLicensesProcessed = true;
    }

    private List<uint> GetAppsInKeyValues(KeyValue keyValue)
    {
        List<uint> apps = new();

        foreach (KeyValue appKeyValue in keyValue["appids"].Children)
        {
            var app = appKeyValue.AsUnsignedInteger();
            if (_responseData.Apps.Any(x => x.AppId == app))
                continue;

            apps.Add(app);
        }

        return apps;
    }

    internal void Log(string message, Logger.Level level = Logger.Level.Info, [CallerMemberName] string callerName = "") =>
        Logger.Log($"{_instance},{Username.ToLower()} - {message}", level, callerName);
}
