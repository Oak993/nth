using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using IoDirectory = System.IO.Directory;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/DownloadsPopover.swift (B3 52bc792), :1-236.
// Records: PR-COMPOSER-078, PR-COMPOSER-079, PR-COMPOSER-080, PR-COMPOSER-081, PR-COMPOSER-082,
// UI-COMPOSER-023, UI-COMPOSER-024, UI-COMPOSER-025, UI-COMPOSER-026, UI-COMPOSER-027, UI-COMPOSER-028.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body` once (R-VIEW-1); NSImage/CGImage are
// Avalonia.Media.IImage; the @Environment(\.dismiss) DownloadRow reads resolves through Popovers.GetClosePopover, the
// same seam Popovers.cs documents for every popover row this tree presents (ENGINEERING DECISION there).
// ED-198: B3 drops the B2 file-private `sizeFormatter` (a ByteCountFormatter, .file style) and formats the size at
// :68 with `Int64(size).formatted(.byteCount(style: .file))` instead; the port reads that through the shared
// SwiftFormatters.ByteCountStyleFile helper (SwiftFormatters.cs:515, already used by AboutUpdateViews.cs:1232), not
// SwiftFormatters.FileByteCount (the ByteCountFormatter-shaped helper the B2 body called; its "1 KB" uppercase and
// 999-byte-exact threshold no longer match this site's "1 kB" lowercase / 999-byte-inclusive-kB output).

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this file's own namespace

/// <summary>:5-12 <c>struct RecentDownload: Identifiable, Hashable</c> (R-VAL-1: struct -&gt; immutable record).
/// <c>url</c> is kept as its plain path string (R-PATH-1); no port site needs a <c>SwiftURL</c>.</summary>
public sealed record RecentDownload(string Url, string Name, string? Size, bool IsImage)
{
    /// <summary>:11 <c>var id: String { url.path }</c>: <see cref="Url"/> already is that path string.</summary>
    public string Id => Url;
}

/// <summary>:14-91 <c>final class RecentDownloads</c>, <c>@MainActor @Observable</c> (R-OBS-6 -&gt; MainActorModel).
/// The user's latest downloads with previews, loaded once per popover opening.</summary>
public sealed class RecentDownloads : MainActorModel
{
    public const int Limit = 15; // :18 nonisolated static let limit = 15
    public const double PreviewPointSize = 30; // :19 nonisolated static let previewPointSize: CGFloat = 30

    // :29-31 nonisolated private static let imageExtensions: Set<String>
    private static readonly HashSet<string> ImageExtensions = new(SwiftStringComparer.Instance)
    {
        "png", "jpg", "jpeg", "heic", "heif", "gif", "tiff", "tif", "bmp", "webp",
    };

    // :25-27 var directory: URL? { FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first }
    // windows_path (PR-COMPOSER-078): SHGetKnownFolderPath(FOLDERID_Downloads). Kept behind an Ambient factory so
    // tests can replace the platform read with a throwaway folder (R-TEST-9) without touching the real profile.
    public static Ambient<Func<string?>> DirectoryProvider { get; } = new(ResolveDownloadsDirectory);

    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    private static string? ResolveDownloadsDirectory()
    {
        if (SHGetKnownFolderPath(FolderIdDownloads, 0, IntPtr.Zero, out var native) != 0) return null;
        try { return Marshal.PtrToStringUni(native); }
        finally { Marshal.FreeCoTaskMem(native); }
    }

    private ValueList<RecentDownload> items = ValueList<RecentDownload>.Empty;

    /// <summary>:21 <c>private(set) var items: [RecentDownload] = []</c>.</summary>
    public ValueList<RecentDownload> Items { get => Read(items); private set => Set(ref items, value); }

    private ValueMap<string, IImage> thumbnails = ValueMap<string, IImage>.Empty;

    /// <summary>:22 <c>private(set) var thumbnails: [String: CGImage] = [:]</c>.</summary>
    public ValueMap<string, IImage> Thumbnails { get => Read(thumbnails); private set => Set(ref thumbnails, value); }

    private bool isLoaded;

    /// <summary>:23 <c>private(set) var isLoaded = false</c>.</summary>
    public bool IsLoaded { get => Read(isLoaded); private set => Set(ref isLoaded, value); }

    /// <summary>:25-27, not tracked: a plain computed read of a platform folder, never part of a body evaluation.</summary>
    public string? Directory => DirectoryProvider.Current();

    /// <summary>:33-90 <c>func load() async</c>.</summary>
    public async Task Load()
    {
        if (Directory is not { } directory) return; // :34 guard let directory else return
        // :35-53 Task.detached(priority: .userInitiated) { ... }: priority dropped (R-TASK-1).
        var scan = TaskHandle.Detached(() => Task.FromResult(ScanDirectory(directory)));
        // :54-58 withTaskCancellationHandler { await scan.value } onCancel: { scan.cancel() }
        var found = await SwiftTask.WithCancellationHandler(() => scan.Value, scan.Cancel);
        if (SwiftTask.IsCancelled) return; // :59
        var built = new List<RecentDownload>(found.Count); // :60-61
        await TaskGroup<(string Path, IImage Image)?>.Run(async group => // :62 withTaskGroup(of: (String, CGImage)?.self)
        {
            foreach (var (path, size) in found) // :63
            {
                var isImage = ImageExtensions.Contains(SwiftString.Lowercased(SwiftPath.PathExtension(path))); // :64
                built.Add(new RecentDownload(
                    path,
                    SwiftPath.LastPathComponent(path), // :67
                    // :68 size: size > 0 ? Int64(size).formatted(.byteCount(style: .file)) : nil (B3, ED-198)
                    size > 0 ? SwiftFormatters.ByteCountStyleFile(size, SwiftFormatters.CurrentLocale.Current) : null,
                    isImage)); // :65-70
                if (isImage) // :71
                {
                    group.AddTask(async () => // :72-79
                    {
                        if (await ThumbnailCache.Shared.Current.ThumbnailFor(path, PreviewPointSize, fillingSquare: true)
                            is not { } thumbnail) return null; // :73-77
                        // DEVIATION (platform adaptation, not a Swift behaviour change): a TaskGroup child runs on
                        // the pool (R-TASK-5), so ToBitmap's Avalonia WriteableBitmap allocation is hopped onto
                        // MainActor.Run here, exactly as Wallpaper.cs's Render()/Install() do for the identical
                        // hazard ("Unable to locate IPlatformRenderInterface" from an arbitrary ThreadPool thread
                        // under contention). CGImage construction in Swift needs no such hop.
                        return (path, (IImage)await MainActor.Run(() => thumbnail.Image.ToBitmap())); // :78
                    });
                }
            }
            Items = new ValueList<RecentDownload>(built); // :82
            Thumbnails = ValueMap<string, IImage>.Empty; // :83
            IsLoaded = true; // :84
            await foreach (var result in group) // :85 for await result in group
            {
                if (SwiftTask.IsCancelled || result is not { } pair) continue; // :86
                Thumbnails = Thumbnails.Mutating(map => map[pair.Path] = pair.Image); // :87
            }
        });
    }

    /// <summary>:35-53: read the directory (skipping hidden entries), drop directories, sort newest modified
    /// first (stable, R-COLL-1) and keep the first <see cref="Limit"/>.</summary>
    private static List<(string Path, long Size)> ScanDirectory(string directory)
    {
        IEnumerable<string> found;
        try { found = IoDirectory.EnumerateFileSystemEntries(directory); } // :38-42 contentsOfDirectory
        catch (Exception) { found = []; } // :42 (try? ... ?? [])
        var candidates = new List<(string Path, DateTimeOffset Modified, long Size)>();
        foreach (var path in found)
        {
            if (SwiftTask.IsCancelled) continue; // :45 guard !Task.isCancelled
            try
            {
                // :41 .skipsHiddenFiles combines the Windows hidden attribute with a leading dot, since neither
                // alone is what .skipsHiddenFiles means on macOS (same convention as NativeFileTools.IsHiddenEntry
                // and TokenLedger's own .skipsHiddenFiles call site).
                if (SwiftPath.LastPathComponent(path).StartsWith('.')) continue; // :41 .skipsHiddenFiles
                var attributes = File.GetAttributes(path); // :46 url.resourceValues(forKeys:)
                if (attributes.HasFlag(FileAttributes.Hidden)) continue; // :41 .skipsHiddenFiles
                if (attributes.HasFlag(FileAttributes.Directory)) continue; // :47 values.isDirectory == false
                var info = new FileInfo(path);
                candidates.Add((path, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length)); // :48
            }
            catch (Exception)
            {
                // :46 try? url.resourceValues(forKeys:) failing -> nil -> the entry is dropped.
            }
        }
        return candidates
            .OrderByDescending(candidate => candidate.Modified) // :50 sorted { $0.1 > $1.1 }
            .Take(Limit) // :51 prefix(RecentDownloads.limit)
            .Select(candidate => (candidate.Path, candidate.Size)) // :52
            .ToList();
    }
}

/// <summary>:93-157 <c>struct DownloadsPopover: View</c>. The attach button's popover: the latest downloads with
/// native icons and photo previews, plus a way out to Explorer.</summary>
public sealed class DownloadsPopover : Decorator
{
    private readonly RecentDownloads downloads = new(); // :99 @State private var downloads = RecentDownloads()
    private readonly Panel listSlot = new();
    private readonly StackPanel rowsPanel = new StackPanel { Orientation = Orientation.Vertical };
    private readonly ForEach<RecentDownload, string> rows;

    /// <summary>:96-97 <c>let pick: (URL) -> Void</c>, <c>let chooseOther: () -> Void</c>.</summary>
    public DownloadsPopover(Action<string> pick, Action chooseOther)
    {
        rows = new ForEach<RecentDownload, string>(
            rowsPanel,
            item => item.Id,
            item => new DownloadRow(item, downloads.Thumbnails.GetValueOrDefault(item.Url), () => pick(item.Url)), // :119-124
            (control, item) => ((DownloadRow)control).Update(item, downloads.Thumbnails.GetValueOrDefault(item.Url)));

        void UpdateList() // :108-126
        {
            listSlot.Children.Clear();
            if (!downloads.IsLoaded) // :109-115
            {
                listSlot.Children.Add(BuildLoadingRow());
            }
            else if (downloads.Items.Count == 0) // :116-117
            {
                listSlot.Children.Add(new PopoverNote("No downloads yet. Anything you download appears here.")); // :117
            }
            else // :118-125
            {
                rows.Apply(downloads.Items); // :119-125 ForEach(downloads.items)
                listSlot.Children.Add(rowsPanel);
            }
        }
        UpdateList();
        // Re-render whenever loading finishes, the item list lands or a thumbnail arrives, as the SwiftUI body
        // re-evaluating on every read of downloads.isLoaded/items/thumbnails would (R-VIEW-1).
        listSlot.Track(() => (downloads.IsLoaded, downloads.Items, downloads.Thumbnails), _ => UpdateList());

        Control BuildContent() // :101-136 the whole PopoverMenu content builder
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(new PopoverSectionHeader("Recent downloads")); // :108
            stack.Children.Add(listSlot);
            stack.Children.Add(new PopoverDivider()); // :127
            // DEVIATION OD-0008 B1: upstream reads "Show in Finder"; the port reads "Show in File Explorer" and
            // opens the Downloads folder in File Explorer.
            stack.Children.Add(new PopoverItem("Show in File Explorer", () => // :128-132
            {
                if (downloads.Directory is { } directory) ShellWorkspace.OpenWithDefaultApp(directory); // :129-131
            }, symbol: "folder"));
            stack.Children.Add(new PopoverItem("Choose Files…", chooseOther, symbol: "doc.on.doc")); // :133-135 U+2026 HORIZONTAL ELLIPSIS
            return stack;
        }

        // :107 PopoverMenu(maxHeight: 700, idealHeight: height) { ... }
        var menu = new PopoverMenu(BuildContent, maxHeight: 700, idealHeight: PopoverHeight());
        if (menu.Child is ScrollViewer scroll)
        {
            // Without forcing the inner content past PopoverMenu's own 210pt minimum, rows truncate (UI-COMPOSER-023
            // notes); the outer .frame(width: 360, ...) at :139 is what does this in SwiftUI's proposal-based layout.
            if (scroll.Content is Border padded) padded.Width = 360;
            menu.Track(() => PopoverHeight(), height => scroll.Height = height ?? double.NaN); // :139, re-applied per :146-150
        }
        Width = 360; // :139 .frame(width: 360, height: height)
        Child = menu;
        this.Task(() => 0, async _ => await downloads.Load()); // :140 .task { await downloads.load() }
    }

    private static Control BuildLoadingRow() // :110-115
    {
        var spinner = new ProgressBar // :112 ProgressView().controlSize(.small)
        {
            IsIndeterminate = true,
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center, // :111, :113 Spacer(minLength: 0) on both sides
        };
        return new Border { Child = spinner, Margin = new Thickness(0, 14), HorizontalAlignment = HorizontalAlignment.Stretch }; // :115 .padding(.vertical, 14)
    }

    /// <summary>:146-150 <c>private var height: CGFloat?</c>.</summary>
    private double? PopoverHeight()
    {
        if (!downloads.IsLoaded) return HeightForRows(RecentDownloads.Limit); // :147
        if (downloads.Items.Count == 0) return null; // :148
        return HeightForRows(downloads.Items.Count); // :149
    }

    /// <summary>:152-156 <c>private static func height(forRows count: Int) -> CGFloat</c>.</summary>
    private static double HeightForRows(int count) => Math.Min(700, count * DownloadRow.RowHeight + 106); // :153-155
}

/// <summary>:159-236 <c>private struct DownloadRow: View</c>. One row: the file's thumbnail or icon, its name and
/// its formatted size; a click arms the ghost flight, closes the popover, then attaches the file.</summary>
file sealed class DownloadRow : Decorator
{
    public const double RowHeight = 38; // :161 static let height: CGFloat = 38

    private readonly Panel previewSlot = new();
    private readonly MiddleTruncatingTextBlock nameBlock = new(); // :190-193 .lineLimit(1).truncationMode(.middle)
    private readonly TextBlock sizeBlock = new(); // :195-200
    private readonly AnimatedColor fill;

    private RecentDownload item;
    private IImage? thumbnail;
    private Action pick;

    // :168 @State private var previewFrame: CGRect = .zero, read only by the click action (R-VIEW-2).
    private Rect previewFrame;

    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        // :167 @State private var isHovering = false
        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    private readonly ViewState state = new();

    /// <summary>For tests: the hover state the row is currently showing.</summary>
    public bool IsHovering => state.IsHovering;

    public DownloadRow(RecentDownload item, IImage? thumbnail, Action pick)
    {
        this.item = item;
        this.thumbnail = thumbnail;
        this.pick = pick;

        SwiftFont.Body.Apply(nameBlock); // :191
        SwiftFont.Callout.Apply(sizeBlock); // :197
        sizeBlock.Foreground = Chrome.SecondaryText.Brush(sizeBlock); // :198
        sizeBlock.MaxLines = 1; // :199 .lineLimit(1)

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; // :188 HStack(spacing: 10)
        row.Children.Add(previewSlot);
        row.Children.Add(nameBlock);
        row.Children.Add(new Control { Width = 16, HorizontalAlignment = HorizontalAlignment.Stretch }); // :194 Spacer(minLength: 16)
        row.Children.Add(sizeBlock);
        row.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.SetValue(TextElement.ForegroundProperty, Chrome.PrimaryText.Brush(row)); // :202
        row.Margin = new Thickness(8, 0); // :203 .padding(.horizontal, 8)
        row.Height = RowHeight; // :204 .frame(height: Self.height)

        fill = new AnimatedColor(row);
        var background = new Border { Child = row, Background = fill.Brush } // :206-207
            .RoundedRectangle(6, RoundedCornerStyle.Continuous);
        fill.Set(SwiftColor.Clear, null);

        var button = PlainControls.Button(background, Activate); // :172-187 the Button label, :211 .buttonStyle(.plain)
        button.PointerEntered += (_, _) => SetHovering(true); // :212-214 .onHover
        button.PointerExited += (_, _) => SetHovering(false);

        Child = button;
        Refresh();
    }

    /// <summary>Called by <see cref="ForEach{TItem,TId}"/> for a surviving id (R-VIEW-8): the item and its
    /// thumbnail may have changed (the thumbnail arrives after the row first renders).</summary>
    public void Update(RecentDownload newItem, IImage? newThumbnail)
    {
        item = newItem;
        thumbnail = newThumbnail;
        Refresh();
    }

    private void Refresh()
    {
        nameBlock.FullText = item.Name; // :190 Text(verbatim: item.name)
        previewSlot.Children.Clear();
        previewSlot.Children.Add(new ScreenFrameReader(rect => previewFrame = rect)); // :225, :233 .background { ScreenFrameReader { previewFrame = $0 } }
        previewSlot.Children.Add(BuildPreview());
        if (item.Size is { } size) // :195-200
        {
            sizeBlock.Text = size;
            sizeBlock.IsVisible = true;
        }
        else
        {
            sizeBlock.IsVisible = false;
        }
    }

    /// <summary>:218-235 <c>@ViewBuilder private var preview: some View</c>.</summary>
    private Control BuildPreview()
    {
        if (thumbnail is { } bitmap)
        {
            var image = new Image { Source = bitmap, Width = 28, Height = 28, Stretch = Stretch.UniformToFill }; // :220-223
            var clip = new Border { Width = 28, Height = 28, Child = image } // :224 .clipShape(.rect(cornerRadius: 6))
                .RoundedRectangle(6, RoundedCornerStyle.Continuous);
            clip.ClipToBounds = true;
            return clip;
        }
        return new Image { Source = FileIcons.Icon(item.Url), Width = 28, Height = 28, Stretch = Stretch.Uniform }; // :227-230
    }

    /// <summary>:172-187 the Button's action.</summary>
    private void Activate()
    {
        MainActor.AssumeIsolated();
        var picture = thumbnail ?? FileIcons.Icon(item.Url); // :173-178
        AttachmentDrops.Shared.Current.Arm(picture, previewFrame, radius: 6); // :179-183
        Popovers.GetClosePopover(this)?.Invoke(); // :184 dismiss()
        MainActor.Spawn(() => { pick(); return Task.CompletedTask; }); // :185-186 Task { @MainActor in pick() }
    }

    // :212-214 .onHover { hovering in withAnimation(Chrome.hover) { isHovering = hovering } }
    private void SetHovering(bool hovering)
    {
        Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering); // :213
        fill.Set(hovering ? Chrome.Overlay(0.1) : SwiftColor.Clear, Chrome.Hover); // :207 .fill(isHovering ? Chrome.overlay(0.1) : Color.clear)
    }
}
