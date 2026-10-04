using Avalonia;
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

// e9c88ede5 (UI-CHAT-052 follow-up): SwiftUI runs a transition only for a view that joins a parent already on screen (TimelineRows.swift:937-952 the merge pill is a conditional child
// of the row's body). A merge row first built with the merge under way, or with its outcome already there, has its pill in place; the settle glide's soft arrival (smooth(0.32), opacity and a
// 4 pt rise, UI-CHAT-052) plays only for a pill that joins a row already on screen. A row that always Inserted would draw the pill at opacity 0 and 4 pt low on its first frame.
[Collection("Headless")]
public sealed class TimelineRowsMergeRowFirstBuildTests(HeadlessSession headless)
{
    private static (double Opacity, double Rise) Drawn(HydraMergeRow row)
    {
        var pill = Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));
        var opacity = 1.0; var rise = 0.0;
        for (Visual? at = pill; at is not null && !ReferenceEquals(at, row); at = at.GetVisualParent())
        {
            opacity *= at.Opacity;
            if (at.RenderTransform is { } transform) rise += transform.Value.M32;
        }
        return (opacity, rise);
    }

    [Fact]
    [Trait("Req", "UI-CHAT-052")] // TimelineRows.swift:937-952; e9c88ede5
    public void A_merge_row_first_built_with_the_merge_under_way_draws_its_pill_at_once_and_the_pill_still_glides_away_when_the_merge_lets_go() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraMergeRow row = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStep = HydraMergeStage.Pushing;
            h.Runtime.HydraMergeStage = HydraMergeStage.Pushing.Words;
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging()); // the row's first appearance, the merge already under way
            window = Show(new Border { Padding = new Thickness(24), Child = row });
            Settle(window);
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(16); frames.Advance(TimeSpan.FromMilliseconds(16));
            var (opacity, rise) = h.Main.OnMain(() => { Settle(window); return Drawn(row); });
            Assert.True(opacity >= 0.999, $"one frame after the row first appeared the pill is drawn at opacity {opacity:F3} (a soft arrival on first build)");
            Assert.True(Math.Abs(rise) <= 0.01, $"one frame after the row first appeared the pill is {rise:F2} points low");
            // Placed, not merely added: its wrappers are there, so the merge letting go still glides the pill away.
            h.Main.OnMain(() => { h.Runtime.IsHydraMerging = false; h.Runtime.HydraMergeStep = null; });
            h.Main.OnMain(() => Settle(window));
            h.Main.AdvanceMilliseconds(48); frames.Advance(TimeSpan.FromMilliseconds(48));
            var (leaving, lowered) = h.Main.OnMain(() => { Settle(window); return Drawn(row); });
            Assert.True(leaving < 0.999 || lowered > 0.01, $"the pill did not glide away (opacity {leaving:F3}, {lowered:F2} points low)");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-052")] // TimelineRows.swift:937-952; e9c88ede5
    public void A_merge_row_first_built_with_its_outcome_already_there_draws_its_pill_at_once() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraMergeRow row = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra merge held.\nwhy" }));
            window = Show(new Border { Padding = new Thickness(24), Child = row });
            Settle(window);
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(16); frames.Advance(TimeSpan.FromMilliseconds(16));
            var (opacity, rise) = h.Main.OnMain(() => { Settle(window); return Drawn(row); });
            Assert.True(opacity >= 0.999, $"one frame after the row first appeared the pill is drawn at opacity {opacity:F3} (a soft arrival on first build)");
            Assert.True(Math.Abs(rise) <= 0.01, $"one frame after the row first appeared the pill is {rise:F2} points low");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
