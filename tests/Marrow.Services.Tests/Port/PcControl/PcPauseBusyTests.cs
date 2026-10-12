// PORT-ONLY: Tactus, the step a pause waits for (ED-NEW-pcctl-37; DESIGN.md 10.1 "Pausing after this step"). While an app
// pauses, its status names the oldest step in flight that has words; the next one once that ends; none once it is Paused, or
// when no step in flight has words. A unit ended twice ends once.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-37")]
public sealed class PcPauseBusyTests
{
    private const string Paint = "exe:c:\\windows\\mspaint.exe";

    [Fact]
    public void While_pausing_the_status_names_the_oldest_step_in_flight_with_words_and_none_once_paused()
    {
        var clock = new PcFakeClock { NowMs = 10_000 };
        using var pause = new PcPauseControl(clock, new PcRunControl());
        var quiet = pause.Acting(Paint);
        var drag = pause.Acting(Paint, "dragging");
        var click = pause.Acting(Paint, "clicking Save");
        Assert.Null(pause.Status(Paint));

        pause.Pause(PcScope.App(Paint), PcPauseSource.Pill);
        Assert.Equal(PcPausePhase.Pausing, pause.Status(Paint)!.Phase);
        Assert.Equal("dragging", pause.Status(Paint)!.Busy);

        drag.Dispose();
        drag.Dispose();
        Assert.Equal("clicking Save", pause.Status(Paint)!.Busy);
        click.Dispose();
        Assert.Equal(PcPausePhase.Pausing, pause.Status(Paint)!.Phase);
        Assert.Null(pause.Status(Paint)!.Busy);

        var changed = 0;
        pause.Changed += () => changed++;
        quiet.Dispose();
        Assert.Equal(1, changed);
        var paused = pause.Status(Paint)!;
        Assert.Equal(PcPausePhase.Paused, paused.Phase);
        Assert.Null(paused.Busy);
    }
}
