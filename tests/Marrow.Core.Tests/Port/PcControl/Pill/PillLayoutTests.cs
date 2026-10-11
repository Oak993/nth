// PORT-ONLY: the pill's width (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3, 10.11, canvas v4): the title always fits; the
// second line fits except the step line while working; Stop's key cap folds when the text would not fit beside it and
// always for an ask; Stop's slot never changes when the button group swaps; 200 to 440 DIP.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillLayoutTests
{
    // A Pause button group of 36 DIP and a Stop button of 100 DIP with a 50 DIP key cap.
    private static PillLayoutInput Working(double title, double step) => new(title, step, 36, 100, 50, ShowsStop: true, Working: true, Ask: false);

    [Fact]
    public void The_title_always_fits_and_the_step_line_never_widens_the_pill()
    {
        var shortStep = PillLayout.Compute(Working(80, 60));
        var longStep = PillLayout.Compute(Working(80, 400));

        Assert.Equal(shortStep, longStep);
        Assert.False(shortStep.CapFolded);
        Assert.Equal(80 + 36 + 6 + 100 + 6 + 195, shortStep.Width);
        Assert.True(longStep.TextWidth >= 80 + 2);
    }

    [Fact]
    public void Off_the_working_state_the_second_line_fits_too()
    {
        var paused = PillLayout.Compute(new PillLayoutInput(120, 210, 36, 100, 50, true, Working: false, Ask: false));

        Assert.True(paused.TextWidth >= 212, "the second line fits: " + paused.TextWidth);
    }

    [Fact]
    public void Stop_folds_its_key_cap_when_the_text_would_not_fit_beside_it()
    {
        // "Paused in Excel" / "Ctrl+Alt+Space resumes": fits at 440 only with the cap folded.
        var input = new PillLayoutInput(104, 158, 36, 154, 88, true, Working: false, Ask: false);
        var result = PillLayout.Compute(input);

        Assert.True(PillLayout.Fixed(input, capFolded: false) + 160 > PillLayout.MaxWidth);
        Assert.True(result.CapFolded);
        Assert.True(result.Width <= PillLayout.MaxWidth);
        Assert.True(result.TextWidth >= 160);
    }

    [Fact]
    public void An_ask_always_folds_the_cap()
    {
        var result = PillLayout.Compute(new PillLayoutInput(90, 60, 0, 154, 88, true, Working: false, Ask: true));

        Assert.True(result.CapFolded);
        Assert.Equal(80 + (154 - 88) + 6 + 195, result.Width);
    }

    [Fact]
    public void Swapping_the_button_group_never_moves_Stop()
    {
        var pause = new PillLayoutInput(80, 60, 36, 154, 88, true, true, false);
        var resume = pause with { GroupWidth = 92 };

        // Stop's part of the fixed width is the same whatever the group; only the group's own width differs.
        Assert.Equal(92 - 36, PillLayout.Fixed(resume, false) - PillLayout.Fixed(pause, false));
    }

    [Fact]
    public void The_width_stays_between_200_and_440_DIP()
    {
        Assert.Equal(200, PillLayout.Compute(new PillLayoutInput(10, 0, 0, 0, 0, false, false, false)).Width);
        Assert.Equal(440, PillLayout.Compute(new PillLayoutInput(900, 0, 0, 154, 88, true, false, false)).Width);
    }
}
