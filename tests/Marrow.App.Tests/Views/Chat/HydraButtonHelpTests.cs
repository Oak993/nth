using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The Hydra mark's tooltip and accessibility strings (App/Views/Chat/HydraButton.swift:98-100, :176-197) and the idle popover
/// a click opens while no head is out (:299-319).
/// </summary>
[Collection("Headless")]
public sealed class HydraButtonHelpTests(HeadlessSession headless)
{
    private const string Dot = " · ";

    private void Run(Action<HydraButtonRig> body, bool hydraOn = true, HydraPair? pair = null, ProviderKind? provider = null) =>
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new HydraButtonRig(hydraOn, pair: pair, provider: provider);
            body(rig);
        });

    private static string? Tip(HydraButtonRig rig) => ToolTip.GetTip(rig.Mark) as string;

    private static string? Name(HydraButtonRig rig) => AutomationProperties.GetName(rig.Mark);

    private static string? Value(HydraButtonRig rig) => AutomationProperties.GetItemStatus(rig.Mark);

    [Fact]
    [Trait("Req", "PR-HYDRA-164")] // HydraButton.swift:183 the chat's switch off: exactly this one string
    public void With_the_chats_switch_off_the_tooltip_is_the_settings_line_and_the_value_is_off()
    {
        Run(rig =>
        {
            Assert.Equal("Hydra is on in Settings", Tip(rig));
            Assert.Equal("Off", Value(rig)); // :100
            Assert.Equal("Hydra is on, no heads out yet", Name(rig)); // :99, :177: panelHelp does not look at the switch
        }, hydraOn: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-164")] // HydraButton.swift:184-196 with no heads out the assembly ends at "No heads out yet"
    public void With_no_head_out_the_tooltip_ends_at_no_heads_out_yet()
    {
        Run(rig =>
        {
            Assert.Equal("Hydra is on" + Dot + "Heads run on this chat's model" + Dot + "No heads out yet", Tip(rig));
            Assert.Equal("On", Value(rig));
            Assert.Equal("Hydra is on, no heads out yet", Name(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-164")] // HydraButton.swift:185-186 the pair's heads summary replaces the chat's-model part
    public void A_pair_puts_its_heads_summary_in_the_second_part()
    {
        var pair = new HydraPair(ProviderKind.Claude) { WorkerModel = "sonnet", WorkerEffort = "high" };
        Run(rig =>
        {
            Assert.Equal("Hydra is on" + Dot + "Heads on Sonnet" + Dot + "High effort" + Dot + "No heads out yet", Tip(rig));
        }, pair: pair, provider: ProviderKind.Claude);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-164")] // HydraButton.swift:185 a pair of another provider than the chat's is not the chat's pair
    public void A_pair_of_another_provider_is_ignored()
    {
        var pair = new HydraPair(ProviderKind.Claude) { WorkerModel = "sonnet" };
        Run(rig =>
        {
            Assert.Equal("Hydra is on" + Dot + "Heads run on this chat's model" + Dot + "No heads out yet", Tip(rig));
        }, pair: pair, provider: ProviderKind.Codex);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-164")] // HydraButton.swift:178, :190-196 heads out: the working count, then the panel's state
    public void With_heads_out_the_tooltip_names_the_working_count_and_the_panels_state()
    {
        Run(rig =>
        {
            var first = rig.AddHead(HydraHeadInfo.Status.Completed);
            Assert.Equal("Hydra is on" + Dot + "Heads run on this chat's model" + Dot + "Hide the panel", Tip(rig)); // :195
            Assert.Equal("Hydra is on, show the panel", Name(rig)); // :178 heads out, none running

            rig.Click(); // the panel is hidden
            Assert.Equal("Hydra is on" + Dot + "Heads run on this chat's model" + Dot + "Show the panel", Tip(rig));
            rig.Click();

            rig.SetHeadStatus(first, HydraHeadInfo.Status.Running);
            Assert.Equal("Hydra is on" + Dot + "Heads run on this chat's model" + Dot + "1 head working" + Dot + "Hide the panel", Tip(rig)); // :190
            Assert.Equal("Hydra is on, 1 head working, show or hide the panel", Name(rig)); // :179

            rig.AddHead(HydraHeadInfo.Status.Running);
            Assert.Equal("Hydra is on" + Dot + "Heads run on this chat's model" + Dot + "2 heads working" + Dot + "Hide the panel", Tip(rig));
            Assert.Equal("Hydra is on, 2 heads working, show or hide the panel", Name(rig));
            Assert.Equal("On", Value(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-164")] // HydraButton.swift:185-190 with a pair, the summary comes before the working count
    public void With_a_pair_and_a_head_working_the_parts_come_in_order()
    {
        var pair = new HydraPair(ProviderKind.Claude) { WorkerModel = "sonnet" };
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            Assert.Equal("Hydra is on" + Dot + "Heads on Sonnet" + Dot + "1 head working" + Dot + "Hide the panel", Tip(rig));
        }, pair: pair, provider: ProviderKind.Claude);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-165")] // HydraButton.swift:306-309, :317 a pair: its title and its heads summary, in a 290-point column
    public void The_idle_popover_with_a_pair_names_the_pair_and_its_heads()
    {
        var pair = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "opus", WorkerModel = "sonnet", WorkerEffort = "high",
        };
        Run(rig =>
        {
            rig.Click();
            var popover = rig.Open<HydraIdlePopover>();
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Select(text => text.Text ?? "").ToArray();

            Assert.Equal(
                [
                    "Hydra is on",
                    "Opus + Sonnet", // HydraPairSummary.title
                    "Heads on Sonnet" + Dot + "High effort", // HydraPairSummary.workers
                    "No heads are out yet. Ask for something big enough to split up and this chat sends them out, each with a panel of its own.",
                    "Right-click the mark to switch Hydra off for this chat.",
                ],
                texts);
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<PopoverDivider>(popover).Count());
            Assert.Equal(290, popover.Width);
        }, pair: pair, provider: ProviderKind.Claude);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-165")] // HydraButton.swift:310 no pair: the single note, then the same two notes
    public void The_idle_popover_without_a_pair_has_the_one_note_in_the_same_order()
    {
        Run(rig =>
        {
            rig.Click();
            var popover = rig.Open<HydraIdlePopover>();
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Select(text => text.Text ?? "").ToArray();

            Assert.Equal(
                [
                    "Hydra is on",
                    "Heads run on this chat's model and effort.",
                    "No heads are out yet. Ask for something big enough to split up and this chat sends them out, each with a panel of its own.",
                    "Right-click the mark to switch Hydra off for this chat.",
                ],
                texts);
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<PopoverDivider>(popover).Count());
            Assert.Equal(290, popover.Width);
        });
    }
}
