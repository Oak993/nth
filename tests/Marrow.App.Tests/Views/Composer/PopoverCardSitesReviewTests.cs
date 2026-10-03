using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
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
}
