using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzControlsTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzListTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzRingTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzUsageTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :4-58. The area's approval stack and the changes
// popover it owns (the blitz-composerview unit).
[Collection("Headless")]
public sealed class ComposerViewBlitzAreaTests(HeadlessSession headless)
{
    // ---- PR-COMPOSER-002 (:23-31) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:23-31
    public void B3_approval_cards_and_the_pill_share_one_glass_container_of_spacing_8_and_a_removal_keeps_runtime_order_and_the_12_point_gaps()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var runtime = Unresolved(h);
            h.Main.OnMain(() =>
            {
                foreach (var id in new[] { "a", "b", "c" }) runtime.Rehearse(new ProviderEvent.Approval(Approval(id)));
            });
            var area = h.Main.OnMain(() => new ComposerArea(runtime, null, showsChanges: false));
            var window = h.Main.OnMain(() => Show(area, h.App, 500));
            Pump(h, window);

            string[] Order() => LogicalQuery.SelfAndDescendants<ApprovalCard>(area)
                .Select(card => LogicalQuery.SelfAndDescendants<TextBlock>(card).Single(text => text.Text is "a" or "b" or "c").Text!).ToArray();
            void AssertGaps()
            {
                var stack = Stack(area);
                var cards = LogicalQuery.SelfAndDescendants<ApprovalCard>(area).ToArray();
                Assert.Equal(12, stack.Spacing);
                for (var index = 1; index < cards.Length; index++)
                    Assert.Equal(12, Frame(cards[index], stack).Y - Frame(cards[index - 1], stack).Bottom, 5); // between cards
                Assert.Equal(12, Frame(One<ComposerView>(area), stack).Y - Frame(cards[^1], stack).Bottom, 5); // last card to the pill
            }

            h.Main.OnMain(() =>
            {
                Assert.Equal(["a", "b", "c"], Order());
                AssertGaps();
                // :23 one GlassEffectContainer(spacing: 8) holds every card and the tab and pill stack.
                var container = Ancestors(Stack(area)).OfType<Control>().Single(control => SwiftGlass.GetContainerSpacing(control) is not null);
                Assert.Equal(8, SwiftGlass.GetContainerSpacing(container));
                Assert.Equal(3, LogicalQuery.SelfAndDescendants<ApprovalCard>(container).Count());
                Assert.Single(LogicalQuery.SelfAndDescendants<ComposerView>(container));
                Assert.Single(Ancestors(Stack(area)).OfType<Control>(), control => SwiftGlass.GetContainerSpacing(control) is not null);
            });

            // The middle request resolves: the others keep the runtime's order and the 12 point gaps; nothing of it is left.
            h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.RequestResolved("b")));
            Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(1));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(1)));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(["a", "c"], Order());
                AssertGaps();
                Assert.Equal(3, Stack(area).Children.Count(child => child is not Marrow.App.Views.Chat.TactusStrip)); // two card rows and the group: no row left behind
                Assert.Single(Stack(area).Children.OfType<Marrow.App.Views.Chat.TactusStrip>()); // OD-0145, ED-NEW-pcctl-27: the one Tactus strip, empty and hidden, sits between them and adds no gap
            });
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-008 (:39-49) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-008")] // App/Views/Composer/ComposerView.swift:39-49; App/Views/Diff/DiffInspector.swift:521-523, :599-612
    [Trait("Rule", "R-TEST-4")] // The thread-change arm (:40-41) cannot be driven: ThreadRuntime.threadID is `let` (ThreadRuntime.swift:169), so the
    // area's runtime never changes (the coordinator's own arm is PR-COMPOSER-207); the appear arm is On_appear_the_popover_coordinator_closes_...;
    // the 640 by 540 size is also The_changes_popover_the_area_owns_is_640_by_540.
    public void The_area_opens_reopens_and_closes_the_changes_popover_from_the_flag_the_open_request_the_hidden_column_and_its_own_disappearance()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var runtime = h.Runtime;
            GiveStats(h, runtime);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            Pump(h, window);
            bool Presented() => presenter.Calls.Count > 0 && presenter.Calls[^1].Presented;
            var tab = h.Main.OnMain(() => One<ThreadChangesTab>(area));
            var composerArea = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(area).First());

            // :42-44 isDiffVisible opens it, at 640 by 540 (DiffInspector.swift:521-523), and closes it.
            h.Main.OnMain(() => runtime.IsDiffVisible = true);
            Pump(h, window);
            Assert.True(Presented());
            var popup = presenter.Calls[^1].Popup;
            Assert.Equal(640, popup.Width);
            Assert.Equal(540, popup.Height);
            var panel = Assert.IsType<Border>(PopoverHost.Content(popup));
            Assert.Equal(640, panel.Width);
            Assert.Equal(540, panel.Height);
            h.Main.OnMain(() => runtime.IsDiffVisible = false);
            Pump(h, window);
            Assert.False(Presented());

            // :45-47 a bump of diffOpenRequest opens it on the requested anchor; a bump that leaves it where it hangs
            // presents nothing again; a bump naming another anchor moves it there.
            h.Main.OnMain(() => runtime.ShowDiff(tab, null));
            Pump(h, window);
            Assert.True(Presented());
            Assert.Same(tab, popup.PlacementTarget);
            var shown = presenter.Calls.Count;
            h.Main.OnMain(() => runtime.ShowDiff(tab, null));
            Pump(h, window);
            Assert.Equal(shown, presenter.Calls.Count);
            h.Main.OnMain(() => runtime.ShowDiff(composerArea, null));
            Pump(h, window);
            Assert.True(Presented());
            Assert.Equal(shown + 2, presenter.Calls.Count);
            Assert.False(presenter.Calls[^2].Presented);
            Assert.Same(composerArea, popup.PlacementTarget);

            // The popover that a reopen moved is in the state of upstream defect 67 (DiffInspector.swift:640-657, reproduced, PR-GIT-137): the delayed close of the
            // reopen dropped shownRuntime, so a later close by the coordinator itself leaves the flag set. It is closed by its flag here and shown afresh, so the
            // next step reads the ordinary close (DiffPopoverAuditTests holds the moved popover's own close).
            h.Main.OnMain(() => runtime.IsDiffVisible = false);
            Pump(h, window);
            Assert.False(Presented());
            h.Main.OnMain(() => runtime.IsDiffVisible = true);
            Pump(h, window);
            Assert.True(Presented());

            // :49 the chat column going behind another closes it, and shown again it does not open on its own.
            h.Main.OnMain(() => Marrow.App.Views.RootView.SetChatColumnIsShown(Host(window), false));
            Pump(h, window);
            Assert.False(Presented());
            Assert.False(runtime.IsDiffVisible);
            var closed = presenter.Calls.Count;
            h.Main.OnMain(() => Marrow.App.Views.RootView.SetChatColumnIsShown(Host(window), true));
            Pump(h, window);
            Assert.Equal(closed, presenter.Calls.Count);
            Assert.False(runtime.IsDiffVisible);

            // :48 the area leaving the window closes it.
            h.Main.OnMain(() => runtime.ShowDiff(tab, null));
            Pump(h, window);
            Assert.True(Presented());
            h.Main.OnMain(() => Host(window).Child = null);
            Pump(h, window);
            Assert.False(Presented());
            Assert.False(runtime.IsDiffVisible);
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-009 (:32-34, :84-85) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-009")] // App/Views/Composer/ComposerView.swift:32-34; App/Views/Diff/DiffInspector.swift:542-545, :583-586, :614-625
    [Trait("Rule", "R-TEST-4")] // The anchor precedence is The_changes_popover_hangs_from_the_changes_tab_and_from_the_composer_area_...; this is the failure case
    public void With_neither_anchor_in_a_window_the_desire_is_held_and_the_popover_shows_where_the_first_anchor_lands()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var runtime = h.Runtime;
            var coordinator = new DiffPopoverCoordinator();
            h.Main.OnMain(() => coordinator.Sync(false, runtime)); // adopts the runtime, as the area's appearance does
            h.Main.OnMain(() =>
            {
                runtime.IsDiffVisible = true;
                coordinator.Sync(true, runtime);
            });
            Assert.Empty(presenter.Calls); // neither the tab nor the composer area is anywhere yet: nothing shows

            var fallback = new Border { Width = 200, Height = 40 };
            var window = h.Main.OnMain(() => Show(fallback, h.App, width: 300));
            h.Main.OnMain(() => coordinator.SetFallbackAnchor(fallback)); // :583-586 setFallbackAnchor shows what was wanted
            var (popup, presented) = Assert.Single(presenter.Calls);
            Assert.True(presented);
            Assert.Same(fallback, popup.PlacementTarget);
            Assert.Equal(PlacementMode.Top, popup.Placement); // :614-625 above it, the Swift's .maxY edge

            var tab = new Border { Width = 40, Height = 20 };
            h.Main.OnMain(() => coordinator.SetAnchor(tab)); // a tab landing later leaves the shown popover where it hangs
            Assert.Single(presenter.Calls);
            h.Main.OnMain(() => coordinator.Close()); // review rec-e1: stop the coordinator's key/pointer monitors so they do not leak into later tests
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-009")] // App/Views/Composer/ComposerView.swift:32-34, :84-85; App/Views/Diff/DiffInspector.swift:581-586, :614-625
    [Trait("Rule", "R-TEST-4")] // The failure case is With_neither_anchor_in_a_window_the_desire_is_held_...
    public void The_changes_popover_opens_above_the_tab_and_above_the_composer_area_when_the_queue_holds_the_slot()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var runtime = h.Runtime;
            GiveStats(h, runtime);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            Pump(h, window);
            h.Main.OnMain(() => runtime.IsDiffVisible = true);
            Pump(h, window);
            static bool Within<T>(Control view) where T : class =>
                Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(view).OfType<T>().Any();
            var onTab = presenter.Calls.Last(call => call.Presented).Popup;
            Assert.True(Within<ThreadChangesTab>((Control)onTab.PlacementTarget!), "the popover hangs from the changes tab");
            Assert.Equal(PlacementMode.Top, onTab.Placement); // DiffInspector.swift:614-625 the Swift's .maxY edge: above
            h.Main.OnMain(() => runtime.IsDiffVisible = false);
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                runtime.RehearseTurn(null);
                runtime.EnqueueFollowUp("queued prompt", []);
            });
            Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Pump(h, window);
            h.Main.OnMain(() => runtime.IsDiffVisible = true);
            Pump(h, window);
            var onArea = presenter.Calls.Last(call => call.Presented).Popup;
            Assert.IsType<AttachmentAnchorCapture>(onArea.PlacementTarget);
            Assert.Equal(PlacementMode.Top, onArea.Placement);
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-003 (:56) ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-COMPOSER-003")] // App/Views/Composer/ComposerView.swift:56; porting/extracts/chrome-design-tokens.md:287-291
    [Trait("Rule", "R-TEST-4")] // The stack's own re-layout (the cards that stay, the area's height) is not animated: see the unit's notes
    public void A_card_arriving_and_a_card_resolved_fade_along_the_panel_slide_spring_or_with_reduced_motion_its_ease_out_of_018(bool reduceMotion)
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
            var curve = reduceMotion ? Animation.EaseOut(duration: 0.18) : Animation.Spring(response: 0.32, dampingFraction: 0.9);
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, null, showsChanges: false));
            var window = h.Main.OnMain(() => Show(area, h.App, 500));
            Pump(h, window);
            // The two curves the record names, at the instants this test reads.
            Assert.Equal(curve.Progress(0.03), Chrome.PanelSlide.Progress(0.03), 9);
            Assert.Equal(curve.Progress(0.08), Chrome.PanelSlide.Progress(0.08), 9);
            Assert.Equal(reduceMotion ? 0.18 : curve.Duration, Chrome.PanelSlide.Duration, 9);

            h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Approval(Approval("only"))));
            Pump(h, window);
            var row = h.Main.OnMain(() => (Control)One<ApprovalCard>(area).Parent!);
            Assert.Equal(0, h.Main.OnMain(() => row.Opacity));
            h.Main.Advance(TimeSpan.FromSeconds(0.03));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.03)));
            Assert.InRange(curve.Progress(0.03), 0.001, 0.999);
            Assert.Equal(curve.Progress(0.03), h.Main.OnMain(() => row.Opacity), 3);
            h.Main.Advance(TimeSpan.FromSeconds(0.05));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.05)));
            Assert.Equal(curve.Progress(0.08), h.Main.OnMain(() => row.Opacity), 3);
            h.Main.Advance(TimeSpan.FromSeconds(0.12));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.12)));
            if (reduceMotion) Assert.Equal(1, h.Main.OnMain(() => row.Opacity)); // 0.20 s: the 0.18 s ease-out is over
            else Assert.NotEqual(1, h.Main.OnMain(() => row.Opacity)); // the spring is still settling at 0.20 s
            h.Main.Advance(TimeSpan.FromSeconds(3));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(3)));
            Assert.Equal(1, h.Main.OnMain(() => row.Opacity));

            // Resolved: the same curve, run the other way, and nothing of the card is left.
            h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.RequestResolved("only")));
            Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(0.03));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.03)));
            Assert.Equal(1 - curve.Progress(0.03), h.Main.OnMain(() => row.Opacity), 3);
            h.Main.Advance(TimeSpan.FromSeconds(3));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(3)));
            Pump(h, window);
            h.Main.OnMain(() => Assert.Empty(LogicalQuery.SelfAndDescendants<ApprovalCard>(area)));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-003")] // App/Views/Composer/ComposerView.swift:56 .animation(Chrome.panelSlide, value: runtime.approvals.map(\.id))
    [Trait("Rule", "R-TEST-4")] // The curves: the test above
    public void Changing_a_request_without_changing_the_id_list_runs_no_animation_and_keeps_the_card_where_it_is()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var runtime = Unresolved(h);
            h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Approval(Approval("same"))));
            var area = h.Main.OnMain(() => new ComposerArea(runtime, null, showsChanges: false));
            var window = h.Main.OnMain(() => Show(area, h.App, 500));
            Pump(h, window);
            var (row, card) = h.Main.OnMain(() => ((Control)One<ApprovalCard>(area).Parent!, One<ApprovalCard>(area)));
            Assert.Equal(0, frames.Pending);
            Assert.Equal(1, h.Main.OnMain(() => row.Opacity));
            var top = h.Main.OnMain(() => Frame(row, Stack(area)).Y);

            // The same id, new words: the card is drawn again in its row, at once, asking for no frame at all.
            h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Approval(Approval("same") with { Title = "changed words" })));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Same(row, (Control)One<ApprovalCard>(area).Parent!);
                Assert.NotSame(card, One<ApprovalCard>(area));
                Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(area), text => text.Text == "changed words");
                Assert.Equal(1, row.Opacity);
                Assert.Equal(top, Frame(row, Stack(area)).Y);
            });
            Assert.Equal(0, frames.Pending);
            Close(h, window);
        });
    }
}
