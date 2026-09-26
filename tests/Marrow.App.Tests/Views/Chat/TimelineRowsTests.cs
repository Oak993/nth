using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B2 1fbf81e), :1209-1350 (AttachmentStrip, AttachmentThumbnail).
// The rest of TimelineRows.swift is still MARROW_STUB; PR-CHAT-079 also needs the user row (:89-91) and is not claimed.
[Collection("Headless")]
public sealed class TimelineRowsTests(HeadlessSession headless)
{
    private static Attachment Photo(string path) => new() { Name = "photo.png", Path = path, MimeType = "image/png" };

    private static AvaloniaWindow Show(Control control)
    {
        var window = new AvaloniaWindow { Content = control };
        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Border Plate(Control thumbnail) =>
        LogicalQuery.SelfAndDescendants<Border>(thumbnail).First(border => border.ClipToBounds);

    private static void OnMain(HeadlessSession headless, System.Action body) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(body);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1237, :1292-1304
    public void A_photo_thumbnail_is_a_56_point_plate_rounded_12_and_clipped()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewSlot());
            var window = Show(thumbnail);
            var plate = Plate(thumbnail);
            Assert.Equal(56, plate.Width);
            Assert.Equal(56, plate.Height);
            Assert.Equal(new CornerRadius(12), plate.CornerRadius);
            Assert.True(plate.ClipToBounds);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1237, :1256-1262
    public void A_caller_size_sizes_the_plate()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewCoordinator(), size: 48);
            var window = Show(thumbnail);
            Assert.Equal(48, Plate(thumbnail).Width);
            Assert.Equal(48, Plate(thumbnail).Height);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1307-1323
    public void A_file_that_is_neither_photo_nor_movie_shows_its_icon_inset_by_eight_hundredths_and_is_named()
    {
        OnMain(headless, () =>
        {
            var file = new Attachment { Name = "notes.txt", Path = @"C:\no-such-folder\notes.txt", MimeType = "text/plain" };
            var thumbnail = new AttachmentThumbnail(file, new AttachmentPreviewSlot());
            var window = Show(thumbnail);
            var plate = Plate(thumbnail);
            var icon = Assert.Single(LogicalQuery.SelfAndDescendants<Image>(plate));
            Assert.Equal(new Thickness(56 * 0.08), icon.Margin);
            Assert.Equal(Stretch.Uniform, icon.Stretch);
            Assert.Equal("notes.txt", AutomationProperties.GetName(plate));
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1305-1306
    public void A_movie_uses_the_video_thumbnail()
    {
        OnMain(headless, () =>
        {
            var movie = new Attachment { Name = "clip.mp4", Path = @"C:\no-such-folder\clip.mp4", MimeType = "video/mp4" };
            var thumbnail = new AttachmentThumbnail(movie, new AttachmentPreviewSlot());
            Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentVideoThumbnail>(thumbnail));
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1270-1271, :1295-1301
    public void A_photo_already_in_memory_starts_out_drawn_and_a_missing_one_leaves_the_plate_empty()
    {
        OnMain(headless, () =>
        {
            var drawnPath = @"C:\no-such-folder\seen.png";
            ThumbnailMemory.Store(new DecodedBitmap(2, 2, 8, new byte[16]), drawnPath, 56);
            var seen = new AttachmentThumbnail(Photo(drawnPath), new AttachmentPreviewSlot());
            Assert.Single(LogicalQuery.SelfAndDescendants<Image>(Plate(seen)));

            var missing = new AttachmentThumbnail(Photo(@"C:\no-such-folder\never-seen.png"), new AttachmentPreviewSlot());
            var window = Show(missing);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Image>(Plate(missing)));
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1327
    public void A_thumbnail_button_never_takes_focus()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewSlot());
            Assert.False(LogicalQuery.SelfAndDescendants<Button>(thumbnail).Single().Focusable);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1222-1225
    public void The_strip_lays_thumbnails_6_apart_each_with_its_name_as_tooltip()
    {
        OnMain(headless, () =>
        {
            var a = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
            var b = new Attachment { Name = "b.txt", Path = @"C:\no-such-folder\b.txt", MimeType = "text/plain" };
            var strip = new AttachmentStrip(new ValueList<Attachment>([a, b]));
            var window = Show(strip);
            var row = LogicalQuery.SelfAndDescendants<StackPanel>(strip).First();
            Assert.Equal(6, row.Spacing);
            var thumbnails = LogicalQuery.SelfAndDescendants<AttachmentThumbnail>(strip).ToList();
            Assert.Equal(2, thumbnails.Count);
            Assert.Equal("a.txt", ToolTip.GetTip(thumbnails[0]));
            Assert.Equal("b.txt", ToolTip.GetTip(thumbnails[1]));
            window.Close();
        });
    }
}
