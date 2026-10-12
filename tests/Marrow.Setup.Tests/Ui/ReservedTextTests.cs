using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Ui;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3b ("Reserved, not animated": the stall line's height is kept) and 4
// (the stall line); unit U3 (ED-NEW-inst-u3-1). The line keeps the height of its tallest wording at every text size and
// width, so the stall line arriving or leaving moves nothing; the words cross-fade (out, swap, in 60 ms later), fade back
// when asked back early, take 0.15 s fades in Calm, swap at once with no frame loop, and are read by Narrator as a polite
// live region. Frames are driven by hand.
[Collection("SetupHeadless")]
public sealed class ReservedTextTests(SetupHeadlessSession headless)
{
    private static readonly string Usual = SetupStrings.InstallingSubtitle;
    private static readonly string Slow = SetupStrings.InstallingSlow;

    [Theory]
    [InlineData(1.0, 420)]
    [InlineData(1.5, 420)]
    [InlineData(2.25, 420)]
    [InlineData(1.0, 220)]
    [InlineData(2.25, 220)]
    [Trait("Decision", "ED-247")]
    public void The_line_keeps_its_tallest_wording_s_height_whichever_it_shows(double textScale, double width)
    {
        headless.OnUiThread(() =>
        {
            var (line, _, window, probe, below) = Rig(textScale, width);
            window.Show();
            window.UpdateLayout();
            var height = line.Bounds.Height;
            var under = below.Bounds;

            Assert.Equal(probe.Bounds.Height, height, 1);
            line.Now(Slow);
            window.UpdateLayout();
            Assert.Equal(height, line.Bounds.Height, 3);
            Assert.Equal(under, below.Bounds);
            line.Now(Usual);
            window.UpdateLayout();
            Assert.Equal(height, line.Bounds.Height, 3);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_text_size_change_reserves_the_new_tallest_height()
    {
        headless.OnUiThread(() =>
        {
            var (line, _, window, probe, _) = Rig(1, 260);
            window.Show();
            window.UpdateLayout();
            var small = line.Bounds.Height;

            SetupTheme.Apply(window.Resources, SetupPalette.Dark, 2.25);
            window.UpdateLayout();

            Assert.True(line.Bounds.Height > small * 1.8, "the reserved height follows the text size");
            Assert.Equal(probe.Bounds.Height, line.Bounds.Height, 1);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_words_fade_out_swap_and_fade_back_in_60_ms_later_without_moving_anything()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames, window, _, below) = Rig(1, 420);
            window.Show();
            window.UpdateLayout();
            var place = line.Bounds;
            var under = below.Bounds;

            line.Set(Slow);
            Assert.Equal(Slow, line.Words);
            Assert.Equal(Usual, line.DrawnWords);
            var swappedAt = -1.0;
            var shownAt = -1.0;
            var clock = 0.0;
            for (var i = 0; i < 90; i++)
            {
                frames.Frame();
                clock += 1.0 / 60;
                window.UpdateLayout();
                Assert.Equal(place, line.Bounds);
                Assert.Equal(under, below.Bounds);
                if (swappedAt < 0 && line.DrawnWords == Slow)
                {
                    swappedAt = clock;
                    Assert.Equal(0, line.LineOpacity, 2);
                }
                if (swappedAt >= 0 && shownAt < 0 && line.LineOpacity > 0) shownAt = clock;
            }

            Assert.InRange(swappedAt, 0.1, 0.5);
            Assert.InRange(shownAt - swappedAt, HoldLineArrival - 0.02, HoldLineArrival + 0.04);
            Assert.Equal(1, line.LineOpacity, 2);
            Assert.False(frames.HasPendingFrame);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Asked_back_before_the_old_words_have_gone_it_fades_back_to_them()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames, window, _, _) = Rig(1, 420);
            window.Show();
            line.Set(Slow);
            frames.Run(0.05);
            Assert.True(line.LineOpacity < 1);
            line.Set(Usual);
            frames.Run(1, () => Assert.Equal(Usual, line.DrawnWords));
            Assert.Equal(1, line.LineOpacity, 2);
            Assert.Equal(Usual, line.Words);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Calm_fades_are_quicker_and_without_a_frame_loop_the_words_swap_at_once()
    {
        headless.OnUiThread(() =>
        {
            var normal = Settle(calm: false);
            var calm = Settle(calm: true);
            Assert.True(calm < normal, $"Calm took {calm} s, the usual fade {normal} s");

            var loose = new ReservedText(TextRole.Body, SetupTheme.Ink2, () => false, [Usual, Slow]);
            loose.Set(Slow);
            Assert.Equal(Slow, loose.DrawnWords);
            Assert.Equal(1, loose.LineOpacity);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Narrator_reads_the_new_words_from_a_polite_live_region_and_not_the_reserved_ones()
    {
        headless.OnUiThread(() =>
        {
            var (line, _, window, _, _) = Rig(1, 420);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(line));
            Assert.Equal(Usual, AutomationProperties.GetName(line));
            line.Set(Slow);
            Assert.Equal(Slow, AutomationProperties.GetName(line));
            var reserves = line.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Opacity == 0 && !t.IsHitTestVisible).ToList();
            Assert.Equal(2, reserves.Count);
            Assert.All(reserves, r => Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(r)));
            Assert.Equal([Usual, Slow], line.Wordings);
            line.Reserve("A third wording");
            line.Reserve(Usual);
            Assert.Equal(3, line.Wordings.Count);
            window.Close();
        });
    }

    private static double HoldLineArrival => Marrow.Setup.Shell.HoldLine.ArrivalDelay;

    private static double Settle(bool calm)
    {
        var frames = new ManualFrameSource();
        var line = new ReservedText(TextRole.Body, SetupTheme.Ink2, () => calm, [Usual, Slow]);
        line.Attach(new Animator(frames));
        line.Set(Slow);
        var seconds = 0.0;
        while (frames.HasPendingFrame && seconds < 5)
        {
            frames.Frame();
            seconds += 1.0 / 60;
        }
        Assert.Equal(Slow, line.DrawnWords);
        return seconds;
    }

    private static (ReservedText Line, ManualFrameSource Frames, Window Window, TextBlock Probe, Border Below) Rig(double textScale, double width)
    {
        var frames = new ManualFrameSource();
        var line = new ReservedText(TextRole.Body, SetupTheme.Ink2, () => false, [Usual, Slow], TextAlignment.Left) { MaxWidth = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        line.Attach(new Animator(frames));
        var probe = SetupTheme.Text(new TextBlock { Text = Slow, TextWrapping = TextWrapping.Wrap, MaxWidth = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Opacity = 0 }, TextRole.Body, SetupTheme.Ink2);
        var below = new Border { Height = 30 };
        var window = new Window { Width = 520, SizeToContent = SizeToContent.Height, Content = new StackPanel { Children = { new Border { Height = 40 }, line, below, probe } } };
        SetupTheme.Apply(window.Resources, SetupPalette.Dark, textScale);
        return (line, frames, window, probe, below);
    }
}
