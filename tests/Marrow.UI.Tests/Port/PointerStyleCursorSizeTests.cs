using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media.Imaging;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-19 (lean pass L19): the grab cursors follow the Windows cursor size setting, and at the default size (32 px at 100%) every
// bitmap is pixel-equal to the one drawn before the change. The hashes are SHA-256 over the raw BGRA pixels, taken on the unchanged code.
[Collection("Headless")]
public sealed class PointerStyleCursorSizeTests(HeadlessSession headless)
{
    internal static string PixelHash(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var bytes = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { handle.Free(); }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static TheoryData<PointerStyle, double, string> DefaultSizeHashes => new()
    {
        { PointerStyle.GrabIdle, 1, "8C7D04923B50347DF28AECF7AC2501F559678CDCCBF1FD01233F7D55AE5C7E0E" },
        { PointerStyle.GrabIdle, 1.5, "B169D507BDB2A8B94B7F3DA297907B99E338EE4988AF2E706E7504B3FE6B2A40" },
        { PointerStyle.GrabIdle, 2, "793500FA3F023E199D2258BF08FCCCCF82A0AAC68AF0BD0BCF2908733993C16A" },
        { PointerStyle.GrabActive, 1, "ACE31F5EA0464071197CB7D75851AD4FD2B32311FE0F030885986BC4560AD02D" },
        { PointerStyle.GrabActive, 1.5, "AACD9B98E7587E9C83A4F161AB20EB7442B4E1E21A8EF6781104F1ABAC73A779" },
        { PointerStyle.GrabActive, 2, "6FE01C7B05389E81B5420C4D1D34BDED2DF18F3BB386C6340B7E0D45C42D037B" },
    };

    [Theory]
    [MemberData(nameof(DefaultSizeHashes))]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void At_the_default_cursor_size_the_bitmap_is_pixel_equal_to_the_one_drawn_before(PointerStyle style, double scale, string hash) => headless.OnUiThread(() =>
    {
        var actual = PixelHash(PointerStyleCursors.Render(style, scale));
        Assert.True(actual == hash, $"{style} at {scale}: {actual}");
    });

    [Theory]
    [InlineData(PointerStyle.GrabIdle, 1)]
    [InlineData(PointerStyle.GrabActive, 1.5)]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void At_the_default_cursor_size_the_sized_cursor_is_the_same_cursor(PointerStyle style, double scale) => headless.OnUiThread(() =>
    {
        Assert.Same(PointerStyleCursors.Render(style, scale), PointerStyleCursors.Render(style, scale, PointerStyleCursors.BaseSize));
        Assert.Same(PointerStyleCursors.Get(style, scale), PointerStyleCursors.Get(style, scale, PointerStyleCursors.BaseSize));
        Assert.Equal(PointerStyleCursors.Hotspot(scale), PointerStyleCursors.Hotspot(scale, PointerStyleCursors.BaseSize));
    });

    [Theory]
    [InlineData(48, 1, 48)]
    [InlineData(64, 1.5, 96)]
    [InlineData(256, 1, 256)]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void A_larger_cursor_setting_draws_a_larger_square_with_its_hotspot_at_the_centre(double size, double scale, int side) => headless.OnUiThread(() =>
    {
        Assert.Equal(side, PointerStyleCursors.PixelSize(scale, size));
        Assert.Equal(new PixelPoint(side / 2, side / 2), PointerStyleCursors.Hotspot(scale, size));
        Assert.Equal(new PixelSize(side, side), PointerStyleCursors.Render(PointerStyle.GrabActive, scale, size).PixelSize);
        Assert.NotSame(PointerStyleCursors.Get(PointerStyle.GrabActive, scale), PointerStyleCursors.Get(PointerStyle.GrabActive, scale, size));
    });

    [Theory]
    [InlineData(PointerStyle.GrabIdle)]
    [InlineData(PointerStyle.GrabActive)]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void A_cursor_twice_the_size_is_the_glyph_drawn_at_twice_the_scale(PointerStyle style) => headless.OnUiThread(() =>
        Assert.Equal(PixelHash(PointerStyleCursors.Render(style, 2)), PixelHash(PointerStyleCursors.Render(style, 1, 2 * PointerStyleCursors.BaseSize))));

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void The_modifier_draws_the_grab_cursors_at_the_system_cursor_size() => headless.OnUiThread(() =>
    {
        var control = new Avalonia.Controls.Border();
        using (SystemCursorSize.Setting.Override(new SystemCursorSize(PointerStyleCursors.BaseSize)))
        {
            control.PointerStyle(PointerStyle.GrabIdle);
            Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabIdle, 1), control.Cursor);
        }
        using (SystemCursorSize.Setting.Override(new SystemCursorSize(64)))
        {
            control.PointerStyle(PointerStyle.GrabActive);
            Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, 1, 64), control.Cursor);
            control.PointerStyle(PointerStyle.Link);
            Assert.Same(PointerStyleCursors.Get(PointerStyle.Link, 1), control.Cursor);
        }
    });
}
