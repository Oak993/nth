// PORT-ONLY: Tactus pill v2, "Do it now" and "Not now" under the card's misclick shield (ED-NEW-pcctl-38; DESIGN.md 4.5
// "Hands-off behaviour", critique C12: "no button under the pointer when the request appears, a 400 ms shield, and a
// 150 ms hover before a mouse click counts"). A click in the first 400 ms does nothing; one after a 150 ms rest counts; a
// button that appeared under the pointer waits, at 35 %, until the pointer has left it; the others rise from 35 % to full;
// a new request starts it all again; the protected wait's lone Not now is shielded the same way. The mark's ring takes the
// fill it is given. Real pointer events in the headless window; a recording animator and a manual clock.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class ControlPillHandsShieldTests(HeadlessSession headless)
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");

    private static PillView Working() => PillStates.Compute(new PillFacts { Apps = [Excel] }, 10);

    private static PillView Hands(double since) => PillStates.Compute(new PillFacts
    {
        Apps = [Excel],
        Notices = [PillNotice.HandsOff("Click Save in Excel", "Tactus needs the mouse for one step: click Save in Excel.", 0, false, false, since)],
    }, since);

    private static PillView Protected(double since) => PillStates.Compute(new PillFacts { Apps = [Excel], Notices = [PillNotice.ProtectedForeground(admin: true, since)] }, since);

    private static void Pass(ManualTimeProvider clock, double seconds)
    {
        clock.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static PillButton Button(ControlPill pill, PillGroupKind group, int index) =>
        Assert.IsType<PillButton>(Assert.IsType<StackPanel>(pill.GroupOf(group)).Children[index]);

    // the layout and the composition the hit test reads, as the frames after a change leave them: a part that has just
    // appeared reaches the composition one frame after its layout (its visual still has no size after the first), so two
    // frames, as a live window always draws (ED-NEW-pcctl-46)
    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static Point Centre(PillButton button, Window window)
    {
        Flush(window);
        return button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    }

    // the point is on the button itself (not the pill's body around it)
    private static void On(Window window, Point at, PillButton button)
    {
        var hit = window.InputHitTest(at) as Visual;
        Assert.True(hit is not null && (ReferenceEquals(hit, button) || button.IsVisualAncestorOf(hit)), $"the press at {at} lands on {hit?.GetType().Name}, not the button");
    }

    private static void Click(Window window, Point at)
    {
        Flush(window);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void A_click_counts_only_after_the_shield_and_a_rest_and_a_button_that_appeared_under_the_pointer_waits_until_it_left() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var animator = new RecordingPillAnimator();
        var clock = new ManualTimeProvider();
        var pill = new ControlPill(PillDriverKind.RenderThread, animator, clock);
        var window = new Window { Width = 900, Height = 300, Content = pill };
        window.Show();
        try
        {
            pill.Show(Working());
            pill.Place(new PillVector(450, 8), 0.5);
            pill.Arrive();
            Dispatcher.UIThread.RunJobs();
            var commands = new List<PillCommand>();
            pill.Command += commands.Add;

            pill.Show(Hands(pill.Now));
            window.UpdateLayout();
            var doIt = Button(pill, PillGroupKind.HandsOff, 0);
            var at = Centre(doIt, window);
            On(window, at, doIt);
            Assert.False(pill.HandsShield.IsInert(ControlPill.DoItNowId));
            Assert.NotNull(animator.Last(doIt, "Fade"));

            // inside the 400 ms shield nothing counts, even after a rest
            window.MouseMove(at);
            Pass(clock, 0.2);
            Click(window, at);
            Assert.Empty(commands);

            // past the shield, after resting on it: Do it now
            Pass(clock, 0.3);
            Click(window, at);
            Assert.Equal([new PillCommand(PillCommandKind.DoItNow)], commands);

            // a new request appears with the pointer resting on Do it now: it waits at 35 % until the pointer leaves it
            commands.Clear();
            pill.Show(Working());
            Pass(clock, 0.5);
            pill.Show(Hands(pill.Now));
            window.UpdateLayout();
            Assert.True(pill.HandsShield.IsInert(ControlPill.DoItNowId));
            Assert.Equal(CardShield.ShieldFrom, animator.Last(doIt, "SetOpacity")!.A);
            Pass(clock, 0.6);
            Click(window, at);
            Assert.Empty(commands);

            window.MouseMove(new Point(5, 290));
            Assert.False(pill.HandsShield.IsInert(ControlPill.DoItNowId));
            Assert.Equal(1, animator.Last(doIt, "SetOpacity")!.A);
            window.MouseMove(at);
            Click(window, at);
            Assert.Empty(commands);
            Pass(clock, 0.2);
            Click(window, at);
            Assert.Equal([new PillCommand(PillCommandKind.DoItNow)], commands);

            // the protected wait's Not now: the same shield
            commands.Clear();
            pill.Show(Protected(pill.Now));
            window.UpdateLayout();
            var notNow = Button(pill, PillGroupKind.NotNow, 0);
            var there = Centre(notNow, window);
            On(window, there, notNow);
            window.MouseMove(new Point(5, 290));
            window.MouseMove(there);
            Click(window, there);
            Assert.Empty(commands);
            Pass(clock, 0.5);
            Click(window, there);
            Assert.Equal([new PillCommand(PillCommandKind.NotNow)], commands);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_mark_s_ring_takes_the_fill_it_is_given() => headless.OnUiThread(() =>
    {
        var pill = new ControlPill(PillDriverKind.RenderThread, new RecordingPillAnimator(), new ManualTimeProvider());
        pill.Show(Hands(10));
        pill.RingFill = 0.4;
        Assert.Equal(0.4, pill.RingFill);
        pill.RingFill = 3;
        Assert.Equal(1, pill.RingFill);
    });
}
