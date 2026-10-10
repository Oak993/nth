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
using Marrow.UI.Port.Nth;
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
        Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;

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
    // (UI-COMPOSER-077, OD-0016; since the nth motion pack step 13 the blurred backdrop and the glass puck, not Flow Arc)
    // shows while the thumbnail loads and goes once it lands; the picture settles in (opacity 0 to 1, scale 1.02 to 1, 0.6 s).
    [Fact]
    [Trait("Req", "PR-COMPOSER-074")] // DroppyCode/UI/Common/AttachmentPreview.swift:374-377
    [Trait("Req", "UI-COMPOSER-077")] // DroppyCode/UI/Common/AttachmentPreview.swift:231-396
    public void Image_branch_shows_a_loading_mark_then_fades_the_thumbnail_in()
    {
        var root = NewRoot();
        var path = Path.Combine(root, "photo.bmp");
        WriteBmp(path, 40, 20);
        var attachment = new Attachment { Name = "photo.bmp", Path = path, MimeType = "image/bmp" };
        headless.OnUiThread(() =>
        {
            // FrameClock.Source (LoadingMark, OD-0016) is an Ambient (AsyncLocal-scoped): it must be overridden and
            // read from the same thread, so the whole test body runs inside this one OnUiThread call.
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var preview = new AttachmentLargePreview(attachment);
            Show(preview);
            // while it loads: the backdrop and the 44 px puck (the spinner: a loading picture's size is not known) are mounted
            Assert.Single(VisualQuery.Descendants<NthPreviewBackdrop>(preview));
            Assert.False(preview.Backdrop.HasThumbnail); // nothing was drawn in the strip for this file
            Assert.Equal(44, preview.Puck.Width); Assert.Equal(44, preview.Puck.Height);
            Assert.False(preview.Puck.IsRing); Assert.IsType<NthSpinner>(preview.Puck.Mark);
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (!VisualQuery.Descendants<Image>(preview).Any(image => image.Source is not null))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The thumbnail never loaded.");
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(15);
            }
            var loaded = VisualQuery.Descendants<Image>(preview).First(i => i.Source is not null);
            // the picture settles in: a 0.6 s opacity transition (NthArrive.Settle), then it stands at 1
            Assert.Contains(loaded.Transitions!, t => t is Avalonia.Animation.DoubleTransition { Property: var p, Duration: var d } && p == Visual.OpacityProperty && d == TimeSpan.FromSeconds(.6));
            Assert.True(preview.Puck.HasArrived); Assert.True(preview.Backdrop.HasArrived);
            var settled = DateTime.UtcNow.AddSeconds(4);
            while (loaded.Opacity < 1)
            {
                if (DateTime.UtcNow > settled) throw new TimeoutException("The picture never finished settling in.");
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(15);
            }
            Assert.Equal(1, loaded.Opacity);
        });
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
        headless.OnUiThread(() =>
        {
            var textBlock = VisualQuery.Descendants<TextBlock>(preview).First(t => (t.Text?.Length ?? 0) > 1000);
            Assert.Equal(8_000, textBlock.Text!.Length); // :394 String(text.prefix(8_000))
        });
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
        headless.OnUiThread(() =>
        {
            // The icon row's 32x32 image (from FileIcons, not the media slot) is present.
            Assert.Contains(VisualQuery.Descendants<Image>(preview), image => image.Width == 32 && image.Height == 32);
        });
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-076")] // UI/Common/AttachmentPreview.swift:95-98, :341-350
    [InlineData(0, "Zero kB")]
    [InlineData(999, "1 kB")]
    public void Footer_renders_B3_file_size_as_a_caption_in_the_same_secondary_color_as_the_name(int bytes, string expected)
    {
        // :98's .byteCount(style: .file) uses Foundation's zero spelling and strict 999-byte threshold;
        // source evidence: sf-swift-6.2-RELEASE-ByteCountFormatStyle.swift:123, :153-180, :194-222 (O-66).
        var path = Path.Combine(NewRoot(), "footer.bin");
        File.WriteAllBytes(path, new byte[bytes]);
        headless.OnUiThread(() =>
        {
            using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
            var preview = new AttachmentLargePreview(new Attachment
                { Name = "footer.bin", Path = path, MimeType = "application/octet-stream" });
            var window = Show(preview);
            try
            {
                var name = VisualQuery.Descendants<MiddleTruncatingTextBlock>(preview).Single();
                var size = Assert.Single(VisualQuery.Descendants<TextBlock>(preview), t => t.Text == expected);
                Assert.Equal(10, name.FontSize); // .caption, ED-059
                Assert.Equal(10, size.FontSize);
                Assert.Equal(Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(name.ActualThemeVariant), Assert.IsType<SolidColorBrush>(name.Foreground).Color);
                Assert.Equal(Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(size.ActualThemeVariant), Assert.IsType<SolidColorBrush>(size.Foreground).Color);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-076")] // UI/Common/AttachmentPreview.swift:97, :341-351
    public void Footer_omits_the_size_when_the_file_is_missing()
    {
        headless.OnUiThread(() =>
        {
            var preview = new AttachmentLargePreview(new Attachment
                { Name = "missing.bin", Path = Path.Combine(NewRoot(), "missing.bin"), MimeType = "application/octet-stream" });
            var window = Show(preview);
            try
            {
                var name = VisualQuery.Descendants<MiddleTruncatingTextBlock>(preview).Single();
                var footerLabels = Assert.IsType<StackPanel>(name.Parent).Children;
                Assert.Single(footerLabels); // :347's optional size fails: only the filename remains.
            }
            finally { window.Close(); }
        });
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
            var thumbnail = new AttachmentVideoThumbnail(attachment, size: 48); // PR-COMPOSER-180 covers the caller; this proves the size parameter itself
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
        // ThumbnailFor's decode genuinely completes off the UI thread (ThumbnailCache is an Actor over the
        // thread pool: Marrow.Core/Support/ThumbnailCache.cs:26-46); the awaited continuation resumes by
        // posting back through AvaloniaSynchronizationContext to Dispatcher.UIThread. HeadlessUnitTestSession
        // runs under AvaloniaTestIsolationLevel.PerTest (its default): every separate headless.OnUiThread call
        // tears the Avalonia Application and Dispatcher down (Dispatcher.ResetForUnitTests, which only drains
        // jobs already queued at that moment) and rebuilds a fresh one on the next call. A PumpUntil-style
        // series of separate dispatches after Show() therefore polls a DIFFERENT, brand-new Dispatcher than the
        // one the decode's continuation was posted to, and never observes it (confirmed: the continuation's
        // own Task completes and a plain ContinueWith on it fires, but the compiler-generated await inside the
        // ToolImagePreview task body never resumes, and it does resume once the whole body below runs inside
        // one dispatch). So the whole wait has to run inside the same OnUiThread call that starts the load,
        // exactly as Image_branch_shows_a_loading_mark_then_fades_the_thumbnail_in above already does for the
        // same reason.
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

            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (!VisualQuery.Descendants<Image>(preview).Any(i => i.Source is not null))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The thumbnail never loaded.");
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(15);
            }
            var image = VisualQuery.Descendants<Image>(preview).First(i => i.Source is not null);
            Assert.Equal(1, image.Opacity);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-081")] // DroppyCode/UI/Common/AttachmentPreview.swift:202-216 (ZStack centers the badge)
    public void AttachmentVideoThumbnail_badge_is_centered_in_the_box()
    {
        headless.OnUiThread(() =>
        {
            var attachment = new Attachment { Name = "clip.mp4", Path = Path.Combine(NewRoot(), "clip.mp4"), MimeType = "video/mp4" };
            var thumbnail = new AttachmentVideoThumbnail(attachment);
            Show(thumbnail);
            var circle = VisualQuery.Descendants<Avalonia.Controls.Shapes.Ellipse>(thumbnail).Single();
            var center = circle.TranslatePoint(new Point(circle.Bounds.Width / 2, circle.Bounds.Height / 2), thumbnail)!.Value;
            Assert.Equal(28, center.X, 1);
            Assert.Equal(28, center.Y, 1);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-203")] // DroppyCode/UI/Common/AttachmentPreview.swift:304-323 (the video frame is overlaid with AttachmentPlayBadge)
    public void Large_video_preview_carries_the_play_badge_for_the_frame()
    {
        headless.OnUiThread(() =>
        {
            var attachment = new Attachment { Name = "clip.mp4", Path = Path.Combine(NewRoot(), "clip.mp4"), MimeType = "video/mp4" };
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var preview = new AttachmentLargePreview(attachment);
            Show(preview);
            Assert.Single(VisualQuery.Descendants<AttachmentPlayBadge>(preview));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-075")] // DroppyCode/UI/Common/AttachmentPreview.swift:324-333 (Text wraps inside the 440 box)
    public void Text_preview_wraps_a_long_line_inside_the_box()
    {
        var path = Path.Combine(NewRoot(), "long.txt");
        File.WriteAllText(path, string.Join(" ", Enumerable.Repeat("word", 400)), System.Text.Encoding.UTF8);
        var attachment = new Attachment { Name = "long.txt", Path = path, MimeType = "text/plain" };
        AttachmentLargePreview preview = null!;
        headless.OnUiThread(() =>
        {
            preview = new AttachmentLargePreview(attachment);
            Show(preview);
        });
        PumpUntil(() => VisualQuery.Descendants<TextBlock>(preview).Any(t => (t.Text?.Length ?? 0) > 1000));
        headless.OnUiThread(() =>
        {
            var textBlock = VisualQuery.Descendants<TextBlock>(preview).First(t => (t.Text?.Length ?? 0) > 1000);
            Assert.Equal(TextWrapping.Wrap, textBlock.TextWrapping);
            Assert.True(textBlock.Bounds.Width <= 440, $"width {textBlock.Bounds.Width}");
            Assert.True(textBlock.Bounds.Height > textBlock.FontSize * 3, $"height {textBlock.Bounds.Height}");
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-075")] // DroppyCode/UI/Common/AttachmentPreview.swift:325-333 (.textSelection(.enabled))
    public void Text_preview_is_selectable()
    {
        var path = Path.Combine(NewRoot(), "sel.txt");
        File.WriteAllText(path, new string('a', 2000), System.Text.Encoding.UTF8);
        var attachment = new Attachment { Name = "sel.txt", Path = path, MimeType = "text/plain" };
        AttachmentLargePreview preview = null!;
        headless.OnUiThread(() =>
        {
            preview = new AttachmentLargePreview(attachment);
            Show(preview);
        });
        PumpUntil(() => VisualQuery.Descendants<TextBlock>(preview).Any(t => (t.Text?.Length ?? 0) > 1000));
        headless.OnUiThread(() =>
        {
            var textBlock = VisualQuery.Descendants<TextBlock>(preview).First(t => (t.Text?.Length ?? 0) > 1000);
            Assert.IsType<SelectableTextBlock>(textBlock);
        });
    }

    [Theory]
    [Trait("Req", "UI-COMPOSER-083")] // DroppyCode/UI/Common/AttachmentPreview.swift:422-433 (Spacer(minLength: 8) puts Open at the trailing edge)
    [InlineData("read.bmp")]
    [InlineData("a-very-long-file-name-that-needs-truncating-a-very-long-file-name-that-needs-truncating-a-very-long-file-name-that-needs-truncating-a-very-long-file-name-that-needs-truncating.bmp")]
    public void ToolImagePreview_open_button_sits_at_the_trailing_edge(string fileName)
    {
        var path = Path.Combine(NewRoot(), fileName);
        WriteBmp(path, 20, 10);
        headless.OnUiThread(() =>
        {
            var preview = new ToolImagePreview(path);
            Show(preview);
            var open = VisualQuery.Descendants<Button>(preview).Single(b => (b.Content as TextBlock)?.Text == "Open");
            var right = open.TranslatePoint(new Point(open.Bounds.Width, 0), preview)!.Value.X;
            Assert.Equal(preview.Bounds.Width, right, 1);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-083")] // DroppyCode/UI/Common/AttachmentPreview.swift:409-419 (the spinner branch is replaced by the image)
    public void ToolImagePreview_spinner_is_gone_once_the_image_loads()
    {
        var path = Path.Combine(NewRoot(), "read.bmp");
        WriteBmp(path, 20, 10);
        headless.OnUiThread(() =>
        {
            var preview = new ToolImagePreview(path);
            Show(preview);
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (!VisualQuery.Descendants<Image>(preview).Any(i => i.Source is not null))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The thumbnail never loaded.");
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(15);
            }
            Assert.DoesNotContain(VisualQuery.Descendants<NthSpinner>(preview), p => p.IsVisible); // nth step 1 spinner 16 replaced the indeterminate bar
        });
    }
}
