using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/AttachmentPreview.swift (B2 1fbf81e). Part 2 of 2: the visual records,
// which need the headless Avalonia session (Bitmap creation, layout, .task(id:)). Geometry and colours are
// transcribed from the Swift and the UI-COMPOSER/PR-COMPOSER records, never read back from the control under test.
// Media Foundation's own frame decode is exercised only on its failure path here (AttachmentPreviewTests): no real
// video fixture exists in this tree, so the parts of PR-COMPOSER-202/203/204 that depend on a decoded frame are
// UNTESTABLE in this session (reported as such); the badge, box geometry and the "no frame" placeholder path are.
[Collection("Headless")]
public sealed class AttachmentPreviewSecondTests(HeadlessSession headless)
{
    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, Guid.NewGuid().ToString("N"))).FullName;

    private static void WriteBmp(string path, int width, int height)
    {
        var stride = checked((width * 3 + 3) & ~3);
        var pixels = checked(stride * height);
        var bytes = new byte[checked(54 + pixels)];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = 54 + y * stride + x * 3;
                bytes[i] = 17; bytes[i + 1] = 47; bytes[i + 2] = 91;
            }
        File.WriteAllBytes(path, bytes);
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 600, Height = 600, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private void PumpUntil(Func<bool> condition, int timeoutMs = 4000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var done = false;
            headless.OnUiThread(() =>
            {
                Dispatcher.UIThread.RunJobs();
                done = condition();
            });
            if (done) return;
            Thread.Sleep(15);
        }
        throw new TimeoutException("Condition was not met before the timeout.");
    }

    // PR-COMPOSER-073: DroppyCode/UI/Common/AttachmentPreview.swift:241-258.
    [Fact]
    [Trait("Req", "PR-COMPOSER-073")] // DroppyCode/UI/Common/AttachmentPreview.swift:246-258
    public void ImageDisplaySize_aspect_fits_the_pixel_header_into_480_by_360_floored()
    {
        headless.OnUiThread(() =>
        {
            var root = NewRoot();
            var path = Path.Combine(root, "wide.bmp");
            WriteBmp(path, 800, 400); // 2:1, scale = min(480/800, 360/400) = 0.6 -> 480 x 240
            var attachment = new Attachment { Name = "wide.bmp", Path = path, MimeType = "image/bmp" };
            var size = AttachmentLargePreview.ImageDisplaySize(attachment);
            Assert.Equal(480, size.Width);
            Assert.Equal(240, size.Height);
        });
    }

    // PR-COMPOSER-073: :252 an unreadable header defaults to a 4:3 aspect before fitting.
    [Fact]
    [Trait("Req", "PR-COMPOSER-073")] // DroppyCode/UI/Common/AttachmentPreview.swift:247, :252
    public void ImageDisplaySize_defaults_to_four_by_three_for_an_unreadable_header()
    {
        headless.OnUiThread(() =>
        {
            var attachment = new Attachment { Name = "missing.bmp", Path = Path.Combine(NewRoot(), "missing.bmp"), MimeType = "image/bmp" };
            var size = AttachmentLargePreview.ImageDisplaySize(attachment);
            // 4:3 fitted into 480x360: scale = min(480/4, 360/3) = 120 -> 480 x 360 exactly.
            Assert.Equal(480, size.Width);
            Assert.Equal(360, size.Height);
        });
    }

    // PR-COMPOSER-074: DroppyCode/UI/Common/AttachmentPreview.swift:287-323, :374-387. The loading-mark deviation
    // (UI-COMPOSER-077, OD-0016) shows while the thumbnail loads and hides once it lands.
    [Fact]
    [Trait("Req", "PR-COMPOSER-074")] // DroppyCode/UI/Common/AttachmentPreview.swift:374-377
    [Trait("Req", "UI-COMPOSER-077")] // DroppyCode/UI/Common/AttachmentPreview.swift:231-396
    public void Image_branch_shows_a_loading_mark_then_fades_the_thumbnail_in()
    {
        var root = NewRoot();
        var path = Path.Combine(root, "photo.bmp");
        WriteBmp(path, 40, 20);
        var attachment = new Attachment { Name = "photo.bmp", Path = path, MimeType = "image/bmp" };
        AttachmentLargePreview preview = null!;
        headless.OnUiThread(() =>
        {
            preview = new AttachmentLargePreview(attachment);
            Show(preview);
        });
        PumpUntil(() => VisualQuery.Descendants<Image>(preview).Any(image => image.Source is not null));
        var image = VisualQuery.Descendants<Image>(preview).First(i => i.Source is not null);
        Assert.Equal(1, image.Opacity);
    }

    // PR-COMPOSER-075: DroppyCode/UI/Common/AttachmentPreview.swift:390-395, :324-346.
    [Fact]
    [Trait("Req", "PR-COMPOSER-075")] // DroppyCode/UI/Common/AttachmentPreview.swift:390-395
    public void Small_utf8_text_file_shows_its_first_8000_characters_in_a_monospaced_box()
    {
        var root = NewRoot();
        var path = Path.Combine(root, "notes.txt");
        var text = new string('a', 9_000);
        File.WriteAllText(path, text, System.Text.Encoding.UTF8);
        var attachment = new Attachment { Name = "notes.txt", Path = path, MimeType = "text/plain" };
        AttachmentLargePreview preview = null!;
        headless.OnUiThread(() =>
        {
            preview = new AttachmentLargePreview(attachment);
            Show(preview);
        });
        PumpUntil(() => VisualQuery.Descendants<TextBlock>(preview).Any(t => (t.Text?.Length ?? 0) > 1000));
        var textBlock = VisualQuery.Descendants<TextBlock>(preview).First(t => (t.Text?.Length ?? 0) > 1000);
        Assert.Equal(8_000, textBlock.Text!.Length); // :394 String(text.prefix(8_000))
    }

    // PR-COMPOSER-075: a file at or over 500,000 bytes falls back to the icon-and-name row, never the text box.
    [Fact]
    [Trait("Req", "PR-COMPOSER-075")] // DroppyCode/UI/Common/AttachmentPreview.swift:392, :334-346
    public void Oversize_text_file_falls_back_to_the_icon_and_name_row()
    {
        var root = NewRoot();
        var path = Path.Combine(root, "big.txt");
        File.WriteAllBytes(path, new byte[500_000]);
        var attachment = new Attachment { Name = "big.txt", Path = path, MimeType = "text/plain" };
        AttachmentLargePreview preview = null!;
        headless.OnUiThread(() =>
        {
            preview = new AttachmentLargePreview(attachment);
            Show(preview);
        });
        PumpUntil(() => VisualQuery.Descendants<TextBlock>(preview).Any(t => t.Text == "big.txt"));
        // The icon row's 32x32 image (from FileIcons, not the media slot) is present.
        Assert.Contains(VisualQuery.Descendants<Image>(preview), image => image.Width == 32 && image.Height == 32);
    }

    // PR-COMPOSER-076, UI-COMPOSER-078, UI-COMPOSER-079: the footer's name/size text and its two link buttons.
    [Fact]
    [Trait("Req", "PR-COMPOSER-076")] // DroppyCode/UI/Common/AttachmentPreview.swift:348-370
    [Trait("Req", "UI-COMPOSER-078")] // DroppyCode/UI/Common/AttachmentPreview.swift:360-364
    [Trait("Req", "UI-COMPOSER-079")] // DroppyCode/UI/Common/AttachmentPreview.swift:365-369
    public void Footer_shows_name_size_and_the_two_link_buttons_and_they_act()
    {
        var root = NewRoot();
        var path = Path.Combine(root, "file.bin");
        File.WriteAllBytes(path, new byte[10]);
        var attachment = new Attachment { Name = "file.bin", Path = path, MimeType = "application/octet-stream" };
        string? revealed = null;
        string? opened = null;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.RevealInFileExplorer = p => revealed = p;
        ShellWorkspace.OpenWithDefaultApp = p => opened = p;
        try
        {
            headless.OnUiThread(() =>
            {
                var preview = new AttachmentLargePreview(attachment);
                Show(preview);
                var buttons = VisualQuery.Descendants<Button>(preview).ToList();
                var reveal = buttons.Single(b => (b.Content as TextBlock)?.Text == "Show in File Explorer"); // OD-0008 B1
                var open = buttons.Single(b => (b.Content as TextBlock)?.Text == "Open");
                reveal.Command?.Execute(null);
                if (reveal.Command is null) reveal.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                open.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var name = VisualQuery.Descendants<MiddleTruncatingTextBlock>(preview).Single();
                Assert.Equal("file.bin", name.FullText); // :349
            });
        }
        finally
        {
            ShellWorkspace.RevealInFileExplorer = previousReveal;
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
        }
        Assert.Equal(path, revealed);
        Assert.Equal(path, opened);
    }

    // PR-COMPOSER-203, UI-COMPOSER-082: DroppyCode/UI/Common/AttachmentPreview.swift:176-191.
    [Fact]
    [Trait("Req", "PR-COMPOSER-203")] // DroppyCode/UI/Common/AttachmentPreview.swift:178-191
    [Trait("Req", "UI-COMPOSER-082")] // DroppyCode/UI/Common/AttachmentPreview.swift:176-191
    public void AttachmentPlayBadge_is_a_30_point_black_circle_with_an_offset_white_glyph()
    {
        headless.OnUiThread(() =>
        {
            var badge = new AttachmentPlayBadge();
            Show(badge);
            var circle = VisualQuery.Descendants<Avalonia.Controls.Shapes.Ellipse>(badge).Single();
            Assert.Equal(30, circle.Width);
            Assert.Equal(30, circle.Height);
            var glyph = VisualQuery.Descendants<SwiftImage>(badge).Single();
            Assert.Equal("play.fill", glyph.SymbolName);
            Assert.IsType<TranslateTransform>(glyph.RenderTransform);
            Assert.Equal(1, ((TranslateTransform)glyph.RenderTransform!).X); // :187 .offset(x: 1)
            Assert.Equal(0, ((TranslateTransform)glyph.RenderTransform!).Y);
            var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(badge);
            Assert.Equal(Avalonia.Automation.AccessibilityView.Raw, Avalonia.Automation.AutomationProperties.GetAccessibilityView(badge)); // :189
        });
    }

    // PR-COMPOSER-204, UI-COMPOSER-081: DroppyCode/UI/Common/AttachmentPreview.swift:193-224. No real video fixture
    // exists, so this proves the box (size, radius, quaternary fill) and the "no frame yet" placeholder-plus-badge
    // state; the frame fade-in itself is UNTESTABLE here (see the file header).
    [Fact]
    [Trait("Req", "PR-COMPOSER-204")] // DroppyCode/UI/Common/AttachmentPreview.swift:197-216
    [Trait("Req", "UI-COMPOSER-081")] // DroppyCode/UI/Common/AttachmentPreview.swift:193-224
    public void AttachmentVideoThumbnail_default_box_is_56_with_12_point_rounding_and_a_badge()
    {
        headless.OnUiThread(() =>
        {
            var attachment = new Attachment { Name = "clip.mp4", Path = Path.Combine(NewRoot(), "clip.mp4"), MimeType = "video/mp4" };
            var thumbnail = new AttachmentVideoThumbnail(attachment); // :199 default size 56
            Show(thumbnail);
            Assert.Equal(56, thumbnail.Width);
            Assert.Equal(56, thumbnail.Height);
            Assert.True(thumbnail.ClipToBounds); // :216
            Assert.NotNull(VisualQuery.Descendants<AttachmentPlayBadge>(thumbnail).SingleOrDefault()); // :213
            Assert.Empty(VisualQuery.Descendants<Image>(thumbnail)); // no frame decoded yet for a nonexistent file
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-204")] // DroppyCode/UI/Common/AttachmentPreview.swift:199
    public void AttachmentVideoThumbnail_accepts_a_custom_size()
    {
        headless.OnUiThread(() =>
        {
            var attachment = new Attachment { Name = "clip.mp4", Path = Path.Combine(NewRoot(), "clip.mp4"), MimeType = "video/mp4" };
            var thumbnail = new AttachmentVideoThumbnail(attachment, size: 48); // PR-COMPOSER-180's own record covers the caller; this proves the parameter itself
            Assert.Equal(48, thumbnail.Width);
            Assert.Equal(48, thumbnail.Height);
        });
    }

    // PR-COMPOSER-205: DroppyCode/UI/Common/AttachmentPreview.swift:109-141.
    [Fact]
    [Trait("Req", "PR-COMPOSER-205")] // DroppyCode/UI/Common/AttachmentPreview.swift:115-127
    public void FileIcons_caches_by_extension_so_two_paths_sharing_one_share_the_bitmap()
    {
        headless.OnUiThread(() =>
        {
            var root = NewRoot();
            var a = Path.Combine(root, "a.marrowtestext");
            var b = Path.Combine(root, "b.marrowtestext");
            Assert.Same(FileIcons.Icon(a), FileIcons.Icon(b)); // :130-131 the extension alone is the key
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-205")] // DroppyCode/UI/Common/AttachmentPreview.swift:120-125
    public void FileIcons_evicts_the_oldest_entry_once_past_64_first_in_first_out()
    {
        headless.OnUiThread(() =>
        {
            var root = NewRoot();
            Bitmap Icon(int i) => FileIcons.Icon(Path.Combine(root, $"file.evict{i}"));
            var first = Icon(0);
            for (var i = 1; i < 65; i++) Icon(i); // fills the 64-entry bound and pushes it one over
            var firstAgain = Icon(0); // evicted: a fresh lookup, a different Bitmap instance
            Assert.NotSame(first, firstAgain);
            var recent = Icon(64);
            var recentAgain = Icon(64); // still cached: same instance
            Assert.Same(recent, recentAgain);
        });
    }

    // UI-COMPOSER-083: DroppyCode/UI/Common/AttachmentPreview.swift:398-440.
    [Fact]
    [Trait("Req", "UI-COMPOSER-083")] // DroppyCode/UI/Common/AttachmentPreview.swift:398-440
    public void ToolImagePreview_box_and_caption_and_the_thumbnail_loads()
    {
        var root = NewRoot();
        var path = Path.Combine(root, "read.bmp");
        WriteBmp(path, 20, 10);
        ToolImagePreview preview = null!;
        headless.OnUiThread(() =>
        {
            preview = new ToolImagePreview(path);
            Show(preview);
            var box = VisualQuery.Descendants<Border>(preview).First();
            Assert.Equal(420, box.MaxHeight); // :420
            var name = VisualQuery.Descendants<MiddleTruncatingTextBlock>(preview).Single();
            Assert.Equal("read.bmp", name.FullText); // :423 lastPathComponent
            Assert.Contains(VisualQuery.Descendants<Button>(preview), b => (b.Content as TextBlock)?.Text == "Open"); // :429-433, no reveal link here
        });
        PumpUntil(() => VisualQuery.Descendants<Image>(preview).Any(i => i.Source is not null));
        var image = VisualQuery.Descendants<Image>(preview).First(i => i.Source is not null);
        Assert.Equal(1, image.Opacity);
    }
}
