using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Window;

// ED-NEW-perf-startup-structure-1: a move or size drag raises PositionChanged and Resized at the display rate; each settings write
// serializes the whole store and replaces its file, so the frame is written at most every half second, with the latest held frame
// written when the window is hidden, deactivated or the app ends.
public sealed class WindowFrameAutosaveTests : IDisposable
{
    private const string Name = "DroppyCodeMainWindow";
    private readonly TestRuntime runtime = new();
    private WindowFrameAutosave.Frame? frame = new(10, 20, 1320, 860);

    public void Dispose() => runtime.Dispose();

    private WindowFrameAutosave Create() => new(runtime.Defaults, Name, () => frame);

    private int Writes() => runtime.Defaults.Writes.Count(key => key == Name);

    private IReadOnlyDictionary<string, object> Saved() => Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(runtime.Defaults.Object(Name));

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-1")] // ED-NEW-perf-startup-structure-1
    public void The_first_frame_after_a_quiet_time_is_written_at_once_in_the_stores_format()
    {
        var autosave = Create();
        runtime.OnMain(autosave.Changed);
        Assert.Equal(1, Writes());
        var saved = Saved();
        Assert.Equal(10L, saved["x"]);
        Assert.Equal(20L, saved["y"]);
        Assert.Equal(1320d, saved["width"]);
        Assert.Equal(860d, saved["height"]);
        Assert.False(autosave.HasPending);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-1")] // ED-NEW-perf-startup-structure-1
    public void A_two_second_drag_at_60_Hz_writes_at_most_five_times_and_the_last_frame_is_saved_within_half_a_second()
    {
        var autosave = Create();
        for (var tick = 0; tick < 120; tick++)
        {
            frame = new(10 + tick, 20 + tick, 1320, 860);
            runtime.OnMain(autosave.Changed);
            runtime.AdvanceMilliseconds(1000.0 / 60);
        }
        Assert.InRange(Writes(), 1, 5); // the first change, then one per 500 ms
        runtime.AdvanceMilliseconds(500); // the drag ended: the held frame is written when its interval ends
        Assert.False(autosave.HasPending);
        Assert.Equal(129L, Saved()["x"]);
        Assert.Equal(139L, Saved()["y"]);
        var after = Writes();
        runtime.AdvanceMilliseconds(5000);
        Assert.Equal(after, Writes()); // nothing is written for a frame already written
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-1")] // ED-NEW-perf-startup-structure-1
    public void A_frame_equal_to_the_last_one_is_not_written_again()
    {
        var autosave = Create();
        runtime.OnMain(autosave.Changed);
        runtime.AdvanceMilliseconds(600);
        runtime.OnMain(autosave.Changed); // a move that reports the same frame as the resize before it
        Assert.Equal(1, Writes());
        Assert.False(autosave.HasPending);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-1")] // ED-NEW-perf-startup-structure-1
    public void Flush_writes_the_held_frame_now_and_nothing_is_left_waiting()
    {
        var autosave = Create();
        runtime.OnMain(autosave.Changed);
        frame = new(300, 400, 1500, 900);
        runtime.AdvanceMilliseconds(100);
        runtime.OnMain(autosave.Changed);
        Assert.True(autosave.HasPending);
        Assert.Equal(10L, Saved()["x"]);
        runtime.OnMain(autosave.Flush);
        Assert.False(autosave.HasPending);
        Assert.Equal(300L, Saved()["x"]);
        Assert.Equal(1500d, Saved()["width"]);
        Assert.Equal(2, Writes());
        runtime.AdvanceMilliseconds(2000);
        Assert.Equal(2, Writes()); // the cancelled timer writes nothing more
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-1")] // ED-NEW-perf-startup-structure-1
    public void A_window_that_is_not_in_its_normal_state_writes_no_frame_and_keeps_the_one_it_held()
    {
        var autosave = Create();
        runtime.OnMain(autosave.Changed);
        frame = new(300, 400, 1500, 900);
        runtime.AdvanceMilliseconds(100);
        runtime.OnMain(autosave.Changed);
        frame = null; // minimised: parked at (-32000, -32000)
        runtime.OnMain(autosave.Changed);
        runtime.OnMain(autosave.Flush);
        Assert.Equal(300L, Saved()["x"]); // the last normal frame, not the parked one
        Assert.Equal(2, Writes());
    }
}
