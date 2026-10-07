using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.App.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveThreadPortsTests
{
    [Fact]
    public void The_composition_offer_exposes_real_engines_and_pins_the_next_request()
    {
        using var rig = new ManualAccountPinRig();
        rig.Send("Preserve the recorded conversation.");
        var owner = rig.Rig.H.Main.OnMain(() => new LiveThreadPorts(rig.Rig.App));
        try
        {
            var ports = owner.Ports;
            var port = rig.Rig.H.Main.OnMain(() => ports.Account(rig.Rig.InitialThread.Id, ProviderKind.Deepseek)!);
            Assert.IsType<Marrow.Services.Port.MultiAccount.LinkEngine>(ports.Links);
            Assert.IsType<Marrow.Services.Port.MultiAccount.OpinionsEngine>(ports.Opinions);
            var changes = 0;
            rig.Rig.H.Main.OnMain(() => port.Changed += () => changes++);
            var pin = rig.Rig.H.Main.OnMain(() => port.PinAsync(rig.Target));
            Assert.Equal(PinResult.Pinned, rig.Rig.H.Main.Loop.Run(() => pin, AppModelHarness.Bound));
            Assert.Equal(rig.Target, rig.Rig.H.Main.OnMain(() => port.PinnedAccountId));
            Assert.True(changes > 0);
            rig.Send("Use my chosen key.");
            Assert.Equal("Bearer offline-next", rig.Http.Received[^1].Header("Authorization"));
            Assert.Equal(rig.Target, rig.Rig.H.Main.OnMain(() => port.ActiveAccountId));
            rig.Rig.H.Main.OnMain(() =>
            {
                rig.Rig.App.Settings.AccountDecisions.SetThread(rig.Rig.InitialThread.Id, DecisionKind.OnLimit, DecisionValues.Ask);
                Assert.Equal(new Choice(DecisionKind.OnLimit, DecisionValues.Ask), Assert.Single(port.ThreadChoices));
                port.UseSettings();
                Assert.Empty(port.ThreadChoices);
                port.BackToOrder();
                Assert.Null(port.PinnedAccountId);
                Assert.False(rig.Rig.App.Settings.AccountHandshakeEnabled);
            });
        }
        finally { rig.Rig.H.Main.OnMain(owner.Dispose); }
    }

    [Theory]
    [InlineData(false, PinResult.Disabled)]
    [InlineData(true, PinResult.OutOfService)]
    public void The_live_port_refuses_off_or_capped_accounts_without_changing_the_pin(bool cap, PinResult expected)
    {
        using var rig = new ManualAccountPinRig();
        rig.Rig.H.Main.OnMain(() =>
        {
            using var owner = new LiveThreadPorts(rig.Rig.App);
            var port = owner.Ports.Account(rig.Rig.InitialThread.Id, ProviderKind.Deepseek)!;
            if (cap) rig.Rig.App.Settings.AccountKeys.SetCap(rig.Target, 0);
            else rig.Rig.App.Settings.Accounts.SetEnabled(rig.Target, false);
            Assert.Equal(expected, port.PinAsync(rig.Target).GetAwaiter().GetResult());
            Assert.Null(port.PinnedAccountId);
            Assert.Empty(rig.Http.Received);
        });
    }
}
