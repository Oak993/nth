using Avalonia.Controls;
using Marrow.App.Views.Chat;
using Marrow.Testing;
using Marrow.UI.Port;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ReserveRetargetSpeedTests
{
    [Theory]
    [InlineData(60, false)]
    [InlineData(144, false)]
    [InlineData(60, true)]
    [InlineData(144, true)]
    [Trait("Decision", "ED-NEW-motion-drawer-2")]
    public void Panel_reserve_reversals_keep_timeline_and_composer_speed(int hertz, bool composer)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var content = new Border();
        var slide = new ReserveSlide(new PanelReserve(), Slides: true, AnimatesWidth: composer);
        slide.Body(content);
        slide = slide with { Reserve = new PanelReserve { Leading = 100, Trailing = 40 } };
        slide.Body(content);
        runtime.AdvanceMilliseconds(50); frames.Advance(TimeSpan.Zero);
        var curve = Chrome.PanelSlide;
        var speed = (composer ? 100 : 30) * curve.Speed(.05);
        var before = composer ? content.Margin.Left : slide.Offset;
        var beforeRight = content.Margin.Right;
        slide = slide with { Reserve = new PanelReserve() };
        slide.Body(content);
        var from = composer ? before : before + 30; // final-width layout changed; its transform compensates immediately
        Assert.Equal(from, composer ? content.Margin.Left : slide.Offset, 9);
        var dt = TimeSpan.FromSeconds(1d / hertz);
        runtime.Advance(dt); frames.Advance(TimeSpan.Zero);
        var seconds = dt.TotalSeconds;
        var expected = from * (1 - curve.Progress(seconds)) + speed * curve.Impulse(seconds);
        Assert.Equal(expected, composer ? content.Margin.Left : slide.Offset, 6);
        if (composer) Assert.Equal(beforeRight * (1 - curve.Progress(seconds)) + 40 * curve.Speed(.05) * curve.Impulse(seconds), content.Margin.Right, 6);
        var values = new List<double> { from, expected };
        for (var i = 0; i < 100 && values[^1] != 0; i++)
        {
            runtime.Advance(dt); frames.Advance(TimeSpan.Zero);
            values.Add(composer ? content.Margin.Left : slide.Offset);
        }
        Assert.Equal(0, values[^1]);
        Assert.True(Math.Abs(values[^1] - values[^2]) <= Math.Abs(values[^2] - values[^3]) + 1e-6);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-motion-drawer-2")]
    public void A_reserve_retarget_under_Reduce_Motion_lands_immediately()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var content = new Border();
        var slide = new ReserveSlide(new PanelReserve(), Slides: true, AnimatesWidth: true);
        slide.Body(content);
        slide = slide with { Reserve = new PanelReserve { Leading = 100 } }; slide.Body(content);
        runtime.AdvanceMilliseconds(50); frames.Advance(TimeSpan.Zero);
        EnvironmentValues.SetAccessibilityReduceMotion(content, true);
        slide = slide with { Reserve = new PanelReserve() }; slide.Body(content);
        Assert.Equal(0, content.Margin.Left);
        runtime.AdvanceMilliseconds(50); frames.Advance(TimeSpan.Zero);
        Assert.Equal(0, content.Margin.Left);
    }
}
