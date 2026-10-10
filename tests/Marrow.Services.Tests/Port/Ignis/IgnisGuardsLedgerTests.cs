using Marrow.Core.Port.Ignis;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Ignis;

// ED-NEW-ignis-guards: the guard events (a held keep-warm, the user's cool, a switch-cost notice, a cache never reused)
// are kept in the ledger with their reason and read back after a restart, so History shows them on later days too.
public sealed class IgnisGuardsLedgerTests
{
    private static readonly Guid Chat = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void Guard_events_and_their_reasons_survive_a_restart()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var at = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            new IgnisEvent("held", Chat, IgnisEventKind.KeepWarmHeld, at, Reason: "IdleLimit"),
            new IgnisEvent("cool", Chat, IgnisEventKind.LetCool, at.AddMinutes(1)),
            new IgnisEvent("switch", Chat, IgnisEventKind.SwitchCost, at.AddMinutes(2), 1.5m, IgnisAmountKind.Cost, Model: "Opus 5.5", AtTokens: 300_000),
            new IgnisEvent("miss", Chat, IgnisEventKind.CacheNotReused, at.AddMinutes(3), Reason: "ModelSwitches"),
        };
        var ledger = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);
        foreach (var e in events) ledger.Record(e);

        var reread = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);
        foreach (var e in events) Assert.Equal(e, reread.Event(e.Id));
        Assert.Equal(4, reread.Events(Chat, new DateOnly(2026, 10, 10)).Count);
    }
}
