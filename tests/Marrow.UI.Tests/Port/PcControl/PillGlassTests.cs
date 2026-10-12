// PORT-ONLY: the blur under the Tactus pill (OD-0145, ED-NEW-pcctl-24; DESIGN.md 10.6 "If the glass cannot attach, the
// opaque panel fallback applies (ED-238)"): a window that is not drawn through Windows.UI.Composition (the headless one)
// reports the glass unavailable with the reason, so the body draws the dense panel; hiding never throws. The real blur is
// NP-16's to measure.
using Avalonia.Controls;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class PillGlassTests(HeadlessSession headless)
{
    [Fact]
    public void Without_Windows_composition_the_glass_is_unavailable_and_says_why() => headless.OnUiThread(() =>
    {
        var window = new Window { Width = 600, Height = 200 };
        window.Show();
        try
        {
            using var glass = PillGlass.For(window);

            Assert.False(glass.Show(new PillRect(100, 8, 300, 48), PillLayout.Radius));
            Assert.False(glass.Available);
            Assert.False(string.IsNullOrWhiteSpace(glass.Failure));
            // the same shape again asks nothing more and still reports the fallback
            Assert.False(glass.Show(new PillRect(100, 8, 300, 48), PillLayout.Radius));
            glass.Hide();
            glass.Hide();
            Assert.False(glass.Available);
        }
        finally
        {
            window.Close();
        }
    });
}
