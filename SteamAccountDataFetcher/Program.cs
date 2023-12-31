﻿using SteamAccountDataFetcher.SteamDataClient;
using SteamAccountDataFetcher.FSAgent;

namespace SteamAccountDataFetcher;

public class Program
{
    public static async Task Main()
    {
        ReadCSVAgent csvAccounts;
        try
        {
            csvAccounts = new ReadCSVAgent(Configuration.ReadCSVFilePath);
        } catch (Exception e)
        {
            Logger.Log($"Unable to read CSV file, {e.Message}", Logger.Level.Error);
            return;
        }
        var jsonAccounts = new WriteJSONAgent(Configuration.WriteJSONFilePath);

        Client.LoadPackagesCache(jsonAccounts.PackageInfos);

        while (csvAccounts.Count > 0)
        {
            AccountLoginInfo account = csvAccounts.First();

            Client steamDataClient = new(account.Username, account.Password, account.SharedSecret);
            AccountInfo data = await steamDataClient.GetResponseDataAsync();

            jsonAccounts.AddAccount(data);
            csvAccounts.Remove(account);
        }
    }
}