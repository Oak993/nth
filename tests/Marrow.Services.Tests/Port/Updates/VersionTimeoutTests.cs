using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Updates;

[Trait("Decision", "ED-308")]
public sealed class VersionTimeoutTests
{
    [Fact]
    public async Task A_listing_timeout_is_an_offline_check_error_instead_of_application_cancellation()
    {
        using var client = new HttpClient(new TimedOutVersionHandler());
        var listing = new FetchedVersionListing((uri, token) => HttpVersionFetch.Get(uri, token, client));
        await Assert.ThrowsAsync<TimeoutException>(() => listing.Latest("codex", default));
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        using var client = new HttpClient(new TimedOutVersionHandler());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HttpVersionFetch.Get(new("https://test.invalid/listing"), cancellation.Token, client));
    }
}
