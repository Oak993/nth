using Avalonia.Controls;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Xunit;

namespace Marrow.App.Tests.Views.Sidebar;

// UE-5 review: the unknown-length bar of a row (a CLI's own updater reports no progress) runs only while its row is updating and takes no frames when the row is not; under reduce motion it is the nth
// pack's still middle segment (motion pack step 3 option B, calm).
[Collection("Headless")]
public sealed class UpdateProgressBarTests(HeadlessSession headless)
{
    private static UpdateProgressBar Hosted(out Avalonia.Controls.Window window)
    {
        var bar = new UpdateProgressBar { Width = 200 };
        window = new Avalonia.Controls.Window { Content = bar, Width = 300, Height = 60 };
        window.Show();
        window.UpdateLayout();
        return bar;
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void An_unknown_amount_sweeps_only_while_the_bar_is_active() => headless.OnUiThread(() =>
    {
        using var bound = FrameClock.Source.Override(new ManualFrameSource());
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        var bar = Hosted(out var window);
        try
        {
            bar.Set(null, null);
            Assert.False(bar.IsSweeping); // a row that is not updating takes no frames
            bar.Active = true;
            Assert.True(bar.IsSweeping);
            bar.Active = false;
            bar.Set(null, null);
            Assert.False(bar.IsSweeping || bar.Active);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Under_reduce_motion_an_unknown_amount_is_the_packs_still_middle_segment_whose_opacity_eases() => headless.OnUiThread(() =>
    {
        using var bound = FrameClock.Source.Override(new ManualFrameSource());
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var bar = Hosted(out var window);
        EnvironmentValues.SetAccessibilityReduceMotion(window, true); // as the app's SystemReduceMotion puts the system setting on each window
        try
        {
            bar.Active = true;
            bar.Set(null, null);
            Assert.True(bar.Bar.Calm);
            Assert.True(bar.Bar.IsIndeterminate);
            // step 3 "Calm": a still segment from 35 % to 65 % of the width, its opacity easing 0.55 to 1 every 2.4 s
            var width = 200.0;
            var still = NthMotion.BarIndeterminate(0, width, UpdateProgressBar.TrackHeight, calm: true);
            Assert.Equal((width * .35, width * .65), (still.X0, still.X1));
            var later = NthMotion.BarIndeterminate(1.2, width, UpdateProgressBar.TrackHeight, calm: true);
            Assert.Equal((still.X0, still.X1), (later.X0, later.X1)); // it does not travel
            Assert.NotEqual(still.Alpha, later.Alpha);
            Assert.InRange(still.Alpha, .55, 1);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_known_amount_is_the_3_px_nth_bar_springing_to_it_and_never_runs_backwards() => headless.OnUiThread(() =>
    {
        using var bound = FrameClock.Source.Override(new ManualFrameSource());
        var bar = Hosted(out var window);
        try
        {
            bar.Active = true;
            bar.Set(0.4, null);
            Assert.False(bar.IsSweeping);
            Assert.False(bar.Bar.IsIndeterminate);
            Assert.Equal(3, bar.Bar.Height);
            Assert.Equal(0.4, bar.Shown); // with no animation it is there at once
            bar.Set(0.9, Animation.EaseOut(.2));
            Assert.Equal(0.9, bar.Target);
            Assert.Equal(0.9, bar.Bar.Value);
            bar.Set(0.1, Animation.EaseOut(.2)); // a new run starts from nothing
            Assert.Equal(0.1, bar.Shown);
        }
        finally { window.Close(); }
    });
}
