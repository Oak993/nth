// PORT-ONLY: Tactus pill v2, where a group and the badge show the first time (ED-NEW-pcctl-38 item 11). A group that
// appears is measured once it shows: it sits at the right of its 320 DIP box, inside the clip, and its buttons take the
// click (a part measured while hidden measured to nothing and sat past the clip, unseen and unclickable). The count badge
// scales about its real centre. Real hit tests in the headless window; a recording animator and a manual clock.
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
public sealed class ControlPillGroupPlaceTests(HeadlessSession headless)
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");

    private static PillView Working() => PillStates.Compute(new PillFacts { Apps = [Excel] }, 10);

    [Fact]
    public void A_group_that_appears_sits_inside_its_box_and_takes_the_click_and_the_badge_scales_about_its_centre() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var animator = new RecordingPillAnimator();
        var pill = new ControlPill(PillDriverKind.RenderThread, animator, new ManualTimeProvider());
        var window = new Window { Width = 900, Height = 300, Content = pill };
        window.Show();
        try
        {
            pill.Show(Working());
            pill.Place(new PillVector(450, 8), 0.5);
            pill.Arrive();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            var run = Assert.IsType<StackPanel>(pill.GroupOf(PillGroupKind.Run));
            Assert.True(run.Bounds.Width > 0);
            Assert.Equal(ControlPill.GroupBox - run.Bounds.Width, Canvas.GetLeft(run), 6);
            Assert.Equal((PillLayout.Height - run.Bounds.Height) / 2, Canvas.GetTop(run), 6);
            var pause = Assert.IsType<PillButton>(run.Children[0]);
            var at = pause.TranslatePoint(new Point(pause.Bounds.Width / 2, pause.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(at) as Visual;
            Assert.True(hit is not null && (ReferenceEquals(hit, pause) || pause.IsVisualAncestorOf(hit)), $"the press at {at} lands on {hit?.GetType().Name}, not Pause");

            var commands = new List<PillCommand>();
            pill.Command += commands.Add;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.Equal([new PillCommand(PillCommandKind.PauseAll)], commands);

            pill.Show(Working() with { Badge = 2 });
            var centre = animator.Last(pill.BadgePart, "SetCentre")!;
            Assert.True(pill.BadgePart.DesiredSize.Width >= 18);
            Assert.Equal(pill.BadgePart.DesiredSize.Width / 2, centre.A, 6);
            Assert.Equal(9, centre.B, 6);
        }
        finally
        {
            window.Close();
        }
    });
}
