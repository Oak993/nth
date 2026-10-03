using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial review of unit/effort-title-fidelity, 2026-10-03): OD-0095 hangs EVERY popup the presentation
// seam shows on a tipped PopoverCard (PopoverHost.Prepare -> Wrap). These sites go through the real presenter here;
// every App test of them so far used RecordingPresenter, which never calls Prepare, so the card was never exercised.
[Collection("Headless")]
public sealed class PopoverCardSitesReviewTests(HeadlessSession headless)
{
    // ChatView.swift:1456-1463,1489-1492: the edit and commit sheets are SwiftUI .sheet, not .popover; OD-0095 gives the tip
    // only to "every popover the source presents with SwiftUI .popover (an NSPopover)". ChatSheetPresentation shows them
    // through the same seam (Placement Center), PopoverHost.Facing maps every placement it does not list to a top tip and
    // the card then aims it at the column, so the sheet carries a tip on an edge, aimed at the chat column, and its
    // content is pushed 13 pt off centre by the tip's room.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void REVIEW_A_sheet_is_not_hung_on_a_tipped_popover_card() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        using var seam = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 600, Height = 400, Background = Brushes.Transparent };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = anchor };
        SettingsProviderPopupLayers.Install(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            ChatSheetPresentation.Show(anchor, _ => new Border { Width = 300, Height = 200 });
            Dispatcher.UIThread.RunJobs();
            var popup = Assert.Single(presenter.Popups);
            Assert.Equal(PlacementMode.Center, popup.Placement);
            Assert.False(popup.Child is PopoverCard, $"the sheet hangs on a PopoverCard with its tip on the {(popup.Child as PopoverCard)?.TipEdge} edge");
            popup.IsOpen = false;
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    // PromptEditor.swift:193-230: the editor popover reuses its Popup and sizes it to its content (ResizeToContent). The
    // first show hangs the content on the card only when the presenter opens it. On the next show the Popup is already
    // carded, so setting popup.Child = host wraps the host in a new card at once (the Child-changed handler); ResizeToContent
    // then takes popup.Child away to measure the host in a window of its own and puts the host back, which still belongs
    // to that card: "The Control already has a parent". The editor popover cannot open a second time (and had it opened,
    // ResizeToContent sets the Popup to the bare content size, 15 pt short of the card's room).
    [Fact]
    [Trait("Req", "PR-COMPOSER-181")] // PromptEditor.swift:176-216, with OD-0095's card
    public void REVIEW_The_prompt_editor_popover_opens_a_second_time_at_its_first_size() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        using var seam = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 40, Height = 30, Background = Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = anchor };
        SettingsProviderPopupLayers.Install(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popover = new PromptEditorPopover<Border>();
        popover.SetAnchor(anchor);
        try
        {
            PromptEdit Edit() => runtime.OnMain(() => new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty));
            var firstEdit = Edit();
            runtime.OnMain(() => popover.Sync(firstEdit, () => { }, _ => new Border { Width = 400, Height = 100 }));
            var popup = Assert.Single(presenter.Popups);
            Assert.IsType<PopoverCard>(popup.Child);
            var first = new Size(popup.Width, popup.Height);
            runtime.OnMain(popover.Close);
            for (var i = 0; i < 40 && popup.IsOpen; i++)
            {
                runtime.Time.Advance(TimeSpan.FromMilliseconds(20));
                frames.Advance(TimeSpan.FromMilliseconds(20));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(popup.IsOpen);
            var secondEdit = Edit();
            try { runtime.OnMain(() => popover.Sync(secondEdit, () => { }, _ => new Border { Width = 400, Height = 100 })); }
            catch (InvalidOperationException error)
            {
                // Show -> popup.Child = host (Wrap hangs it on a new card at once) -> ResizeToContent takes popup.Child away
                // to measure the content in a window of its own, but the content is still the card's child.
                var where = (error.StackTrace ?? "").Split('\n').Where(line => line.Contains("Marrow.", StringComparison.Ordinal)).Take(4).Select(line => line.Trim());
                Assert.Fail($"the second show throws: {error.Message} | {string.Join(" | ", where)}");
            }
            Assert.True(popup.IsOpen);
            Assert.IsType<PopoverCard>(popup.Child);
            Assert.Equal(first, new Size(popup.Width, popup.Height));
            runtime.OnMain(popover.Close);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    // DiffInspector.swift:599-636: a click on another turn's Review button while the diff popover shows opens it again on
    // that button (DiffPopoverCoordinator.Reopen: HidePopover, then Show). Through the real presenter the popup is still
    // open at that moment (it shrinks into its tip for 0.17 s, PopoverAppear.Dismiss), so the card on screen stays the one
    // hung for the first button (Avalonia 12.1.2 hosts a Popup's child once, at Popup.Open), and that card keeps the anchor
    // it was hung with (PopoverHost.Wrap reads PlacementTarget once into PopoverCard.Anchor). Avalonia moves the popup above
    // the Review button; the card aims its tip at the first button, which lies wholly above it, so the tip turns to the
    // card's top edge, pointing away from the button the popover now hangs from. NSPopover.show(relativeTo:of:
    // preferredEdge:) aims its arrow at the view it is shown from.
    [Fact]
    [Trait("Req", "PR-GIT-137")] // DiffInspector.swift:599-636, with OD-0095's card
    public void REVIEW_The_diff_popover_reopened_on_another_Review_button_points_its_tip_at_that_button() => headless.OnUiThread(() =>
    {
        using var store = new AppModelHarness();
        var project = AppModelSamples.Project("Diff", store.NewFolder("diff"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var app = store.MakeModel();
        store.Main.OnMain(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current);
            using var seam = Presentation.Presenter.Override(presenter);
            var runtime = new ThreadRuntime(thread.Id, app);
            var chrome = new Border { Width = 80, Height = 24, Background = Brushes.Transparent };
            var review = new Border { Width = 80, Height = 24, Background = Brushes.Transparent };
            Canvas.SetLeft(chrome, 40);
            Canvas.SetTop(chrome, 20);
            Canvas.SetLeft(review, 600);
            Canvas.SetTop(review, 760);
            var window = new Avalonia.Controls.Window { Width = 1400, Height = 900, Content = new Canvas { Children = { chrome, review } } };
            SettingsProviderPopupLayers.Install(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var coordinator = new Marrow.App.Views.Diff.DiffPopoverCoordinator();
            try
            {
                coordinator.SetAnchor(chrome);
                coordinator.Sync(false, runtime);
                runtime.IsDiffVisible = true;
                coordinator.Sync(true, runtime); // shown from the chrome's button
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var popup = Assert.Single(presenter.Popups);
                Assert.True(popup.IsOpen);
                runtime.ShowDiff(review, Guid.NewGuid()); // the Review button of a turn further down
                coordinator.Reopen(runtime);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(review, popup.PlacementTarget);
                var card = Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<PopoverCard>()); // the card on screen
                var tip = card.TranslatePoint(card.TipPoint, window)!.Value;
                var middle = review.TranslatePoint(new Point(40, 12), window)!.Value;
                Assert.True(card.TipEdge == PopoverTipEdge.Bottom && Math.Abs(tip.X - middle.X) < 0.5,
                    $"the tip is on the card's {card.TipEdge} edge at ({tip.X:F1}, {tip.Y:F1}); the Review button the popover is shown from has its middle at ({middle.X:F1}, {middle.Y:F1}), below the card");
            }
            finally
            {
                coordinator.Close();
                foreach (var shown in presenter.Popups) shown.IsOpen = false;
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
        GC.KeepAlive(app);
    });

    // MessagePopoverMenu.swift:9-16: every right click replaces the anchor and re-evaluates the actions, including while the
    // menu shows (the popover sets its new menu and its new rect, and is presented already). Avalonia 12.1.2 gives a Popup's
    // child to its host once, when it opens, so the second menu was never shown: the first menu stayed, with its tip
    // aimed at the old point's rect no longer. Through the real presenter the new menu is on screen and the tip points at the
    // new point (NSPopover.show(relativeTo:of:preferredEdge:) aims its arrow at the rect it is shown from).
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095's card at MessagePopoverMenu.swift:9-16 (PR-CHAT-087's arrow at the point)
    public void A_second_right_click_while_the_message_menu_shows_shows_the_new_menu_with_its_tip_at_the_new_point() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        using var seam = Presentation.Presenter.Override(presenter);
        var reads = 0;
        var message = new Border { Width = 400, Height = 60, Background = Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var view = Marrow.App.Views.Chat.RightClickPopoverExtensions.RightClickPopover(message, () => { reads++; return MessageActionsSample.Make(reads > 1, []); });
        var window = new Avalonia.Controls.Window { Width = 600, Height = 400, Content = new Border { Margin = new Thickness(10, 200, 0, 0), Child = view, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top } };
        SettingsProviderPopupLayers.Install(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        void RightClick(Point at)
        {
            window.MouseDown(at, Avalonia.Input.MouseButton.Right, Avalonia.Input.RawInputModifiers.None);
            window.MouseUp(at, Avalonia.Input.MouseButton.Right, Avalonia.Input.RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        try
        {
            RightClick(new Point(60, 230));
            var popup = Assert.Single(presenter.Popups);
            Assert.True(popup.IsOpen);
            var first = Assert.IsType<Marrow.UI.Chrome.PopoverMenu>(PopoverHost.Content(popup));
            Assert.True(first.IsAttachedToVisualTree());
            RightClick(new Point(300, 240));
            Assert.Equal(2, reads);
            var second = Assert.IsType<Marrow.UI.Chrome.PopoverMenu>(PopoverHost.Content(popup));
            Assert.NotSame(first, second);
            Assert.True(second.IsAttachedToVisualTree(), "the menu of the second click is not shown");
            Assert.False(first.IsAttachedToVisualTree(), "the menu of the first click is still shown");
            var card = Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<PopoverCard>());
            var tip = card.TranslatePoint(card.TipPoint, window)!.Value;
            var point = message.TranslatePoint(popup.PlacementRect!.Value.Center, window)!.Value;
            Assert.True(Math.Abs(tip.X - point.X) < 0.5, $"the tip is at x {tip.X:F1}; the point the menu is shown from is at {point.X:F1}");
        }
        finally
        {
            foreach (var shown in presenter.Popups) shown.IsOpen = false;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
