using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Composer.MultiAccount;

[Collection("Headless")]
[Trait("Rule", "R-TEST-4")]
public sealed class LiveCapsuleStateTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Pi)]
    public void The_same_in_sync_tick_follows_the_card_stream_and_clears_when_the_pipeline_waits(ProviderKind service)
    {
        var rig = new AccountsRig();
        var first = rig.Connect(service);
        var next = rig.ConnectAnother(service);
        var cards = new FakeCardPort(); var thread = Guid.NewGuid();
        ComposerCapsuleSlot slot = null!;
        MultiAccountHarness.Run(headless, rig, () =>
        {
            slot = new(new(rig.Accounts, rig.Pills, rig.IsConnected, id => rig.Usage.GetValueOrDefault(id)),
                new CapsuleStyleSetting(rig.Settings), () => new(service, first.Id, null, false, null));
            ComposerCapsuleSeams.Observe(slot, thread, cards, null);
            return slot;
        }, h =>
        {
            cards.Raise(new(thread, HandshakeEventKind.InSyncReady, HandshakePhase.Linking, HandshakePhase.InSync,
                first.Id.ToString("D"), next.Id.ToString("D"), HandshakeReason.NearLimit, DateTimeOffset.UtcNow, Detail: "in sync"));
            h.Settle();
            Assert.Contains(slot.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "✓" && text.IsEffectivelyVisible);
            cards.Raise(new(thread, HandshakeEventKind.PhaseChanged, HandshakePhase.InSync, HandshakePhase.Waiting,
                first.Id.ToString("D"), next.Id.ToString("D"), HandshakeReason.HardLimit, DateTimeOffset.UtcNow));
            h.Settle();
            Assert.DoesNotContain(slot.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "✓" && text.IsEffectivelyVisible);
        });
    }
}
