using System.Reflection;
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
            // ED-NEW-gatefix3-2 / ED-NEW-inst-c7-5: admission wraps the real engines, as the
            // approved standby fixture checks both its adapter and its underlying session.
            var links = Assert.IsType<InstallerLinkEngine>(ports.Links);
            var opinions = Assert.IsType<InstallerOpinionsEngine>(ports.Opinions);
            Assert.Same(owner.Links, links);
            Assert.Same(owner.Opinions, opinions);
            Assert.IsType<Marrow.Services.Port.MultiAccount.LinkEngine>(typeof(InstallerLinkEngine)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(field => field.FieldType == typeof(Marrow.Services.Port.MultiAccount.LinkEngine)).GetValue(links));
            Assert.IsType<Marrow.Services.Port.MultiAccount.OpinionsEngine>(typeof(InstallerOpinionsEngine)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(field => field.FieldType == typeof(Marrow.Services.Port.MultiAccount.OpinionsEngine)).GetValue(opinions));
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
