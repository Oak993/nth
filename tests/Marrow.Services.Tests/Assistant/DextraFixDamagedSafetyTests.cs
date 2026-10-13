using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-7")]
public sealed class DextraFixDamagedSafetyTests
{
    [Theory]
    [InlineData(AssistantStore.CostsFile, "{\"version\":1,\"days\":[")]
    [InlineData(AssistantStore.DutyHealthFile, "{\"version\":1,\"duties\":[")]
    [InlineData(AssistantStore.CostsFile, "[]")]
    [InlineData(AssistantStore.DutyHealthFile, "[]")]
    public void An_unreadable_safety_book_refuses_start_and_preserves_the_file(string file, string damaged)
    {
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var path = Path.Combine(store.Root, file);
        File.WriteAllText(path, damaged);
        var before = File.ReadAllBytes(path);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
        Assert.Throws<InvalidDataException>(() => new ReservePersistence(store, new ReserveSettings(), new UsageCalibrator(clock),
            new TurnCostHistory(clock), clock, new CostLedger(clock), new DutyHealthBook()));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(0, clock.ActiveTimers);
    }
}
