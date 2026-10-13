using Avalonia;
using Marrow.Setup.Shell;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, "Custom caption"; unit U1 (ED-NEW-inst-u1-1). The window answers
// WM_NCHITTEST by hand: the 34 DIP band drags, the two 46 DIP buttons at the right are Minimise and Close, below the band
// is the window's own, and nothing is ever HTMAXBUTTON, so the snap-layout flyout never opens.
public sealed class CaptionHitTestTests
{
    private const int Nowhere = 0, Client = 1, Caption = 2, Minimise = 8, Maximise = 9, Close = 20;

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(10, 17, 600, Caption)]
    [InlineData(300, 0, 600, Caption)]
    [InlineData(507, 33.9, 600, Caption)]
    [InlineData(508, 17, 600, Minimise)]
    [InlineData(553.9, 17, 600, Minimise)]
    [InlineData(554, 17, 600, Close)]
    [InlineData(599.9, 0, 600, Close)]
    [InlineData(300, 34, 600, Client)]
    [InlineData(580, 200, 600, Client)]
    [InlineData(600, 17, 600, Nowhere)]
    [InlineData(-1, 17, 600, Nowhere)]
    [InlineData(10, -1, 600, Nowhere)]
    [InlineData(440, 17, 480, Close)]
    [InlineData(400, 17, 480, Minimise)]
    public void Each_point_answers_its_part(double x, double y, double width, int expected)
    {
        Assert.Equal(expected, CaptionHitTest.At(new Point(x, y), width));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void No_point_of_the_window_is_ever_the_maximise_button()
    {
        for (var x = -2.0; x <= 602; x += 0.5)
        {
            for (var y = -2.0; y <= 60; y += 0.5)
            {
                Assert.NotEqual(Maximise, CaptionHitTest.At(new Point(x, y), 600));
            }
        }
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Only_minimise_and_close_are_buttons_and_the_caption_is_the_canvas_size()
    {
        Assert.True(CaptionHitTest.IsButton(Minimise));
        Assert.True(CaptionHitTest.IsButton(Close));
        Assert.False(CaptionHitTest.IsButton(Caption));
        Assert.False(CaptionHitTest.IsButton(Maximise));
        Assert.Equal(34, CaptionBar.BandHeight);
        Assert.Equal(46, CaptionBar.ButtonWidth);
    }
}
