using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class SwitchLatencyTests
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-024")] // Core/Support/SwitchLatency.swift:20-21
    [Trait("Decision", "ED-005")]
    public void Start_is_a_recorded_no_op_and_never_throws()
    {
        // ED-005: the Swift guard `AppInfo.isDevelopment, observer == nil` (:20-21) means the
        // observer is installed only in the separate Dev bundle (iordv.droppycode.dev); in the
        // shipped bundle Start() always returns immediately and no CFRunLoopObserver is ever
        // created. ED-005 decided not to port the observer or its os_log instrumentation at all:
        // this member exists only so App-layer callers keep compiling, and it does nothing.
        SwitchLatency.Start();
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-024")] // Core/Support/SwitchLatency.swift:41-45
    [Trait("Decision", "ED-005")]
    public void Note_is_a_recorded_no_op_and_never_throws()
    {
        // Swift :43 guards `observer != nil`; since Start() never installs an observer (ED-005),
        // the Swift itself never reaches the log.notice call at :44 in the shipped bundle either.
        SwitchLatency.Note("a note beside the switch timings");
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-024")] // Core/Support/SwitchLatency.swift:47-57
    [Trait("Decision", "ED-005")]
    public void NoteSwitch_is_a_recorded_no_op_and_never_throws()
    {
        // Swift :49 guards `observer != nil`; the signpost (:51) and window reset (:52-56) are
        // likewise unreachable in the shipped bundle. ED-005: not ported.
        SwitchLatency.NoteSwitch("a switch title longer than twenty-four characters");
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-024")] // Core/Support/SwitchLatency.swift:1-58
    [Trait("Decision", "ED-005")]
    public void Repeated_calls_in_any_order_stay_a_no_op_with_no_observable_state()
    {
        // Exercises every member, interleaved, the way DroppyCodeApp.swift, AppModel.swift and
        // RootView.swift call this type, to confirm ED-005's "entire observable behavior is
        // nothing at all" holds for any call sequence: no exception, no state to observe.
        SwitchLatency.NoteSwitch("first switch");
        SwitchLatency.Start();
        SwitchLatency.Note("busy");
        SwitchLatency.NoteSwitch("second switch");
        SwitchLatency.Note("busy again");
        SwitchLatency.Start();
    }
}
