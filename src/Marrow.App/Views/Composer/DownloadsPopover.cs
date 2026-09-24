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

// Source: DroppyCode/App/Views/Composer/DownloadsPopover.swift (B2 1fbf81e), :1-242.
// Records: PR-COMPOSER-078, PR-COMPOSER-079, PR-COMPOSER-080, PR-COMPOSER-081, PR-COMPOSER-082,
// UI-COMPOSER-023, UI-COMPOSER-024, UI-COMPOSER-025, UI-COMPOSER-026, UI-COMPOSER-027, UI-COMPOSER-028.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body` once (R-VIEW-1); NSImage/CGImage are
// Avalonia.Media.IImage; the @Environment(\.dismiss) DownloadRow reads resolves through Popovers.GetClosePopover, the
// same seam Popovers.cs documents for every popover row this tree presents (ENGINEERING DECISION there).

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this file's own namespace

/// <summary>:5-12 <c>struct RecentDownload: Identifiable, Hashable</c> (R-VAL-1: struct -&gt; immutable record).
/// <c>url</c> is kept as its plain path string (R-PATH-1); no port site needs a <c>SwiftURL</c>.</summary>
public sealed record RecentDownload(string Url, string Name, string? Size, bool IsImage)
{
    /// <summary>:11 <c>var id: String { url.path }</c>: <see cref="Url"/> already is that path string.</summary>
    public string Id => Url;
}

/// <summary>:14-97 <c>final class RecentDownloads</c>, <c>@MainActor @Observable</c> (R-OBS-6 -&gt; MainActorModel).
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

    /// <summary>:39-96 <c>func load() async</c>.</summary>
    public async Task Load()
    {
        if (Directory is not { } directory) return; // :40 guard let directory else return
        // :41-59 Task.detached(priority: .userInitiated) { ... }: priority dropped (R-TASK-1).
        var scan = TaskHandle.Detached(() => Task.FromResult(ScanDirectory(directory)));
        // :60-64 withTaskCancellationHandler { await scan.value } onCancel: { scan.cancel() }
        var found = await SwiftTask.WithCancellationHandler(() => scan.Value, scan.Cancel);
        if (SwiftTask.IsCancelled) return; // :65
        var built = new List<RecentDownload>(found.Count); // :66-67
        await TaskGroup<(string Path, IImage Image)?>.Run(async group => // :68 withTaskGroup(of: (String, CGImage)?.self)
        {
            foreach (var (path, size) in found) // :69
            {
                var isImage = ImageExtensions.Contains(SwiftString.Lowercased(SwiftPath.PathExtension(path))); // :70
                built.Add(new RecentDownload(
                    path,
                    SwiftPath.LastPathComponent(path), // :73
                    size > 0 ? SwiftFormatters.FileByteCount(size, SwiftFormatters.CurrentLocale.Current) : null, // :74
                    isImage)); // :71-76
                if (isImage) // :77
                {
                    group.AddTask(async () => // :78-85
                    {
                        if (await ThumbnailCache.Shared.Current.ThumbnailFor(path, PreviewPointSize, fillingSquare: true)
                            is not { } thumbnail) return null; // :79-83
                        return (path, (IImage)thumbnail.Image.ToBitmap()); // :84
                    });
                }
            }
            Items = new ValueList<RecentDownload>(built); // :88
            Thumbnails = ValueMap<string, IImage>.Empty; // :89
            IsLoaded = true; // :90
            await foreach (var result in group) // :91 for await result in group
            {
                if (SwiftTask.IsCancelled || result is not { } pair) continue; // :92
                Thumbnails = Thumbnails.Mutating(map => map[pair.Path] = pair.Image); // :93
            }
        });
    }

    /// <summary>:41-59: read the directory (skipping hidden entries), drop directories, sort newest modified
    /// first (stable, R-COLL-1) and keep the first <see cref="Limit"/>.</summary>
    private static List<(string Path, long Size)> ScanDirectory(string directory)
    {
        IEnumerable<string> found;
        try { found = IoDirectory.EnumerateFileSystemEntries(directory); } // :44-48 contentsOfDirectory
        catch (Exception) { found = []; } // :48 (try? ... ?? [])
        var candidates = new List<(string Path, DateTimeOffset Modified, long Size)>();
        foreach (var path in found)
        {
            if (SwiftTask.IsCancelled) continue; // :51 guard !Task.isCancelled
            try
            {
                var attributes = File.GetAttributes(path); // :52 url.resourceValues(forKeys:)
                if (attributes.HasFlag(FileAttributes.Hidden)) continue; // :47 .skipsHiddenFiles
                if (attributes.HasFlag(FileAttributes.Directory)) continue; // :53 values.isDirectory == false
                var info = new FileInfo(path);
                candidates.Add((path, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length)); // :54
            }
            catch (Exception)
            {
                // :52 try? url.resourceValues(forKeys:) failing -> nil -> the entry is dropped.
            }
        }
        return candidates
            .OrderByDescending(candidate => candidate.Modified) // :56 sorted { $0.1 > $1.1 }
            .Take(Limit) // :57 prefix(RecentDownloads.limit)
            .Select(candidate => (candidate.Path, candidate.Size)) // :58
            .ToList();
    }
}

/// <summary>:99-163 <c>struct DownloadsPopover: View</c>. The attach button's popover: the latest downloads with
/// native icons and photo previews, plus a way out to Explorer.</summary>
public sealed class DownloadsPopover : Decorator
{
    private readonly RecentDownloads downloads = new(); // :105 @State private var downloads = RecentDownloads()
    private readonly Panel listSlot = new();
    private readonly StackPanel rowsPanel = new StackPanel { Orientation = Orientation.Vertical };
    private readonly ForEach<RecentDownload, string> rows;

    /// <summary>:102-103 <c>let pick: (URL) -> Void</c>, <c>let chooseOther: () -> Void</c>.</summary>
    public DownloadsPopover(Action<string> pick, Action chooseOther)
    {
        rows = new ForEach<RecentDownload, string>(
            rowsPanel,
            item => item.Id,
            item => new DownloadRow(item, downloads.Thumbnails.GetValueOrDefault(item.Url), () => pick(item.Url)), // :125-130
            (control, item) => ((DownloadRow)control).Update(item, downloads.Thumbnails.GetValueOrDefault(item.Url)));

        void UpdateList() // :114-132
        {
            listSlot.Children.Clear();
            if (!downloads.IsLoaded) // :115-121
            {
                listSlot.Children.Add(BuildLoadingRow());
            }
            else if (downloads.Items.Count == 0) // :122-123
            {
                listSlot.Children.Add(new PopoverNote("No downloads yet. Anything you download appears here.")); // :123
            }
            else // :124-131
            {
                rows.Apply(downloads.Items); // :125-131 ForEach(downloads.items)
                listSlot.Children.Add(rowsPanel);
            }
        }
        UpdateList();
        // Re-render whenever loading finishes, the item list lands or a thumbnail arrives, as the SwiftUI body
        // re-evaluating on every read of downloads.isLoaded/items/thumbnails would (R-VIEW-1).
        listSlot.Track(() => (downloads.IsLoaded, downloads.Items, downloads.Thumbnails), _ => UpdateList());

        Control BuildContent() // :107-142 the whole PopoverMenu content builder
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(new PopoverSectionHeader("Recent downloads")); // :114
            stack.Children.Add(listSlot);
            stack.Children.Add(new PopoverDivider()); // :133
            // DEVIATION OD-0008 B1: upstream reads "Show in Finder"; the port reads "Show in File Explorer" and
            // opens the Downloads folder in File Explorer.
            stack.Children.Add(new PopoverItem("Show in File Explorer", () => // :134-138
            {
                if (downloads.Directory is { } directory) ShellWorkspace.OpenWithDefaultApp(directory); // :135-137
            }, symbol: "folder"));
            stack.Children.Add(new PopoverItem("Choose Files…", chooseOther, symbol: "doc.on.doc")); // :139-141 U+2026 HORIZONTAL ELLIPSIS
            return stack;
        }

        // :113 PopoverMenu(maxHeight: 700, idealHeight: height) { ... }
        var menu = new PopoverMenu(BuildContent, maxHeight: 700, idealHeight: PopoverHeight());
        if (menu.Child is ScrollViewer scroll)
        {
            // Without forcing the inner content past PopoverMenu's own 210pt minimum, rows truncate (UI-COMPOSER-023
            // notes); the outer .frame(width: 360, ...) at :145 is what does this in SwiftUI's proposal-based layout.
            if (scroll.Content is Border padded) padded.Width = 360;
            menu.Track(() => PopoverHeight(), height => scroll.Height = height ?? double.NaN); // :145, re-applied per :152-156
        }
        Width = 360; // :145 .frame(width: 360, height: height)
        Child = menu;
        this.Task(() => 0, async _ => await downloads.Load()); // :146 .task { await downloads.load() }
    }

    private static Control BuildLoadingRow() // :116-121
    {
        var spinner = new ProgressBar // :118 ProgressView().controlSize(.small)
        {
            IsIndeterminate = true,
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center, // :117, :119 Spacer(minLength: 0) on both sides
        };
        return new Border { Child = spinner, Margin = new Thickness(0, 14), HorizontalAlignment = HorizontalAlignment.Stretch }; // :121 .padding(.vertical, 14)
    }

    /// <summary>:152-156 <c>private var height: CGFloat?</c>.</summary>
    private double? PopoverHeight()
    {
        if (!downloads.IsLoaded) return HeightForRows(RecentDownloads.Limit); // :153
        if (downloads.Items.Count == 0) return null; // :154
        return HeightForRows(downloads.Items.Count); // :155
    }

    /// <summary>:158-162 <c>private static func height(forRows count: Int) -> CGFloat</c>.</summary>
    private static double HeightForRows(int count) => Math.Min(700, count * DownloadRow.RowHeight + 106); // :159-161
}

/// <summary>:165-242 <c>private struct DownloadRow: View</c>. One row: the file's thumbnail or icon, its name and
/// its formatted size; a click arms the ghost flight, closes the popover, then attaches the file.</summary>
file sealed class DownloadRow : Decorator
{
    public const double RowHeight = 38; // :167 static let height: CGFloat = 38

    private readonly Panel previewSlot = new();
    private readonly MiddleTruncatingTextBlock nameBlock = new(); // :196-199 .lineLimit(1).truncationMode(.middle)
    private readonly TextBlock sizeBlock = new(); // :201-206
    private readonly AnimatedColor fill;

    private RecentDownload item;
    private IImage? thumbnail;
    private Action pick;

    // :174 @State private var previewFrame: CGRect = .zero, read only by the click action (R-VIEW-2).
    private Rect previewFrame;

    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        // :173 @State private var isHovering = false
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

        SwiftFont.Body.Apply(nameBlock); // :197
        SwiftFont.Callout.Apply(sizeBlock); // :203
        sizeBlock.Foreground = Chrome.SecondaryText.Brush(sizeBlock); // :204
        sizeBlock.MaxLines = 1; // :205 .lineLimit(1)

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; // :194 HStack(spacing: 10)
        row.Children.Add(previewSlot);
        row.Children.Add(nameBlock);
        row.Children.Add(new Control { Width = 16, HorizontalAlignment = HorizontalAlignment.Stretch }); // :200 Spacer(minLength: 16)
        row.Children.Add(sizeBlock);
        row.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.SetValue(TextElement.ForegroundProperty, Chrome.PrimaryText.Brush(row)); // :208
        row.Margin = new Thickness(8, 0); // :209 .padding(.horizontal, 8)
        row.Height = RowHeight; // :210 .frame(height: Self.height)

        fill = new AnimatedColor(row);
        var background = new Border { Child = row, Background = fill.Brush } // :212-213
            .RoundedRectangle(6, RoundedCornerStyle.Continuous);
        fill.Set(SwiftColor.Clear, null);

        var button = PlainControls.Button(background, Activate); // :178-193 the Button label, :217 .buttonStyle(.plain)
        button.PointerEntered += (_, _) => SetHovering(true); // :218-220 .onHover
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
        nameBlock.FullText = item.Name; // :196 Text(verbatim: item.name)
        previewSlot.Children.Clear();
        previewSlot.Children.Add(new ScreenFrameReader(rect => previewFrame = rect)); // :231, :239 .background { ScreenFrameReader { previewFrame = $0 } }
        previewSlot.Children.Add(BuildPreview());
        if (item.Size is { } size) // :201-206
        {
            sizeBlock.Text = size;
            sizeBlock.IsVisible = true;
        }
        else
        {
            sizeBlock.IsVisible = false;
        }
    }

    /// <summary>:224-241 <c>@ViewBuilder private var preview: some View</c>.</summary>
    private Control BuildPreview()
    {
        if (thumbnail is { } bitmap)
        {
            var image = new Image { Source = bitmap, Width = 28, Height = 28, Stretch = Stretch.UniformToFill }; // :226-229
            var clip = new Border { Width = 28, Height = 28, Child = image } // :230 .clipShape(.rect(cornerRadius: 6))
                .RoundedRectangle(6, RoundedCornerStyle.Continuous);
            clip.ClipToBounds = true;
            return clip;
        }
        return new Image { Source = FileIcons.Icon(item.Url), Width = 28, Height = 28, Stretch = Stretch.Uniform }; // :233-236
    }

    /// <summary>:178-193 the Button's action.</summary>
    private void Activate()
    {
        MainActor.AssumeIsolated();
        var picture = thumbnail ?? FileIcons.Icon(item.Url); // :179-184
        AttachmentDrops.Shared.Current.Arm(picture, previewFrame, radius: 6); // :185-189
        Popovers.GetClosePopover(this)?.Invoke(); // :190 dismiss()
        MainActor.Spawn(() => { pick(); return Task.CompletedTask; }); // :191-192 Task { @MainActor in pick() }
    }

    // :218-220 .onHover { hovering in withAnimation(Chrome.hover) { isHovering = hovering } }
    private void SetHovering(bool hovering)
    {
        Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering); // :219
        fill.Set(hovering ? Chrome.Overlay(0.1) : SwiftColor.Clear, Chrome.Hover); // :213 .fill(isHovering ? Chrome.overlay(0.1) : Color.clear)
    }
}
