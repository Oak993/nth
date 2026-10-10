using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.Common;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this file's own namespace

// Source: DroppyCode/UI/Common/AttachmentPreview.swift (B3 52bc792), :1-433.
// Shared helpers for showing large image previews of attachments and files the agent read.

/// <summary>PreviewImages: finding an image a tool call named, the per-path Attachment identity cache, and file size
/// formatting. AttachmentPreview.swift:8-107.</summary>
public static class PreviewImages
{
    // :9-12
    public static readonly IReadOnlyCollection<string> Extensions = new HashSet<string>(SwiftStringComparer.Instance)
    {
        "png", "jpg", "jpeg", "gif", "webp", "heic", "heif",
        "bmp", "tif", "tiff", "avif", "ico", "svg",
    };

    // :14-16
    public static bool IsImagePath(string path) => Extensions.Contains(SwiftString.Lowercased(SwiftPath.PathExtension(path)));

    // :18-22 Answers already found, keyed by everything the answer is made of. Bounded at 600 entries (RecentCache).
    private static readonly RecentCache<string, string?> resolvedPaths = new(600);

    // :24-40
    public static string? ResolveToolImagePath(ToolCall call, string? workingDirectory)
    {
        MainActor.AssumeIsolated(); // :28 @MainActor
        // :30-32 Most rows name no image at all: no cache entry is made for them.
        if (!(MentionsImage(call.Title) || (call.Detail is { } detail && MentionsImage(detail)))) return null;
        // :33-35 The status is part of the question: a running command's answer must not outlive it.
        var key = string.Concat(
            SwiftEnum<ToolCall.Kind>.RawValue(call.ToolCallKind), "\u0001",
            SwiftEnum<ToolCall.Status>.RawValue(call.ToolCallStatus), "\u0001",
            call.Title, "\u0001", call.Detail ?? "", "\u0001", workingDirectory ?? "");
        if (resolvedPaths.TryGetValue(key, out var hit)) return hit; // :36
        var resolved = Search(call, workingDirectory); // :37
        resolvedPaths.Insert(resolved, key); // :38
        return resolved;
    }

    // :42-45
    private static bool MentionsImage(string text)
    {
        var lowered = SwiftString.Lowercased(text);
        return Extensions.Any(extension => lowered.Contains("." + extension, StringComparison.Ordinal));
    }

    // :47-49 The search itself: the string work first, and the disk only for a candidate that already looks like an
    // image path.
    private static string? Search(ToolCall call, string? workingDirectory)
    {
        var candidates = new List<string>();
        if (call.ToolCallKind == ToolCall.Kind.Read) // :51-53
        {
            candidates.Add(call.Title);
            if (call.Detail is { Length: > 0 } detail) candidates.Add(detail);
        }
        else // :54-58
        {
            foreach (var text in new[] { call.Title, call.Detail ?? "" })
            {
                if (text.Length == 0) continue;
                candidates.AddRange(SplitOnCandidateSeparators(text));
            }
        }
        foreach (var candidate in candidates) // :60-76
        {
            var trimmed = candidate.Trimmed(); // :61
            if (trimmed.Length == 0 || !IsImagePath(trimmed)) continue; // :62
            string absolute;
            if (SwiftPath.IsAbsolute(trimmed)) // :64-65. windows_path: a drive letter or a UNC path, not only a leading slash.
            {
                absolute = trimmed;
            }
            else if (SwiftString.HasPrefix(trimmed, "file://")) // :66-67
            {
                var urlPath = SwiftURL.Create(trimmed)?.Path ?? trimmed;
                // windows_path: SwiftURL.Path follows RFC 8089 and keeps the leading slash before a Windows drive
                // letter ("file:///C:/x" -> "/C:/x", as URL.path does on macOS for a POSIX path with no drive
                // letter to strip); a native Windows path has no such leading slash. ENGINEERING DECISION.
                var stripped = urlPath.Length >= 3 && urlPath[0] == '/' && char.IsAsciiLetter(urlPath[1]) && urlPath[2] == ':'
                    ? urlPath[1..]
                    : urlPath;
                absolute = stripped.Replace('/', System.IO.Path.DirectorySeparatorChar);
            }
            else if (SwiftString.HasPrefix(trimmed, "~/")) // :68-69. windows_path: the user profile directory.
            {
                absolute = SwiftPath.ExpandingTildeInPath(trimmed);
            }
            else if (workingDirectory is { } directory) // :70-71
            {
                absolute = SwiftPath.AppendingPathComponent(directory, trimmed);
            }
            else
            {
                continue; // :72-73
            }
            if (File.Exists(absolute) || Directory.Exists(absolute)) return absolute; // :74-75 fileExists
        }
        return null; // :77
    }

    // :56 `CharacterSet.whitespacesAndNewlines.union(CharacterSet(charactersIn: "\"'`()<>,;"))`, then
    // `text.components(separatedBy: separators)`. No SwiftString helper covers a CharacterSet union split (R-STR-3
    // lists only newline- and whitespace-only splitters), so this walks Characters (grapheme clusters, matching
    // Swift's String.components) and keeps empty runs between adjacent separators, exactly as components(separatedBy:)
    // does. ENGINEERING DECISION.
    private static readonly HashSet<string> ExtraSeparatorCharacters = ["\"", "'", "`", "(", ")", "<", ">", ",", ";"];

    private static IReadOnlyList<string> SplitOnCandidateSeparators(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var character in SwiftString.Characters(text))
        {
            if (SwiftCharacter.IsWhitespace(character) || ExtraSeparatorCharacters.Contains(character))
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        parts.Add(current.ToString());
        return parts;
    }

    // :80-82 One attachment value per file, so the preview panel's toggle recognises the same image on a second tap
    // and closes instead of reopening. Unbounded (upstream_defects.md #101).
    private static readonly Dictionary<string, Attachment> attachments = new(SwiftStringComparer.Instance);

    // :84-93
    public static Attachment Attachment(string path)
    {
        MainActor.AssumeIsolated(); // :85 @MainActor
        if (attachments.TryGetValue(path, out var known)) return known; // :87
        var name = SwiftPath.LastPathComponent(path); // :88
        var mimeType = MimeTypeForExtension(SwiftString.Lowercased(SwiftPath.PathExtension(path))) ?? "image/*"; // :89
        var attachment = new Attachment
        {
            Name = name,
            Path = path,
            MimeType = SwiftString.HasPrefix(mimeType, "image/") ? mimeType : "image/*", // :90
        };
        attachments[path] = attachment; // :91
        return attachment;
    }

    // :89 `UTType(filenameExtension:)?.preferredMIMEType`. Windows has no UTType; the mapping only needs to recognise
    // image extensions (the caller discards anything that is not "image/..."), so this covers PreviewImages.Extensions
    // by the well-known IANA media type for each, the same set the port must recognise as an image everywhere else.
    private static string? MimeTypeForExtension(string extension) => extension switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "heic" => "image/heic",
        "heif" => "image/heif",
        "bmp" => "image/bmp",
        "tif" or "tiff" => "image/tiff",
        "avif" => "image/avif",
        "ico" => "image/x-icon",
        "svg" => "image/svg+xml",
        _ => null,
    };

    // :95-98 Int64.formatted(.byteCount(style: .file)): Foundation's ByteCountFormatStyle, delegated to the
    // shared ICU formatter (R-FMT-2), with the current locale and all other style defaults unchanged.
    public static string? FileSize(string path)
    {
        MainActor.AssumeIsolated(); // :95 @MainActor
        if (!File.Exists(path)) return null; // :97 `try? FileManager...attributesOfItem`
        long size;
        try { size = new FileInfo(path).Length; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        return SwiftFormatters.ByteCountStyleFile(size, SwiftFormatters.CurrentLocale.Current); // :98
    }
}

/// <summary>Finder icons by extension, cached: one lookup per row per render stalled scrolling.
/// AttachmentPreview.swift:109-141.</summary>
public static class FileIcons
{
    private static readonly Dictionary<string, Bitmap> cache = new(SwiftStringComparer.Instance); // :112
    private static readonly List<string> order = []; // :113
    private static readonly Bitmap fallback = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);

    // :115-127
    public static Bitmap Icon(string path)
    {
        MainActor.AssumeIsolated(); // :110-111 @MainActor
        var key = IconKey(path); // :116
        if (cache.TryGetValue(key, out var hit)) return hit; // :117
        // NSWorkspace.shared.icon(forFile:) never fails; ShellFileIcons.Icon can, so `fallback` (a blank 1x1 bitmap)
        // stands in for that difference, marked ENGINEERING DECISION.
        var image = ShellFileIcons.Icon(path)?.ToBitmap() ?? fallback; // :118
        cache[key] = image; // :119
        order.Add(key); // :120
        while (order.Count > 64) // :122-125 Bounded: icons are small but unbounded kinds are not.
        {
            var oldest = order[0];
            order.RemoveAt(0);
            cache.Remove(oldest);
        }
        return image; // :126
    }

    // :129-140
    private static string IconKey(string path)
    {
        var ext = SwiftString.Lowercased(SwiftPath.PathExtension(path)); // :130
        if (ext.Length != 0) return ext; // :131
        if (Directory.Exists(path)) return "folder"; // :132-135
        // :136-137 the resolved UTType identifier of an extensionless, non-directory path: Windows exposes no direct
        // read of a file's own content type from its bytes without a package. Both this tail case and the literal
        // "file" fallback (:139) collapse onto the same cache key (ENGINEERING DECISION): the icon itself is still
        // looked up for the exact path above, so only the cache key coarsens for this rare case.
        return "file"; // :139
    }
}

/// <summary>Video/movie attachments, shown with the same photo container. AttachmentPreview.swift:144-166.</summary>
public static class PreviewVideos
{
    // :145-147
    public static readonly IReadOnlyCollection<string> Extensions = new HashSet<string>(SwiftStringComparer.Instance)
    {
        "mov", "mp4", "m4v", "mpg", "mpeg", "avi", "mkv", "webm",
    };

    // :149-151
    public static bool IsVideoPath(string path) => Extensions.Contains(SwiftString.Lowercased(SwiftPath.PathExtension(path)));

    // :153-165 One video frame off the main actor, so opening a movie never stalls drawing.
    public static async Task<DecodedBitmap?> Thumbnail(string path, double pointSize)
    {
        var pixels = Math.Max(1L, (long)Math.Ceiling(pointSize * 2)); // :156
        // :162-163 0.5 seconds in, so a black first frame does not become the thumbnail.
        return await TaskHandle.Detached(() => Task.FromResult(MediaFrameGrabber.Frame(path, pixels, 0.5))).Value;
    }
}

/// <summary>The Attachment.isVideo extension. AttachmentPreview.swift:168-174. ED-164: an extension of a type from a
/// lower assembly (Marrow.Core.Models.Attachment) is one static class named after the Swift file with C# 14
/// extension blocks.</summary>
public static class AttachmentPreview
{
    extension(Attachment attachment)
    {
        // :170-173 A movie by MIME type, or by extension when the MIME type is generic.
        public bool IsVideo => SwiftString.HasPrefix(attachment.MimeType, "video/") || PreviewVideos.IsVideoPath(attachment.Path);
    }
}

/// <summary>The play badge drawn centered over a video thumbnail. It lives inside the same clipped container as the
/// photo, so the container style is preserved. AttachmentPreview.swift:176-191.</summary>
public sealed class AttachmentPlayBadge : Panel
{
    public AttachmentPlayBadge()
    {
        var circle = new Ellipse { Width = 30, Height = 30 }; // :181-183
        circle.Fill = SwiftColor.Black.Opacity(0.45).Brush(circle); // :182
        var glyph = SwiftImage.SystemName("play.fill") // :184
            .ForegroundStyle(SwiftColor.White) // :185
            .Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)); // :186
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.RenderTransform = new TranslateTransform(1, 0); // :187 .offset(x: 1)
        Children.Add(circle);
        Children.Add(glyph);
        // :189 .accessibilityHidden(true): the attachment row already names the file.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }
}

/// <summary>A list thumbnail for a movie that matches the photo thumbnail in AttachmentThumbnail: same size box,
/// same 12pt continuous rounding, same quaternary fill, same fill image content, plus a centered play badge.
/// AttachmentPreview.swift:193-224.</summary>
public sealed class AttachmentVideoThumbnail : Border
{
    private readonly Panel stack = new();
    // nth motion pack step 6: the plate with the light while the frame loads; the frame settles over it (the plate stays under it).
    private readonly NthSkeleton plate = new(NthSkeletonKind.Plate);
    private bool hasFrame;

    /// <summary>The placeholder plate shown while the frame loads (tests).</summary>
    public NthSkeleton Plate => plate;

    public AttachmentVideoThumbnail(Attachment attachment, double size = 56) // :198-199
    {
        this.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :205
        ClipToBounds = true; // :216 .clipShape(.rect(cornerRadius: 12, style: .continuous))
        // :206 .fill(.quaternary.opacity(0.6)). UNVERIFIED OD-0001: SwiftColor.Quaternary has no assigned ED yet
        // (see its definition in Port/SwiftColor.cs).
        Background = SwiftColor.Quaternary.Opacity(0.6).Brush(this);
        Width = size; // :215
        Height = size;
        stack.Children.Add(plate);
        stack.Children.Add(new AttachmentPlayBadge()); // :213, drawn over the (still empty) image slot
        Child = stack;
        this.Task(() => attachment.Path, async path => // :217 .task(id: attachment.path)
        {
            await AttachmentLargePreview.PrefetchVideoSize(attachment); // :218
            if (hasFrame) return; // :219 guard image == nil else return
            var frame = await PreviewVideos.Thumbnail(path, size); // :220
            if (SwiftTask.IsCancelled) return;
            if (frame is null) { plate.Finish(); return; } // no frame to wait for: the plate stops (it fades back to the quaternary fill)
            hasFrame = true;
            var image = new Image { Source = frame.ToBitmap(), Stretch = Stretch.UniformToFill }; // :208-210
            stack.Children.Insert(1, image); // over the plate, below the badge
            NthArrive.Settle(image); // :211, :221 nth step 6: opacity 0 to 1 and scale 1.02 to 1 over 0.6 s, in place of the 0.15 s fade
            // the plate stays under the picture while it settles, then stops
            DispatcherTimer.RunOnce(plate.Finish, TimeSpan.FromSeconds(.6));
        });
    }
}

/// <summary>A large preview shown in a popover when an attachment thumbnail is clicked. The photo slot is sized up
/// front, so the spinner and the loaded photo take exactly the same room. AttachmentPreview.swift:226-396.</summary>
public sealed class AttachmentLargePreview : Border
{
    // :241 The largest a photo is shown at inside the panel.
    public static readonly Size ImageBounds = new(480, 360);

    // :264 The movie aspect-fitted into imageBounds, by path, for the life of the process. No bound, no eviction.
    private static readonly Dictionary<string, Size> videoSizes = new(SwiftStringComparer.Instance);

    // :246-258 The photo aspect-fitted into imageBounds, from the file's pixel size (orientation applied). Reads
    // the header only (WicImageDecoder.ReadHeader), never decoding pixels.
    public static Size ImageDisplaySize(Attachment attachment)
    {
        var pixels = new Size(4, 3); // :247
        if (WicImageDecoder.ReadHeader(attachment.Path) is { } header && header.Width > 0 && header.Height > 0) // :248-252
        {
            pixels = header.Orientation >= 5 ? new Size(header.Height, header.Width) : new Size(header.Width, header.Height); // :253-254
        }
        var scale = Math.Min(ImageBounds.Width / pixels.Width, ImageBounds.Height / pixels.Height); // :256
        return new Size(Math.Floor(pixels.Width * scale), Math.Floor(pixels.Height * scale)); // :257
    }

    // :266-279 Loads a movie's display size off the tap path, once per path.
    public static async Task PrefetchVideoSize(Attachment attachment)
    {
        MainActor.AssumeIsolated(); // :264 @MainActor isolated storage
        if (!attachment.IsVideo || videoSizes.ContainsKey(attachment.Path)) return; // :269
        var natural = await TaskHandle.Detached(() => Task.FromResult(MediaFrameGrabber.NaturalSize(attachment.Path))).Value; // :270-273
        if (natural is not { } size) return; // :271-273 guard let ... else return
        if (size.Width <= 0 || size.Height <= 0) return; // :276 guard size.width > 0, size.height > 0
        var scale = Math.Min(ImageBounds.Width / size.Width, ImageBounds.Height / size.Height); // :277
        videoSizes[attachment.Path] = new Size(Math.Floor(size.Width * scale), Math.Floor(size.Height * scale)); // :278
    }

    // :281-285 The coordinator can pass this as imageSize so the panel is sized up front exactly like a photo.
    public static Size VideoDisplaySize(Attachment attachment)
    {
        MainActor.AssumeIsolated();
        return videoSizes.TryGetValue(attachment.Path, out var size) ? size : ImageBounds; // :284
    }

    // :390-395
    private static string? TextPreview(Attachment attachment)
    {
        if (!File.Exists(attachment.Path)) return null;
        FileInfo info;
        try { info = new FileInfo(attachment.Path); } catch (IOException) { return null; }
        if (info.Length >= 500_000) return null; // :392
        string text;
        try
        {
            var bytes = File.ReadAllBytes(attachment.Path);
            if (SwiftText.DecodeUtf8OrNil(bytes) is not { } decoded) return null; // :393
            text = decoded;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        if (text.Length == 0) return null; // :393
        return SwiftString.Prefix(text, 8_000); // :394
    }

    private readonly Attachment attachment; // :232
    private readonly Size? imageSize; // :234
    private readonly Image mediaImage = new(); // :236 @State private var image / :237 videoFrame, one control for either
    // nth motion pack step 13 (replaces OD-0113's Flow Arc): the blurred thumbnail with the light band behind the picture, and the
    // glass puck holding a white spinner over it. The size of a loading picture is not known, so the puck holds the spinner.
    private readonly NthPreviewBackdrop backdrop;
    private readonly PreviewPuck puck = new();

    /// <summary>The placeholder behind the picture (tests).</summary>
    public NthPreviewBackdrop Backdrop => backdrop;

    /// <summary>The glass puck over it (tests).</summary>
    public PreviewPuck Puck => puck;

    // The thumbnail the strip already decoded for this file (it was shown at 56 or 48 points), if the strip drew it.
    private static DecodedBitmap? KnownThumbnail(Attachment attachment) =>
        attachment.IsImage ? ThumbnailMemory.ImageFor(attachment.Path, 56) ?? ThumbnailMemory.ImageFor(attachment.Path, 48) : null;
    private readonly Panel contentArea = new();
    private readonly AttachmentPlayBadge videoBadge = new() { IsVisible = false }; // :312 overlay { AttachmentPlayBadge() }, shown with the frame

    // :232-234
    public AttachmentLargePreview(Attachment attachment, Size? imageSize = null)
    {
        this.attachment = attachment;
        this.imageSize = imageSize;
        backdrop = new NthPreviewBackdrop(KnownThumbnail(attachment));
        Padding = new Thickness(12); // :372
        MaxWidth = 504; // :373
        var outer = new StackPanel { Spacing = 10 }; // :288 VStack(alignment: .leading, spacing: 10)
        outer.Children.Add(BuildContentArea());
        outer.Children.Add(BuildFooter());
        Child = outer;
        this.Task(() => attachment.Path, LoadAsync); // :374 .task(id: attachment.path)
    }

    // :289-346 the content area: image, video, text preview or the icon-and-name row.
    private Control BuildContentArea()
    {
        if (attachment.IsImage || attachment.IsVideo) // :289, :304
        {
            var slot = imageSize ?? ImageBounds; // :290, :309
            mediaImage.Width = slot.Width; // :302, :322
            mediaImage.Height = slot.Height;
            mediaImage.Opacity = 0;
            mediaImage.Stretch = Stretch.Uniform; // :295, :314 .aspectRatio(contentMode: .fit)
            var clip = new Border // :296, :315 .clipShape(.rect(cornerRadius: 12, style: .continuous))
            {
                Width = slot.Width,
                Height = slot.Height,
                Child = attachment.IsVideo ? new Panel { Children = { backdrop, mediaImage, videoBadge, puck } } : new Panel { Children = { backdrop, mediaImage, puck } },
                HorizontalAlignment = HorizontalAlignment.Center, // :303, :323 .frame(maxWidth: .infinity, alignment: .center)
            }.RoundedRectangle(12, RoundedCornerStyle.Continuous);
            clip.ClipToBounds = true;
            return clip;
        }
        contentArea.HorizontalAlignment = HorizontalAlignment.Left;
        return contentArea; // :324-346, filled once the task resolves
    }

    // :324-333 the text preview branch, :334-346 the file icon and name row.
    private void SetFallbackContent(string? textPreview)
    {
        contentArea.Children.Clear();
        if (textPreview is { } text)
        {
            var textBlock = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; // :325-326, :328 Text wraps in the finite box, :328 .textSelection(.enabled)
            SwiftFont.System(SwiftFont.TextStyle.Caption, design: SwiftFont.Design.Monospaced).Apply(textBlock); // :327
            textBlock.HorizontalAlignment = HorizontalAlignment.Stretch; // :329
            var scroll = new ScrollViewer { Content = textBlock, Padding = new Thickness(10), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; // :330
            var height = Math.Min(320, Math.Max(120, SwiftString.Count(text) / 4)); // :332
            var box = new Border // :333 .background(.quaternary.opacity(0.45), in: .rect(cornerRadius: 10, style: .continuous))
            {
                Width = 440,
                Height = height,
                Child = scroll,
            }.RoundedRectangle(10, RoundedCornerStyle.Continuous);
            box.Background = SwiftColor.Quaternary.Opacity(0.45).Brush(box); // UNVERIFIED OD-0001, see SwiftColor.Quaternary
            contentArea.Children.Add(box);
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Width = 320, Margin = new Thickness(0, 8) }; // :335, :344-345
            var icon = new Image { Source = FileIcons.Icon(attachment.Path), Width = 32, Height = 32, Stretch = Stretch.Uniform }; // :336-339
            var name = new TextBlock { Text = attachment.Name, MaxLines = 2, TextWrapping = TextWrapping.Wrap }; // :340-342
            SwiftFont.Callout.Apply(name);
            row.Children.Add(icon);
            row.Children.Add(name);
            contentArea.Children.Add(row);
        }
    }

    // :341-363 the footer.
    private Control BuildFooter()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 12 }); // :359 Spacer(minLength: 12)
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; // :348
        var name = new MiddleTruncatingTextBlock { FullText = attachment.Name }; // :349, :353 .truncationMode(.middle)
        SwiftFont.Caption.Apply(name); // :350
        name.Foreground = Chrome.SecondaryText.Brush(name); // :351
        left.Children.Add(name);
        if (PreviewImages.FileSize(attachment.Path) is { } size) // :354
        {
            var sizeText = new TextBlock { Text = size };
            SwiftFont.Caption.Apply(sizeText); // :356
            sizeText.Foreground = Chrome.SecondaryText.Brush(sizeText); // :357
            left.Children.Add(sizeText);
        }

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; // :348, same HStack
        // :360-364 "Show in Finder" -> OD-0008 B1: "Show in File Explorer", revealing the file selected.
        var reveal = LinkButton("Show in File Explorer", () => ShellWorkspace.RevealInFileExplorer(attachment.Path));
        var open = LinkButton("Open", () => ShellWorkspace.OpenWithDefaultApp(attachment.Path)); // :365-369
        right.Children.Add(reveal);
        right.Children.Add(open);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    // .buttonStyle(.link), caption. No `.link` button style precedent exists yet in this tree; the accent-coloured
    // plain-text button below is an ENGINEERING DECISION, UNVERIFIED under OD-0001 for its exact rendering (underline,
    // hover state).
    private static Button LinkButton(string title, Action action)
    {
        var label = new TextBlock { Text = title };
        SwiftFont.Caption.Apply(label);
        // OD-0088 (ED-253): `.buttonStyle(.link)` draws its label in the view's tint, which the window root sets to the theme's
        // accent (WindowManager.swift:186-192, Popovers.swift:7-12): Chrome.accent, the user's pick when there is one.
        label.Foreground = Chrome.Accent.Brush(label);
        var button = PlainControls.Button(label, action);
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        return button;
    }

    // :374-387
    private async Task LoadAsync(string path)
    {
        if (attachment.IsImage) // :375
        {
            ScheduleLoadingMark();
            var loaded = await ThumbnailCache.Shared.Current.ThumbnailFor(path, ImageBounds.Width); // :376
            if (SwiftTask.IsCancelled) return;
            if (loaded is null) return; // PR-COMPOSER-074 failure case: the spinner (loading mark) stays.
            CancelLoadingMark();
            mediaImage.Source = loaded.Image.ToBitmap(); // :377
            NthArrive.Settle(mediaImage); // nth step 13: opacity 0 to 1 and scale 1.02 to 1 over 0.6 s, in place of the 0.15 s fade
        }
        else if (attachment.IsVideo) // :378
        {
            ScheduleLoadingMark();
            var frame = await PreviewVideos.Thumbnail(path, ImageBounds.Width); // :379
            if (SwiftTask.IsCancelled) return;
            if (frame is null) return; // :380-382. PR-COMPOSER-075: textPreview would be set here but the body never
                                        // reaches the textPreview branch for a video (PR-COMPOSER-201 decides the
                                        // branch once); PR-COMPOSER-202: the placeholder (the loading mark) just stays.
            CancelLoadingMark();
            mediaImage.Source = frame.ToBitmap(); // :383
            videoBadge.IsVisible = true; // :312
            NthArrive.Settle(mediaImage); // nth step 13, as for a photo
        }
        else // :384-386
        {
            SetFallbackContent(TextPreview(attachment));
        }
    }

    // UI-COMPOSER-077 (nth step 13): the backdrop shows at once; the puck keeps the 0.4 s gate the old loader had and appears with a 0.2 s fade.
    private void ScheduleLoadingMark()
    {
        // both start when they are mounted: nothing to begin here
    }

    private void CancelLoadingMark()
    {
        puck.Arrive(); // opacity 0 and scale .9 over 0.24 s
        backdrop.Arrive(); // the light band fades out in 0.2 s; the blurred thumbnail stays under the sharp picture
    }
}

/// <summary>Full-width image preview at the top of an expanded read tool row, so it is clear which photo the agent
/// looked at. AttachmentPreview.swift:398-440.</summary>
public sealed class ToolImagePreview : StackPanel
{
    private readonly Image image = new(); // :403 @State private var image: CGImage?
    private readonly NthSpinner spinner = new() { Width = 16, Height = 16 }; // :416-417 ProgressView().controlSize(.small); nth step 1 spinner 16

    public ToolImagePreview(string path) // :401
    {
        Orientation = Orientation.Vertical; // :406
        Spacing = 6;
        image.Opacity = 0;
        image.Stretch = Stretch.Uniform; // :413 .aspectRatio(contentMode: .fit)
        var box = new Border // :409 .fill(.quaternary.opacity(0.45))
        {
            MaxHeight = 420, // :420 .frame(maxWidth: .infinity, maxHeight: 420)
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new Panel { Children = { spinner, image } },
        }.RoundedRectangle(10, RoundedCornerStyle.Continuous); // :421
        box.ClipToBounds = true;
        box.Background = SwiftColor.Quaternary.Opacity(0.45).Brush(box); // UNVERIFIED OD-0001, see SwiftColor.Quaternary

        var caption = new DockPanel(); // :422 HStack(spacing: 8): the name, Spacer(minLength: 8), Open at the trailing edge
        var name = new MiddleTruncatingTextBlock { FullText = SwiftPath.LastPathComponent(path) }; // :423, :427 .truncationMode(.middle)
        SwiftFont.Caption.Apply(name); // :424
        name.Foreground = Chrome.SecondaryText.Brush(name); // :425
        var open = new TextBlock { Text = "Open" }; // :429-433
        SwiftFont.Caption.Apply(open);
        open.Foreground = Chrome.Accent.Brush(open); // :425 .buttonStyle(.link): the tint, Chrome.accent (ED-253)
        var openButton = PlainControls.Button(open, () => ShellWorkspace.OpenWithDefaultApp(path));

        openButton.Margin = new Thickness(8, 0, 0, 0); // :428 Spacer(minLength: 8)
        DockPanel.SetDock(openButton, Dock.Right);
        caption.Children.Add(openButton);
        caption.Children.Add(name);
        Children.Add(box);
        Children.Add(caption);

        this.Task(() => path, async currentPath => // :436 .task(id: path)
        {
            var loaded = await ThumbnailCache.Shared.Current.ThumbnailFor(currentPath, 900); // :437
            if (SwiftTask.IsCancelled || loaded is null) return;
            image.Source = loaded.Image.ToBitmap();
            image.Opacity = 1;
            spinner.IsVisible = false; // :409-419 the ProgressView branch is replaced by the image
        });
    }
}
