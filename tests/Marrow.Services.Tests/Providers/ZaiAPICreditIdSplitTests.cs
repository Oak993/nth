using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Audit fix (svc): ZaiAPI.swift:159-160 splits with `split(separator: ":", maxSplits: 1)`, which omits empty subsequences: ":1" gives
// ["1"], the two-part guard fails, and unknownOutcome is thrown before any request is made (a loopback server stands in for the API).
public sealed class ZaiAPICreditIdSplitTests
{
    [Trait("Req", "PR-PROVAPI-071")] // Services/Providers/ZaiAPI.swift:159-160
    [Fact]
    public async Task A_credit_id_with_an_empty_type_throws_unknownOutcome_before_any_request()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var pending = ZaiAPI.ConsumeResetCredit("key-abc", ":1", server.Url("/reset-use"));

        var error = await Assert.ThrowsAsync<ResetCreditError>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsType<ResetCreditError.Case.UnknownOutcome>(error.ErrorCase);
    }
}
