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
    // STOP opencode-App.Views.Chat.TimelineRows #12 PR-CHAT-113.
    // Kind: a production gap this test exposed while attempting the full proof (not a missing dependency).
    // Swift: App/Views/Chat/TimelineRows.swift:2261,2286-2288 (PlanCard's `@State private var isShowingPlan`
    // stays put while `body` re-renders as `plan` changes); ChatBadge.swift:19 (ChatBadge's own
    // `@State private var coordinator = BadgePopoverCoordinator()` likewise survives a re-render of the same
    // ChatBadge identity, so the open popover's underlying AppKit popover is never torn down by a body change,
    // only by `isPresented` itself changing) and :66-73 (`.onChange(of: isPresented)` calls `coordinator.show`
    // only when `isPresented` flips, not on every render, so the SAME coordinator/popover keeps the content
    // detail() re-evaluates against the new `plan`).
    // What is missing: PlanCard.Build (TimelineRows.cs, this unit) does `Child = new ChatBadge(...)` on every
    // `entry.Item.TimelineItemContent` change (:2813-2814 `this.Track(() => entry.Item.TimelineItemContent,
    // Build)`), which replaces the whole ChatBadge subtree, including its own new BadgePopoverCoordinator and
    // its own new (closed) Popup. The old ChatBadge's `button.DetachedFromVisualTree` handler
    // (ChatBadge.cs:152) closes the old popover as that subtree is torn down, and the new ChatBadge's Track on
    // `isPresented` (ChatBadge.cs:138) never re-fires (the bound value did not change, only the object
    // holding it did), so no popover is shown again: a plan's open detail popover always closes the instant
    // the plan's own state or markdown changes underneath it, where Swift keeps it open with fresh content.
    // Proven below instead: opening a drafting plan's popover shows the correct streaming markdown (real
    // click, real hit testing, real popover content) - the part of the record production already gets right.
    // What is needed: PlanCard needs to stop recreating ChatBadge on every content change (or ChatBadge needs
    // a reactive update entry point, like WorkingIndicator's own `UpdateValues` delegate in ThreadTimeline.cs)
    // so the SAME coordinator/popover survives a plan update, matching ChatBadge's own `@State`. ChatBadge.cs
    // belongs to unit App.Views.Chat.ChatBadge, whose own PR-CHAT-140/141 records are already proven off the
    // current one-shot-construction design (ChatBadgeTests.cs): a fix needs either that unit's cooperation or
    // an owner ruling on how a `@State`-holding SwiftUI view is kept alive across its host's data-driven
    // rebuilds. Left for the orchestrator; not something this unit can decide or fix alone by the STOP rule.
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
