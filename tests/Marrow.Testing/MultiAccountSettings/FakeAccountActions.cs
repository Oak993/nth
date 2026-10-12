using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>Records the sign-in and sign-out Settings > Providers asks for; nothing is launched (MA-8 fixtures, shared by the Core and App tests). <see cref="Vault"/> stands in for the Credential Manager
/// the app keeps API keys in (OD-0149).</summary>
public sealed class FakeAccountActions : IAccountActions
{
    public List<ProviderKind> Added { get; } = [];
    public List<Guid> SignedOut { get; } = [];
    public string? AddFails { get; set; }
    public List<string> LocalAddresses { get; } = [];
    public LocalConnectResult Local { get; set; } = new(LocalConnectOutcome.Found, 1, "this PC");

    /// <summary>When set, a sign-in runs this (it may add the account, wait, fail or throw) instead of answering <see cref="AddFails"/> at once.</summary>
    public Func<ProviderKind, Task<string?>>? AddRuns { get; set; }

    public Task<string?> AddAccountAsync(ProviderKind service)
    {
        Added.Add(service);
        return AddRuns is { } runs ? runs(service) : Task.FromResult(AddFails);
    }

    /// <summary>When set, a local connect waits for it (a test looks at the "Looking" line meanwhile).</summary>
    public Task? LocalHold { get; set; }

    public async Task<LocalConnectResult> ConnectLocalAsync(string address)
    {
        LocalAddresses.Add(address);
        if (LocalHold is { } hold) await hold;
        return Local;
    }

    public Task SignOutAsync(Guid id)
    {
        SignedOut.Add(id);
        return Task.CompletedTask;
    }

    /// <summary>The keys kept, by account (the Credential Manager of the app).</summary>
    public Dictionary<Guid, string> Vault { get; } = [];

    /// <summary>When set, a key is not kept and this line says why.</summary>
    public string? StoreFails { get; set; }

    public List<Guid> Forgotten { get; } = [];

    public string? KeySecret(Guid accountId) => Vault.GetValueOrDefault(accountId);

    public Task<string?> StoreKeyAsync(Guid accountId, string secret)
    {
        if (StoreFails is null) Vault[accountId] = secret;
        return Task.FromResult(StoreFails);
    }

    public Task ForgetKeyAsync(Guid accountId)
    {
        Forgotten.Add(accountId);
        Vault.Remove(accountId);
        return Task.CompletedTask;
    }
}
