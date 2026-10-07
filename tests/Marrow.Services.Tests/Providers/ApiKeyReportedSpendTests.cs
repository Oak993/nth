using Marrow.Core.Models;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Rule", "R-TEST-4")]
public sealed class ApiKeyReportedSpendTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Service_reported_dollar_cost_is_charged_to_the_key_that_succeeded_and_takes_precedence_over_an_estimate(bool estimate)
    {
        await using var rig = new KeyRotationRig(["402", KeyRotationRig.Ok], provider: ProviderKind.OpenRouter, reportedCost: 0.031);
        if (estimate) rig.Session.KeySpendEstimator = _ => 999;
        Assert.Null(await rig.RunTurn());
        Assert.Equal(0.031, rig.Spend.SpendSince("b", DateTimeOffset.MinValue), 8);
        Assert.Equal(0, rig.Spend.SpendSince("a", DateTimeOffset.MinValue));
        Assert.Equal(["Bearer sk-a", "Bearer sk-b"], rig.Requests.Select(request => request.Bearer));
        Assert.Equal(rig.Requests[0].Body, rig.Requests[1].Body);
        Assert.DoesNotContain(rig.Notices(), notice => notice.Contains("sk-", StringComparison.Ordinal));
    }
}
