using System.Runtime.CompilerServices;
using SteamKit2;
using SteamKit2.Authentication;

namespace SteamAccountDataFetcher.SteamDataClient;

public class Client : IDisposable
{
    private SteamClient _steamClient;
    private AutoTwoFactorAuthenticator _autoTwoFactorAuthenticator;
    private SteamWebClient _steamWebClient;
    private CallbackManager _callbackManager;
    private SteamUser _steamUser;
    private SteamApps _steamApps;

    private bool _isRunning = false;
    private bool _isLicensesProcessed = false;
    private bool _isWebAPIProcessed = false;

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

    private static uint _instance = 0;
    private static DateTime _lastConnectionTime = DateTime.MinValue;
    private static List<PackageInfo>? _packagesInfo = null;
    
    private AccountInfo _responseAccountInfo;

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

            if (_isLicensesProcessed && _isWebAPIProcessed)
                _steamClient.Disconnect();
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private async Task LoginAsync()
    {
        CredentialsAuthSession authSession = await _steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new()
        {
            Username = Username,
            Password = Password,
            Authenticator = _autoTwoFactorAuthenticator
        });

        AuthPollResult authPollResult;
        try
        {
            authPollResult = await authSession.PollingWaitForResultAsync();
        }
        catch (AuthenticationException e)
        {
            Log($"Unable to authenticate user to Steam Client with error {e.Message}.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }

        AccessToken = authPollResult.AccessToken;
        RefreshToken = authPollResult.RefreshToken;

        _steamUser.LogOn(new()
        {
            Username = Username,
            AccessToken = RefreshToken
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

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
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
        _steamWebClient.InitAsync();
    }

    private async void OnIsLimitedAccount(DataFetcher.IsLimitedAccountCallback callback)
    {
        _responseAccountInfo.IsLocked = callback.Locked;
        _responseAccountInfo.IsBanned = callback.CommunityBanned;
        _responseAccountInfo.IsLimited = callback.Limited;

        if (callback.Locked)
            Log("Account is locked.", Logger.Level.Warning);

        if (callback.CommunityBanned)
            Log("Account is banned.", Logger.Level.Warning);

        if (callback.Limited)
        {
            Log("Account is limited.", Logger.Level.Warning);
            
            // Limited accounts is unable to retrieve Web API
            _isWebAPIProcessed = true;
            return;
        }

        Log("Retrieving Web API Key.");
        await Task.Delay(Configuration.DefaultWebRequestTimeout);

        (bool success, string webApiKey) = await _steamWebClient.GetWebApiKeyAsync();
        if (!success)
        {
            Log("Unable to retrieve Web Api Key.", Logger.Level.Error);
            _steamClient.Disconnect();
            return;
        }
        Log("Retrieved Web API Key.");

        _responseAccountInfo.ApiKey = webApiKey;
        _isWebAPIProcessed = true;
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

    public void Dispose() => _steamWebClient?.Dispose();

    internal void Log(string message, Logger.Level level = Logger.Level.Info, [CallerMemberName] string callerName = "") =>
        Logger.Log($"{_instance},{Username.ToLower()} - {message}", level, callerName);
}
