using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Composer.MultiAccount;

[Collection("Headless")]
[Trait("Rule", "R-TEST-4")]
public sealed class LiveCapsuleCompletionTests(HeadlessSession headless)
{
    [Fact]
    public void A_switch_that_completes_between_frames_keeps_the_real_ready_tick_visible_for_the_sync_span()
    {
        var rig = new AccountsRig(); var source = rig.Connect(ProviderKind.Codex); var target = rig.ConnectAnother(ProviderKind.Codex);
        var cards = new FakeCardPort(); var thread = Guid.NewGuid();
        MultiAccountHarness.Run(headless, rig, () =>
        {
            var slot = new ComposerCapsuleSlot(new(rig.Accounts, rig.Pills, rig.IsConnected, id => rig.Usage.GetValueOrDefault(id)),
                new(rig.Settings), () => new(ProviderKind.Codex, source.Id, null, false, null));
            ComposerCapsuleSeams.Observe(slot, thread, cards, null); return slot;
        }, h =>
        {
            var ready = new HandshakeEvent(thread, HandshakeEventKind.InSyncReady, HandshakePhase.Linking, HandshakePhase.InSync,
                source.Id.ToString("D"), target.Id.ToString("D"), HandshakeReason.NearLimit, h.Time.GetUtcNow());
            cards.Raise(ready);
            cards.Raise(ready with { Kind = HandshakeEventKind.PhaseChanged, From = HandshakePhase.InSync, To = HandshakePhase.Switch });
            cards.Raise(ready with { Kind = HandshakeEventKind.PhaseChanged, From = HandshakePhase.Switch, To = HandshakePhase.Idle });
            Assert.NotNull(h.TryFind("account-in-sync"));
            h.Step(799); Assert.NotNull(h.TryFind("account-in-sync"));
            h.Step(1); Assert.Null(h.TryFind("account-in-sync"));
            Assert.Equal(0, h.Frames.Pending);
        }, motion: true);
    }
}
