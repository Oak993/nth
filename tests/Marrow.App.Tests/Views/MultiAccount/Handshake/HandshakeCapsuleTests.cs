using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Handshake;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Handshake;

// PORT-ONLY: MA-4-3, live-event presentation, OD-0116 and OD-0122 injected readiness uses the same tick.
[Collection("Headless")]
public sealed class HandshakeCapsuleTests(HeadlessSession headless)
{
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Live_phases_show_incoming_capsule_status_and_same_tick_without_swallowing_clicks(bool reduced)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var accounts = new FakeThreadAccountPort(rig, ProviderKind.Claude); var cards = new FakeCardPort(); HandshakeCapsuleHost? view = null; ComposerCapsuleSlot? slot = null; var clicks = 0;
        MultiAccountHarness.Run(headless, rig, () =>
        {
            slot = new ComposerCapsuleSlot(new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, _ => null), new CapsuleStyleSetting(rig.Settings), () => new CapsuleSubject(ProviderKind.Claude, a.Id, null, false, null)); slot.Clicked += () => clicks++;
            return view = new HandshakeCapsuleHost(slot, slot, accounts.ThreadId, cards, () => accounts.Accounts, id => new CapsuleContent("M2", "Claude Max 2", 0, false, "Claude Max 2", null), new HandshakePresentationContext(_ => HandshakeReadiness.Injected));
        }, h =>
        {
            Assert.Null(view!.Plan); Assert.Null(view.Ghost);
            var state = new HandshakeEvent(accounts.ThreadId, HandshakeEventKind.PhaseChanged, HandshakePhase.Idle, HandshakePhase.Standby, a.Id.ToString("D"), b.Id.ToString("D"), HandshakeReason.NearLimit, DateTimeOffset.UnixEpoch);
            cards.Raise(state); h.Settle(); Assert.Equal("M2", view.Ghost!.Mono); Assert.Contains("Standby · Claude Max 2 warms up in the background", h.Words());
            slot!.Capsule!.Face.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, clicks); Assert.False(view.Canvas.IsHitTestVisible);
            cards.Raise(state with { To = HandshakePhase.Linking }); h.Settle(); cards.Raise(state with { To = HandshakePhase.InSync, Kind = HandshakeEventKind.InSyncReady }); h.Settle();
            Assert.True(view.Plan!.ShowTick); Assert.True(view.TickShown); Assert.Contains("In sync", h.Words());
            Assert.Equal(reduced, view.Plan.UseCrossfade); if (reduced) Assert.Equal(1, view.GhostScale);
        }, reduceMotion: reduced);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Sound_recipe_is_delivered_once_when_enabled_and_never_while_disabled()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude); var rows = new FakeThreadAccountPort(rig, ProviderKind.Claude); var cards = new FakeCardPort(); var sounds = new List<HandshakeTone>(); var enabled = false;
        MultiAccountHarness.Run(headless, rig, () =>
        {
            var slot = new ComposerCapsuleSlot(new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, _ => null), new CapsuleStyleSetting(rig.Settings), () => new CapsuleSubject(ProviderKind.Claude, a.Id, null, false, null));
            return new HandshakeCapsuleHost(slot, slot, rows.ThreadId, cards, () => rows.Accounts, _ => null, new HandshakePresentationContext(null, () => enabled, tones => sounds.AddRange(tones)));
        }, h =>
        {
            var state = new HandshakeEvent(rows.ThreadId, HandshakeEventKind.PhaseChanged, HandshakePhase.Idle, HandshakePhase.Standby, a.Id.ToString("D"), b.Id.ToString("D"), HandshakeReason.NearLimit, DateTimeOffset.UnixEpoch);
            cards.Raise(state); h.Settle(); Assert.Empty(sounds); enabled = true; cards.Raise(state with { To = HandshakePhase.Linking }); h.Settle();
            Assert.Equal(new[] { 523.25, 783.99 }, sounds.Select(t => t.Hertz)); cards.Raise(state with { To = HandshakePhase.InSync, Kind = HandshakeEventKind.InSyncReady }); h.Settle();
            Assert.Equal(1318.5, sounds[^1].Hertz); var count = sounds.Count; h.Settle(); Assert.Equal(count, sounds.Count);
        });
    }
}
