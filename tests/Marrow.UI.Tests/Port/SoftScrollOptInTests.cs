using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

// ED-NEW-softscroll-1 (OD-0071, amends ED-333): the soft scrolling of ED-246 is every scroll view's default; a scroll view that opted
// out (SoftScroll.IsEnabled false) keeps Avalonia's own wheel, which moves 50 points a notch in the report's own frame; a plain one,
// with nothing set on it, glides there along the measured curve (SoftScrollTests).
[Collection("Headless")]
public sealed class SoftScrollOptInTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-246")]
    public void A_scroll_view_that_opted_out_keeps_Avalonias_wheel_and_a_plain_one_glides() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig(viewer => SoftScroll.SetIsEnabled(viewer, false));
        rig.Wheel(new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, rig.Scroll.Offset.Y); // Avalonia's step, at once
        Assert.False(SoftScroll.IsScrolling(rig.Scroll));

        using var soft = new SoftScrollRig(); // nothing set on it
        Assert.True(SoftScroll.GetIsEnabled(soft.Scroll));
        Assert.False(soft.Scroll.IsSet(SoftScroll.IsEnabledProperty));
        soft.Wheel(new Vector(0, -1));
        Assert.Equal(0, soft.Scroll.Offset.Y); // nothing in the report's own frame
        var offsets = soft.Run(1.0);
        Assert.InRange(offsets[0], 0.01, 25); // the first frame moves part of the way
        Assert.Equal(50, offsets[^1]);
    });
}
