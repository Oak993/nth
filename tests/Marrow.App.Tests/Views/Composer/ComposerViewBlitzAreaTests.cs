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

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :4-58. The area's approval stack and the changes
// popover it owns (the blitz-composerview unit).
public sealed partial class ComposerViewTests
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
                Assert.Equal(3, Stack(area).Children.Count); // two card rows and the group: no row left behind
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
}
