using System.Runtime.CompilerServices;
using SteamKit2;
using SteamKit2.Authentication;

namespace SteamAccountDataFetcher.SteamDataClient;

public class Client : IDisposable
{
    SteamClient _steamClient;
    AutoTwoFactorAuthenticator _autoTwoFactorAuthenticator;
    SteamWebClient _steamWebClient;
    CallbackManager _callbackManager;
    SteamUser _steamUser;
    SteamApps _steamApps;

    bool _isRunning = false;
    bool _isAuthUnsuccess = false;
    bool _isLicensesProcessed = false;
    bool _isLimitDetailsProcessed = false;

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
    internal string AccessToken { get; private set; } = string.Empty;
    internal string RefreshToken { get; private set; } = string.Empty;

    static uint _instance = 0;
    static bool _rateLimitReached = false;
    static DateTime _lastConnectionTime = DateTime.MinValue;
    static List<PackageInfo>? _packagesInfo = null;
    
    AccountInfo _responseAccountInfo;

    internal Client(string username, string password, string sharedSecret)
    {
        if (_packagesInfo == null)
            throw new InvalidOperationException($"{nameof(_packagesInfo)} is null. Call \"LoadPackagesCache\" before constructor.");

        ++_instance;

        Username = username;
        Password = password;

        _responseAccountInfo = new()
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
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOffAsync);
        _callbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseListAsync);
        _callbackManager.Subscribe<DataFetcher.IsLimitedAccountCallback>(OnIsLimitedAccount);

        _autoTwoFactorAuthenticator = new(this, sharedSecret);
        _steamWebClient = new(this);
    }

    internal static void LoadPackagesCache(List<PackageInfo> packagesInfo) => _packagesInfo = packagesInfo;

    internal async Task<AccountInfo> GetResponseDataAsync()
    {
        await RunAsync();
        return _responseAccountInfo;
    }

    internal async Task RunAsync()
    {
        await WaitOrProceed();

        _isRunning = true;
        _steamClient.Connect();

        while (_isRunning)
        {
            _callbackManager.RunCallbacks();

            if (_isLicensesProcessed && _isLimitDetailsProcessed)
                _steamClient.Disconnect();
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    async Task LoginAsync()
    {
        try
        {
            CredentialsAuthSession authSession = await _steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new()
            {
                Username = Username,
                Password = Password,
                Authenticator = _autoTwoFactorAuthenticator
            });
            AuthPollResult authPollResult = await authSession.PollingWaitForResultAsync();
            AccessToken = authPollResult.AccessToken;
            RefreshToken = authPollResult.RefreshToken;
        }
        catch (AuthenticationException e)
        {
            if (e.Result == EResult.AccountLoginDeniedThrottle || e.Result == EResult.RateLimitExceeded)
            {
                Log("RateLimit reached.", Logger.Level.Warning);
                _rateLimitReached = true;
                _steamClient.Disconnect();
                return;
            }

            Log($"Unable to authenticate user to Steam Client with error {e.Message}.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }
        catch (TaskCanceledException)
        {
            Log("Failure to authenticate user to Steam Client. Retrying...", Logger.Level.Warning);
            _isAuthUnsuccess = true;
            _steamClient.Disconnect();
            return;
        }

        _steamUser.LogOn(new()
        {
            Username = Username,
            AccessToken = RefreshToken
        });
    }

    async void OnConnectedAsync(SteamClient.ConnectedCallback callback)
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

    async void OnDisconnectedAsync(SteamClient.DisconnectedCallback callback)
    {
        if (callback.UserInitiated && !_rateLimitReached && !_isAuthUnsuccess)
        {
            Log("Disconnected from Steam Network by user.");

            _isRunning = false;
            return;
        }

        _isAuthUnsuccess = false;
        Log("Disconnected from Steam Network.", Logger.Level.Error);
        await WaitOrProceed();

        _steamClient.Connect();
    }

    void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            Log($"Unable to logon with reason {callback.Result}.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }

        if (callback.ClientSteamID == null)
        {
            Log($"{nameof(callback.ClientSteamID)} is empty.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }
        _responseAccountInfo.SteamId = callback.ClientSteamID.ConvertToUInt64();
        Log("Logged into Steam.");
        _steamWebClient.InitAsync();
    }

    void OnIsLimitedAccount(DataFetcher.IsLimitedAccountCallback callback)
    {
        _responseAccountInfo.IsLocked = callback.Locked;
        _responseAccountInfo.IsBanned = callback.CommunityBanned;
        _responseAccountInfo.IsLimited = callback.Limited;

        if (callback.Locked)
            Log("Account is locked.", Logger.Level.Warning);

        if (callback.CommunityBanned)
            Log("Account is banned.", Logger.Level.Warning);

        if (callback.Limited)
            Log("Account is limited.", Logger.Level.Warning);

        _isLimitDetailsProcessed = true;
    }

    async void OnLoggedOffAsync(SteamUser.LoggedOffCallback callback)
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

    async void OnLicenseListAsync(SteamApps.LicenseListCallback callback)
    {
        var licenseCount = callback.LicenseList.Count - 1;
        if (licenseCount > 0)
        {
            Log($"Account has {licenseCount} packages.");

            foreach (SteamApps.LicenseListCallback.License package in callback.LicenseList)
            {
                if (package.PackageID == 0)
                    continue;

                var packageUnixTime = (new DateTimeOffset(package.TimeCreated)).ToUnixTimeMilliseconds();

                _responseAccountInfo.Packages.Add(new()
                {
                    PackageId = package.PackageID,
                    RegistrationTime = packageUnixTime
                });

                if (_packagesInfo!.Any(x => x.PackageID == package.PackageID))
                    continue;

                Log($"Retrieving PICS info for package {package.PackageID}.");

                PackageInfo packageInfo = new()
                {
                    PackageID = package.PackageID
                };

                List<SteamApps.PICSRequest> packages = new()
                {
                    new(package.PackageID, package.AccessToken)
                };

                AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet productInfo;
                while (true)
                {
                    try
                    {
                        productInfo = await _steamApps.PICSGetProductInfo(new List<SteamApps.PICSRequest>(0), packages);
                        break;
                    }
                    catch (TaskCanceledException)
                    {
                        Log($"Failure to receive PICS info for package {package.PackageID}. Retrying...", Logger.Level.Warning);
                        await Task.Delay(1000);
                        continue;
                    }
                }
                if (productInfo == null || !productInfo.Complete || productInfo.Failed || productInfo.Results == null)
                {
                    Log($"Unable to receive PICS info", Logger.Level.Error);
                    _steamClient.Disconnect();
                    return;
                }

                foreach (SteamApps.PICSProductInfoCallback product in productInfo.Results)
                    foreach (var packageResponse in product.Packages.Values)
                        packageInfo.ExtensionData = packageResponse.KeyValues.ToDictionary();

                _packagesInfo!.Add(packageInfo);
                // No spam delay
                await Task.Delay(1000);
            }
        }
        else
        {
            Log("Account has no packages.");
        }

        _isLicensesProcessed = true;
    }

    async Task WaitOrProceed()
    {
        if (_lastConnectionTime == DateTime.MinValue)
            return;

        TimeSpan deltaBetweenLastConnect = DateTime.Now - _lastConnectionTime;
        TimeSpan timeToWait = Configuration.DefaultLoginTimeout - deltaBetweenLastConnect;

        if (_rateLimitReached)
        {
            _rateLimitReached = false;
            timeToWait += Configuration.DefaultRateLimitTimeout;
        }

        if (timeToWait.TotalSeconds > 1)
        {
            Log($"Waiting {timeToWait.TotalSeconds:N2} seconds.");
            await Task.Delay(timeToWait);
        }
    }

    public void Dispose() => _steamWebClient?.Dispose();

    internal void Log(string message, Logger.Level level = Logger.Level.Info, [CallerMemberName] string callerName = "") =>
        Logger.Log($"{_instance},{Username.ToLower()} - {message}", level, callerName);
}
