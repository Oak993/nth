// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1, ED-NEW-pcctl-5; DESIGN.md 2.3, 2.4, 3.1). Window
// capture: raw BGRA frames of one window from a chain of sources (Windows.Graphics.Capture in the app, then PrintWindow,
// then a screen copy of the window's rectangle only when it is in front), and the JPEG step that turns a frame into the
// model's image (downscaled to the model grid, quality 75). Encoder and the first capture source are bound by the app;
// tests use fakes, so no test ever captures a real window.
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

public interface IPcJpegEncoder
{
    /// <summary>The frame's <paramref name="crop"/> (frame pixels), resized with high quality to width x height, as a JPEG.</summary>
    byte[] Encode(PcFrame frame, PcRect crop, int width, int height, int quality);
}

/// <summary>A frame and the JPEG made from it for the model.</summary>
public sealed record PcShot(PcFrame Frame, byte[] Jpeg, PcGrid Grid, string Source);

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
        var left = Math.Clamp(crop.X, 0, frame.Width);
        var top = Math.Clamp(crop.Y, 0, frame.Height);
        var width = Math.Clamp(crop.Width, 0, frame.Width - left);
        var height = Math.Clamp(crop.Height, 0, frame.Height - top);
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(frame.Bgra, ((top + y) * frame.Stride) + (left * 4), bytes, y * width * 4, width * 4);
        return PcFrame.Packed(bytes, width, height);
    }
}

/// <summary>
/// The capture chain: each source in turn until one gives a frame that is not blank; then the JPEG on the model grid
/// (long edge per the Detail setting, at most 1 MP, 2 MP for High). nth's own windows are suppressed meanwhile
/// (ED-NEW-pcctl-2).
/// </summary>
public sealed class PcCapturePipeline(IReadOnlyList<IWindowCapture> sources, IPcJpegEncoder encoder)
{
    public async Task<PcShot?> Shoot(nint window, PcRect frame, int longEdge, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PcFrame? captured;
            try { captured = await source.Capture(window, frame, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) { captured = null; }
            if (captured is null || PcFrames.IsBlank(captured)) continue;
            var grid = CoordinateMapper.Grid(captured.Width, captured.Height, longEdge, longEdge > 1280 ? 2_000_000 : 1_000_000);
            var jpeg = encoder.Encode(captured, new PcRect(0, 0, captured.Width, captured.Height), grid.Width, grid.Height, PcFrames.JpegQuality);
            return new PcShot(captured, jpeg, grid, source.Name);
        }
        return null;
    }

    /// <summary>A region of an earlier frame, up to twice its size on the model grid, at most 1280 px on the long edge.</summary>
    public byte[] Zoom(PcFrame frame, PcRect crop, double scale, out int width, out int height)
    {
        var factor = Math.Min(2.0, 1.0 / Math.Max(scale, 1e-6)) * scale;
        width = Math.Max(1, (int)Math.Round(crop.Width * factor));
        height = Math.Max(1, (int)Math.Round(crop.Height * factor));
        var cap = Math.Min(1.0, 1280.0 / Math.Max(width, height));
        width = Math.Max(1, (int)Math.Round(width * cap));
        height = Math.Max(1, (int)Math.Round(height * cap));
        return encoder.Encode(frame, crop, width, height, PcFrames.JpegQuality);
    }
}

/// <summary>The capture seams the app binds at start-up: the first source (Windows.Graphics.Capture) and the encoder.</summary>
public static class PcCaptureSeams
{
    public static Ambient<Func<IWindowCapture?>> Primary { get; } = new(new Func<IWindowCapture?>(() => null));
    public static Ambient<Func<IPcJpegEncoder?>> Encoder { get; } = new(new Func<IPcJpegEncoder?>(() => null));
}
