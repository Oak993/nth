using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-4")]
public sealed class StandbyControlReviewTests
{
    [Theory]
    [InlineData(ProviderKind.Codex, false)]
    [InlineData(ProviderKind.Codex, true)]
    [InlineData(ProviderKind.Pi, false)]
    [InlineData(ProviderKind.Pi, true)]
    public async Task A_waiting_or_discarding_account_rejects_model_compaction_before_touching_its_transport(ProviderKind provider, bool discarding)
    {
        var session = ReviewWaitingControls.Session(provider, discarding);

        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Compact());

        Assert.Contains("waiting", error.ErrorDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Codex_standby_being_deleted_cannot_dispatch_a_new_turn()
    {
        var session = ReviewWaitingControls.Session(ProviderKind.Codex, discarding: true, openConnection: true);

        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Send(new TurnInput
        {
            Text = "must not dispatch", Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        }));

        Assert.Contains("waiting", error.ErrorDescription, StringComparison.OrdinalIgnoreCase);
    }
}
