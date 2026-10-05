using Avalonia.Controls;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Sidebar;

// UE-5 review: the indeterminate bar of a row (a CLI's own updater reports no progress) sweeps only while its row is updating, takes no frames when the row is not, and stands still at a
// third of the track under reduce motion.
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
    public void Under_reduce_motion_an_unknown_amount_stands_still_at_a_third_and_takes_no_frames() => headless.OnUiThread(() =>
    {
        using var bound = FrameClock.Source.Override(new ManualFrameSource());
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var bar = Hosted(out var window);
        try
        {
            bar.Active = true;
            bar.Set(null, null);
            Assert.False(bar.IsSweeping);
            Assert.Equal(0.33, UpdateProgressBar.StillShare);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_known_amount_takes_no_frames_of_its_own() => headless.OnUiThread(() =>
    {
        using var bound = FrameClock.Source.Override(new ManualFrameSource());
        var bar = Hosted(out var window);
        try
        {
            bar.Active = true;
            bar.Set(0.4, null);
            Assert.False(bar.IsSweeping);
            Assert.Equal(0.4, bar.Shown);
        }
        finally { window.Close(); }
    });
}
