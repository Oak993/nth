using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial, unit rec-tlr). Since SoftAppearModifier carries a progress (UI-CHAT-052, Chrome.cs), every SwiftTransition.Insert of a SoftAppear or
// SettleGlide child plays a 0.32 s arrival. SwiftUI runs a transition only for a view that joins a parent already on screen: the ForEach children of a row
// that first appears are there from its first frame (the unit's own ED-NEW-rec-tlr (9), applied to HydraMergeRow in e9c88ede5). HydraHeadsWorkingRow
// (TimelineRows.cs ApplyTeam) inserts every finished-head pill with SwiftTransition.Insert on its first attach too, so a row first built with finished
// heads (a chat opened mid-batch, a row that joins the timeline with its heads already finished) fades and rises each pill in where the Mac draws it at once.
[Collection("Headless")]
public sealed class TimelineRowsReviewTlrFirstBuildTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-CHAT-052")] // TimelineRows.swift:398-418 ForEach(finished, id: \.index) { FinishedHeadPill(head:).transition(.settleGlide) }: pills present when the row first appears take no transition
    public void REVIEW_a_heads_row_first_built_with_finished_heads_draws_its_pills_at_once_without_the_soft_arrival() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var (store, app, runtime) = TimelineRowsFinTeam.Make(
            TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Failed), TimelineRowsFinTeam.Head(1, HydraHeadInfo.Status.Completed));
        using (store)
        {
            HydraHeadsWorkingRow row = null!;
            Avalonia.Controls.Window window = null!;
            store.Main.OnMain(() =>
            {
                row = new HydraHeadsWorkingRow(new ValueList<int>([1, 0]), runtime);
                var host = new Border { Width = 500, Child = row };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                window = Show(new StackPanel { Children = { host } }); // the row's first appearance, both heads already finished
                Settle(window);
            });
            frames.Advance(TimeSpan.Zero);
            store.Main.AdvanceMilliseconds(16);
            frames.Advance(TimeSpan.FromMilliseconds(16));
            try
            {
                store.Main.OnMain(() =>
                {
                    Settle(window);
                    var pills = Mounted<FinishedHeadPill>(row).ToList();
                    Assert.Equal(2, pills.Count);
                    foreach (var pill in pills)
                    {
                        var opacity = 1.0; var rise = 0.0;
                        for (Visual? at = pill; at is not null && !ReferenceEquals(at, row); at = at.GetVisualParent())
                        {
                            opacity *= at.Opacity;
                            if (at.RenderTransform is { } transform) rise += transform.Value.M32;
                        }
                        var name = AutomationProperties.GetName(pill);
                        Assert.True(opacity >= 0.999, $"{name}: one frame after the row first appeared the pill is drawn at opacity {opacity:F3} (a soft arrival on first build)");
                        Assert.True(Math.Abs(rise) <= 0.01, $"{name}: one frame after the row first appeared the pill is {rise:F2} points low");
                    }
                });
            }
            finally { store.Main.OnMain(() => Close(window)); }
        }
    });

    // Second finding of the same change: SwiftTransition.Remove (SwiftAnimation.cs) stops an insertion still running and presents the removal from its start, and the
    // removal halves of SoftAppear (.opacity) and SettleGlide (SettleGlideModifier at progress 0) both write opacity 1, so a soft arrival cut short by a removal jumps to
    // full opacity in one frame before it leaves. Before UI-CHAT-052 an arrival was already at 1, so nothing jumped. SwiftUI keeps the arrival's in-flight values running
    // under the removal (the view is not seen to flash to full strength). Reached by the merge pill when a merge that suspends briefly lets go without a note (the
    // all-.alreadyOnMain pass of ED-NEW-rec-tlr (2)), and by the composer's queue and changes tabs.
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // TimelineRows.swift:937-952 the merging pill goes with the settle glide when the flag drops with no note; Chrome.swift:994-1000 it arrived softly
    public void REVIEW_a_merging_pill_that_lets_go_while_it_is_still_arriving_does_not_jump_to_full_opacity_before_it_glides_away() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraMergeRow row = null!;
        Avalonia.Controls.Window window = null!;
        double Drawn()
        {
            var pill = Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));
            var opacity = 1.0;
            for (Visual? at = pill; at is not null && !ReferenceEquals(at, row); at = at.GetVisualParent()) opacity *= at.Opacity;
            return opacity;
        }
        h.Main.OnMain(() =>
        {
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging()); // first built with nothing under way: no pill
            window = Show(new Border { Padding = new Thickness(24), Child = row });
            Settle(window);
        });
        try
        {
            // The merge starts: the pill joins a row on screen and arrives softly.
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = HydraMergeStage.Pushing;
                h.Runtime.HydraMergeStage = HydraMergeStage.Pushing.Words;
            });
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            for (var i = 0; i < 3; i++) { h.Main.AdvanceMilliseconds(16); frames.Advance(TimeSpan.FromMilliseconds(16)); h.Main.OnMain(() => Settle(window)); }
            var arriving = h.Main.OnMain(Drawn);
            Assert.InRange(arriving, 0.02, 0.8); // part way in (the unit's own arrival)
            // 48 ms in, the merge lets go with no note: the pill leaves on the settle glide.
            h.Main.OnMain(() => { h.Runtime.IsHydraMerging = false; h.Runtime.HydraMergeStep = null; });
            h.Main.OnMain(() => Settle(window));
            var next = h.Main.OnMain(Drawn);
            h.Main.AdvanceMilliseconds(16); frames.Advance(TimeSpan.FromMilliseconds(16));
            h.Main.OnMain(() => Settle(window));
            var after = h.Main.OnMain(Drawn);
            // One frame of smooth(0.32) near 48 ms adds about 0.1; a jump to full strength is not an arrival carrying on.
            Assert.True(next <= arriving + 0.15 && after <= arriving + 0.15, $"the pill was {arriving:F3} in when the merge let go, then {next:F3} and {after:F3} a frame later: it jumped towards full opacity");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
