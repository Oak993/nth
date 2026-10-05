using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Testing;
using System.Text.Json;

namespace Marrow.Core.Tests.Accounts.Pills;

// MA-7 with MA-5: the relay survives a restart end to end, and the pill's "same for every step" point reaches the Handshake.
public sealed class PillRestartHandshakeTests
{
    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T231")]
    [Trait("Rule", "R-TEST-4")]
    public async Task After_a_restart_the_thread_continues_its_relay_from_the_stored_step()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        // First run: a pill c1 -> c2 -> x1; the thread has moved on to step 1 and its state is saved.
        var settings = new InMemorySettingsStore();
        var store = new PillStore(new PillStorage(settings));
        var pill = store.Create("Relay", [new PillStep("c1"), new PillStep("c2"), new PillStep("x1")]);
        var json = JsonSerializer.Serialize(
            new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised).WithPillRelay(new PillThreadState(pill.Id, 1, "c2")),
            Marrow.Core.Support.Coding.StorageOptions);

        // Second run: everything is read back from storage.
        var thread = JsonSerializer.Deserialize<ChatThread>(json, Marrow.Core.Support.Coding.StorageOptions)!;
        var resume = PillRelayResolver.Resolve(new PillStore(new PillStorage(settings)).Pills, thread.PillRelay!);
        await using var rig = new HandshakeRig(active: "c2");
        rig.SettleAll();
        rig.Host.Pill = resume.Pill;
        rig.Host.StepIndex = resume.StepIndex;
        await rig.Machine.LimitHitAsync("c2", null, rig.Ct);

        Assert.Equal("x1", rig.Host.ActiveAccountId);
        Assert.Equal(2, rig.Host.StepIndex);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T232")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Same_for_every_step_sets_the_start_point_for_every_step_over_the_steps_own()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.Pill = new Pill("P", [new PillStep("c1", HandshakeAt: HandshakePoint.At(85)), new PillStep("c2")], HandshakeMode: PillHandshakeMode.SameForEvery, SameHandshake: HandshakePoint.At(99));
        rig.Host.StepIndex = 0;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Host.Log);
        rig.SetUsed("c1", 99);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T233")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Per_step_mode_still_uses_each_steps_own_point()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.Pill = new Pill("P", [new PillStep("c1", HandshakeAt: HandshakePoint.At(85)), new PillStep("c2")]);
        rig.Host.StepIndex = 0;
        rig.SetUsed("c1", 86);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }
}
