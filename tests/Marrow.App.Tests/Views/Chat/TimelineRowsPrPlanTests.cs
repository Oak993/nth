using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class TimelineRowsPrPlanTests(HeadlessSession headless)
{
    // STOP opencode-App.Views.Chat.TimelineRows #12 PR-CHAT-113, RETIRED 2026-10-04: the record was amended to the source (an open popover keeps what it opened with,
    // only a reopened one switches) and is proven by TimelineRowsParityScenarioTests.A_plan_opened_while_drafting_... (Req). The production gap was fixed in the parity
    // pass 2026-10-03 (round 1 of the adversarial review the same day). PlanCard keeps one ChatBadge across the plan's updates (ChatBadge.Update) and one popover identity per plan
    // entry across the running turn's other rows arriving, as ChatBadge.swift:20 keeps its coordinator as @State and
    // ThreadTimeline.swift:1979-1984 keeps the card; an open plan popover outlives both, with the content it opened with
    // (:138-147), and the next opening builds the finished surface: TimelineRowsParityScenarioTests.A_plan_opened_while_drafting_...
    // and ...An_open_plan_popover_survives_the_card_being_built_again_..., and TimelineRowsParityReviewTests. The record says the
    // open popover switched the moment the plan left drafting before its amendment; the source freezes it, so only a reopened popover switches.
    // This test stays as the drafting half, a real click opening the streaming renderer.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Source-derived assertions are partial evidence, not a full Req claim.
    public void An_open_drafting_plans_popover_shows_the_streaming_markdown_by_a_real_click()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var presenter = new TimelineRowsPrPopupPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var draft = new ProposedPlan { Markdown = "First paragraph.\n\nSecond paragraph.", ProposedPlanState = ProposedPlan.State.Drafting };
            var entry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Plan(draft)));
            PlanCard? card = null;
            Avalonia.Controls.Window? window = null;
            try
            {
                h.Main.OnMain(() =>
                {
                    // Warms Marrow.UI.Port.AppCurrentEvent's lazy event subscription (EnsureSubscribed) before the
                    // window's own key/mouse events fire, matching ChatBadgeTests' own warm-up: BadgePopoverCoordinator's
                    // WindowRectAnchor resolves the anchor window from AppCurrentEvent.CurrentWindow, which only reflects
                    // events raised after the first read of that property subscribes it.
                    _ = Marrow.UI.Port.AppCurrentEvent.CurrentWindow;
                    card = new PlanCard(entry, h.Runtime) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                    window = Show(card);
                    Click(window, LogicalQuery.SelfAndDescendants<Button>(card).First());
                });
                h.Main.OnMain(() =>
                {
                    var popup = presenter.Calls.Last(call => call.Presented).Popup;
                    Assert.True(popup.IsOpen);
                    var markdown = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(popup.Child!));
                    Assert.True(markdown.IsStreaming);
                    Assert.NotEmpty(LogicalQuery.SelfAndDescendants<MarkdownBlockView>(markdown));
                    Assert.Empty(LogicalQuery.SelfAndDescendants<ProseRunView>(markdown));
                });
            }
            finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
        });
    }
}
