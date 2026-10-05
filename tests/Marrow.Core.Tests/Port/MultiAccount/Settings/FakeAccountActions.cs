using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

/// <summary>Records the sign-in and sign-out the Models page asks for; nothing is launched (MA-8 fixtures).</summary>
public sealed class FakeAccountActions : IAccountActions
{
    public List<ProviderKind> Added { get; } = [];
    public List<Guid> SignedOut { get; } = [];
    public string? AddFails { get; set; }

    public Task<string?> AddAccountAsync(ProviderKind service)
    {
        Added.Add(service);
        return Task.FromResult(AddFails);
    }

    public Task SignOutAsync(Guid id)
    {
        SignedOut.Add(id);
        return Task.CompletedTask;
    }
}
