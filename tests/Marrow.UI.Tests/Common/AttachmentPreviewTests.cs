using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/AttachmentPreview.swift (B2 1fbf81e). Part 1 of 2: the pure, non-visual
// logic (PreviewImages, PreviewVideos, the Attachment.isVideo extension, and the Media Foundation frame grabber's
// failure paths). Every expected value is transcribed from the Swift and the records, never from the C# under test.
// The UI-drawing records (badges, thumbnails, the large preview panel, ToolImagePreview) are in
// AttachmentPreviewSecondTests, which needs the headless Avalonia session for Bitmap creation.
public sealed class AttachmentPreviewTests
{
    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;

    // PreviewImages.ResolveToolImagePath, .Attachment and .FileSize, and AttachmentLargePreview's video-size cache,
    // are all @MainActor (R-ISO-1): run each call on the manual main loop, as MainActorTests does.
    private static T OnMain<T>(TestRuntime runtime, Func<T> body)
    {
        var result = default(T)!;
        runtime.Loop.Enqueue(() => result = body());
        runtime.RunUntilIdle();
        return result;
    }

    // PR-CHAT-103: DroppyCode/UI/Common/AttachmentPreview.swift:9-16 the recognized image extensions.
    [Theory]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:9-16
    [InlineData("a.png", true)]
    [InlineData("a.JPG", true)] // :15 the extension test is case-insensitive through the lowercasing
    [InlineData("a.svg", true)]
    [InlineData("a.txt", false)]
    [InlineData("noextension", false)]
    public void IsImagePath_matches_the_thirteen_recognised_extensions(string path, bool expected) =>
        Assert.Equal(expected, PreviewImages.IsImagePath(path));

    // PR-CHAT-103: :51-53 a read call's title and detail are taken whole, spaces included.
    [Fact]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:29-40, :51-53
    public void Read_call_title_with_spaces_resolves_as_one_whole_path()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        var withSpaces = Path.Combine(root, "my photo.png");
        File.WriteAllBytes(withSpaces, [1]);
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = withSpaces };
        Assert.Equal(withSpaces, OnMain(runtime, () => PreviewImages.ResolveToolImagePath(call, null)));
    }

    // PR-CHAT-103: :54-58 any other kind's title/detail are split on whitespace, newlines and the punctuation set.
    [Fact]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:49-68
    public void Command_call_finds_an_image_candidate_among_split_words()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        var shot = Path.Combine(root, "shot.png");
        File.WriteAllBytes(shot, [1]);
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "saved to \"" + shot + "\" (done)" };
        Assert.Equal(shot, OnMain(runtime, () => PreviewImages.ResolveToolImagePath(call, null)));
    }

    // PR-CHAT-103: :30-32 no image extension anywhere means no candidate is split for and no cache entry is made.
    [Fact]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:29-32
    public void Title_mentioning_no_image_extension_returns_null_without_a_disk_check()
    {
        using var runtime = new TestRuntime();
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "does not exist at all" };
        Assert.Null(OnMain(runtime, () => PreviewImages.ResolveToolImagePath(call, null)));
    }

    // PR-CHAT-103: :64 an absolute path (drive letter, per windows_path) is used as is.
    [Fact]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:63-65
    public void Absolute_drive_letter_path_is_used_as_is()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        var path = Path.Combine(root, "a.png");
        File.WriteAllBytes(path, [1]);
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = path };
        Assert.Equal(path, OnMain(runtime, () => PreviewImages.ResolveToolImagePath(call, null)));
    }

    // PR-CHAT-103: :66-67 a file:// URL is resolved to its path.
    [Fact]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:66-67
    public void File_url_candidate_is_resolved_to_its_path()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        var path = Path.Combine(root, "b.png");
        File.WriteAllBytes(path, [1]);
        var url = new Uri(path).AbsoluteUri;
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = url };
        Assert.Equal(path, OnMain(runtime, () => PreviewImages.ResolveToolImagePath(call, null)));
    }

    // PR-CHAT-103: :70-71 a relative candidate resolves against the thread's working directory.
    [Fact]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:70-73
    public void Relative_candidate_resolves_against_the_working_directory_and_is_dropped_without_one()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        File.WriteAllBytes(Path.Combine(root, "c.png"), [1]);
        var relative = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "c.png" };
        Assert.Equal(Path.Combine(root, "c.png"), OnMain(runtime, () => PreviewImages.ResolveToolImagePath(relative, root)));
        Assert.Null(OnMain(runtime, () => PreviewImages.ResolveToolImagePath(relative, null))); // :72-73 continue: no candidate
    }

    // PR-CHAT-103: :74-75 only a file that exists counts.
    [Fact]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:74-76
    public void Nonexistent_candidate_path_yields_no_image()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = Path.Combine(root, "missing.png") };
        Assert.Null(OnMain(runtime, () => PreviewImages.ResolveToolImagePath(call, null)));
    }

    // PR-CHAT-103: :33-35 the status is part of the cache key: a running call's cached "not found" answer does not
    // outlive it once the call completes and the file now exists.
    [Fact]
    [Trait("Req", "PR-CHAT-103")] // DroppyCode/UI/Common/AttachmentPreview.swift:33-40
    public void Cache_key_includes_status_so_a_finished_calls_answer_is_looked_up_again()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        var path = Path.Combine(root, "d.png");
        var running = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = path, ToolCallStatus = ToolCall.Status.Running };
        Assert.Null(OnMain(runtime, () => PreviewImages.ResolveToolImagePath(running, null))); // file does not exist yet: cached null under "running"
        File.WriteAllBytes(path, [1]);
        Assert.Null(OnMain(runtime, () => PreviewImages.ResolveToolImagePath(running, null))); // same key: still the cached null
        var completed = running with { ToolCallStatus = ToolCall.Status.Completed };
        Assert.Equal(path, OnMain(runtime, () => PreviewImages.ResolveToolImagePath(completed, null))); // different key: looked up again
    }

    // PR-COMPOSER-201: DroppyCode/UI/Common/AttachmentPreview.swift:168-174.
    [Theory]
    [Trait("Req", "PR-COMPOSER-201")] // DroppyCode/UI/Common/AttachmentPreview.swift:170-173
    [InlineData("video/mp4", "a.txt", true)] // MIME prefix wins outright
    [InlineData("application/octet-stream", "a.mov", true)] // extension fallback, lowercase
    [InlineData("application/octet-stream", "a.MP4", true)] // extension fallback is case-insensitive via lowercasing
    [InlineData("application/octet-stream", "a.png", false)]
    [InlineData("application/octet-stream", "noext", false)] // an empty extension is not in the set
    [InlineData("VIDEO/mp4", "a.txt", false)] // :171 hasPrefix is not case-insensitive; falls to the extension test
    public void IsVideo_is_mime_prefix_then_the_eight_extensions(string mimeType, string path, bool expected)
    {
        var attachment = new Attachment { Name = "x", Path = path, MimeType = mimeType };
        Assert.Equal(expected, attachment.IsVideo);
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-201")] // DroppyCode/UI/Common/AttachmentPreview.swift:145-147
    [InlineData("mov")] [InlineData("mp4")] [InlineData("m4v")] [InlineData("mpg")]
    [InlineData("mpeg")] [InlineData("avi")] [InlineData("mkv")] [InlineData("webm")]
    public void PreviewVideos_recognises_exactly_the_eight_listed_extensions(string extension) =>
        Assert.True(PreviewVideos.IsVideoPath("clip." + extension));

    [Fact]
    [Trait("Req", "PR-COMPOSER-201")] // DroppyCode/UI/Common/AttachmentPreview.swift:145-151
    public void PreviewVideos_rejects_an_extension_outside_the_eight_but_accepts_it_uppercased()
    {
        Assert.False(PreviewVideos.IsVideoPath("clip.mp3"));
        Assert.True(PreviewVideos.IsVideoPath("clip.MOV")); // case-insensitive via lowercasing
    }

    // PR-COMPOSER-206: DroppyCode/UI/Common/AttachmentPreview.swift:80-93.
    [Fact]
    [Trait("Req", "PR-COMPOSER-206")] // DroppyCode/UI/Common/AttachmentPreview.swift:84-93
    public void Attachment_cache_returns_the_same_value_for_the_same_path()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        var path = Path.Combine(root, "photo.png");
        var first = OnMain(runtime, () => PreviewImages.Attachment(path));
        var second = OnMain(runtime, () => PreviewImages.Attachment(path));
        Assert.Same(first, second); // load-bearing identity: the preview panel's toggle compares by this
        Assert.Equal("photo.png", first.Name);
        Assert.Equal("image/png", first.MimeType);
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-206")] // DroppyCode/UI/Common/AttachmentPreview.swift:89-90
    [InlineData("x.png", "image/png")]
    [InlineData("x.jpg", "image/jpeg")]
    [InlineData("x.txt", "image/*")] // recognised-but-not-image type: literal fallback
    [InlineData("x.unknownext", "image/*")] // unrecognised extension: literal fallback
    public void Attachment_mime_type_falls_back_to_the_literal_image_star(string name, string expectedMime)
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + "-" + name);
        Assert.Equal(expectedMime, OnMain(runtime, () => PreviewImages.Attachment(path)).MimeType);
    }

    // PR-COMPOSER-076: DroppyCode/UI/Common/AttachmentPreview.swift:96-106 fileSize via ByteCountFormatter(.file).
    [Fact]
    [Trait("Req", "PR-COMPOSER-076")] // DroppyCode/UI/Common/AttachmentPreview.swift:102-106
    public void FileSize_is_null_for_a_missing_file_and_set_for_a_real_one()
    {
        using var runtime = new TestRuntime();
        var root = NewRoot();
        Assert.Null(OnMain(runtime, () => PreviewImages.FileSize(Path.Combine(root, "missing.bin"))));
        var real = Path.Combine(root, "real.bin");
        File.WriteAllBytes(real, new byte[1024]);
        Assert.NotNull(OnMain(runtime, () => PreviewImages.FileSize(real)));
    }

    // PR-COMPOSER-202/209/210 (MediaFrameGrabber): a nonexistent or non-video file yields no frame and no size, the
    // same "no track" failure path the Swift side records ("No readable video track... nothing is cached"). Real
    // frame-pixel decoding needs a real, valid container (mp4/mov/etc.); none exists as a checked-in fixture and
    // authoring one from scratch is out of scope here, so the decode-success path is UNTESTABLE in this session and
    // is not claimed as tested (reported as such).
    [Fact]
    [Trait("Req", "PR-COMPOSER-202")] // DroppyCode/UI/Common/AttachmentPreview.swift:153-165
    [Trait("Req", "PR-COMPOSER-209")] // DroppyCode/UI/Common/AttachmentPreview.swift:266-279
    public void MediaFrameGrabber_returns_null_for_an_unreadable_path()
    {
        var root = NewRoot();
        var missing = Path.Combine(root, "missing.mp4");
        Assert.Null(MediaFrameGrabber.Frame(missing, 100, 0.5));
        Assert.Null(MediaFrameGrabber.NaturalSize(missing));
        var notAVideo = Path.Combine(root, "notavideo.mp4");
        File.WriteAllBytes(notAVideo, [1, 2, 3, 4]);
        Assert.Null(MediaFrameGrabber.Frame(notAVideo, 100, 0.5));
        Assert.Null(MediaFrameGrabber.NaturalSize(notAVideo));
    }

    // PR-COMPOSER-209: :269 a non-video attachment's prefetch returns at once, caching nothing.
    [Fact]
    [Trait("Req", "PR-COMPOSER-209")] // DroppyCode/UI/Common/AttachmentPreview.swift:266-269
    public async Task PrefetchVideoSize_is_a_no_op_for_a_non_video_attachment()
    {
        using var runtime = new TestRuntime();
        var attachment = new Attachment { Name = "a.png", Path = "a.png", MimeType = "image/png" };
        // :269 the guard is before any await, so the returned Task is already completed when Enqueue's work finishes.
        var task = OnMain(runtime, () => AttachmentLargePreview.PrefetchVideoSize(attachment));
        await task;
        // No cached size: the panel falls back to the full imageBounds (PR-COMPOSER-210).
        Assert.Equal(AttachmentLargePreview.ImageBounds, OnMain(runtime, () => AttachmentLargePreview.VideoDisplaySize(attachment)));
    }

    // PR-COMPOSER-210: :283-285 nothing cached falls back to the full 480 by 360 imageBounds.
    [Fact]
    [Trait("Req", "PR-COMPOSER-210")] // DroppyCode/UI/Common/AttachmentPreview.swift:281-285
    public void VideoDisplaySize_falls_back_to_imageBounds_when_nothing_is_cached()
    {
        using var runtime = new TestRuntime();
        var attachment = new Attachment { Name = "clip.mp4", Path = Path.Combine(NewRoot(), "clip.mp4"), MimeType = "video/mp4" };
        Assert.Equal(new Avalonia.Size(480, 360), OnMain(runtime, () => AttachmentLargePreview.VideoDisplaySize(attachment)));
    }
}
