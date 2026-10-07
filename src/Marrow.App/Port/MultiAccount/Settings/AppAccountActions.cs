// PORT-ONLY: T11 step 2 (ENGINEERING DECISION, ED-NEW-cloud-ma-settings). The Models page's "Add another account" and "Sign out", over the provider registry's account sign-in (ProviderRegistry.SignIn(Guid)). A sign-in that fails leaves no
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
        string? problem;
        try { problem = await model.Providers.SignIn(account.Id); }
        catch (InvalidOperationException e) { problem = e.Message; }
        if (problem is not null) accounts.Remove(account.Id);
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
