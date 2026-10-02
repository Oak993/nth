using System.Reflection;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Port;

// ED-219 (PORT-ONLY): the Win32 half of the taskbar badge. Only the part that touches no window and no taskbar is run: the
// system icon built from the badge's pixels. The ITaskbarList3 call and the DPI lookup (which needs the window manager)
// are never made here (R-TEST-10); they are reported as unverified.
public sealed class WindowsTaskbarOverlayIconTests
{
    private static readonly MethodInfo CreateIcon =
        typeof(WindowsTaskbarOverlay).GetMethod("CreateIcon", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(WindowsTaskbarOverlay), "CreateIcon"); // ED-119: nothing public observes it

    [Theory]
    [Trait("Decision", "ED-219")]
    [InlineData(16)]
    [InlineData(32)]
    public void The_system_icon_made_from_the_badge_has_its_size_and_its_pixels(int size)
    {
        var badge = TaskbarBadge.Render(26, size);

        var icon = (IntPtr)CreateIcon.Invoke(null, [badge])!;
        try
        {
            Assert.NotEqual(IntPtr.Zero, icon);
            var (width, height, bgra) = NativeIconProbe.ReadColor(icon);
            Assert.Equal((size, size), (width, height));
            // Row 0 is the top row, and the disc's top is the badge red (blue 48, green 59, red 255, alpha 255).
            var top = (1 * size + size / 2) * 4;
            Assert.Equal(new byte[] { 48, 59, 255, 255 }, bgra[top..(top + 4)]);
            // The corner is outside the disc and fully transparent.
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, bgra[0..4]);
            // Every pixel, in the same rows, is the badge's pixel premultiplied by its alpha (the form icon bitmaps take).
            for (var offset = 0; offset < bgra.Length; offset += 4)
            {
                var alpha = badge.Bgra[offset + 3];
                Assert.Equal(
                    new[] { badge.Bgra[offset] * alpha / 255, badge.Bgra[offset + 1] * alpha / 255, badge.Bgra[offset + 2] * alpha / 255, alpha },
                    new[] { (int)bgra[offset], bgra[offset + 1], bgra[offset + 2], bgra[offset + 3] });
            }
        }
        finally { NativeIconProbe.Destroy(icon); }
    }
}
