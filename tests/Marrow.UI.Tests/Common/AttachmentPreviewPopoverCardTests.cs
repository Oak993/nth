using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// REVIEW (final review of unit/effort-title-fidelity, round 1, 2026-10-03): the attachment preview is one Popup per strip,
// swapped in place (AttachmentPreviewPanel.swift:64-70, :99-107: tapping another photo while the panel is open "re-shows at
// the new anchor"). It sets the new content first and the new anchor after, on a popup that is open. Through the real
// presenter (the overlay, as headless has no native popup) the new preview has to be what is on screen, hung on the card,
// with the tip on the thumbnail it was tapped from. Avalonia 12.1.2 gives a Popup's child to its host once, when it opens, so
// the old preview stayed; and the card read the anchor it was made with, so its tip stayed at the first thumbnail.
[Collection("Headless")]
public sealed class AttachmentPreviewPopoverCardTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("hide")]
    [InlineData("popup")]
    [Trait("Decision", "ED-107")]
    public void Hiding_the_owner_releases_preview_monitors_and_the_same_attachment_reopens_in_one_click(string removal) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 60, Height = 60 };
        var window = HeadlessPopupHost.OverlayWindow(new Canvas { Children = { anchor } });
        window.Show(); Dispatcher.UIThread.RunJobs();
        var coordinator = new AttachmentPreviewCoordinator();
        var popup = coordinator.PopupForTests; popup.ShouldUseOverlayLayer = true;
        var attachment = Sample("hidden.txt");
        try
        {
            coordinator.Toggle(attachment, anchor); Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            if (removal == "hide") window.Hide(); else popup.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.Null(coordinator.CurrentIDForTests);
            foreach (var field in new[] { "pointerMonitor", "keyMonitor", "monitorWindow" })
                Assert.Null(typeof(AttachmentPreviewCoordinator).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(coordinator));
            window.Show(); Dispatcher.UIThread.RunJobs();
            coordinator.Toggle(attachment, anchor); Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
        }
        finally { coordinator.Close(); popup.IsOpen = false; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static Attachment Sample(string name) => new() { Name = name, Path = "Z:\\does-not-exist\\" + name, MimeType = "text/plain" };

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095's card at AttachmentPreviewPanel.swift:64-70, :99-107 (PR-COMPOSER-069's swap)
    public void A_preview_swapped_to_another_thumbnail_shows_that_preview_with_its_tip_on_that_thumbnail() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var first = new Border { Width = 60, Height = 60, Background = Avalonia.Media.Brushes.Gray };
        var second = new Border { Width = 60, Height = 60, Background = Avalonia.Media.Brushes.Gray };
        Canvas.SetLeft(first, 40);
        Canvas.SetTop(first, 220);
        Canvas.SetLeft(second, 300);
        Canvas.SetTop(second, 220);
        var window = HeadlessPopupHost.OverlayWindow(new Canvas { Children = { first, second } });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var coordinator = new AttachmentPreviewCoordinator();
        var popup = coordinator.PopupForTests;
        popup.ShouldUseOverlayLayer = true;
        try
        {
            coordinator.Toggle(Sample("a.txt"), first);
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            var before = Assert.IsType<PopoverCard>(popup.Child).Child!;
            Assert.True(before.IsAttachedToVisualTree());
            coordinator.Toggle(Sample("b.txt"), second); // another photo while the panel is open
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(second, popup.PlacementTarget);
            var after = Assert.IsType<PopoverCard>(popup.Child).Child!;
            Assert.NotSame(before, after);
            Assert.True(after.IsAttachedToVisualTree(), "the preview of the second photo is not shown");
            Assert.False(before.IsAttachedToVisualTree(), "the preview of the first photo is still shown");
            var card = Assert.Single(window.GetVisualDescendants().OfType<PopoverCard>());
            var tip = card.TranslatePoint(card.TipPoint, window)!.Value;
            var middle = second.TranslatePoint(new Point(30, 30), window)!.Value;
            Assert.True(Math.Abs(tip.X - middle.X) < 0.5, $"the tip is at x {tip.X:F1}; the thumbnail the preview is shown from has its middle at {middle.X:F1}");
        }
        finally
        {
            coordinator.Close();
            popup.IsOpen = false;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
