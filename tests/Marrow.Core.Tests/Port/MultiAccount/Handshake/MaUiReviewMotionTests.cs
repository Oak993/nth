using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;
using Xunit;

namespace Marrow.Core.Tests.Port.MultiAccount.Handshake;

// T26 REVIEW ONLY: minimum presentation time follows the locked animation, even when
// real engine events arrive together. Slow engine events may extend these intervals.
public sealed class MaUiReviewMotionTests
{
    [Theory, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    [InlineData("MAUI-051")] // HTML 3039: standby 1000 -> linking 2000 = 1000ms
    [InlineData("MAUI-052")] // HTML 3039: in-sync 3000 -> switch 4400 = 1400ms
    public void Queued_events_preserve_the_locked_phase_intervals(string finding)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude);
        var accounts = new FakeThreadAccountPort(rig, ProviderKind.Claude); var cards = new FakeCardPort();
        var clock = new ManualTimeProvider(); var thread = Guid.NewGuid();
        using var model = new HandshakeMotionModel(thread, cards, () => accounts.Accounts, clock);
        void Send(HandshakePhase phase, HandshakeEventKind kind = HandshakeEventKind.PhaseChanged) =>
            cards.Raise(new(thread, kind, HandshakePhase.Idle, phase, a.Id.ToString(), b.Id.ToString(), HandshakeReason.NearLimit, clock.GetUtcNow()));
        if (finding == "MAUI-051")
        {
            Send(HandshakePhase.Standby); Send(HandshakePhase.Linking);
            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.Equal(HandshakePhase.Standby, model.Plan(420, 160, false).Phase);
        }
        else
        {
            Send(HandshakePhase.InSync, HandshakeEventKind.InSyncReady); Send(HandshakePhase.Switch);
            clock.Advance(TimeSpan.FromMilliseconds(1399));
            Assert.Equal(HandshakePhase.InSync, model.Plan(420, 160, false).Phase);
        }
    }
}
