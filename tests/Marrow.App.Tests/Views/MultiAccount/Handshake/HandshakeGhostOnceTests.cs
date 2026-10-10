using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Handshake;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Handshake;

// ED-NEW-stutterfix-5: the ghost of an incoming account is made once per incoming account and capsule style. A plain account's ghost
// is "plain" while the thread's capsule shows a pill (or the other way round); the host compared the ghost's style with the slot's and
// so removed and remade the ghost, glass and all, on every Advance of a running handshake (each frame and each composer keystroke). So
// was a pill's ghost (style "1" by default) beside a slot that shows no capsule.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-stutterfix-5")]
public sealed class HandshakeGhostOnceTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void A_ghost_whose_style_differs_from_the_slots_is_made_once_for_its_incoming_account(bool slotShowsPill)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var pill = rig.MakePill("Work", a, b); var rows = new FakeThreadAccountPort(rig, ProviderKind.Claude); var cards = new FakeCardPort();
        var model = new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, _ => new(.3, null, .2, null, false, false));
        var outgoing = new CapsuleSubject(ProviderKind.Claude, a.Id, slotShowsPill ? new PillThreadState(pill.Id, 0, a.Id.ToString("D")) : null, false, null);
        var incoming = new CapsuleSubject(ProviderKind.Claude, b.Id, slotShowsPill ? null : new PillThreadState(pill.Id, 1, b.Id.ToString("D")), false, null);
        var content = model.For(incoming)!; ComposerCapsuleSlot? slot = null; HandshakeCapsuleHost? view = null; var asked = 0;
        MultiAccountHarness.Run(headless, rig, () =>
        {
            var setting = new CapsuleStyleSetting(rig.Settings) { Value = "1" };
            slot = new ComposerCapsuleSlot(model, setting, () => slotShowsPill ? outgoing : null);
            return view = new HandshakeCapsuleHost(slot, slot, rows.ThreadId, cards, () => rows.Accounts, _ => { asked++; return content; });
        }, h =>
        {
            cards.Raise(new(rows.ThreadId, HandshakeEventKind.PhaseChanged, HandshakePhase.Idle, HandshakePhase.Standby, a.Id.ToString("D"), b.Id.ToString("D"), HandshakeReason.NearLimit, h.Time.GetUtcNow()));
            h.Step(100);
            var ghost = Assert.IsType<AccountCapsule>(view!.Ghost);
            Assert.NotEqual(slot!.Capsule?.Style, ghost.Style); // the case that was remade on every Advance
            Assert.Equal(slotShowsPill ? "plain" : "1", ghost.Style);
            Assert.Equal(content.Mono, ghost.Mono);
            asked = 0;
            for (var i = 0; i < 30; i++) { view.Advance(); h.Step(16); }
            Assert.Same(ghost, view.Ghost);
            Assert.Equal(0, asked);
            Assert.Single(view.Children.OfType<AccountCapsule>());
        }, motion: true);
    }
}
