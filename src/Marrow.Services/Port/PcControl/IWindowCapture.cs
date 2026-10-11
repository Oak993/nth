// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1, ED-NEW-pcctl-5, ED-NEW-pcctl-7; DESIGN.md 2.3, 2.4,
// 2.12, 3.1). Window capture: raw BGRA frames of one window from a chain of sources (Windows.Graphics.Capture in the app,
// then PrintWindow, then a screen copy of the window's rectangle only when it is in front), and the JPEG step that turns a
// frame into the model's image (downscaled to the caller's model grid, quality 75, element numbers drawn when asked;
// ED-NEW-pcctl-7 item 6), plus native-resolution crops and tiles. Encoder and the first capture source are bound by the
// app; tests use fakes, so no test ever captures a real window.
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>A captured frame: top-down BGRA32 rows, <see cref="Stride"/> bytes apart, in physical pixels.</summary>
public sealed record PcFrame(byte[] Bgra, int Width, int Height, int Stride)
{
    public static PcFrame Packed(byte[] bgra, int width, int height) => new(bgra, width, height, width * 4);
}

public interface IWindowCapture
{
    /// <summary>A short name for the audit log ("wgc", "printwindow", "screen").</summary>
    string Name { get; }

    /// <summary>One frame of the window, whose frame (DWM extended bounds) is <paramref name="frame"/>; null when this source cannot.</summary>
    Task<PcFrame?> Capture(nint window, PcRect frame, CancellationToken cancellationToken);
}

/// <summary>An element number drawn on the model's copy of a screenshot (set-of-mark), at its rectangle in frame pixels.</summary>
public sealed record PcMark(int Number, PcRect Rect);

public interface IPcJpegEncoder
{
    /// <summary>
    /// The frame's <paramref name="crop"/> (frame pixels), resized with high quality to width x height, as a JPEG, with each
    /// mark's number drawn in a small label at its rectangle (DESIGN.md 2.12; only on this copy).
    /// </summary>
    byte[] Encode(PcFrame frame, PcRect crop, int width, int height, int quality, IReadOnlyList<PcMark> marks);
}

/// <summary>A frame and the JPEG made from it for the model.</summary>
public sealed record PcShot(PcFrame Frame, byte[] Jpeg, PcGrid Grid, string Source);

/// <summary>A full-resolution piece of a frame: where it is (frame pixels), its JPEG and its size.</summary>
public sealed record PcCrop(PcRect Rect, byte[] Jpeg, int Width, int Height);

public static class PcFrames
{
    public const int JpegQuality = 75;

    /// <summary>True when every pixel is black or fully transparent: a GPU window PrintWindow could not draw.</summary>
    public static bool IsBlank(PcFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0) return true;
        // Sample a grid of up to 64 x 64 points; a real window always has some non-black pixel on it.
        var stepX = Math.Max(1, frame.Width / 64);
        var stepY = Math.Max(1, frame.Height / 64);
        for (var y = 0; y < frame.Height; y += stepY)
        {
            var row = y * frame.Stride;
            for (var x = 0; x < frame.Width; x += stepX)
            {
                var at = row + (x * 4);
                if (frame.Bgra[at] != 0 || frame.Bgra[at + 1] != 0 || frame.Bgra[at + 2] != 0) return false;
            }
        }
        return true;
    }

    /// <summary>A frame's sub-rectangle as a new packed frame (clipped to the frame).</summary>
    public static PcFrame Crop(PcFrame frame, PcRect crop)
    {
        var clip = Clip(frame, crop);
        var bytes = new byte[clip.Width * clip.Height * 4];
        for (var y = 0; y < clip.Height; y++)
            Buffer.BlockCopy(frame.Bgra, ((clip.Y + y) * frame.Stride) + (clip.X * 4), bytes, y * clip.Width * 4, clip.Width * 4);
        return PcFrame.Packed(bytes, clip.Width, clip.Height);
    }

    /// <summary>A rectangle clipped to the frame.</summary>
    public static PcRect Clip(PcFrame frame, PcRect crop)
    {
        var left = Math.Clamp(crop.X, 0, frame.Width);
        var top = Math.Clamp(crop.Y, 0, frame.Height);
        var right = Math.Clamp(crop.X + crop.Width, left, frame.Width);
        var bottom = Math.Clamp(crop.Y + crop.Height, top, frame.Height);
        return new PcRect(left, top, right - left, bottom - top);
    }
}

/// <summary>
/// The capture chain: each source in turn until one gives a frame that is not blank; then the JPEG on the model grid of the
/// caller's grid profile (DESIGN.md 2.3, critique C27: 1568 px and 1.15 MP by default), with element numbers drawn when
/// asked. Crops (zoom, region, the native crop after a point click, the tiles of a weak tree) go up to native pixels.
/// </summary>
public sealed class PcCapturePipeline(IReadOnlyList<IWindowCapture> sources, IPcJpegEncoder encoder)
{
    /// <summary>The native crop around a point click (DESIGN.md 2.3): 256 x 256 physical pixels.</summary>
    public const int ClickCrop = 256;

    /// <summary>The most tiles a weak tree's screenshot is split into; past that the region is shown downscaled.</summary>
    public const int MaxTiles = 4;

    public async Task<PcShot?> Shoot(nint window, PcRect frame, PcGridProfile profile, IReadOnlyList<PcMark> marks, CancellationToken cancellationToken)
    {
        if (await Grab(window, frame, cancellationToken).ConfigureAwait(false) is not { } grabbed) return null;
        var (captured, name) = grabbed;
        var grid = CoordinateMapper.Grid(captured.Width, captured.Height, profile.LongEdge, profile.MaxPixels);
        var jpeg = encoder.Encode(captured, new PcRect(0, 0, captured.Width, captured.Height), grid.Width, grid.Height, PcFrames.JpegQuality, marks);
        return new PcShot(captured, jpeg, grid, name);
    }

    /// <summary>A frame from the first source that gives one that is not blank, and that source's name; null when none can.</summary>
    public async Task<(PcFrame Frame, string Source)?> Grab(nint window, PcRect frame, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PcFrame? captured;
            try { captured = await source.Capture(window, frame, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) { captured = null; }
            if (captured is null || PcFrames.IsBlank(captured)) continue;
            return (captured, source.Name);
        }
        return null;
    }

    /// <summary>
    /// A region of a frame at up to native pixels (DESIGN.md 2.12: pc_zoom and region go up to 1:1), shrunk only as far as
    /// the grid profile needs; never upscaled.
    /// </summary>
    public PcCrop Native(PcFrame frame, PcRect crop, PcGridProfile profile)
    {
        var clip = PcFrames.Clip(frame, crop);
        var grid = CoordinateMapper.Grid(clip.Width, clip.Height, profile.LongEdge, profile.MaxPixels);
        return new PcCrop(clip, encoder.Encode(frame, clip, grid.Width, grid.Height, PcFrames.JpegQuality, []), grid.Width, grid.Height);
    }

    /// <summary>The 256 x 256 native crop centred on a frame point (clipped to the frame).</summary>
    public PcCrop AroundPoint(PcFrame frame, PcPoint point, PcGridProfile profile) =>
        Native(frame, new PcRect(point.X - (ClickCrop / 2), point.Y - (ClickCrop / 2), ClickCrop, ClickCrop), profile);

    /// <summary>
    /// A region split into native-resolution tiles that each fit the grid profile (DESIGN.md 2.3, weak trees); when it would
    /// take more than <see cref="MaxTiles"/>, one crop of the region instead (downscaled to the profile).
    /// </summary>
    public IReadOnlyList<PcCrop> Tiles(PcFrame frame, PcRect region, PcGridProfile profile)
    {
        var clip = PcFrames.Clip(frame, region);
        if (clip.Width <= 0 || clip.Height <= 0) return [];
        // The fewest tiles (in a columns x rows grid) whose every tile fits the profile at native size.
        var (columns, rows) = (0, 0);
        for (var count = 1; count <= MaxTiles && columns == 0; count++)
        {
            for (var c = 1; c <= count; c++)
            {
                if (count % c != 0) continue;
                var w = (long)Math.Ceiling((double)clip.Width / c);
                var h = (long)Math.Ceiling((double)clip.Height / (count / c));
                if (w > profile.LongEdge || h > profile.LongEdge || w * h > profile.MaxPixels) continue;
                (columns, rows) = (c, count / c);
                break;
            }
        }
        if (columns == 0) return [Native(frame, clip, profile)];
        var tileWidth = (int)Math.Ceiling((double)clip.Width / columns);
        var tileHeight = (int)Math.Ceiling((double)clip.Height / rows);
        var tiles = new List<PcCrop>(columns * rows);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
                tiles.Add(Native(frame, new PcRect(clip.X + (column * tileWidth), clip.Y + (row * tileHeight), tileWidth, tileHeight), profile));
        }
        return tiles;
    }
}

/// <summary>The capture seams the app binds at start-up: the first source (Windows.Graphics.Capture) and the encoder.</summary>
public static class PcCaptureSeams
{
    public static Ambient<Func<IWindowCapture?>> Primary { get; } = new(new Func<IWindowCapture?>(() => null));
    public static Ambient<Func<IPcJpegEncoder?>> Encoder { get; } = new(new Func<IPcJpegEncoder?>(() => null));
}
