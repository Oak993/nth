// PORT-ONLY: Tactus, the hands-off request on screen (ED-NEW-pcctl-38; DESIGN.md 2.7, 4.4 "Fail closed", 4.5 "Hands-off
// behaviour", 4.7 "Sound when Tactus needs a moment"). The pill's Do it now and Not now answer the request it shows; the
// mark's ring fills each frame from the gate's stillness and drains smoothly when the user moves; a new request plays the
// soft sound only with the setting on; a stop chord that fails shows "Stop key is taken" with Settings, and each try says
// whether it works. Fakes and a manual clock; nothing leaves the headless window.
using Avalonia.Threading;
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class TactusScreenHandsOffTests(HeadlessSession headless)
{
    private const string Paint = "exe:c:\\windows\\mspaint.exe";
    private const string Excel = "exe:c:\\office\\excel.exe";

    private static readonly PcSettings On = PcSettings.Default with { Enabled = true, RealInput = true };

    private static PcManualClock Clock()
    {
        var clock = new PcManualClock(inline: true);
        clock.Advance(10_000);
        return clock;
    }

    // The lease's polls run here, on the UI thread, as the screen's work must: a continuation does not run inline under the
    // UI thread's synchronization context (the runtime queues it to the thread pool), so Advance runs without it.
    private static void Advance(PcManualClock clock, int milliseconds)
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            clock.Advance(milliseconds);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    private static void Settle(PcScreenRig rig)
    {
        rig.Pill.Clock.Advance(TimeSpan.FromSeconds(0.1));
        Dispatcher.UIThread.RunJobs();
    }

    private static PcBorrowWait Wait(string stream) => new(stream, "a step") { Words = new("click Save", "clicked Save", "one click") };

    private static PcRawInput Key(PcManualClock clock, bool down) => new(clock.NowMs, PcRawKind.Key, down, 0x41, false, 0) { HeldKeys = down ? 1 : 0 };

    private static PcRawInput Move(PcManualClock clock) => new(clock.NowMs, PcRawKind.Move, false, 0, false, 0) { Moved = new(4, 4) };

    private static ControlPill Shown(PcScreenRig rig) => rig.Pill.Host.Pill(PcTestMonitors.Main.Id)!;

    [Fact]
    public void Do_it_now_and_Not_now_on_the_pill_answer_the_request_it_shows_and_the_sound_plays_once_with_the_setting_on() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var clock = Clock();
        using var rig = new PcScreenRig(On with { Sound = true }, clock: clock);
        rig.Parts.Input.Record(Key(clock, down: true));
        var paint = Wait(Paint);
        var excel = Wait(Excel);
        var first = rig.Assembly.Lease.Still(paint, null, CancellationToken.None);
        var second = rig.Assembly.Lease.Still(excel, null, CancellationToken.None);
        Settle(rig);

        var pill = Shown(rig);
        Assert.Equal("Hands off for a moment", pill.View!.Title);
        Assert.Equal(PillGroupKind.HandsOff, pill.View.Group);
        Assert.Equal(1, rig.Chimes);

        pill.Raise(PillCommandKind.DoItNow);
        Assert.True(paint.DoItNowChosen);
        Assert.False(excel.DoItNowChosen);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Release the keys first", Shown(rig).View!.Line);

        pill.Raise(PillCommandKind.NotNow);
        Assert.True(paint.NotNowChosen);
        Assert.False(excel.NotNowChosen);
        excel.NotNow();
        Advance(clock, RealInputLease.PollMs);
        Assert.True(first.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(second.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(PcBorrowStatus.Deferred, first.Result);
        Assert.Equal(1, rig.Chimes);
    });

    [Fact]
    public void Without_the_setting_a_request_plays_no_sound() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var clock = Clock();
        using var rig = new PcScreenRig(On, clock: clock);
        rig.Parts.Input.Record(Key(clock, down: true));
        var paint = Wait(Paint);
        var still = rig.Assembly.Lease.Still(paint, null, CancellationToken.None);
        Settle(rig);

        Assert.Equal(PillGroupKind.HandsOff, Shown(rig).View!.Group);
        Assert.Equal(0, rig.Chimes);
        paint.NotNow();
        Advance(clock, RealInputLease.PollMs);
        Assert.True(still.Wait(TimeSpan.FromSeconds(5)));
    });

    [Fact]
    public void The_ring_fills_each_frame_from_the_gate_s_stillness_and_drains_smoothly_when_the_user_moves() => headless.OnUiThread(() =>
    {
        var source = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(source);
        var clock = Clock();
        using var rig = new PcScreenRig(On, clock: clock);
        rig.Parts.Input.Record(Move(clock));
        var paint = Wait(Paint);
        var still = rig.Assembly.Lease.Still(paint, null, CancellationToken.None);
        Settle(rig);
        Assert.Equal(TactusMarkState.HandsOff, Shown(rig).View!.Mark);

        // 175 ms still of the pointer's 350: the ring is the gate's stillness then
        Advance(clock, 175);
        source.Advance(TimeSpan.FromMilliseconds(16));
        var stillness = PcInputGate.Stillness(rig.Parts.Input.Snapshot(), clock.NowMs);
        Assert.InRange(stillness, 0.2, 0.6);
        Assert.Equal(stillness, Shown(rig).RingFill, 6);

        // the user moves: the stillness is 0, and the ring drains 16 ms of its 260 a frame, never at once
        rig.Parts.Input.Record(Move(clock));
        rig.Pill.Clock.Advance(TimeSpan.FromMilliseconds(16));
        source.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(stillness - (0.016 / HandsRing.DrainSeconds), Shown(rig).RingFill, 3);
        rig.Pill.Clock.Advance(TimeSpan.FromMilliseconds(16));
        source.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(stillness - (0.032 / HandsRing.DrainSeconds), Shown(rig).RingFill, 3);

        paint.NotNow();
        Advance(clock, RealInputLease.PollMs);
        Assert.True(still.Wait(TimeSpan.FromSeconds(5)));
        Dispatcher.UIThread.RunJobs();
        source.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Null(rig.Feed.HandsOff);
    });

    [Fact]
    public void A_stop_chord_that_fails_shows_Stop_key_is_taken_with_Settings_and_each_try_says_whether_it_works() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig(On);
        var tries = new List<bool>();
        rig.Hotkeys.StopKeyChecked += tries.Add;
        rig.KeysHost.Refuse(TactusHotkeys.StopId);

        Assert.False(rig.Hotkeys.Arm());
        Assert.False(rig.Hotkeys.Arm());
        Settle(rig);

        var view = Shown(rig).View!;
        Assert.Equal("Stop key is taken by another app", view.Title);
        Assert.Equal("New steps wait for a stop key that works", view.Line);
        Assert.Equal(PillGroupKind.Settings, view.Group);
        Assert.Equal([false, false], tries);
        Assert.Single(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.StopKeyTaken);

        Shown(rig).Raise(PillCommandKind.Settings);
        Assert.Equal(1, rig.SettingsOpened);
        Assert.DoesNotContain(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.StopKeyTaken);

        using var working = new PcScreenRig(On);
        var works = new List<bool>();
        working.Hotkeys.StopKeyChecked += works.Add;
        Assert.True(working.Hotkeys.Arm());
        Assert.True(working.Hotkeys.Arm());
        Assert.Equal([true], works);
    });
}
