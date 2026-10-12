// PORT-ONLY: Tactus pill v2, the hands-off family's view (ED-NEW-pcctl-38; DESIGN.md 4.5 "States", "Hands-off behaviour";
// canvas pc-control-ui gallery 12 and `cuP[4].kp`). A live notice shows even with nothing working (no Pause then); the hands-off request turns the mark's ring on and names when it began
// (a new request starts the buttons' shield again), as the protected wait does for its Not now; "Stop key is taken" with
// nothing working says new steps wait; the protected wait can speak in the gate's own words; Put them back says what it did.
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class PillStatesHandsOffTests
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");

    private static PillView View(PillNotice notice, double now = 10) => PillStates.Compute(new PillFacts { Apps = [Excel], Notices = [notice] }, now);

    [Fact]
    public void The_request_turns_the_ring_on_and_names_when_it_began()
    {
        var hands = View(PillNotice.HandsOff("Click Save in Excel", "Tactus needs the mouse for one step: click Save in Excel.", 0, false, false, 9.5));
        Assert.Equal(TactusMarkState.HandsOff, hands.Mark);
        Assert.Equal(9.5, hands.RequestSince);
        Assert.Equal("Click Save in Excel", hands.Line);
        Assert.Equal("Release the mouse first", View(PillNotice.HandsOff("Click Save in Excel", "s", 0, false, true, 9.5)).Line);
        Assert.Equal("Release the keys first", View(PillNotice.HandsOff("Click Save in Excel", "s", 0, false, true, 9.5, keysHeld: true)).Line);

        var protectedWait = View(PillNotice.ProtectedForeground(admin: true, 9.25));
        Assert.Equal(TactusMarkState.Working, protectedWait.Mark);
        Assert.Equal(9.25, protectedWait.RequestSince);

        Assert.Null(View(PillNotice.KeptKeys(6, 9)).RequestSince);
        Assert.Equal(TactusMarkState.Working, View(PillNotice.Done("Clicked Save in Excel", 9.9)).Mark);
        Assert.Equal(TactusMarkState.Working, PillStates.Compute(new PillFacts { Apps = [Excel] }, 10).Mark);
    }

    [Fact]
    public void With_nothing_working_a_live_notice_still_shows_without_Pause_and_Stopped_shows_once_it_ends()
    {
        var kept = PillStates.Compute(new PillFacts { Notices = [PillNotice.KeptKeys(6, 9)] }, 10);
        Assert.Equal("Tactus kept 6 keys you typed", kept.Title);
        Assert.Equal(PillGroupKind.PutBack, kept.Group);
        Assert.False(kept.Stopped);

        var done = PillStates.Compute(new PillFacts { Notices = [PillNotice.Done("Clicked Save in Excel", 9.5)] }, 10);
        Assert.Equal("Done, all yours", done.Title);
        Assert.Equal("Clicked Save in Excel", done.Line);
        Assert.Equal(PillGroupKind.None, done.Group);
        Assert.False(done.Working);
        Assert.False(done.ShowsStop);

        var after = PillStates.Compute(new PillFacts { Notices = [PillNotice.Done("Clicked Save in Excel", 9.5)] }, 11);
        Assert.Equal("Stopped", after.Title);
        Assert.True(after.Stopped);

        // a stop of everything still reads Stopped for its 2 s
        var stop = PillStates.Compute(new PillFacts { Stopped = new PillStop(9.5), Notices = [PillNotice.KeptKeys(6, 9)] }, 10);
        Assert.Equal("Stopped", stop.Title);
        Assert.True(stop.Stopped);
    }

    [Fact]
    public void Stop_key_taken_with_nothing_working_says_new_steps_wait_and_keeps_the_working_line_otherwise()
    {
        var alone = PillStates.Compute(new PillFacts { Notices = [PillNotice.StopKeyTaken(9)] }, 10);
        Assert.Equal("Stop key is taken by another app", alone.Title);
        Assert.Equal("New steps wait for a stop key that works", alone.Line);
        Assert.Equal(PillGroupKind.Settings, alone.Group);

        Assert.Equal("Excel: Filling row 12 of 40", View(PillNotice.StopKeyTaken(9)).Line);
    }

    [Fact]
    public void The_protected_wait_speaks_in_the_gate_s_words_and_Put_them_back_says_what_it_did()
    {
        var prompt = View(PillNotice.ProtectedForeground("Waiting until Windows' prompt closes", 9));
        Assert.Equal("Waiting until Windows' prompt closes", prompt.Title);
        Assert.Equal(PillGroupKind.NotNow, prompt.Group);

        var put = PillNotice.PutBack(6, 9);
        Assert.Equal("Put back 6 keys", View(put).Title);
        Assert.Equal("Where you typed them", View(put).Line);
        Assert.Equal(1.2, put.Lasts);
        Assert.Equal("Put back 1 key", PillNotice.PutBack(1, 9).Title);
        Assert.Equal("Using Excel", View(put, 10.3).Title);
    }
}
