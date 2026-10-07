using System.Text.Json;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Accounts;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts.Pills;

[Trait("Rule", "R-TEST-4")]
public sealed class PillMalformedKeyTests
{
    [Theory]
    [InlineData("123")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    public void A_malformed_present_key_identity_is_skipped_and_never_reinterpreted_as_the_anchor_sign_in(string invalid)
    {
        var id = Guid.NewGuid();
        var settings = new InMemorySettingsStore();
        var raw = "{\"version\":1,\"useForNewThreads\":" + JsonSerializer.Serialize(id) + ",\"pills\":[{\"id\":"
            + JsonSerializer.Serialize(id) + ",\"name\":\"Preserve\",\"steps\":[{\"account\":\"anchor\",\"key\":" + invalid + "}]}]}";
        settings.Set(PillStorage.DefaultKey, raw);
        var load = new PillStorage(settings).Load();
        Assert.Equal(1, load.Skipped);
        Assert.Empty(Assert.Single(load.Pills).Steps);
        Assert.Null(new PillStore(new PillStorage(settings)).NewThreadRelay(_ => true));
        Assert.Equal(raw, settings.Object(PillStorage.DefaultKey));
    }
}
