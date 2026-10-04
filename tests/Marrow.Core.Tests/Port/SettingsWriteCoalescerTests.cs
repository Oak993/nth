using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// SA-1 (customization-SPEC CZ-13.5, voice-SPEC V-16.6): a slider drag must not write the settings store every frame.
public sealed class SettingsWriteCoalescerTests : IDisposable
{
    private readonly TestRuntime runtime = new();

    public void Dispose() => runtime.Dispose();

    private SettingsWriteCoalescer Create() => new(runtime.Defaults);

    private int Writes(string key) => runtime.Defaults.Writes.Count(w => w == key);

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T27")]
    [Trait("Decision", "ED-251")] // CZ-13.5: the first change after a quiet time reaches the store at once
    public void The_first_change_is_written_at_once()
    {
        var coalescer = Create();
        runtime.OnMain(() => coalescer.Write("k", "one"));
        Assert.Equal("one", runtime.Defaults.Object("k"));
        Assert.Equal(1, Writes("k"));
        Assert.False(coalescer.HasPending);
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T27")]
    [Trait("Decision", "ED-251")] // CZ-13.5: at most every 250 ms while values keep changing; the store gets the latest value
    public void Changes_inside_the_interval_wait_and_the_store_gets_only_the_latest()
    {
        var coalescer = Create();
        runtime.OnMain(() => coalescer.Write("k", "one"));
        runtime.AdvanceMilliseconds(100);
        runtime.OnMain(() => { coalescer.Write("k", "two"); coalescer.Write("k", "three"); });
        Assert.Equal("one", runtime.Defaults.Object("k"));
        Assert.True(coalescer.HasPending);
        runtime.AdvanceMilliseconds(149);
        Assert.Equal("one", runtime.Defaults.Object("k"));
        runtime.AdvanceMilliseconds(1); // 250 ms after the first write
        Assert.Equal("three", runtime.Defaults.Object("k"));
        Assert.Equal(2, Writes("k"));
        Assert.False(coalescer.HasPending);
        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(2, Writes("k")); // nothing is written for a value already written
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T27")]
    [Trait("Decision", "ED-251")] // CZ-T27: a 2 s drag at 60 Hz writes at most 9 times plus once at the end
    public void A_two_second_drag_at_60_Hz_writes_at_most_9_times_and_once_more_at_its_end()
    {
        var coalescer = Create();
        const int frames = 120;
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (long)frame;
            runtime.OnMain(() => coalescer.Write("k", value));
            runtime.Advance(TimeSpan.FromSeconds(1.0 / 60));
        }
        var duringDrag = Writes("k");
        Assert.InRange(duringDrag, 2, 9);
        Assert.NotEqual((long)(frames - 1), runtime.Defaults.Object("k")); // the store is behind the drag
        runtime.OnMain(coalescer.Flush); // the drag ends
        Assert.Equal(duringDrag + 1, Writes("k"));
        Assert.Equal((long)(frames - 1), runtime.Defaults.Object("k"));
        runtime.AdvanceMilliseconds(2000);
        Assert.Equal(duringDrag + 1, Writes("k")); // the flush cancelled the wait
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T27")]
    [Trait("Decision", "ED-251")] // CZ-13.5: once when a drag ends, on app exit; nothing pending writes nothing
    public void A_flush_writes_what_is_pending_now_and_a_flush_with_nothing_pending_writes_nothing()
    {
        var coalescer = Create();
        runtime.OnMain(coalescer.Flush);
        Assert.Empty(runtime.Defaults.Writes);
        runtime.OnMain(() => { coalescer.Write("a", 1L); coalescer.Write("a", 2L); coalescer.Write("b", "x"); coalescer.Write("b", "y"); });
        Assert.Equal(new[] { "a", "b" }, runtime.Defaults.Writes);
        runtime.OnMain(coalescer.Flush);
        Assert.Equal(2L, runtime.Defaults.Object("a"));
        Assert.Equal("y", runtime.Defaults.Object("b"));
        Assert.Equal(4, runtime.Defaults.Writes.Count);
        runtime.OnMain(coalescer.Flush);
        Assert.Equal(4, runtime.Defaults.Writes.Count);
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T27")]
    [Trait("Decision", "ED-251")] // CZ-13.5 / CZ-13.4: keys wait independently, so two keys changed together both reach the store at once
    public void Keys_wait_independently_of_each_other()
    {
        var coalescer = Create();
        runtime.OnMain(() => coalescer.Write("a", 1L));
        runtime.AdvanceMilliseconds(50);
        runtime.OnMain(() => { coalescer.Write("a", 2L); coalescer.Write("b", "first"); });
        Assert.Equal(1L, runtime.Defaults.Object("a"));       // a is inside its interval
        Assert.Equal("first", runtime.Defaults.Object("b"));  // b is not
        runtime.AdvanceMilliseconds(200);
        Assert.Equal(2L, runtime.Defaults.Object("a"));
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T27")]
    [Trait("Decision", "ED-251")] // REVIEW (SA-1): a key held while another key's later wait is armed is still written when ITS interval ends (ED-NEW-f-sa-1 item 7)
    public void A_key_whose_interval_ends_first_is_not_held_for_another_keys_later_timer()
    {
        var coalescer = Create();
        runtime.OnMain(() => coalescer.Write("b", 1L));        // t=0: b written
        runtime.AdvanceMilliseconds(100);
        runtime.OnMain(() => coalescer.Write("a", 1L));        // t=100: a written
        runtime.AdvanceMilliseconds(10);
        runtime.OnMain(() => coalescer.Write("a", 2L));        // t=110: a held; its interval ends at t=350
        runtime.AdvanceMilliseconds(10);
        runtime.OnMain(() => coalescer.Write("b", 2L));        // t=120: b held; its interval ends at t=250
        runtime.AdvanceMilliseconds(140);                       // t=260
        Assert.Equal(2L, runtime.Defaults.Object("b"));
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T27")]
    [Trait("Decision", "ED-251")] // after a quiet interval the next change is written at once again
    public void After_a_quiet_interval_the_next_change_is_written_at_once()
    {
        var coalescer = Create();
        runtime.OnMain(() => coalescer.Write("k", 1L));
        runtime.AdvanceMilliseconds(250);
        runtime.OnMain(() => coalescer.Write("k", 2L));
        Assert.Equal(2L, runtime.Defaults.Object("k"));
        Assert.Equal(2, Writes("k"));
    }

    [Fact]
    [Trait("Feature", "SA-1")]
    [Trait("Spec", "CZ-T27")]
    [Trait("Decision", "ED-251")] // a null value removes the key, as UserDefaults.set(nil) does, and is coalesced the same way
    public void A_null_value_removes_the_key()
    {
        var coalescer = Create();
        runtime.OnMain(() => coalescer.Write("k", "v"));
        runtime.AdvanceMilliseconds(10);
        runtime.OnMain(() => coalescer.Write("k", null));
        Assert.Equal("v", runtime.Defaults.Object("k"));
        runtime.OnMain(coalescer.Flush);
        Assert.Null(runtime.Defaults.Object("k"));
    }
}
