# SteamAccountDataFetcher

Utility for collecting Steam account information.  
The utility collects information about:

- SteamID;
- WebAPI (in case of missing - it will generate);
- The presence of a Community ban on the account;
- Availability of rights for full use of the account (Limited or Unlimited);
- All purchased licenses on the account.

## Note

First of all, this utility is for my own internal needs. You can see this by the completely static part that is supposed to be the [configuration](./SteamAccountDataFetcher/SteamDataClient/Configuration.cs).

But still, I decided to share my work in the public domain.
If someone finds it useful – you're welcome.

## How to get

- Download source zip file and compile
- Or download from [Release](https://github.com/SeRi0uS007/SteamAccountDataFetcher/releases) page

## Usage

1. Prepare a CSV file with login, password, and TOTP key for generating two-factor authentication;
2. This file needs to be renamed to **SteamAccountsLogin.txt**;
3. Put this file together with the executable file;
4. Run and wait;
5. The result will be a file named **SteamAccounts.json**, which will be automatically placed next to it.

## CSV File Format

```csv
AccountLogin;AccountPassword;SharedSecret
login1;password1;sharedSecret1
login2;password2;sharedSecret2
login3;password3;sharedSecret3
```

## TODO

Someday in the future. After I have used it, I write down a list of things to do.

- Add proxy support to speed up data collection;
- Finalize the configuration code.
