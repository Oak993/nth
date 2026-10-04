using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
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

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :973-1032 (the draft attachment strip, its thumbnails and
// their remove badges): the blitz-composerview unit's proofs for UI-COMPOSER-009 and UI-COMPOSER-010.
[Collection("Headless")]
public sealed class ComposerViewBlitzStripTests(HeadlessSession headless)
{
    internal static (DraftAttachments Strip, AvaloniaWindow Window) StripOnScreen(ThreadRuntimePart12Harness h, params Attachment[] attachments)
    {
        h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Attachments = new ValueList<Attachment>(attachments) });
        var strip = h.Main.OnMain(() => new DraftAttachments(h.Runtime));
        var window = h.Main.OnMain(() => Show(strip, h.App, 400));
        Pump(h, window);
        return (strip, window);
    }

    internal static AttachmentPreviewCoordinator PreviewOf(DraftAttachments strip)
    {
        var slot = (AttachmentPreviewSlot)typeof(DraftAttachments).GetField("preview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(strip)!;
        return slot.Panel;
    }

    internal static Rect ScreenRect(AvaloniaWindow window, Control control)
    {
        var origin = window.PointToScreen(control.TranslatePoint(default, window)!.Value);
        var scale = window.RenderScaling;
        return new Rect(origin.X / scale, origin.Y / scale, control.Bounds.Width, control.Bounds.Height);
    }

    // ---- UI-COMPOSER-009 (:1000-1023) ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-009")] // App/Views/Composer/ComposerView.swift:990-1019
    [Trait("Rule", "R-TEST-4")] // The thumbnail's own look and fade: TimelineRowsTests (UI-CHAT-009); the 48 and the badge's name: B3_attachment_strip_has_48_point_thumbnails_...
    public void Each_cell_is_a_48_point_thumbnail_with_2_above_and_8_after_it_so_cells_stand_56_apart_and_the_strip_leaves_8_below()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var (strip, window) = StripOnScreen(h, Fake(1), Fake(2), Fake(3));
            h.Main.OnMain(() =>
            {
                Assert.Equal(new Thickness(0, 0, 0, 8), Assert.IsType<ScrollViewer>(strip.Child).Margin); // .padding(.bottom, 8)
                var cells = LogicalQuery.SelfAndDescendants<AttachmentArrival>(strip).ToArray();
                Assert.Equal(3, cells.Length);
                var contents = cells.Select(cell => Assert.IsType<Border>(cell.Children[1])).ToArray();
                Assert.All(contents, content => Assert.Equal(new Thickness(0, 2, 8, 0), content.Padding)); // .padding(.top, 2).padding(.trailing, 8)
                var frames = contents.Select(content => Frame(content, strip)).ToArray();
                Assert.Equal(new[] { 56.0, 56.0 }, new[] { frames[1].X - frames[0].X, frames[2].X - frames[1].X }); // HStack(spacing: 0): 48 + 8, nothing between
                Assert.All(frames, frame => Assert.Equal(new Size(56, 50), frame.Size));
                var thumbnails = LogicalQuery.SelfAndDescendants<AttachmentThumbnail>(strip).ToArray();
                Assert.Equal(3, thumbnails.Length);
                Assert.All(thumbnails, thumbnail => Assert.Equal(new Size(48, 48), thumbnail.Bounds.Size));
                for (var index = 0; index < 3; index++)
                {
                    var frame = Frame(thumbnails[index], strip);
                    Assert.Equal(frames[index].X, frame.X, 3); // the thumbnail starts at the cell's leading edge
                    Assert.Equal(frames[index].Y + 2, frame.Y, 3); // 2 below the cell's top
                }
                Assert.Equal(50, strip.Bounds.Height - 8, 3); // the strip is the cells' 50 and the 8 below
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-009")] // App/Views/Composer/ComposerView.swift:1001-1002, :1021-1022; AttachmentFlight.swift:189-205
    [Trait("Rule", "R-TEST-4")] // The geometry: Each_cell_is_a_48_point_thumbnail_...; the fly itself: AttachmentFlightTests
    public void A_picked_attachments_cell_claims_the_armed_flight_once_and_a_cell_that_stays_does_not_claim_the_next_one()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var drops = new AttachmentDrops();
            var platform = new FlightPlatform { MainScreen = new Rect(0, 0, 3000, 3000) };
            using var dropSeam = AttachmentDrops.Shared.Override(drops);
            using var flightSeam = AttachmentFlight.Shared.Override(new AttachmentFlight(platform));
            var (a, b) = (Fake(1), Fake(2)); // a draft holds the same attachment, with its id, from one change to the next
            var (strip, window) = StripOnScreen(h, a);
            Assert.Empty(platform.Created); // nothing was armed when the first cell appeared
            var first = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<AttachmentArrival>(strip).Single());
            // A pick arms the ghost, then the file lands in the draft: the new cell takes it.
            h.Main.OnMain(() => drops.Arm(new FlightImage(), new Rect(500, 500, 40, 40)));
            h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Attachments = new ValueList<Attachment>([a, b]) });
            Pump(h, window);
            var landed = Assert.Single(platform.Created);
            var second = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<AttachmentArrival>(strip).Last());
            Assert.NotSame(first, second);
            Elapse(h, frames, window, 3);
            var end = landed.Surface.States[^1].Frame;
            var wanted = h.Main.OnMain(() => ScreenRect(window, (Control)second.Children[1]));
            Assert.True(Math.Abs(wanted.X - end.X) < 0.1 && Math.Abs(wanted.Y - end.Y) < 0.1, $"it lands in the new cell, not in the one that was there: wanted {wanted}, landed {end}, states {landed.Surface.States.Count}, frame {landed.Frame}");
            // A later drop, and the first cell moving because the second is removed: the cell that stayed is no longer new.
            h.Main.OnMain(() => drops.Arm(new FlightImage(), new Rect(600, 600, 40, 40)));
            h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Attachments = new ValueList<Attachment>([b]) });
            Pump(h, window);
            Elapse(h, frames, window, 1);
            Assert.Single(platform.Created); // no second flight
            Assert.NotNull(h.Main.OnMain(drops.Take)); // the drop is still armed for the next new cell
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-009")] // App/Views/Composer/ComposerView.swift:975-976, :1020; UI/Common/AttachmentPreviewPanel.swift:64-70
    [Trait("Req", "UI-COMPOSER-010")] // App/Views/Composer/ComposerView.swift:1004-1007, :1028-1029
    [Trait("Rule", "R-TEST-4")] // The first click opening: B3_attachment_strip_has_48_point_thumbnails_...
    public void A_second_click_on_the_same_thumbnail_closes_the_shared_preview_another_moves_it_and_removing_that_attachment_retires_it()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var presenterSeam = Presentation.Presenter.Override(presenter);
            var (one, two) = (Fake(1), Fake(2));
            var (strip, window) = StripOnScreen(h, one, two);
            var panel = PreviewOf(strip);
            var thumbnails = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<AttachmentThumbnail>(strip).ToArray());
            void Tap(AttachmentThumbnail thumbnail)
            {
                h.Main.OnMain(() => Click(LogicalQuery.SelfAndDescendants<Button>(thumbnail).First()));
                Pump(h, window);
            }
            Tap(thumbnails[0]);
            Assert.Equal(one.Id, panel.CurrentIDForTests);
            Tap(thumbnails[0]);
            Assert.Null(panel.CurrentIDForTests); // the same thumbnail again toggles it shut
            Tap(thumbnails[0]);
            Assert.Equal(one.Id, panel.CurrentIDForTests);
            Tap(thumbnails[1]);
            Assert.Equal(two.Id, panel.CurrentIDForTests); // one panel for the strip, moved to the other photo
            h.Main.OnMain(() => Click(Named(strip, "Remove " + two.Name)));
            Pump(h, window);
            Assert.DoesNotContain(two, h.Runtime.Draft.Attachments);
            Assert.Null(panel.CurrentIDForTests); // the removed attachment's preview is retired with it
            Close(h, window);
        });
    }

    // ---- UI-COMPOSER-010 (:1004-1017) ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-010")] // App/Views/Composer/ComposerView.swift:1008-1015; EditorAttachments.swift:30
    [Trait("Rule", "R-TEST-4")] // The geometry, name and click: B3_attachment_badge_reserves_4_point_padding_... and the strip tests
    public void The_remove_badge_is_a_white_cross_on_a_black_60_percent_disc_that_reaches_the_strips_top_edge_unclipped()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var file = Fake(1);
            var (strip, window) = StripOnScreen(h, file);
            h.Main.OnMain(() => Host(window).Background = Brushes.White);
            Pump(h, window);
            var scroller = h.Main.OnMain(() => One<ScrollViewer>(strip));
            var (centre, disc, glyphTop) = h.Main.OnMain(() =>
            {
                var image = One<SwiftImage>(Named(strip, "Remove " + file.Name));
                var frame = Frame(image, window);
                Assert.Equal("xmark.circle.fill", image.SymbolName);
                Assert.Equal(SymbolRenderingMode.Palette, image.SymbolRendering);
                return (frame.Center, new Point(frame.Center.X + 5, frame.Center.Y), Frame(image, scroller).Y);
            });
            var hues = h.Main.OnMain(() => FramePixels.Hues(window, ((int)Math.Floor(centre.X), (int)Math.Floor(centre.Y)), ((int)Math.Floor(disc.X), (int)Math.Floor(disc.Y))));
            var levels = h.Main.OnMain(() => FramePixels.Levels(window, ((int)Math.Floor(centre.X), (int)Math.Floor(centre.Y)), ((int)Math.Floor(disc.X), (int)Math.Floor(disc.Y))));
            Assert.True(levels[0] > 0.9, $"the cross is white: {levels[0]}");
            Assert.True(levels[1] < 0.55, $"the disc is black at 0.6 over the plate: {levels[1]}");
            Assert.InRange(hues[1].Green, 0.8, 1.01); // grey, not tinted
            // The disc starts at the strip's top edge (the cell's 2 above the photo are the 2 the badge shows): a point just inside it is the disc.
            Assert.InRange(glyphTop, 0, 2);
            var top = h.Main.OnMain(() => Frame(One<SwiftImage>(Named(strip, "Remove " + file.Name)), window));
            var hueAtTop = h.Main.OnMain(() => FramePixels.Levels(window, ((int)Math.Floor(top.Center.X), (int)Math.Floor(top.Y + 3))))[0];
            Assert.True(hueAtTop < 0.55, $"the disc's top is not cut off by the strip: {hueAtTop}");
            Close(h, window);
        });
    }
}
