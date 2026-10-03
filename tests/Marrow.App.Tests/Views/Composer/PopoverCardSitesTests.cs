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

// OD-0095 at the sites, through the real card-wrapping presenter (ReviewOverlayPresenter keeps each popup in the window's
// overlay, since Avalonia 12.1.2 headless cannot open a PopupRoot): what a site that sizes its popup does with the card.
[Collection("Headless")]
public sealed class PopoverCardSitesTests(HeadlessSession headless)
{
    // PromptEditor.swift:218-230 sizes the popover to its hosted view; in the port that view hangs on the tipped card
    // (the editor shows below its anchor, PromptEditor.cs: Placement Bottom, so the tip is on the card's top edge), and
    // the popup takes the card's room as well as the view's size, so the view keeps all of it.
    [Fact]
    [Trait("Req", "PR-COMPOSER-181")] // PromptEditor.swift:176-230, with OD-0095's card
    public void The_prompt_editor_popup_is_its_contents_size_and_the_cards_room() => headless.OnUiThread(() =>
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
            var edit = runtime.OnMain(() => new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty));
            var content = new Border { Width = 400, Height = 100 };
            runtime.OnMain(() => popover.Sync(edit, () => { }, _ => content));
            var popup = Assert.Single(presenter.Popups);
            var card = Assert.IsType<PopoverCard>(popup.Child);
            Assert.Same(content, card.Child);
            Assert.Equal(PopoverTipEdge.Top, card.TipEdge);
            var room = PopoverHost.Room(popup); // the card's edge, tip and gap, and the room it keeps for its shadow
            Assert.Equal(new Size(2 + 2 * 11, 2 + 13 + 11), room);
            Assert.Equal(new Size(400 + room.Width, 100 + room.Height), new Size(popup.Width, popup.Height));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Size(400 + room.Width, 100 + room.Height), new Size(popup.Width, popup.Height)); // and after its layout
            Assert.Equal(new Size(400, 100), content.Bounds.Size);
            runtime.OnMain(popover.Close);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    // ChatView.swift:1456-1463,1489-1492: the edit and commit sheets are SwiftUI .sheet. They keep ED-238's panel on their
    // own top-level (the presenter's panel class, no tip class).
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_sheet_keeps_the_panel_on_its_top_level() => headless.OnUiThread(() =>
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
            var sheet = new Border { Width = 300, Height = 200 };
            ChatSheetPresentation.Show(anchor, _ => sheet);
            Dispatcher.UIThread.RunJobs();
            var popup = Assert.Single(presenter.Popups);
            Assert.Same(sheet, popup.Child);
            Assert.True(PopoverHost.GetIsSheet(popup));
            Assert.Contains("marrow-popover", popup.Classes);
            Assert.DoesNotContain("marrow-popover-tipped", popup.Classes);
            popup.IsOpen = false;
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
