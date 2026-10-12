// PORT-ONLY: Tactus (ED-NEW-pcctl-5, ED-NEW-pcctl-9). The Skia JPEG step on synthetic frames: sizes, the crop and stride,
// the box-filtered downscale, the element labels and their bounds. No window is captured.
using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using SkiaSharp;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-9")]
public sealed class JpegEncoderTests
{
    private static readonly JpegEncoder Encoder = new();

    [Fact]
    public void A_jpeg_of_the_asked_size_comes_out()
    {
        var frame = Solid(400, 300, 30, 120, 200);

        var jpeg = Encoder.Encode(frame, new PcRect(0, 0, 400, 300), 200, 150, 75, []);

        Assert.Equal([0xFF, 0xD8, 0xFF], jpeg.Take(3).ToArray());
        using var image = SKBitmap.Decode(jpeg);
        Assert.Equal(200, image.Width);
        Assert.Equal(150, image.Height);
        AssertNear(image.GetPixel(100, 75), 30, 120, 200, 6);
    }

    [Fact]
    public void Only_the_crop_is_encoded_and_a_padded_stride_is_honoured()
    {
        // 100 x 80 frame, rows padded to 512 bytes, grey everywhere but a red 40 x 30 block at (50, 40).
        const int stride = 512;
        var bytes = new byte[stride * 80];
        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var red = x >= 50 && x < 90 && y >= 40 && y < 70;
                Set(bytes, (y * stride) + (x * 4), red ? (byte)220 : (byte)128, red ? (byte)20 : (byte)128, red ? (byte)20 : (byte)128);
            }
        }
        var frame = new PcFrame(bytes, 100, 80, stride);

        var jpeg = Encoder.Encode(frame, new PcRect(50, 40, 40, 30), 40, 30, 90, []);

        using var image = SKBitmap.Decode(jpeg);
        Assert.Equal((40, 30), (image.Width, image.Height));
        AssertNear(image.GetPixel(20, 15), 220, 20, 20, 8);
        AssertNear(image.GetPixel(2, 2), 220, 20, 20, 12);
    }

    [Fact]
    public void A_crop_reaching_past_the_frame_is_clipped_to_it()
    {
        var frame = Solid(64, 64, 10, 200, 10);

        var jpeg = Encoder.Encode(frame, new PcRect(32, 32, 100, 100), 32, 32, 90, []);

        using var image = SKBitmap.Decode(jpeg);
        AssertNear(image.GetPixel(16, 16), 10, 200, 10, 8);
    }

    [Fact]
    public void A_smaller_grid_averages_fine_detail_instead_of_dropping_it()
    {
        // Columns repeat every 8 px: 2 white, 6 black. An 8x reduction must land near the area mean (64), not on the
        // black that a point-sampled or plain bilinear read at each output pixel's centre would give.
        const int size = 512;
        var bytes = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var v = x % 8 < 2 ? (byte)255 : (byte)0;
                Set(bytes, ((y * size) + x) * 4, v, v, v);
            }
        }
        var frame = PcFrame.Packed(bytes, size, size);

        var jpeg = Encoder.Encode(frame, new PcRect(0, 0, size, size), size / 8, size / 8, 95, []);

        using var image = SKBitmap.Decode(jpeg);
        var sum = 0;
        for (var x = 8; x < 56; x++) sum += image.GetPixel(x, 32).Green;
        var mean = sum / 48.0;
        Assert.InRange(mean, 44, 84);
    }

    [Fact]
    public void A_native_size_crop_keeps_its_pixels()
    {
        // A checkerboard of 8 px squares at native size: every square keeps its own value.
        var bytes = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var v = ((x / 8) + (y / 8)) % 2 == 0 ? (byte)240 : (byte)15;
                Set(bytes, ((y * 64) + x) * 4, v, v, v);
            }
        }

        var jpeg = Encoder.Encode(PcFrame.Packed(bytes, 64, 64), new PcRect(0, 0, 64, 64), 64, 64, 95, []);

        using var image = SKBitmap.Decode(jpeg);
        Assert.InRange((int)image.GetPixel(3, 3).Green, 220, 255);
        Assert.InRange((int)image.GetPixel(11, 3).Green, 0, 35);
    }

    [Fact]
    public void A_mark_draws_its_label_at_the_element_and_nowhere_else()
    {
        var frame = Solid(400, 300, 245, 245, 245);
        PcMark[] marks = [new PcMark(17, new PcRect(200, 100, 80, 40))];

        var plain = Encoder.Encode(frame, new PcRect(0, 0, 400, 300), 200, 150, 90, []);
        var marked = Encoder.Encode(frame, new PcRect(0, 0, 400, 300), 200, 150, 90, marks);

        using var image = SKBitmap.Decode(marked);
        // The label sits at the element's top-left corner, (100, 50) at half scale, filled with the label colour.
        var label = image.GetPixel(102, 52);
        Assert.True(label.Red > 150 && label.Green < 90, "label pixel " + label);
        // Far from the element nothing changed.
        AssertNear(image.GetPixel(20, 130), 245, 245, 245, 6);
        Assert.NotEqual(plain, marked);
    }

    [Fact]
    public void A_mark_outside_the_crop_draws_nothing()
    {
        var frame = Solid(300, 300, 200, 200, 200);

        var plain = Encoder.Encode(frame, new PcRect(0, 0, 100, 100), 100, 100, 90, []);
        var marked = Encoder.Encode(frame, new PcRect(0, 0, 100, 100), 100, 100, 90, [new PcMark(3, new PcRect(150, 150, 20, 20))]);

        Assert.Equal(plain, marked);
    }

    [Fact]
    public void A_label_at_the_edge_stays_inside_the_image()
    {
        var frame = Solid(100, 100, 250, 250, 250);

        var jpeg = Encoder.Encode(frame, new PcRect(0, 0, 100, 100), 100, 100, 90, [new PcMark(123, new PcRect(96, 96, 4, 4))]);

        using var image = SKBitmap.Decode(jpeg);
        var corner = image.GetPixel(97, 97);
        Assert.True(corner.Red > 150 && corner.Green < 120, "corner pixel " + corner);
    }

    [Fact]
    public void Lower_quality_gives_a_smaller_file()
    {
        var random = new Random(7);
        var bytes = new byte[256 * 256 * 4];
        random.NextBytes(bytes);
        var frame = PcFrame.Packed(bytes, 256, 256);

        var low = Encoder.Encode(frame, new PcRect(0, 0, 256, 256), 256, 256, 30, []);
        var high = Encoder.Encode(frame, new PcRect(0, 0, 256, 256), 256, 256, 95, []);

        Assert.True(low.Length < high.Length, $"{low.Length} >= {high.Length}");
    }

    [Fact]
    public void An_empty_crop_or_size_is_refused()
    {
        var frame = Solid(50, 50, 0, 0, 0);

        Assert.Throws<ArgumentException>(() => Encoder.Encode(frame, new PcRect(60, 60, 10, 10), 10, 10, 75, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Encoder.Encode(frame, new PcRect(0, 0, 50, 50), 0, 10, 75, []));
    }

    [Fact]
    public void The_shared_encoder_is_one_instance()
    {
        Assert.Same(JpegEncoder.Shared, JpegEncoder.Shared);
        Assert.IsAssignableFrom<IPcJpegEncoder>(JpegEncoder.Shared);
    }

    private static PcFrame Solid(int width, int height, byte red, byte green, byte blue)
    {
        var bytes = new byte[width * height * 4];
        for (var at = 0; at < bytes.Length; at += 4) Set(bytes, at, red, green, blue);
        return PcFrame.Packed(bytes, width, height);
    }

    private static void Set(byte[] bytes, int at, byte red, byte green, byte blue)
    {
        bytes[at] = blue;
        bytes[at + 1] = green;
        bytes[at + 2] = red;
        bytes[at + 3] = 255;
    }

    private static void AssertNear(SKColor color, int red, int green, int blue, int tolerance)
    {
        Assert.InRange((int)color.Red, red - tolerance, red + tolerance);
        Assert.InRange((int)color.Green, green - tolerance, green + tolerance);
        Assert.InRange((int)color.Blue, blue - tolerance, blue + tolerance);
    }
}
