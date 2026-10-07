using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Port.Accounts;

[Trait("Feature", "MA-1")]
[Trait("Rule", "R-SET-1")]
public sealed class AccountThreadBindingTests
{
    [Fact]
    public void Damaged_account_binding_cannot_be_interpreted_as_a_legacy_account()
    {
        var thread = JsonSerializer.Deserialize<ChatThread>("{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"projectID\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"providerSessionID\":\"native-new-account\",\"providerAccountID\":\"broken\"}", Coding.StorageOptions)!;
        Assert.Equal(Guid.Empty, thread.ProviderAccountID);
        Assert.Equal("native-new-account", thread.ProviderSessionID);
    }

    [Fact]
    public void Legacy_native_session_keeps_its_id_and_has_no_account_binding_until_used()
    {
        var thread = JsonSerializer.Deserialize<ChatThread>("{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"projectID\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"providerSessionID\":\"native-original\"}", Coding.StorageOptions)!;
        Assert.Null(thread.ProviderAccountID);
        Assert.Equal("native-original", thread.ProviderSessionID);
        var id = Guid.NewGuid();
        var bound = thread with { ProviderAccountID = id };
        var reopened = JsonSerializer.Deserialize<ChatThread>(JsonSerializer.Serialize(bound, Coding.StorageOptions), Coding.StorageOptions)!;
        Assert.Equal(id, reopened.ProviderAccountID);
        Assert.Equal("native-original", reopened.ProviderSessionID);
    }
}
