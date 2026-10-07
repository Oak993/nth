using Avalonia;
using Avalonia.Media.Imaging;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class StrikeProductionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Showing_a_retained_track_again_requests_a_clean_start_without_changing_its_lanes() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var monitor = Marrow.App.Views.Chat.FrontMonitor.Shared.Current;
        var visible = typeof(Marrow.App.Views.Chat.FrontMonitor).GetProperty("IsVisible")!; var prior = monitor.IsVisible;
        var effect = new TrackEffect(new TrackLook(TrackLook.Kind.Fast,EffortBrand.Claude));
        var window = new Avalonia.Controls.Window { Content = effect, Width=298,Height=34 };
        try
        {
            window.Show(); effect.Measure(new Size(298,34)); effect.Arrange(new Rect(0,0,298,34));
            using var bitmap = new RenderTargetBitmap(new PixelSize(298,34)); using(var context=bitmap.CreateDrawingContext()) effect.Child!.Render(context);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var before)); Assert.False(effect.StrikeStartPending);
            visible.SetValue(monitor,false); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            visible.SetValue(monitor,true); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(effect.StrikeStartPending);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var after)); Assert.Same(before.MixLanes, after.MixLanes);
        }
        finally { window.Close(); visible.SetValue(monitor,prior); }
    });

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Production_frame_uses_full_tube_geometry_random_clean_start_mix_lanes_and_shared_knob_time() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); var effect = new TrackEffect(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude)) { TrackWidth = 298, KnobInset = 24, KnobRadius = 24 };
        effect.Measure(new Size(190, 34)); effect.Arrange(new Rect(0, 0, 190, 34));
        typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(effect, 0d);
        using var bitmap = new RenderTargetBitmap(new PixelSize(190, 34)); using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var frame));
        var mixProperty = typeof(LiquidTrack.Frame).GetProperty("Mix"); Assert.NotNull(mixProperty); Assert.NotNull(mixProperty.GetValue(frame));
        var geometry = effect.StrikeGeometryFor(new Size(190, 34)); Assert.Equal(298, geometry.Width); Assert.Equal(164, geometry.Knob);
        var offset = typeof(TrackEffect).GetProperty("StrikeOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); Assert.NotNull(offset);
        var t = (double)offset.GetValue(effect)!; Assert.InRange(t, 48000, 1008055); Assert.True(new StrikeStarts(effect.StrikeSeed).IsClean(t));
        var lanes = typeof(LiquidTrack.Frame).GetProperty("MixLanes"); Assert.NotNull(lanes); Assert.IsType<StrikeBubbleLanes>(lanes.GetValue(frame));
        var shared = typeof(TrackEffect).GetProperty("StrikeFrameTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); Assert.NotNull(shared); Assert.Equal(t, (double)shared.GetValue(effect)!);
    });

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Production_fast_off_keeps_the_resolved_scene_during_the_ramp_and_model_changes_request_clean_reentry() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); var effect = new TrackEffect(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude)) { TrackWidth = 298 };
        effect.Measure(new Size(298, 34)); effect.Arrange(new Rect(0, 0, 298, 34));
        using var bitmap = new RenderTargetBitmap(new PixelSize(298, 34)); using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out _)); effect.SetLook(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Claude));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var frame)); var mix = typeof(LiquidTrack.Frame).GetProperty("Mix"); Assert.NotNull(mix); Assert.NotNull(mix.GetValue(frame));
        effect.SetLook(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Silver));
        var pending = typeof(TrackEffect).GetProperty("StrikeStartPending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); Assert.NotNull(pending); Assert.True((bool)pending.GetValue(effect)!);
    });
}
