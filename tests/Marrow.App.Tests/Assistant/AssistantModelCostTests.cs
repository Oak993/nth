using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;
using Marrow.App.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-2")]
public sealed class AssistantModelCostTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    public void Dispose() => temp.Dispose();
    [Fact]
    public void Same_tick_usage_and_completion_do_not_book_the_admission_twice()
    {
        using var rig = new ReserveRig(temp.Folder);
        rig.Guard.OnReadings(ReserveRig.CodexA, [new ReserveWindow("five", ReserveWindowKind.FiveHour, 50, rig.Clock.GetUtcNow(), rig.Clock.GetUtcNow().AddHours(4))]);
        var receipt = rig.Guard.BeginModelCall(ReserveRig.CodexA, "root", ReserveRig.Model)!;
        var before = rig.Guard.Standing(ReserveRig.CodexA, "root", null, null, ReserveRig.Model, 0)!.Windows.Single().Estimate;
        receipt.Observe(new Marrow.Core.Models.TokenSpend(Marrow.Core.Models.ProviderKind.Codex, 1));
        receipt.Dispose();
        Assert.Equal(before, rig.Guard.Standing(ReserveRig.CodexA, "root", null, null, ReserveRig.Model, 0)!.Windows.Single().Estimate);
    }

    [Fact]
    public void A_delegate_call_at_the_floor_is_refused_before_any_spend()
    {
        using var rig = new ReserveRig(temp.Folder);
        rig.Settings.Add(new ReserveRule("floor", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30 });
        rig.Guard.OnReadings(ReserveRig.CodexA, [new ReserveWindow("five", ReserveWindowKind.FiveHour, 70, rig.Clock.GetUtcNow(), rig.Clock.GetUtcNow().AddHours(4))]);
        Assert.False(rig.Guard.TryBookModelCall(ReserveRig.CodexA, "root", ReserveRig.Model, null, out _));
    }

    [Fact]
    public void A_delegate_call_counts_on_its_actual_account_before_the_next_call_checks_the_floor()
    {
        using var rig = new ReserveRig(temp.Folder);
        rig.Settings.Add(new ReserveRule("floor", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30 });
        rig.Guard.OnReadings(ReserveRig.CodexA, [new ReserveWindow("five", ReserveWindowKind.FiveHour, 65, rig.Clock.GetUtcNow(), rig.Clock.GetUtcNow().AddHours(4))]);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(rig.Guard.TryBookModelCall(ReserveRig.CodexA, "root", ReserveRig.Model, null, out var tokens));
        Assert.True(tokens > 100_000);
        var before = rig.Guard.Standing(ReserveRig.CodexA, "root", null, null, ReserveRig.Model, 1)!;
        Assert.True(before.Windows.Single().Estimate > 66, $"Booked estimate: {before.Windows.Single().Estimate}");
        // Existing reserve math rounds spent points upward, then adds its minimum margin and quantum.
        // The next full p90 crosses 70 even though the 1-token diagnostic projection still has room.
        Assert.False(rig.Guard.TryBookModelCall(ReserveRig.CodexA, "root", ReserveRig.Model, null, out _));
    }

    [Fact]
    public void A_delegate_call_uses_the_assistants_daily_cap_and_the_API_global_dollar_cap()
    {
        using var rig = new CostRig(temp.Folder);
        rig.Settings.AddCap(new CostCap("daily", CostCapScope.AssistantDay) { AssistantId = "root", Percent = 2 });
        Assert.True(rig.Guard.TryBookModelCall(CostRig.CodexA, "root", CostRig.Model, null, out _));
        Assert.False(rig.Guard.TryBookModelCall(CostRig.CodexA, "root", CostRig.Model, null, out _));
        Assert.True(rig.Guard.TryBookModelCall(CostRig.Claude20, "other", CostRig.Model, null, out _));
        rig.Settings.AddCap(new CostCap("money", CostCapScope.ApiDay) { Dollars = 2 });
        Assert.True(rig.Guard.TryBookModelCall(CostRig.Key, "root", CostRig.Model, null, out _));
        Assert.False(rig.Guard.TryBookModelCall(CostRig.Key, "other", CostRig.Model, null, out _));
    }

    [Fact]
    public void A_poll_during_an_answer_does_not_erase_its_booking_when_the_answer_finishes()
    {
        using var rig = new ReserveRig(temp.Folder);
        rig.Guard.OnReadings(ReserveRig.CodexA, [new ReserveWindow("five", ReserveWindowKind.FiveHour, 50, rig.Clock.GetUtcNow(), rig.Clock.GetUtcNow().AddHours(4))]);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        var receipt = rig.Guard.BeginModelCall(ReserveRig.CodexA, "root", ReserveRig.Model)!;
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        rig.Guard.OnReadings(ReserveRig.CodexA, [new ReserveWindow("five", ReserveWindowKind.FiveHour, 50, rig.Clock.GetUtcNow(), rig.Clock.GetUtcNow().AddHours(4))]);
        Assert.True(rig.Guard.Standing(ReserveRig.CodexA, "root", null, null, ReserveRig.Model, 0)!.Windows.Single().Projected > 54);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        receipt.Observe(new Marrow.Core.Models.TokenSpend(Marrow.Core.Models.ProviderKind.Codex, 100_000));
        receipt.Dispose();
        Assert.True(rig.Guard.Standing(ReserveRig.CodexA, "root", null, null, ReserveRig.Model, 0)!.Windows.Single().Estimate >= 52);
    }

    [Fact]
    public void Actual_usage_above_p90_is_added_before_the_receipt_is_released()
    {
        using var rig = new ReserveRig(temp.Folder);
        rig.Guard.OnReadings(ReserveRig.CodexA, [new ReserveWindow("five", ReserveWindowKind.FiveHour, 50, rig.Clock.GetUtcNow(), rig.Clock.GetUtcNow().AddHours(4))]);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        using var receipt = rig.Guard.BeginModelCall(ReserveRig.CodexA, "root", ReserveRig.Model)!;
        receipt.Observe(new Marrow.Core.Models.TokenSpend(Marrow.Core.Models.ProviderKind.Codex, 500_000));
        Assert.True(rig.Guard.Standing(ReserveRig.CodexA, "root", null, null, ReserveRig.Model, 0)!.Windows.Single().Estimate >= 55);
    }
}
