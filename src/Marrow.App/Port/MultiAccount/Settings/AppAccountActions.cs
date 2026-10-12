// PORT-ONLY: T11 step 2 (ENGINEERING DECISION, ED-449). Settings > Providers' "Add another account" and "Sign out" (on Settings > Models before OD-0149), over the provider registry's account sign-in (ProviderRegistry.SignIn(Guid)). A sign-in that fails leaves no
// half-made account behind. Signing out of an account Marrow made removes it and the private folder Marrow made for it (that is where its sign-in is kept); the original account's sign-in belongs to the CLI and is never touched here.
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Services.Port.Accounts;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class AppAccountActions(AppModel model) : IAccountActions
{
    public async Task<string?> AddAccountAsync(ProviderKind service)
    {
        var accounts = model.Settings.Accounts;
        if (!AccountCapabilities(service)) return service.DisplayName + " keeps one account.";
        var account = accounts.Add(service);
        string? problem = null;
        var finished = false;
        try
        {
            try { problem = await model.Providers.SignIn(account.Id); }
            catch (InvalidOperationException e) { problem = e.Message; }
            finished = true;
        }
        finally { if (!finished || problem is not null) accounts.Remove(account.Id); } // a sign-in that failed, threw or was cancelled leaves no half-made account (ED-NEW-providersmerge-2)
        return problem;
    }

    /// <summary>Remembers the server's address, looks for it and makes a local entry of each model it offers (LocalAccountCatalog). Nothing is downloaded; a server that does not answer leaves nothing behind but the address.</summary>
    public async Task<LocalConnectResult> ConnectLocalAsync(string address)
    {
        Marrow.Core.Port.Local.LocalServer server;
        try { server = model.Providers.AddLocalAddress(address); }
        catch (ArgumentException) { return new LocalConnectResult(LocalConnectOutcome.BadAddress, 0, ""); }
        var where = server.BaseURL.IsLoopback ? "this PC" : server.BaseURL.Host;
        IReadOnlyList<Marrow.Core.Port.Local.LocalEntry> entries;
        try { entries = await model.Providers.Local.Discover(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException) { return new LocalConnectResult(LocalConnectOutcome.NotFound, 0, where); }
        var found = entries.FirstOrDefault(entry => entry.Server.Id == server.Id);
        if (found is null || found.Models.Count == 0) return new LocalConnectResult(LocalConnectOutcome.NotFound, 0, where);
        Services.Port.MultiAccount.LocalAccountCatalog.Synchronize(model.Settings.Accounts, entries);
        return new LocalConnectResult(LocalConnectOutcome.Found, found.Models.Count, where, found.Kind);
    }

    public Task SignOutAsync(Guid accountId)
    {
        var accounts = model.Settings.Accounts;
        var account = accounts.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null || account.UsesLegacyCredentials) return Task.CompletedTask;
        var folder = new AccountEnvironment(Storage.Root).Folder(account);
        accounts.Remove(accountId);
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (IOException) { /* the account is gone from Marrow; a folder that cannot be deleted now is left */ }
        catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    /// <summary>OD-0149 (ED-NEW-providersmerge-1): the original account's key is the provider's one key of older builds (the card's "API key" box, AppSettings); a further key is kept beside it in the
    /// Credential Manager under its own account name (APIKeychain.Key(Account)), where the engine reads it (ProviderRegistry.ApiKey, AccountKeyStore).</summary>
    public string? KeySecret(Guid accountId)
    {
        if (model.Settings.Accounts.Accounts.FirstOrDefault(a => a.Id == accountId) is not { Service.IsAPIKeyBased: true } account) return null;
        var key = account.UsesLegacyCredentials ? model.Settings.ApiKey(account.Service) : APIKeychain.Key(account, "");
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public async Task<string?> StoreKeyAsync(Guid accountId, string secret)
    {
        var account = model.Settings.Accounts.Get(accountId);
        if (!account.Service.IsAPIKeyBased) return account.Service.DisplayName + " does not use API keys.";
        if (!account.UsesLegacyCredentials) return APIKeychain.SetKey(secret, account) ? null : AppSettings.CredentialRefusalLine;
        model.Settings.SetAPIKeyInput(secret, account.Service);
        if (model.Settings.APIKeyRefusal(account.Service) is { } refused) return refused;
        await model.Providers.Refresh(account.Service);
        return null;
    }

    public async Task ForgetKeyAsync(Guid accountId)
    {
        var account = model.Settings.Accounts.Get(accountId);
        if (!account.Service.IsAPIKeyBased) return;
        if (!account.UsesLegacyCredentials) { APIKeychain.SetKey("", account); return; } // an empty key deletes the item
        model.Settings.SetAPIKeyInput("", account.Service);
        await model.Providers.Refresh(account.Service);
    }

    /// <summary>The week and 5-hour windows the service reported for the account, or null when it reported none.</summary>
    internal AccountUsageLine? Usage(Guid accountId)
    {
        if (model.AutoContinue.UsageLimits?.Snapshot(AccountKey(accountId)) is not { Limits: { } limits }) return null;
        var five = limits.Windows.FirstOrDefault(w => w.DurationMinutes == 300);
        var week = limits.Windows.FirstOrDefault(w => w.DurationMinutes >= 7 * 24 * 60 - 1);
        return new AccountUsageLine(five?.Percent / 100, five?.ResetsAt, week?.Percent / 100, week?.ResetsAt, five is null && limits.Windows.Count > 0, false);
    }

    private Marrow.Core.Port.UsageAccountKey AccountKey(Guid id)
    {
        var account = model.Settings.Accounts.Get(id);
        return account.UsesLegacyCredentials ? Marrow.Core.Port.UsageAccountKey.Default(account.Service) : new(account.Service, id.ToString("D"));
    }

    private static bool AccountCapabilities(ProviderKind service) => Marrow.Core.Port.Accounts.AccountCapabilities.SupportsMultipleAccounts(service);
}
