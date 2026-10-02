using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// ED-236 (OD-0005, OD-0014; closes WindowManager STOP #5). WindowChrome.swift:116-117 makes the window clear
// (`isOpaque = false`, `backgroundColor = .clear`) and the content draws glass over it. On Windows the window itself
// composites Acrylic where it shows the desktop, with the app-drawn scrim over it (probe P3, porting/probes/p3/RESULT.md:
// "keep TransparencyLevelHint = AcrylicBlur as the window edge material, and draw the tint in the app"). The hint list
// is Avalonia's own fallback chain: the first level the platform supports is taken, so Transparent behind Acrylic gives
// the plain clear window where Acrylic cannot be had (a list of Acrylic alone ends as an opaque black window, P3's
// `ctl_none_opaque`). The native evidence is GlassWindowNativeTests.
[Collection("Headless")]
public sealed class WindowChromeAcrylicTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "PR-WIN-024")] // DroppyCode/UI/Chrome/WindowChrome.swift:116-117 through WindowManager.swift:101
    public void Configure_asks_for_the_Acrylic_backdrop_and_keeps_the_clear_window_as_the_fallback() => headless.OnUiThread(() =>
    {
        var window = new Window();
        WindowChrome.Configure(window);
        Assert.Equal([WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent], window.TransparencyLevelHint);
        Assert.Same(Brushes.Transparent, window.Background); // the clear window is still the base the scrim lies on
    });

    [Fact]
    [Trait("Decision", "ED-236")]
    public void The_glass_backdrop_hint_is_Acrylic_first_then_the_clear_window()
    {
        Assert.Equal(WindowTransparencyLevel.AcrylicBlur, WindowChrome.GlassBackdrop[0]);
        Assert.Equal(WindowTransparencyLevel.Transparent, WindowChrome.GlassBackdrop[^1]);
        Assert.Equal(2, WindowChrome.GlassBackdrop.Count);
    }
}
