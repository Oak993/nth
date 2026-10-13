using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Theme/Wallpaper.swift (B3 52bc7926), :1-270. B2 moved this file up to App (IMPLEMENTING
// 2.1, HANDOFF section 9) because it read Services.Store.Storage.wallpaperDirectory. B3 moves wallpaperDirectory
// itself out of Services into Core.Support.StoragePaths (upstream commit d70cff8fe, "layer rules restored";
// StoragePaths.swift:14-15; Wallpaper.swift:89, :128, :130 read StoragePaths.wallpaperDirectory, renames only,
// R3-53), so nothing here needs Marrow.Services any more: ED-195 (lane A10) supersedes ED-189 and moves the file
// back down to Marrow.UI, matching what units.json already named as its target. WallpaperCard.swift (Marrow.App)
// and RootView.swift (Marrow.App, once implemented) remain its readers.

namespace Marrow.UI.Theme;

/// <summary>:9-11 <c>private struct DecodedPicture: @unchecked Sendable</c>: a decoded picture crossing back to the
/// main actor.</summary>
/// <param name="Image">:10 <c>let image: NSImage</c>.</param>
// Wallpaper.swift:9-11
internal sealed record DecodedPicture(IImage Image);

/// <summary>:15-270 <c>@MainActor @Observable final class WallpaperStore</c> (R-OBS-6): the window's picked picture.</summary>
// Wallpaper.swift:15-270
public sealed class WallpaperStore : MainActorModel
{
    /// <summary>
    /// :17 <c>static let shared = WallpaperStore()</c>: an R-LIFE-2 Ambient, matching the same pattern used for
    /// every other test-replaceable `.shared` singleton in the tree (e.g. Marrow.Services.Port.SecretStore.Shared).
    /// A test overrides the value for its own async flow only, so parallel xUnit test classes never share one.
    /// </summary>
    // Wallpaper.swift:17
    public static Ambient<WallpaperStore> Shared { get; } = new(() => new WallpaperStore());

    /// <summary>:19-22 <c>private enum Key</c>: the store's two defaults keys (PR-SETTINGS-103).</summary>
    // Wallpaper.swift:19-22
    private static class Key
    {
        // Wallpaper.swift:20
        public const string Softness = "wallpaperSoftness";

        // Wallpaper.swift:21
        public const string File = "wallpaperFile";
    }

    // Wallpaper.swift:32
    private IImage? image;

    // Wallpaper.swift:34
    private bool isInstalling;

    // Wallpaper.swift:36
    private string? lastError;

    // Wallpaper.swift:41
    private double softness;

    // Wallpaper.swift:61
    private bool isAdjustingSoftness;

    // Wallpaper.swift:74
    private string? fileURL;

    // Wallpaper.swift:76
    private TaskHandle? renderTask;

    // Wallpaper.swift:77
    private TaskHandle? liveTask;

    // Wallpaper.swift:79
    private double? liveRequested;

    // Wallpaper.swift:81. ED-NEW-perf-memory-2: only the width of the full-size copy is kept (it gives the live blur its radius scale, :196). The copy itself
    // (about 23 MB for a 3200 px picture) was read for nothing else and stayed resident for the whole run.
    private int baseWidth;

    // Wallpaper.swift:83
    private DecodedBitmap? previewBase;

    // PORT-ONLY (ED-NEW-perf-memory-2): the file previewBase and baseWidth belong to, so a later render of the same file reuses the preview instead of decoding
    // it again, and a drag never blurs the previous picture's preview after a new one was installed.
    private string? previewFile;

    // Wallpaper.swift:32
    public IImage? Image
    {
        get => Read(image);
        private set => Set(ref image, value);
    }

    // Wallpaper.swift:34
    public bool IsInstalling
    {
        get => Read(isInstalling);
        private set => Set(ref isInstalling, value);
    }

    // Wallpaper.swift:36
    public string? LastError
    {
        get => Read(lastError);
        private set => Set(ref lastError, value);
    }

    // Wallpaper.swift:41-55
    public double Softness
    {
        get => Read(softness);
        set => Set(ref softness, value, _ =>
        {
            var clamped = Softness >= 0 ? (Softness < 1 ? Softness : 1) : 0;
            if (clamped != Softness)
            {
                Softness = clamped;
                return;
            }

            UserDefaults.Standard.Current.Set(Key.Softness, Softness);
            ScheduleRerender();
        });
    }

    // Wallpaper.swift:57
    public bool HasWallpaper => Computed(() => Image is not null || fileURL is not null);

    // Wallpaper.swift:61-71
    public bool IsAdjustingSoftness
    {
        get => Read(isAdjustingSoftness);
        set => Set(ref isAdjustingSoftness, value, oldValue =>
        {
            if (IsAdjustingSoftness == oldValue || fileURL is null) return;
            if (IsAdjustingSoftness)
            {
                RenderLive();
            }
            else
            {
                liveRequested = null;
                Render();
            }
        });
    }

    // Wallpaper.swift:85-95 (B3 :89 reads StoragePaths.wallpaperDirectory, a rename only, R3-53)
    private WallpaperStore()
    {
        var stored = Plist.AsDouble(UserDefaults.Standard.Current.Object(Key.Softness)) ?? 0;
        softness = stored >= 0 ? (stored < 1 ? stored : 1) : 0;

        var name = UserDefaults.Standard.Current.String(Key.File);
        if (name is null || name.Length == 0) return;

        var url = SwiftPath.AppendingPathComponent(StoragePaths.WallpaperDirectory, name); // B3 :89
        if (!File.Exists(url) && !Directory.Exists(url)) return;

        fileURL = url;
        Render();
    }

    // Wallpaper.swift:98-109
    public static bool Accepts(string url)
    {
        var extension = SwiftPath.FileURLPathExtension(url);
        if (extension.Length != 0 && IsSupportedImageExtension(extension)) return true;
        return HasImageSignature(url);
    }

    // Wallpaper.swift:113-144
    public void Install(string from)
    {
        if (!Accepts(from))
        {
            LastError = "That is not a picture.";
            return;
        }

        IsInstalling = true;
        LastError = null;
        renderTask?.Cancel();
        var capturedSoftness = Softness;
        _ = TaskHandle.Detached(async () =>
        {
            var extension = SwiftPath.FileURLPathExtension(from);
            extension = extension.Length == 0 ? "jpg" : SwiftString.Lowercased(extension);
            var destination = SwiftPath.AppendingPathComponent(
                StoragePaths.WallpaperDirectory, // B3 :128
                $"wallpaper-{SwiftUUID.UuidString(Guid.NewGuid())}.{extension}");
            try
            {
                Directory.CreateDirectory(StoragePaths.WallpaperDirectory); // B3 :130
                File.Copy(from, destination);
            }
            catch (Exception e) when (SwiftTry.IsThrownError(e))
            {
                SwiftTry.Ignore(() => FileOps.RemoveItem(destination));
                await MainActor.Run(FailInstall);
                return;
            }

            // DEVIATION (platform adaptation, not a Swift behaviour change): decode the pixels off-main exactly as
            // Wallpaper.swift:137 does (Task.detached), but defer ToImage's Avalonia WriteableBitmap allocation to
            // the MainActor.Run hop just below, as Render() and RenderLive() already do (:236, :276). NSImage
            // construction needs no main-thread binding in Swift, so :262-269 (Decode) builds the AppKit image
            // off-main too; Avalonia's platform render interface is not reliably resolvable from an arbitrary
            // ThreadPool thread under contention (observed: "Unable to locate IPlatformRenderInterface" from this
            // exact call site under the parallel suite), so the port keeps every ToImage call on the main hop.
            // Timing and IsInstalling's observable flip are unchanged: FinishInstall still runs only after decode.
            var decodedBitmap = DecodeBitmap(destination, capturedSoftness);
            if (decodedBitmap is null)
            {
                SwiftTry.Ignore(() => FileOps.RemoveItem(destination));
                await MainActor.Run(FailInstall);
                return;
            }

            await MainActor.Run(() => FinishInstall(destination, new DecodedPicture(ToImage(decodedBitmap))));
        });
    }

    // Wallpaper.swift:147-156
    public void Remove()
    {
        renderTask?.Cancel();
        if (fileURL is { } oldFile) SwiftTry.Ignore(() => FileOps.RemoveItem(oldFile));
        fileURL = null;
        DropCopies(); // PORT-ONLY (ED-NEW-perf-memory-2): the removed picture's preview is not kept
        UserDefaults.Standard.Current.RemoveObject(Key.File);
        Transaction.WithAnimation(Animation.EaseOut(0.25), () => Image = null);
        LastError = null;
    }

    // Wallpaper.swift:160-178
    private void Render()
    {
        if (fileURL is not { } currentFile) return;
        var capturedSoftness = Softness;
        var knownPreview = previewFile == currentFile ? previewBase : null; // the same file's preview is not decoded again
        renderTask?.Cancel();
        renderTask = TaskHandle.Detached(async () =>
        {
            var baseAndPreview = await Task.Run(() =>
                (Thumbnail(currentFile, 3200), knownPreview ?? Thumbnail(currentFile, 1600)));
            if (baseAndPreview.Item1 is not { } baseBitmap) return;

            var rendered = await Task.Run(() =>
                capturedSoftness > 0 ? Blurred(baseBitmap, capturedSoftness) : baseBitmap);
            if (rendered is null) return;

            await MainActor.Run(() =>
            {
                if (SwiftTask.IsCancelled || fileURL != currentFile) return;
                baseWidth = baseBitmap.Width;
                previewBase = baseAndPreview.Item2;
                previewFile = currentFile;
                Image = ToImage(rendered);
            });
        });
    }

    // Wallpaper.swift:180-187
    private void ScheduleRerender()
    {
        if (fileURL is null) return;
        if (IsAdjustingSoftness) RenderLive();
        else Render();
    }

    // Wallpaper.swift:192-210
    private void RenderLive()
    {
        if (previewBase is not { } smallBase || baseWidth <= 0 || previewFile != fileURL)
        {
            Render();
            return;
        }

        liveRequested = Softness;
        if (liveTask is not null) return;

        var scale = (double)smallBase.Width / baseWidth;
        liveTask = TaskHandle.Detached(async () =>
        {
            // DEVIATION (platform adaptation, not a Swift behaviour change): Swift's loop runs on the main actor
            // with no suspension between `takeLiveRequest()` returning nil and `self?.liveTask = nil` (:201-208),
            // so a softness arriving at that moment starts a new loop. Here the take is its own main-actor hop, so
            // the loop's exit decision and the clearing of liveTask are made in that one hop (TakeLiveRequestOrEnd);
            // a separate hop to clear it afterwards left a gap in which renderLive() saw a live task still "running"
            // and dropped the newest value.
            while (await MainActor.Run(TakeLiveRequestOrEnd) is { } value)
            {
                var rendered = await Task.Run(() => Blurred(smallBase, value, scale));
                if (rendered is null)
                {
                    await MainActor.Run(() => liveTask = null); // :205 `break`, then :208
                    return;
                }

                await MainActor.Run(() =>
                {
                    if (!IsAdjustingSoftness) return;
                    Image = ToImage(rendered);
                });
            }
        });
    }

    // Wallpaper.swift:201, :208: the loop's `takeLiveRequest()` and, when it returns nil, `self?.liveTask = nil`, in one hop.
    private double? TakeLiveRequestOrEnd()
    {
        var value = TakeLiveRequest();
        if (value is null) liveTask = null;
        return value;
    }

    // Wallpaper.swift:213-217
    private double? TakeLiveRequest()
    {
        if (!IsAdjustingSoftness || liveRequested is not { } value) return null;
        liveRequested = null;
        return value;
    }

    // Wallpaper.swift:219-227
    private void FinishInstall(string file, DecodedPicture picture)
    {
        if (fileURL is { } previous) SwiftTry.Ignore(() => FileOps.RemoveItem(previous));
        fileURL = file;
        DropCopies(); // PORT-ONLY (ED-NEW-perf-memory-2): the previous picture's preview is not the new one's
        UserDefaults.Standard.Current.Set(Key.File, SwiftPath.LastPathComponent(file));
        Wallpapers.WallpaperLibraryStore.CustomImageInstalled(); // OD-0110: a successful custom pick replaces the built-in.
        Transaction.WithAnimation(Animation.Spring(duration: 0.45, bounce: 0.2), () => Image = picture.Image);
        IsInstalling = false;
    }

    // PORT-ONLY (ED-NEW-perf-memory-2)
    private void DropCopies()
    {
        previewBase = null;
        previewFile = null;
        baseWidth = 0;
    }

    // Wallpaper.swift:229-232
    private void FailInstall()
    {
        IsInstalling = false;
        LastError = "Could not use that picture.";
    }

    // Wallpaper.swift:234-245
    public static DecodedBitmap? Thumbnail(string url, long maxPixel) =>
        WicImageDecoder.Decode(url, maxPixel, fillingSquare: false);

    // Wallpaper.swift:247-260
    public static DecodedBitmap? Blurred(DecodedBitmap @base, double softness, double radiusScale = 1)
    {
        var radius = softness * 28 * radiusScale;
        if (!(radius > 0.01)) return @base;

        return WallpaperBlur.Apply(@base, radius); // ED-NEW-perf-memory-2: the same Gaussian, in whole rows with SIMD and in parallel
    }

    // Wallpaper.swift:262-269
    public static IImage? Decode(string url, double softness)
    {
        var rendered = DecodeBitmap(url, softness);
        return rendered is null ? null : ToImage(rendered);
    }

    // The pixel part of Decode (:262-269), with no Avalonia call in it: safe to run on any thread, unlike ToImage
    // (see the Install() call site above).
    private static DecodedBitmap? DecodeBitmap(string url, double softness)
    {
        var bitmap = Thumbnail(url, 3200);
        if (bitmap is null) return null;
        return softness > 0 ? Blurred(bitmap, softness) : bitmap;
    }

    // Wallpaper.swift:98-108
    private static bool IsSupportedImageExtension(string extension) =>
        SwiftString.Lowercased(extension) is
            "bmp" or "dib" or "gif" or "hdp" or "ico" or "jfif" or "jpe" or "jpeg" or "jpg" or
            "jxr" or "png" or "tif" or "tiff" or "wdp";

    // Wallpaper.swift:104-106
    private static bool HasImageSignature(string url)
    {
        try
        {
            using var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[12];
            var count = stream.Read(header);
            if (count >= 8 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return true;
            if (count >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return true;
            if (count >= 6 &&
                header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' &&
                header[3] == (byte)'8' && header[5] == (byte)'a')
                return true;
            if (count >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M')
                return true;
            if (count >= 4 &&
                ((header[0] == (byte)'I' && header[1] == (byte)'I' && header[2] == 0x2A && header[3] == 0) ||
                 (header[0] == (byte)'M' && header[1] == (byte)'M' && header[2] == 0 && header[3] == 0x2A)))
                return true;
            if (count >= 12 &&
                header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
                header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
                return true;
            return count >= 4 && header[0] == 0 && header[1] == 0 && header[2] == 1 && header[3] == 0;
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return false;
        }
    }

    // Wallpaper.swift:249-260
    private static IImage ToImage(DecodedBitmap bitmap)
    {
        var result = new WriteableBitmap(
            new PixelSize(bitmap.Width, bitmap.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var locked = result.Lock();
        Marshal.Copy(bitmap.Pixels, 0, locked.Address, bitmap.Pixels.Length);
        return result;
    }
}
