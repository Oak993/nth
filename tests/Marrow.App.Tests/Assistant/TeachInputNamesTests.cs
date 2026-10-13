using Marrow.App.Assistant.Teach;
using SkiaSharp;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 1; ED-NEW-dextra-as19-2, -9): the pure parts of the Windows pieces. A shortcut is named with its modifiers in a
// fixed order and the key's own name; a frame is a JPEG no wider than 1280 pixels, made from the pixels a screen copy gives.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-9")]
public sealed class TeachInputNamesTests
{
    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x30, "0")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x09, "Tab")]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x2E, "Delete")]
    [InlineData(0x20, "Space")]
    [InlineData(0x70, "F1")]
    [InlineData(0x7B, "F12")]
    [InlineData(0x87, "F24")]
    [InlineData(0x25, "Left")]
    [InlineData(0x60, "Num0")]
    [InlineData(0xBB, "=")]
    [InlineData(0xDC, "\\")]
    [InlineData(0xFE, "KeyFE")]
    public void A_key_is_named_by_its_letter_or_its_own_name(int vk, string name) => Assert.Equal(name, WindowsTeachInput.KeyName(vk));

    [Fact]
    public void A_chord_lists_the_modifiers_in_a_fixed_order_and_then_the_key()
    {
        Assert.Equal("Ctrl+S", WindowsTeachInput.Chord(true, false, false, false, 0x53));
        Assert.Equal("Ctrl+Alt+Shift+Win+Delete", WindowsTeachInput.Chord(true, true, true, true, 0x2E));
        Assert.Equal("Shift+Tab", WindowsTeachInput.Chord(false, false, true, false, 0x09));
        Assert.Equal("Enter", WindowsTeachInput.Chord(false, false, false, false, 0x0D));
        Assert.Equal("Win+R", WindowsTeachInput.Chord(false, false, false, true, 0x52));
    }

    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                pixels[at] = (byte)(x * 255 / width);
                pixels[at + 1] = (byte)(y * 255 / height);
                pixels[at + 2] = 128;
                pixels[at + 3] = 255;
            }
        }
        return pixels;
    }

    [Fact]
    public void A_wide_screen_is_kept_as_a_jpeg_scaled_down_to_the_frame_width()
    {
        var jpeg = ScreenFrames.Encode(Gradient(1920, 1080), 1920, 1080);
        Assert.NotNull(jpeg);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, jpeg![..3]);
        using var decoded = SKBitmap.Decode(jpeg);
        Assert.Equal((1280, 720), (decoded.Width, decoded.Height));
        Assert.True(jpeg.Length < 1_000_000);
    }

    [Fact]
    public void A_narrow_screen_keeps_its_size()
    {
        var jpeg = ScreenFrames.Encode(Gradient(800, 600), 800, 600);
        using var decoded = SKBitmap.Decode(jpeg!);
        Assert.Equal((800, 600), (decoded.Width, decoded.Height));
    }
}
