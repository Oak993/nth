using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Port.Accounts;

[Trait("Rule", "R-SET-1")]
public sealed class AccountPinPersistenceTests
{
    [Theory]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("broken")]
    public void An_account_pin_survives_storage_independently_of_sidebar_pinning_and_corruption_cannot_unpin_it(string saved)
    {
        var thread = JsonSerializer.Deserialize<ChatThread>("{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"projectID\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"providerAccountPinID\":\"" + saved + "\",\"isPinned\":false}", Coding.StorageOptions)!;
        var property = typeof(ChatThread).GetProperty("ProviderAccountPinID");
        Assert.NotNull(property);
        var expected = saved == "broken" ? Guid.Empty : Guid.Parse(saved);
        Assert.Equal(expected, property.GetValue(thread));
        Assert.False(thread.IsPinned);
        var restored = JsonSerializer.Deserialize<ChatThread>(JsonSerializer.Serialize(thread, Coding.StorageOptions), Coding.StorageOptions)!;
        Assert.Equal(expected, property.GetValue(restored));
    }
}
