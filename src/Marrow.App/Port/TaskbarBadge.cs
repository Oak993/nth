// PORT-ONLY (ED-219): the Windows twin of the Dock badge in AppModel.swift:1270-1273,
//   NSApp.dockTile.badgeLabel = unread > 0 ? String(unread) : nil.
// The badge becomes the main window's taskbar overlay icon (ITaskbarList3::SetOverlayIcon): the count in white on a filled
// red circle, NSColor.systemRed's light value (the Dock badge's own colours, AppTheme.System's Danger, 255,59,48), drawn here
// in code at the system small-icon size for the window's DPI. Above 99 the text stays the decimal count, shrunk to fit (the
// source's String(unread); no "99+" is invented). Zero clears the overlay. The accessible description is the count as text.
// The COM call is behind ITaskbarOverlay: Windows binds WindowsTaskbarOverlay (WindowsTaskbarOverlay.cs) at start-up; a test
// binds a recorder through TaskbarBadge.Overlay, and the process default draws nothing and calls nothing, so a test that
// forgets to bind one can never reach the real taskbar. The digits are set in Inter SemiBold (owner, 2026-10-02: "Use Inter
// for the badge digits"; Inter is the app's face, OD-0009; the weight is an ENGINEERING DECISION for legibility at 16 px), drawn
// with SkiaSharp straight from the font file Avalonia.Fonts.Inter ships, so no Avalonia platform or window is needed: the model
// calls this from the main actor in a headless run too.
using System.Globalization;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Theme;
using SkiaSharp;

namespace Marrow.App.Port;

/// <summary>The badge as the taskbar is asked to show it: the count, its text, and the pixels (straight alpha, BGRA, top row first).</summary>
public sealed record TaskbarOverlayIcon(long Count, string Description, int Size, byte[] Bgra);

/// <summary>The taskbar button's overlay icon surface. A null icon clears the overlay.</summary>
public interface ITaskbarOverlay
{
    /// <summary>The system small-icon size (SM_CXSMICON) at the main window's current DPI, in pixels.</summary>
    int SmallIconSize { get; }

    void SetOverlay(TaskbarOverlayIcon? icon);
}

/// <summary>The process default: no taskbar. Only the Windows start-up binds a real overlay.</summary>
internal sealed class InertTaskbarOverlay : ITaskbarOverlay
{
    public static readonly InertTaskbarOverlay Instance = new();
    private InertTaskbarOverlay() { }
    public int SmallIconSize => 16;
    public void SetOverlay(TaskbarOverlayIcon? icon) { }
}

/// <summary>PORT-ONLY. NSApp.dockTile.badgeLabel as a taskbar overlay icon.</summary>
public static class TaskbarBadge
{
    /// <summary>R-LIFE-2: the taskbar is a process-wide seam; a test replaces it for its own flow.</summary>
    public static Ambient<ITaskbarOverlay> Overlay { get; } = new(InertTaskbarOverlay.Instance);

    /// <summary>AppModel.swift:1270-1273: <c>unread &gt; 0 ? String(unread) : nil</c>.</summary>
    public static void Show(long unread)
    {
        var overlay = Overlay.Current;
        overlay.SetOverlay(unread > 0 ? Render(unread, overlay.SmallIconSize) : null);
    }

    /// <summary>Swift's <c>String(unread)</c>: the decimal count, never capped.</summary>
    public static string Text(long unread) => unread.ToString(CultureInfo.InvariantCulture);

    // NSColor.systemRed at its light value, from the colour the port already keeps for it (AppTheme.System's Danger).
    private static readonly Avalonia.Media.Color Red = AppTheme.System.Spec().Danger.Resolve(ThemeVariant.Light);

    private static readonly Lazy<SKTypeface> face = new(() =>
    {
        using var stream = new Avalonia.Platform.StandardAssetLoader().Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/Inter-SemiBold.ttf"));
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("Inter SemiBold could not be loaded.");
    });

    /// <summary>The face the count is set in: Inter SemiBold.</summary>
    public static SKTypeface Typeface => face.Value;

    /// <summary>The badge for a count: a red disc filling the square, the count in white Inter SemiBold, centred and shrunk to fit.</summary>
    public static TaskbarOverlayIcon Render(long count, int size)
    {
        var text = Text(count);
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var disc = new SKPaint { IsAntialias = true, Color = new SKColor(Red.R, Red.G, Red.B) };
            canvas.DrawCircle(size / 2f, size / 2f, size / 2f, disc);

            // The text may fill 62% of the height, and its ink's diagonal 86% of the diameter, so a long count gets smaller
            // instead of leaving the disc.
            using var font = new SKFont(Typeface, 1) { Edging = SKFontEdging.Antialias, Subpixel = true };
            font.MeasureText(text, out var unit);
            var scale = Math.Min(0.62f * size / unit.Height,
                0.86f * size / MathF.Sqrt(unit.Width * unit.Width + unit.Height * unit.Height));
            font.Size = scale;
            font.MeasureText(text, out var ink);
            using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
            canvas.DrawText(text, size / 2f - ink.MidX, size / 2f - ink.MidY, SKTextAlign.Left, font, white);
        }
        // Straight alpha, BGRA, top row first.
        var pixels = new byte[size * size * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var source = bitmap.PeekPixels();
            source.ReadPixels(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul), handle.AddrOfPinnedObject(), size * 4);
        }
        finally { handle.Free(); }
        return new TaskbarOverlayIcon(count, text, size, pixels);
    }
}
