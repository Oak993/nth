using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class InjectedStandbyConsentReviewTests
{
    [Fact]
    public async Task Revocation_during_a_multi_part_update_blocks_the_remaining_context()
    {
        var allowed = true;
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Codex, WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/offline")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        // The first load succeeds; the first part of the next update revokes consent.
        var calls = 0;
        var guarded = new ReviewInjectionSession { Injecting = _ => { if (++calls == 2) allowed = false; } };
        await using var updating = await InjectedStandbySession.StartAsync(guarded, configuration, CancellationToken.None, () => allowed);
        await updating.ReceiveAsync(Package("old", "old briefing"), false, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => updating.ReceiveAsync(Package("old plus new context", "PRIVATE-NEW-BRIEFING"), false, CancellationToken.None));

        Assert.Equal(2, guarded.Injected.Count);
        Assert.DoesNotContain(guarded.Injected, text => text.Contains("PRIVATE-NEW-BRIEFING", StringComparison.Ordinal));
    }

    private static HandoffPackage Package(string text, string briefing) => new(Bring.MarrowSummary, new(text, null, []), briefing);
}
