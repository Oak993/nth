using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// The model chip in the real composer. SwiftUI re-evaluates ComposerControls and ModelEffortButton with the new thread and keeps the
// chip's @State (isPresented, lockedWidth, the popover) and its views (ComposerView.swift:647,688; EffortSlider.swift:36-150), so
// everything the open popover writes to the thread (the slider's effort, the fast toggle, a pair, a model) lands on the same chip
// and the same popover. These tests mount the composer, open the popover and write the thread as the popover's own controls do.
// Rule R-TEST-4: each proves the in-composer half of the records it names; the component halves are proven where the components are.
[Collection("Headless")]
public sealed class EffortChipLiveInComposerTests(HeadlessSession headless)
{
    private static HydraPair Pair(string? headsEffort = null, string? leadEffort = null) =>
        new(ProviderKind.Codex) { Name = "Duo", WorkerEffort = headsEffort, OrchestratorEffort = leadEffort };

    private static void WithPair(AppModel model, HydraPair pair) { model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair); }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-108 (EffortSlider.swift:105-117) in the real composer: the held chip and its popover through the writes the popover makes
    public void The_chip_holds_its_width_and_keeps_its_popover_while_the_popover_writes_effort_fast_and_history() => headless.OnUiThread(() =>
    {
        using var rig = ComposerChipRig.Start(effort: "low");
        var chip = rig.Chip;
        rig.OpenCard();
        var held = rig.ChipWidth;
        // the slider moves to the longest title ("Extra High"): the chip, its popover and its box stay
        rig.Write(current => current with { Effort = "xhigh" });
        rig.Step(300);
        Assert.Same(chip, rig.Chip);
        Assert.Equal(held, rig.ChipWidth, 0.5);
        Assert.Equal(0, rig.Dismissals);
        Assert.True(rig.Presented);
        // fast mode on: the bolt joins the chip's row inside the held box
        rig.Write(current => current with { FastMode = true });
        rig.Step(300);
        Assert.Same(chip, rig.Chip);
        Assert.Equal(held, rig.ChipWidth, 0.5);
        Assert.Equal(0, rig.Dismissals);
        Assert.Contains(chip.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == "bolt.fill");
        // a turn arrives while it is open: the chip's history input changes under the popover
        rig.OnMain(() => typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Turns))!.SetValue(rig.Harness.Runtime,
            new ValueList<TurnRecord>([new() { Index = 1, Status = TurnStatus.Interrupted }])));
        rig.Pump();
        Assert.Same(chip, rig.Chip);
        Assert.Equal(0, rig.Dismissals);
        // closing frees the chip again: it sizes itself to what it holds now
        rig.ClickChip();
        Assert.False(rig.Presented);
        Assert.Same(chip, rig.Chip);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-108 and UI-COMPOSER-033 in the real composer: the toggle in the open card keeps the chip, the popover and the card
    public void Toggling_fast_mode_in_the_open_card_keeps_the_chip_the_popover_and_the_card_and_the_bolt_bounces() => headless.OnUiThread(() =>
    {
        using var rig = ComposerChipRig.Start(effort: "low");
        var chip = rig.Chip;
        var content = rig.OpenCard();
        var card = rig.Card(content);
        var button = rig.OnMain(() => Assert.Single(card.GetVisualDescendants().OfType<FastModeButton>()));
        rig.OnMain(() => Assert.False(rig.Thread.FastMode));
        rig.Click(button);
        Assert.True(rig.Thread.FastMode); // :561-562 the click writes thread.fastMode
        Assert.Same(chip, rig.Chip);
        Assert.Equal(0, rig.Dismissals);
        Assert.True(rig.Presented);
        Assert.Same(card, rig.Card(content));
        Assert.Same(button, Assert.Single(rig.OnMain(() => card.GetVisualDescendants().OfType<FastModeButton>().ToArray())));
        // the same bolt bounces once, and the plate has moved on its spring and settles on yellow
        rig.Step(100);
        var bolt = rig.OnMain(() => Assert.Single(button.GetVisualDescendants().OfType<SwiftImage>()));
        Assert.True((bolt.RenderTransform as ScaleTransform)?.ScaleX > 1.2, "the bolt swells about 0.1 s after the toggle");
        rig.Step(400);
        Assert.Equal("bolt.fill", bolt.SymbolName);
        Assert.Equal(1, (bolt.RenderTransform as ScaleTransform)?.ScaleX ?? 1);
        // and off again, still in the same popover
        rig.Click(button);
        Assert.False(rig.Thread.FastMode);
        Assert.Equal(0, rig.Dismissals);
        Assert.Same(chip, rig.Chip);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-132 (EffortSlider.swift:760-767,1020-1026) in the real composer: a pair's effect keeps its clock when the effort reaches maximum or fast mode toggles with the popover open
    public void A_pairs_effect_keeps_its_clock_when_the_open_cards_effort_reaches_the_top_and_fast_mode_toggles() => headless.OnUiThread(() =>
    {
        var pair = Pair();
        using var rig = ComposerChipRig.Start(model => WithPair(model, pair), effort: "medium", hydra: true);
        rig.Write(current => current with { HydraPairID = pair.Id });
        var content = rig.OpenCard();
        var card = rig.Card(content);
        var effect = rig.OnMain(() => Assert.Single(card.GetVisualDescendants().OfType<TrackEffect>())); // a pair draws the liquid at every effort
        rig.Step(3000);
        Assert.Equal(3, rig.OnMain(() => effect.TimeAt(SwiftDate.Now)), 2);
        foreach (var change in new Func<ChatThread, ChatThread>[]
        {
            current => current with { Effort = "xhigh" },       // the effort reaches the model's top: the kind changes to supercharged
            current => current with { FastMode = true },        // fast mode toggles: the kind changes again
            current => current with { Effort = "low" },         // and away from the top
            current => current with { FastMode = false },
        })
        {
            rig.Write(change);
            rig.Step(16);
            Assert.Equal(0, rig.Dismissals); // the popover is still open, so the slider and its effect are still on screen
            Assert.Same(card, rig.Card(content));
            Assert.Same(effect, rig.OnMain(() => Assert.Single(card.GetVisualDescendants().OfType<TrackEffect>())));
            Assert.True(rig.OnMain(() => effect.TimeAt(SwiftDate.Now)) > 3, "the pair's clock has not gone back to 0");
        }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-116 and UI-COMPOSER-037 (EffortSlider.swift:642-651, Chrome.swift:949-961) in the real composer: the caption joins and leaves a card that stays
    public void A_pair_joining_or_leaving_with_the_popover_open_soft_appears_or_fades_the_caption_on_the_same_card() => headless.OnUiThread(() =>
    {
        var pair = Pair(headsEffort: "high");
        using var rig = ComposerChipRig.Start(model => WithPair(model, pair), effort: "medium", hydra: true);
        var chip = rig.Chip;
        var content = rig.OpenCard();
        var card = rig.Card(content);
        bool HasCaption() => rig.OnMain(() => card.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text!.StartsWith("The lead's effort", StringComparison.Ordinal)));
        Assert.False(HasCaption());
        rig.Write(current => current with { HydraPairID = pair.Id });
        Assert.Equal(0, rig.Dismissals);
        Assert.Same(chip, rig.Chip);
        Assert.Same(card, rig.Card(content)); // the card stays: the caption joins a live card
        var caption = rig.OnMain(() => card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "The lead's effort · heads run at high effort"));
        Assert.True(Math.Abs(ComposerChipRig.Shown(caption, card).Opacity) < 0.001, "right after the flip the caption is not yet shown");
        Assert.Equal(4, ComposerChipRig.Shown(caption, card).OffsetY, 3);
        rig.Step(160);
        var (opacity, rise) = ComposerChipRig.Shown(caption, card);
        Assert.Equal(Animation.SoftAppear.Progress(0.16), opacity, 3);
        Assert.Equal(4 * (1 - Animation.SoftAppear.Progress(0.16)), rise, 3);
        rig.Step(400);
        Assert.Equal((1.0, 0.0), ComposerChipRig.Shown(caption, card));
        // and the chat leaves the pair with the popover still open: the caption fades out in 0.12 s ease-out, the card stays
        rig.Write(current => current with { HydraPairID = null });
        Assert.Equal(0, rig.Dismissals);
        Assert.Same(card, rig.Card(content));
        var leaving = rig.OnMain(() => card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text!.StartsWith("The lead's effort", StringComparison.Ordinal)));
        rig.Step(60);
        Assert.Equal(1 - Animation.EaseOut(0.12).Progress(0.06), ComposerChipRig.Shown(leaving, card).Opacity, 3);
        rig.Step(80);
        Assert.False(HasCaption());
        Assert.Same(chip, rig.Chip);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-COMPOSER-037 (EffortSlider.swift:196,492-503) "decided by pair.workerEffort, falling back to the launch's resolved worker effort": a card opened for a pair without a heads effort
    public void A_card_opened_for_a_pair_without_its_own_heads_effort_reads_the_launchs_resolved_effort_at_its_first_paint() => headless.OnUiThread(() =>
    {
        var pair = Pair(); // no workerEffort: the heads run one step below a lead above medium (HydraHeadsEffort), here "medium"
        using var rig = ComposerChipRig.Start(model => WithPair(model, pair), effort: "high", hydra: true);
        rig.Write(current => current with { HydraPairID = pair.Id });
        var launch = rig.OnMain(() => rig.App.HydraLaunch(rig.Thread));
        Assert.Equal("medium", launch?.WorkerEffort); // the resolved effort the card must read
        var content = rig.OpenCard();
        var card = rig.Card(content);
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "The lead's effort · heads run at medium effort"); // not "heads keep their own"
        // and it follows the launch while the card stays: the lead drops to medium, so the heads' effort is the lead's own
        rig.Write(current => current with { Effort = "low" });
        Assert.Same(card, rig.Card(content));
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "The lead's effort · heads run at low effort");
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-COMPOSER-034 (EffortSlider.swift:219-224,565,685-708): the reset reads the pair the chat leads with when it is tapped, because the card stays while the pair changes
    public void Reset_in_a_card_that_stayed_while_a_pair_joined_sets_the_pairs_lead_effort() => headless.OnUiThread(() =>
    {
        var pair = Pair(leadEffort: "high");
        using var rig = ComposerChipRig.Start(model => WithPair(model, pair), effort: "low", fast: true, hydra: true);
        var chip = rig.Chip;
        var content = rig.OpenCard(); // no pair yet: the card is made without one
        var card = rig.Card(content);
        rig.Write(current => current with { HydraPairID = pair.Id }); // the pair joins the open card
        Assert.Same(card, rig.Card(content));
        var reset = rig.OnMain(() => Assert.Single(card.GetVisualDescendants().OfType<RoundIconButton>()));
        rig.Click(reset);
        Assert.Equal("high", rig.Thread.Effort); // the pair's lead effort, which the model offers
        Assert.False(rig.Thread.FastMode); // :222 fast mode cleared
        Assert.Equal(0, rig.Dismissals);
        Assert.Same(chip, rig.Chip);
        // and with the chat out of the pair again, the reset clears the effort
        rig.Write(current => current with { HydraPairID = null, FastMode = true });
        rig.Click(Assert.Single(rig.OnMain(() => card.GetVisualDescendants().OfType<RoundIconButton>().ToArray())));
        Assert.Null(rig.Thread.Effort);
        Assert.False(rig.Thread.FastMode);
    });
}
