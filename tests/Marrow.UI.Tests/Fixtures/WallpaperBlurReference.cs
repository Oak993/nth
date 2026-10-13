using Marrow.Core.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// The wallpaper's Gaussian exactly as the first port wrote it (double arithmetic, a clamp per sample, one pixel at a time), frozen as the oracle that the
/// fast blur must match to within one level (ED-NEW-perf-memory-2). It takes the radius directly. Slow on purpose; only tests call it.
/// </summary>
public static class WallpaperBlurReference
{
    public static DecodedBitmap Blurred(DecodedBitmap @base, double radius)
    {
        var sigma = radius;
        var extent = checked((int)Math.Ceiling(sigma * 3));
        var kernel = new double[checked(extent * 2 + 1)];
        var total = 0.0;
        for (var offset = -extent; offset <= extent; offset++)
        {
            var weight = Math.Exp(-(offset * (double)offset) / (2 * sigma * sigma));
            kernel[offset + extent] = weight;
            total += weight;
        }
        for (var index = 0; index < kernel.Length; index++) kernel[index] /= total;

        var width = @base.Width;
        var height = @base.Height;
        var pixels = @base.Pixels;
        var horizontal = new byte[pixels.Length];
        var output = new byte[pixels.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = checked((y * width + x) * 4);
                for (var channel = 0; channel < 4; channel++)
                {
                    var value = 0.0;
                    for (var offset = -extent; offset <= extent; offset++)
                    {
                        var sampleX = Math.Clamp(x + offset, 0, width - 1);
                        value += pixels[checked((y * width + sampleX) * 4 + channel)] * kernel[offset + extent];
                    }
                    horizontal[pixelOffset + channel] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
                }
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = checked((y * width + x) * 4);
                for (var channel = 0; channel < 4; channel++)
                {
                    var value = 0.0;
                    for (var offset = -extent; offset <= extent; offset++)
                    {
                        var sampleY = Math.Clamp(y + offset, 0, height - 1);
                        value += horizontal[checked((sampleY * width + x) * 4 + channel)] * kernel[offset + extent];
                    }
                    output[pixelOffset + channel] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
                }
            }
        }

        return new DecodedBitmap(width, height, checked(width * 4), output);
    }
}
