using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>Records the sign-in and sign-out the Models page asks for; nothing is launched (MA-8 fixtures, shared by the Core and App tests).</summary>
public sealed class FakeAccountActions : IAccountActions
{
    public List<ProviderKind> Added { get; } = [];
    public List<Guid> SignedOut { get; } = [];
    public string? AddFails { get; set; }
    public List<string> LocalAddresses { get; } = [];
    public LocalConnectResult Local { get; set; } = new(LocalConnectOutcome.Found, 1, "this PC");

    public Task<string?> AddAccountAsync(ProviderKind service)
    {
        Added.Add(service);
        return Task.FromResult(AddFails);
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
}
