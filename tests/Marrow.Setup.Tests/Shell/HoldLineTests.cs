using Marrow.Setup.Motion;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, "Close", and 3b's table (the Close-paused line) and test list; unit
// U1 (ED-NEW-inst-u1-1). The line is a Reveal: it fades in 60 ms after it is asked for with a 5 DIP settle and moves
// nothing above it; it leaves by fading with a 4 DIP lift and only then gives up its place; asked back mid-flight it turns
// around from where it is, never jumping. A try to close brightens it (the canvas's 1.4 s ping). Calm (Animation effects
// off): no movement, short fades, and a try keeps it bright. Frames are driven by hand at 60 Hz.
[Collection("SetupHeadless")]
public sealed class HoldLineTests(SetupHeadlessSession headless)
{
    private const string Words = "You can close this window once nth is safely in place.";
    private const double Frame = 1.0 / 60;

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void It_takes_its_place_at_once_then_fades_in_after_60_ms_while_settling_5_DIP()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames) = Rig(calm: false);
            Assert.False(line.IsVisible);

            line.Show(Words);
            Assert.True(line.IsVisible);
            Assert.Equal(Words, line.Words);
            Assert.Equal(0, line.LineOpacity);
            Assert.Equal(HoldLine.SettleFrom, line.LineOffset);

            frames.Run(0.05);
            Assert.Equal(0, line.LineOpacity);
            Assert.True(line.LineOffset > HoldLine.SettleFrom, "the settle rides with the commit");

            frames.Run(0.1);
            Assert.InRange(line.LineOpacity, 0.05, 0.95);

            frames.Run(1);
            Assert.Equal(1, line.LineOpacity);
            Assert.Equal(0, line.LineOffset);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void It_leaves_with_a_lift_and_gives_up_its_place_only_once_faded()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames) = Rig(calm: false);
            line.Show(Words);
            frames.Run(1);

            line.Hide();
            frames.Run(0.08);
            Assert.True(line.IsVisible);
            Assert.True(line.LineOffset < 0, "it lifts as it fades");
            Assert.InRange(line.LineOpacity, 0.01, 0.99);

            frames.Run(1);
            Assert.False(line.IsVisible);
            Assert.Equal(0, line.LineOpacity);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Asked_back_mid_flight_it_turns_around_from_where_it_is()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames) = Rig(calm: false);
            line.Show(Words);
            frames.Run(1);
            line.Hide();
            frames.Run(0.06);
            var opacity = line.LineOpacity;
            var offset = line.LineOffset;

            line.Show(Words);
            var previous = opacity;
            for (var i = 0; i < 60; i++)
            {
                frames.Frame(Frame);
                Assert.True(Math.Abs(line.LineOpacity - previous) < 0.3, $"frame {i} jumped from {previous} to {line.LineOpacity}");
                previous = line.LineOpacity;
            }
            Assert.True(line.LineOpacity > opacity);
            Assert.True(line.IsVisible);
            Assert.True(offset < 0);
            Assert.Equal(1, line.LineOpacity, 2);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Ten_fast_toggles_never_jump_and_end_where_the_last_one_asked()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames) = Rig(calm: false);
            var previous = 0.0;
            for (var toggle = 0; toggle < 10; toggle++)
            {
                if (toggle % 2 == 0) line.Show(Words);
                else line.Hide();
                for (var i = 0; i < 4; i++)
                {
                    frames.Frame(Frame);
                    Assert.True(Math.Abs(line.LineOpacity - previous) < 0.3, $"toggle {toggle} jumped");
                    previous = line.LineOpacity;
                }
            }
            frames.Run(1.5);
            Assert.False(line.IsVisible);
            Assert.Equal(0, line.LineOpacity);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_try_to_close_brightens_it_for_the_canvas_ping_and_lets_it_settle_back()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames) = Rig(calm: false);
            line.Show(Words);
            frames.Run(1);

            line.Ping();
            frames.Run(0.3);
            Assert.Equal(1, line.Brightness);
            frames.Run(0.5);
            Assert.Equal(1, line.Brightness);
            frames.Run(0.3);
            Assert.InRange(line.Brightness, 0.01, 0.99);
            frames.Run(0.6);
            Assert.Equal(0, line.Brightness);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_hidden_line_is_not_brightened()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames) = Rig(calm: false);
            line.Ping();
            frames.Run(0.5);
            Assert.Equal(0, line.Brightness);
            Assert.False(line.IsVisible);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_never_moves_it_fades_quickly_and_a_try_keeps_it_bright()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames) = Rig(calm: true);
            line.Show(Words);
            Assert.Equal(0, line.LineOffset);
            for (var i = 0; i < 30; i++)
            {
                frames.Frame(Frame);
                Assert.Equal(0, line.LineOffset);
            }
            Assert.Equal(1, line.LineOpacity, 2);

            line.Ping();
            frames.Run(3);
            Assert.Equal(1, line.Brightness);

            line.Hide();
            for (var i = 0; i < 30; i++)
            {
                frames.Frame(Frame);
                Assert.Equal(0, line.LineOffset);
            }
            Assert.False(line.IsVisible);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Without_a_frame_loop_it_goes_straight_to_its_end_state()
    {
        headless.OnUiThread(() =>
        {
            var line = new HoldLine(() => false);
            line.Show(Words);
            Assert.Equal(1, line.LineOpacity);
            Assert.Equal(0, line.LineOffset);
            line.Hide();
            Assert.False(line.IsVisible);
            line.Now(Words);
            Assert.True(line.IsVisible);
            Assert.Equal(1, line.LineOpacity);
        });
    }

    private static (HoldLine Line, ManualFrameSource Frames) Rig(bool calm)
    {
        var frames = new ManualFrameSource();
        var line = new HoldLine(() => calm);
        line.Attach(new Animator(frames));
        return (line, frames);
    }
}
