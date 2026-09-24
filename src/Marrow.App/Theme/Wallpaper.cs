using System;
using Avalonia.Media;
using Marrow.Core.Port;

// Source: DroppyCode/UI/Theme/Wallpaper.swift (B2 1fbf81e), :1-270. Moved up from UI to App (IMPLEMENTING 2.1,
// HANDOFF section 9: "WallpaperStore moves to App"): the store writes the library's wallpaper folder
// (Storage.wallpaperDirectory, :89, :128, :130, a Marrow.Services type Marrow.UI may not reference), and every reader
// is an App view (RootView.swift:64, WallpaperCard.swift:8, :96, :171). The file keeps its Swift name and path under
// its new layer, as Models/MCP.cs does in Marrow.Services.
// Skeleton (c100 group 5): both types the file declares, the defaults keys, the observed state and the members the
// other files use. Every member with behaviour is left to the implementing unit (MARROW_STUB).
// Forms: NSImage is IImage, CGImage the decoded pixel buffer DecodedBitmap (WicImageDecoder.cs); a file URL is its
// path string (R-PATH-1, SwiftPath); `defaults` is UserDefaults.Standard, since WebsiteCaptures.defaults is always nil
// outside a capture launch (dead_code.md 28, NOT PORTED); an observed property with a didSet stores and runs the
// didSet, which is the stub (R-OBS-2).
// Not declared yet: the @ObservationIgnored private state (fileURL :74, renderTask :76, liveTask :77, liveRequested
// :79, baseImage :81, previewBase :83), which the implementing unit adds with the bodies, and `ciContext` (:26), a Core
// Image context with no port counterpart; the blur's machinery is the implementing unit's.
// NEEDS DECISION (group 5 report): `acceptedTypes: [UTType] = [.image]` (:28), read by WallpaperCard.swift:171 for the
// open panel, has no port type (UTType); not declared yet.

namespace Marrow.App.Theme;

/// <summary>:9-11 <c>private struct DecodedPicture: @unchecked Sendable</c>: a decoded picture crossing back to the
/// main actor.</summary>
/// <param name="Image">:10 <c>let image: NSImage</c>.</param>
internal sealed record DecodedPicture(IImage Image);

/// <summary>:15-270 <c>@MainActor @Observable final class WallpaperStore</c> (R-OBS-6): the window's picked picture.</summary>
public sealed class WallpaperStore : MainActorModel
{
    // :17 static let shared = WallpaperStore()
    public static WallpaperStore Shared =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:17");

    /// <summary>:19-22 <c>private enum Key</c>: the store's two defaults keys (PR-SETTINGS-103).</summary>
    private static class Key
    {
        public const string Softness = "wallpaperSoftness"; // :20
        public const string File = "wallpaperFile"; // :21
    }

    private IImage? image; // :32
    private bool isInstalling; // :34
    private string? lastError; // :36
    private double softness; // :41
    private bool isAdjustingSoftness; // :61

    // :32 private(set) var image: NSImage?
    public IImage? Image
    {
        get => Read(image);
        private set => Set(ref image, value);
    }

    // :34 private(set) var isInstalling = false
    public bool IsInstalling
    {
        get => Read(isInstalling);
        private set => Set(ref isInstalling, value);
    }

    // :36 private(set) var lastError: String?
    public string? LastError
    {
        get => Read(lastError);
        private set => Set(ref lastError, value);
    }

    // :41-55 var softness: Double { didSet }
    public double Softness
    {
        get => Read(softness);
        set => Set(ref softness, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:42"));
    }

    // :57 var hasWallpaper: Bool { image != nil || fileURL != nil }
    public bool HasWallpaper =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:57");

    // :61-71 var isAdjustingSoftness = false { didSet }
    public bool IsAdjustingSoftness
    {
        get => Read(isAdjustingSoftness);
        set => Set(ref isAdjustingSoftness, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:62"));
    }

    // :85-95 private init()
    private WallpaperStore() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:85");

    // :98-109 nonisolated static func accepts(_ url: URL) -> Bool
    public static bool Accepts(string url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:98");

    // :113-144 func install(from source: URL)
    public void Install(string from) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:113");

    // :147-156 func remove()
    public void Remove() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:147");

    // :160-178 private func render()
    private void Render() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:160");

    // :180-187 private func scheduleRerender()
    private void ScheduleRerender() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:180");

    // :192-210 private func renderLive()
    private void RenderLive() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:192");

    // :213-217 private func takeLiveRequest() -> Double?
    private double? TakeLiveRequest() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:213");

    // :219-227 private func finishInstall(file: URL, picture: DecodedPicture)
    private void FinishInstall(string file, DecodedPicture picture) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:219");

    // :229-232 private func failInstall()
    private void FailInstall() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:229");

    // :236-245 nonisolated static func thumbnail(_ url: URL, maxPixel: Int) -> CGImage?
    public static DecodedBitmap? Thumbnail(string url, long maxPixel) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:236");

    // :249-260 nonisolated static func blurred(_ base: CGImage, softness: Double, radiusScale: CGFloat = 1) -> CGImage?
    public static DecodedBitmap? Blurred(DecodedBitmap @base, double softness, double radiusScale = 1) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:249");

    // :264-269 nonisolated static func decode(_ url: URL, softness: Double) -> NSImage?
    public static IImage? Decode(string url, double softness) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Theme/Wallpaper.swift:264");
}
