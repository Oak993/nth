using Marrow.App.Port.Voice.Countdown;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.App.Tests.Port.Voice.Countdown;

// VT-17 (OD-0106 Countdown C; OD-0099 Q-V-6 B; voice-SPEC V-7.12): the line as the pill will read it from a live session: nothing before 4:30, the last 30 s on the clock,
// the cap finishing the text as a click would, and the two screen-reader announcements. The session runs on scripted seams and the manual clock.
public sealed class CapCountdownLineTests
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-6 B
    public void There_is_no_line_before_270_seconds_or_outside_a_session()
    {
        using var rig = new CountdownRig();
        Assert.Null(rig.Line.Frame());

        rig.Start();
        rig.Advance(269.99);

        Assert.Null(rig.Line.Frame());
        Assert.Empty(rig.Announcements);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-6 B; Oracle OD-0106
    public void From_270_seconds_the_line_is_full_and_drains_with_the_sessions_clock()
    {
        using var rig = new CountdownRig();
        rig.Start();
        rig.Advance(270);

        var full = rig.Line.Frame();
        Assert.True(full!.Value.Visible);
        Assert.Equal(1.0, full.Value.Fraction, 9);
        Assert.Equal(CountdownPalette.Dark.Neutral, full.Value.Colour);

        rig.Advance(15);
        var half = rig.Line.Frame()!.Value;
        Assert.Equal(0.5, half.Fraction, 9);
        Assert.Equal(CountdownPalette.Dark.Neutral, half.Colour); // yellow starts at 0:15, not before

        rig.Advance(0.8 + 0.2);
        var yellow = rig.Line.Frame()!.Value;
        Assert.Equal(CapCountdown.Frame(TimeSpan.FromSeconds(14), false, CountdownPalette.Dark), yellow);
        Assert.Equal(1.0, yellow.Brightening, 9);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle OD-0106
    public void The_lines_palette_and_Reduce_Motion_are_read_at_each_frame()
    {
        using var rig = new CountdownRig();
        rig.Start();
        rig.Advance(270);
        rig.Advance(14.9);

        var normal = rig.Line.Frame()!.Value;
        rig.ReduceMotion = true;
        rig.Palette = CountdownPalette.Light;
        var reduced = rig.Line.Frame()!.Value;

        Assert.Equal(CapCountdown.Frame(TimeSpan.FromSeconds(15.1), false, CountdownPalette.Dark), normal);
        Assert.Equal(CapCountdown.Frame(TimeSpan.FromSeconds(15.1), true, CountdownPalette.Light), reduced);
        Assert.Equal(CountdownPalette.Light.Neutral, reduced.Colour);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req V-7.12; Req Q-V-6 B
    public void At_the_cap_the_text_finishes_as_if_the_orb_were_clicked_and_the_line_is_gone()
    {
        using var capped = new CountdownRig();
        capped.Start();
        capped.Advance(299.99);
        Assert.True(capped.Line.Frame()!.Value.Visible);
        Assert.InRange(capped.Line.Frame()!.Value.Fraction, 0, 0.001);

        capped.Advance(0.02);

        Assert.Equal(VoiceStopReason.Cap, capped.Session.LastStopReason);
        Assert.NotEqual(VoiceSessionState.Listening, capped.Session.State);
        Assert.Null(capped.Line.Frame());

        using var clicked = new CountdownRig();
        clicked.Start();
        clicked.Advance(100);
        clicked.Session.Activate();
        clicked.Advance(3);
        capped.Advance(3);
        Assert.Equal(clicked.States.Skip(1), capped.States.Skip(1)); // the same states after Listening: a click and the cap end alike
        Assert.Equal(VoiceStopReason.Click, clicked.Session.LastStopReason);
    }

    [Fact]
    [Trait("Decision", "ED-267")] // ED-NEW-cloud-vt-17, numbered by the lead at merge
    public void A_screen_reader_hears_the_countdown_once_at_30_seconds_and_once_at_10()
    {
        using var rig = new CountdownRig();
        rig.Start();
        rig.Advance(269.99);
        Assert.Empty(rig.Announcements);

        rig.Advance(0.01);
        Assert.Equal(["30 seconds left."], rig.Announcements);
        rig.Advance(19.98);
        Assert.Single(rig.Announcements);
        rig.Advance(0.02);
        Assert.Equal(["30 seconds left.", "10 seconds left."], rig.Announcements);
        rig.Advance(15);
        Assert.Equal(2, rig.Announcements.Count); // nothing more: the cap's own "Finishing the text." is V-14.5's
    }

    [Fact]
    [Trait("Decision", "ED-267")] // ED-NEW-cloud-vt-17, numbered by the lead at merge
    public void A_session_that_ends_before_the_10_second_moment_makes_no_late_announcement()
    {
        using var rig = new CountdownRig();
        rig.Start();
        rig.Advance(280);
        rig.Session.Activate(); // click: finish early
        rig.Advance(30);

        Assert.Equal(["30 seconds left."], rig.Announcements);
        Assert.Null(rig.Line.Frame());
    }

    [Fact]
    [Trait("Decision", "ED-267")] // ED-NEW-cloud-vt-17, numbered by the lead at merge
    public void A_second_session_counts_down_afresh()
    {
        using var rig = new CountdownRig();
        rig.Start();
        rig.Advance(280);
        rig.Session.Activate();
        rig.Advance(3);
        rig.Start();
        rig.Advance(270);

        Assert.Equal(1.0, rig.Line.Frame()!.Value.Fraction, 9);
        Assert.Equal(2, rig.Announcements.Count(a => a == "30 seconds left."));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // dispose
    public void A_disposed_line_announces_nothing_more()
    {
        using var rig = new CountdownRig();
        rig.Start();
        rig.Line.Dispose();
        rig.Advance(299);

        Assert.Empty(rig.Announcements);
    }
}
