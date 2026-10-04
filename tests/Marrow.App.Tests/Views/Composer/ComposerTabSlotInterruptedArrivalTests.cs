using Avalonia;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Diff;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class ComposerTabSlotInterruptedArrivalTests(HeadlessSession headless)
{
    // ComposerView.swift:70-82 the queue tab arrives with .softAppear (Chrome.swift:949-955: opacity and a 4 pt rise on smooth(0.32), leaving on a 0.12 s ease-out fade) over the
    // changes tab; when the queue empties again while it is still arriving, the fade starts from the opacity the arrival has got to. SwiftTransition.Remove used to present the
    // removal from its own start (opacity 1), so the half-arrived tab flashed to full strength before it faded (REVIEW rec-tlr, second finding).
    [Fact]
    [Trait("Req", "PR-COMPOSER-004")]
    public void A_queue_tab_whose_queue_empties_while_it_is_still_arriving_fades_from_where_the_arrival_had_got_to() => headless.OnUiThread(() =>
    {
        using var h = ComposerViewTests.RuntimeHarness();
        var runtime = h.Runtime;
        ComposerViewTests.GiveStats(h, runtime);
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var slot = h.Main.OnMain(() => new TabSlot(runtime, new DiffPopoverCoordinator(), showsChanges: true));
        var window = h.Main.OnMain(() => ComposerViewTests.Show(slot, h.App, width: 700));
        try
        {
            ComposerViewTests.Pump(h, window);
            double Drawn()
            {
                var tab = ComposerViewTests.One<FollowUpQueueTab>(slot);
                var opacity = 1.0;
                for (Visual? at = tab; at is not null && !ReferenceEquals(at, slot); at = at.GetVisualParent()) opacity *= at.Opacity;
                return opacity;
            }
            h.Main.OnMain(() => runtime.EnqueueFollowUp("queued prompt", []));
            ComposerViewTests.Pump(h, window);
            frames.Advance(TimeSpan.Zero);
            for (var i = 0; i < 3; i++) { h.Main.AdvanceMilliseconds(16); frames.Advance(TimeSpan.FromMilliseconds(16)); ComposerViewTests.Pump(h, window); }
            var arriving = h.Main.OnMain(Drawn);
            Assert.InRange(arriving, 0.02, 0.8); // the arrival part way in
            h.Main.OnMain(() => runtime.RemoveFollowUp(runtime.FollowUps[0].Id)); // the queue empties 48 ms in; the tab leaves
            h.Main.OnMain(() => ComposerViewTests.One<FollowUpQueueTab>(slot)); // still there, fading out
            var next = h.Main.OnMain(Drawn);
            h.Main.AdvanceMilliseconds(16); frames.Advance(TimeSpan.FromMilliseconds(16));
            var after = h.Main.OnMain(Drawn);
            Assert.True(next <= arriving + 0.05 && after <= arriving + 0.05,
                $"the queue tab was {arriving:F3} in when the queue emptied, then {next:F3} and {after:F3} a frame later: it jumped towards full opacity before it faded");
        }
        finally { ComposerViewTests.Close(h, window); }
    });
}
