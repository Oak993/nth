using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

// PR-COMPOSER-003 (ComposerView.swift:23-25, :52-56) through the foundation's WindowGlide (ED-333 9): a stack pinned to the bottom of its
// window, as the composer's approval area is. A glide that a second change interrupts goes on from where the view is drawn at that instant to its newest
// place, never jumping; a view that leaves before it was ever laid out still has its removal run to its end.
[Collection("Headless")]
public sealed class WindowGlideTests(HeadlessSession headless)
{
    private static readonly Animation Curve = Animation.EaseOut(duration: 0.3);

    private static WindowGlideRig Make()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Width = 200 };
        var departures = new Canvas();
        var window = new Window { Width = 300, Height = 600, Content = new Panel { Children = { stack, departures } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return new WindowGlideRig(window, stack, departures, new WindowGlide(stack, departures), new ManualFrameSource(), new ManualTimeProvider());
    }

    private static void Frame(WindowGlideRig rig, double seconds)
    {
        rig.Time.Advance(TimeSpan.FromSeconds(seconds));
        rig.Frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
    }

    private static double Drawn(WindowGlideRig rig, Control child) => child.TranslatePoint(default, rig.Window)!.Value.Y;

    private static void Change(WindowGlideRig rig, Action change)
    {
        var before = rig.Glide.Capture();
        change();
        rig.Glide.GlideAfterLayout(before, Curve);
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-003")] // ComposerView.swift:56 .animation(Chrome.panelSlide, value: runtime.approvals.map(\.id)): a change in flight is carried on, never cut
    public void A_glide_interrupted_part_way_goes_on_from_where_the_view_is_drawn_to_its_newest_place() => headless.OnUiThread(() =>
    {
        var rig = Make();
        using var frames = FrameClock.Source.Override(rig.Frames);
        using var clock = SwiftClock.Provider.Override(rig.Time);
        var card = new Border { Height = 60 };
        rig.Stack.Children.Add(card);
        rig.Window.UpdateLayout();
        var start = Drawn(rig, card);

        // a view under it: the card rises 80 in the window
        Change(rig, () => rig.Stack.Children.Add(new Border { Height = 80 }));
        rig.Frames.Advance(TimeSpan.Zero);
        Assert.Equal(start, Drawn(rig, card), 3); // drawn where it was in the change's own frame
        Frame(rig, 0.1);
        var midway = Drawn(rig, card);
        Assert.InRange(midway, start - 80 + 1, start - 1); // part way up

        // a second view under it, while the first glide runs: from where it is drawn now to 60 higher still
        Change(rig, () => rig.Stack.Children.Add(new Border { Height = 60 }));
        rig.Frames.Advance(TimeSpan.Zero);
        Assert.Equal(midway, Drawn(rig, card), 3); // no jump at the interruption
        var trace = new List<double>();
        for (var k = 0; k < 40; k++)
        {
            Frame(rig, 0.01);
            trace.Add(Drawn(rig, card));
        }
        Assert.All(trace.Zip(trace.Skip(1)), pair => Assert.True(pair.Second <= pair.First + 1e-6, $"the card turned back down ({pair.First:F2} to {pair.Second:F2})"));
        Assert.Equal(start - 140, trace[^1], 3); // and it rests on its newest place
        Assert.Null(card.RenderTransform); // with nothing of the glide left on it
        rig.Window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-003")] // ComposerView.swift:25-27 ForEach(runtime.approvals): a card resolved before it was ever drawn still goes, its removal complete
    public void A_view_that_leaves_before_it_was_laid_out_has_its_removal_run_to_its_end() => headless.OnUiThread(() =>
    {
        var rig = Make();
        using var frames = FrameClock.Source.Override(rig.Frames);
        using var clock = SwiftClock.Provider.Override(rig.Time);
        var card = new Border { Height = 60 };
        rig.Stack.Children.Add(card); // added, and leaves before any layout pass measured it
        var removals = 0;
        rig.Glide.Depart(card, (panel, view) => { removals++; SwiftTransition.Remove(panel, view); });
        Assert.Equal(1, removals);
        Assert.DoesNotContain(card, rig.Stack.Children);
        Frame(rig, 1);
        Assert.Empty(rig.Departures.Children); // gone from the overlay too once its removal is over

        // and one that is not the panel's at all: the removal still runs
        var stray = new Border();
        rig.Glide.Depart(stray, (_, _) => removals++);
        Assert.Equal(2, removals);
        rig.Window.Close();
    });
}
