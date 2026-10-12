// PORT-ONLY: the style-flip hit test, the last fallback (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-22 item 5; DESIGN.md 10.6
// "Hit testing" 3): click-through except while a pointer record (mouse, pen or touch) is inside the shape at the record's
// own time, flipped only when the answer changes, never while a button is held in another app, click-through when hidden;
// the flip thread takes the newest record. Every call goes to a fake.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class StyleFlipHitTestTests
{
    private const nint Window = 0x77;

    private static (StyleFlipHitTest Hit, PcFakeBandNative Native) Make()
    {
        var native = new PcFakeBandNative();
        var hit = new StyleFlipHitTest(native, runThread: false);
        hit.Attach(Window, PcTestMonitors.Main.Band);
        return (hit, native);
    }

    [Fact]
    public void A_record_inside_the_resting_shape_takes_input_and_one_outside_gives_it_back()
    {
        var (hit, native) = Make();
        hit.Rest(PillShape.Resting(new PillRect(740, 8, 440, 48)));

        hit.Pointer(960, 30, 1);
        hit.Process();
        Assert.True(hit.TakesInput);
        hit.Pointer(961, 31, 1.01);
        hit.Process();
        hit.Pointer(960, 300, 1.02);
        hit.Process();

        Assert.False(hit.TakesInput);
        Assert.Equal(new[] { "through 77 True", "through 77 False", "through 77 True" }, native.Calls);
    }

    [Fact]
    public void The_moving_shape_is_tested_at_the_record_s_time()
    {
        var (hit, _) = Make();
        var travel = PillTravel.Start(0, new PillVector(16, 8), new PillVector(1464, 8), PillVector.Zero, false);
        var shape = PillShape.Moving(travel, null, 440, null, 48);
        hit.Moving(shape);
        var early = shape.At(0.03);

        hit.Pointer((int)(early.X + 200), 30, 0.03);
        hit.Process();
        Assert.True(hit.TakesInput);

        hit.Pointer((int)(early.X + 200), 30, 0.4);
        hit.Process();
        Assert.False(hit.TakesInput);
    }

    [Fact]
    public void Pen_and_touch_records_flip_like_the_mouse_because_windows_then_hit_tests_the_band_itself()
    {
        // The flip only opens the band to input; mouse, pen and touch then reach it through Windows' own hit test.
        var (hit, native) = Make();
        hit.Rest(PillShape.Resting(new PillRect(740, 8, 440, 48)));

        foreach (var (x, y) in new[] { (800, 20), (1000, 40), (1100, 25) })
        {
            hit.Pointer(x, y, 1);
            hit.Process();
        }

        Assert.True(hit.TakesInput);
        Assert.Equal(2, native.Calls.Count);
    }

    [Fact]
    public void No_flip_while_a_button_is_held_in_another_app_and_hidden_is_click_through()
    {
        var (hit, native) = Make();
        hit.Rest(PillShape.Resting(new PillRect(740, 8, 440, 48)));
        hit.ButtonHeldElsewhere(true);

        hit.Pointer(960, 30, 1);
        hit.Process();
        Assert.False(hit.TakesInput);

        hit.ButtonHeldElsewhere(false);
        hit.Pointer(960, 30, 1.1);
        hit.Process();
        Assert.True(hit.TakesInput);

        hit.Hidden();
        Assert.False(hit.TakesInput);
        Assert.True(native.ClickThrough[Window]);
    }

    [Fact]
    public void Only_the_newest_record_is_tested()
    {
        var (hit, native) = Make();
        hit.Rest(PillShape.Resting(new PillRect(740, 8, 440, 48)));

        hit.Pointer(960, 30, 1);
        hit.Pointer(10, 900, 1.001);
        hit.Process();

        Assert.False(hit.TakesInput);
        Assert.Single(native.Calls);
    }

    [Fact]
    public void The_flip_thread_answers_a_record_on_its_own()
    {
        var native = new PcFakeBandNative();
        using var hit = new StyleFlipHitTest(native);
        hit.Attach(Window, PcTestMonitors.Main.Band);
        hit.Rest(PillShape.Resting(new PillRect(740, 8, 440, 48)));

        hit.Pointer(960, 30, 1);

        Assert.True(SpinWait.SpinUntil(() => hit.TakesInput == true, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void The_band_is_one_layered_click_through_window_with_no_shadow_window_and_no_hook_path()
    {
        var hit = new StyleFlipHitTest(new PcFakeBandNative(), runThread: false);

        Assert.Equal(OverlayHitTestKind.StyleFlip, hit.Kind);
        Assert.Equal(OverlayStyles.Layered | OverlayStyles.Transparent, hit.InputStyles & (OverlayStyles.Layered | OverlayStyles.Transparent));
        Assert.False(hit.SeparateShadow);
        Assert.False(hit.GrabsInFlight);
    }
}
