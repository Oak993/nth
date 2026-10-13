using Avalonia.Threading;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3b ("the window never jumps"); ED-NEW-inst-picks-1, item D. The two residuals of
// ED-NEW-inst-fix-7 and -8 now glide on the installer's own glide (Reveal.Glide: the canvas's spring at no more than 135 DIP a
// second): a Problem whose reason is taller than every screen the window measured opens the window to it over many frames, and the
// glass card's fill edge follows its rows when a longer path makes it taller (or a shorter one makes it shorter). Both are
// interruptible: a new change carries on from where the last one is, with the speed it has.
[Collection("SetupHeadless")]
public sealed class WindowGlideTests(SetupHeadlessSession headless)
{
    private const string TypicalRoot = @"C:\Users\Ana\AppData\Local\Programs\nth";
    private const string LongPath = @"D:\Apps and tools for the whole family\Shared between several accounts\Development\Editors and agents\nth";
    private const double Frame = 1.0 / 60;

    private static readonly string Sentence = string.Concat(Enumerable.Repeat("Windows would not let nth Setup write to the folder you chose, because another program on this PC is using it. ", 20));

    /// <summary>A reason of about <paramref name="characters"/> characters: some lines at the base type size, many at a large one.</summary>
    private static string Reason(int characters) => Sentence[..characters].Trim();

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(1.0, 300)]
    [InlineData(1.5, 450)]
    public void A_Problem_with_a_long_reason_opens_the_window_over_many_frames_and_never_jumps(double textScale, int characters)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(SystemLook.Default with { TextScale = textScale }) { InstallRoot = TypicalRoot };
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            var opened = Height(rig);

            session.Fail(Reason(characters), safe: true, details: "[12:04:31] Velopack Setup 1.2.161");
            var heights = new List<double> { opened };
            for (var i = 0; i < 300; i++) heights.Add(Frames(rig, 1));

            var problem = Assert.IsType<ProblemScreen>(session.Current);
            Assert.True(problem.View.DesiredSize.Height > opened - CaptionBar.BandHeight + 20, $"the reason ({problem.View.DesiredSize.Height}) is taller than every screen the window measured ({opened})");
            AssertGlided(heights);
            Assert.Equal(CaptionBar.BandHeight + rig.Window.Body.Tallest, heights[^1], 1);
            Assert.Equal(rig.Window.Body.Tallest, rig.Window.Body.Shown, 3);
            Assert.Equal(rig.Window.Body.Tallest, problem.View.Bounds.Height, 1); // the screen was laid out whole all along: nothing in it moved
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_still_longer_reason_while_the_window_is_opening_turns_the_glide_around_from_where_it_is()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig { InstallRoot = TypicalRoot };
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            var heights = new List<double> { Height(rig) };

            session.Fail(Reason(300));
            for (var i = 0; i < 12; i++) heights.Add(Frames(rig, 1));
            var middle = heights[^1];
            Assert.True(middle > heights[0] && middle < CaptionBar.BandHeight + rig.Window.Body.Tallest - 1, "the first glide is still on its way");

            session.Fail(Reason(420)); // the reason's words change at the next commit: the screen is taller still
            for (var i = 0; i < 300; i++) heights.Add(Frames(rig, 1));

            AssertGlided(heights);
            var problem = Assert.IsType<ProblemScreen>(session.Current);
            Assert.Equal(CaptionBar.BandHeight + rig.Window.Body.Tallest, heights[^1], 1);
            Assert.True(heights[^1] > middle + 1, "and it went on to the taller height");
            Assert.True(problem.View.DesiredSize.Height <= rig.Window.Body.Tallest + .5);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void In_Calm_the_window_takes_the_taller_height_at_once_as_the_screen_only_fades()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(WindowRig.Calm) { InstallRoot = TypicalRoot };
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            var opened = Height(rig);

            session.Fail(Reason(300));
            var first = Frames(rig, 1);

            Assert.True(first > opened + 20);
            Assert.Equal(CaptionBar.BandHeight + rig.Window.Body.Tallest, first, 1);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_text_size_change_still_takes_the_taller_height_at_once_with_no_glide_left_over()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig { InstallRoot = TypicalRoot };
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            Assert.Equal(468, Height(rig));

            rig.Change(SystemLook.Default with { TextScale = 2.25 });
            rig.Settle(); // no frame runs: the new height is taken by layout alone, as the type is
            var large = Height(rig);

            Assert.True(large > 468);
            Assert.Equal(CaptionBar.BandHeight + rig.Window.Body.Tallest, large, 1);
            Assert.Equal(rig.Window.Body.Tallest, rig.Window.Body.Shown, 3);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(1.0)]
    [InlineData(2.25)]
    public void The_cards_edge_follows_its_rows_down_when_a_longer_path_makes_the_card_taller(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var (rig, session, options) = OptionsWindow(textScale);
            var group = options.Group;
            var last = group.Rows[^1];
            var probe = new FrameProbe(rig, options.View, [last], [options.Done]) { ButtonsFixed = false };
            var before = group.DrawnHeight;
            var tail = EdgeBelowLastRow(probe, group, last);

            session.SetInstallRoot(LongPath);
            var drawn = new List<double> { before };
            var gap = new List<double>();
            for (var i = 0; i < 300; i++)
            {
                probe.Step();
                drawn.Add(group.DrawnHeight);
                gap.Add(EdgeBelowLastRow(probe, group, last));
            }

            Assert.True(group.Bounds.Height > before + 10, $"the card is taller now ({before} to {group.Bounds.Height})");
            AssertGlidedSteps(drawn);
            Assert.True(drawn.Distinct().Count() > 8, "the edge moved over many frames");
            Assert.Equal(group.Bounds.Height, group.DrawnHeight, 2);
            Assert.Equal(0, group.HeightExtra);
            Assert.True(group.ClipToBounds, "the clip is back once the card is as laid out");
            // the edge and the last row travel together: the same distance below the row from the first frame to the last
            Assert.All(gap, distance => Assert.InRange(distance, tail - 1, tail + 1));
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(1.0)]
    [InlineData(2.25)]
    public void The_cards_edge_follows_its_rows_up_when_a_shorter_path_makes_the_card_shorter_and_never_leaves_a_row_outside_it(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var (rig, session, options) = OptionsWindow(textScale);
            var group = options.Group;
            var last = group.Rows[^1];
            session.SetInstallRoot(LongPath);
            rig.Run(2);
            var probe = new FrameProbe(rig, options.View, [last], [options.Done]) { ButtonsFixed = false };
            var tall = group.DrawnHeight;
            var tail = EdgeBelowLastRow(probe, group, last);

            session.SetInstallRoot(TypicalRoot + @"2");
            var drawn = new List<double> { tall };
            var shownLonger = false;
            for (var i = 0; i < 300; i++)
            {
                probe.Step();
                drawn.Add(group.DrawnHeight);
                Assert.InRange(EdgeBelowLastRow(probe, group, last), tail - 1, tail + 1); // the last row is inside the card on every frame
                if (group.HeightExtra == 0) continue;
                shownLonger = true;
                Assert.False(group.ClipToBounds, "while the card is drawn taller than it is laid out, the rows are not cut off");
            }
            Assert.True(shownLonger, "the card was drawn taller than it is laid out for a while");

            Assert.True(group.Bounds.Height < tall - 10, $"the card is shorter now ({tall} to {group.Bounds.Height})");
            AssertGlidedSteps(drawn);
            Assert.True(drawn[1] > drawn[^1] + 1 && drawn.Zip(drawn.Skip(1)).All(pair => pair.Second <= pair.First + .01), "the edge only goes up");
            Assert.Equal(group.Bounds.Height, group.DrawnHeight, 2);
            Assert.Equal(0, group.HeightExtra);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_second_path_chosen_while_the_edge_is_on_its_way_carries_on_from_where_it_is()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session, options) = OptionsWindow(1.0);
            var group = options.Group;
            var last = group.Rows[^1];
            var probe = new FrameProbe(rig, options.View, [last], [options.Done]) { ButtonsFixed = false };
            var tail = EdgeBelowLastRow(probe, group, last);
            var drawn = new List<double> { group.DrawnHeight };

            session.SetInstallRoot(LongPath);
            for (var i = 0; i < 14; i++)
            {
                probe.Step();
                drawn.Add(group.DrawnHeight);
            }
            Assert.NotEqual(0, group.HeightExtra); // still on its way
            session.SetInstallRoot(TypicalRoot + @"2"); // and the folder is changed again
            for (var i = 0; i < 300; i++)
            {
                probe.Step();
                drawn.Add(group.DrawnHeight);
                Assert.InRange(EdgeBelowLastRow(probe, group, last), tail - 1, tail + 1);
            }

            AssertGlidedSteps(drawn);
            Assert.Equal(group.Bounds.Height, group.DrawnHeight, 2);
            Assert.Equal(0, group.HeightExtra);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void In_Calm_the_card_is_drawn_as_laid_out_from_the_first_frame()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(WindowRig.Calm) { InstallRoot = TypicalRoot };
            var session = rig.Session(SetupScreen.Options);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            var options = Assert.IsType<OptionsScreen>(session.Current);

            session.SetInstallRoot(LongPath);
            for (var i = 0; i < 40; i++)
            {
                rig.Run(Frame);
                Assert.Equal(0, options.Group.HeightExtra);
            }

            Assert.Equal(options.Group.Bounds.Height, options.Group.DrawnHeight, 2);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_edge_is_drawn_by_the_fill_and_the_edge_pieces_to_the_drawn_height()
    {
        headless.OnUiThread(() =>
        {
            var (rig, _, options) = OptionsWindow(1.0);
            var group = options.Group;

            group.HeightExtra = -12;

            Assert.Equal(group.Bounds.Height - 12, group.DrawnHeight, 3);
            Assert.False(group.ClipToBounds);
            var panel = Assert.IsType<Avalonia.Controls.Panel>(group.Child);
            Assert.Equal(-12, Assert.Single(panel.Children.OfType<CardFill>()).Extra);
            Assert.Equal(-12, Assert.Single(panel.Children.OfType<CardEdge>()).Extra);
            Assert.True(panel.Children.IndexOf(panel.Children.OfType<CardFill>().Single()) < panel.Children.IndexOf(group.RowsPanel), "the fill is behind the rows");
            group.HeightExtra = 0;
            Assert.True(group.ClipToBounds);
            rig.Window.CloseNow();
        });
    }

    private (WindowRig Rig, SetupSession Session, OptionsScreen Options) OptionsWindow(double textScale)
    {
        var rig = new WindowRig(SystemLook.Default with { TextScale = textScale }) { InstallRoot = TypicalRoot };
        var session = rig.Session(SetupScreen.Options);
        session.Start();
        rig.Window.Show();
        rig.Settle();
        rig.Run(1);
        return (rig, session, Assert.IsType<OptionsScreen>(session.Current));
    }

    /// <summary>How far below the last row's drawn bottom the card's drawn bottom is (DIP): the card's padding, if the edge travels with the rows.</summary>
    private static double EdgeBelowLastRow(FrameProbe probe, GlassGroup group, Avalonia.Controls.Control last) =>
        probe.Of(group).Box.Top + group.DrawnHeight - probe.Of(last).Box.Bottom;

    private static double Height(WindowRig rig)
    {
        Dispatcher.UIThread.RunJobs();
        return rig.Window.ClientSize.Height;
    }

    /// <summary>Runs <paramref name="count"/> frames and lets the headless platform apply the size the window asked for after each; returns the window's height.</summary>
    private static double Frames(WindowRig rig, int count)
    {
        for (var i = 0; i < count; i++)
        {
            rig.Run(Frame);
            Dispatcher.UIThread.RunJobs();
        }
        return rig.Window.ClientSize.Height;
    }

    /// <summary>The window only grew, over many frames, and no frame moved its bottom edge by more than the plan's 3 DIP.</summary>
    private static void AssertGlided(List<double> heights)
    {
        AssertGlidedSteps(heights);
        Assert.True(heights.Distinct().Count() > 8, $"the window grew over many frames, not in one ({string.Join(", ", heights.Distinct().Take(12))})");
        for (var i = 1; i < heights.Count; i++) Assert.True(heights[i] >= heights[i - 1] - .01, $"frame {i}: the window never gets smaller while open ({heights[i - 1]} to {heights[i]})");
    }

    private static void AssertGlidedSteps(List<double> values)
    {
        var largest = 0.0;
        for (var i = 1; i < values.Count; i++)
        {
            var step = Math.Abs(values[i] - values[i - 1]);
            largest = Math.Max(largest, step);
            Assert.True(step <= FrameProbe.MaxStep + 1e-6, $"frame {i}: moved {step:F2} DIP ({values[i - 1]:F2} to {values[i]:F2})");
        }
        Assert.True(largest > 0, "it moved");
    }
}
