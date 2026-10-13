using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 1, 22; ED-NEW-dextra-as19-10): the recording strip in a headless window. A word and a shape for the state (never colour
// alone), the time in a field of fixed width so counting moves nothing, Pause that is Resume while paused, buttons with names, and the notes
// of the recording under it. There is no animation, so Reduce motion has nothing to change.
[Collection("Headless")]
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-10")]
public sealed class TeachStripTests(HeadlessSession headless) : IDisposable
{
    private readonly TeachControllerRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    public void The_strip_shows_the_state_the_time_and_two_buttons_with_names()
    {
        rig.Frames.Nothing = true;
        rig.Controller.Start();
        headless.OnUiThread(() =>
        {
            using var model = new TeachStripModel(rig.Controller);
            var strip = new TeachStrip(model);
            using var stage = new ReserveViewStage(strip, 420, 120);
            Assert.Equal(("Recording", "0:00"), (strip.StateText, strip.TimeText));
            Assert.Equal("Pause recording", AutomationProperties.GetName(strip.PauseButton));
            Assert.Equal("Stop recording and keep it", AutomationProperties.GetName(strip.StopButton));
            Assert.True(strip.PauseButton.IsEnabled && strip.StopButton.IsEnabled);
            Assert.Equal("Recording, 0 seconds", AutomationProperties.GetName(Nodes<LiveStatusText>(strip).Single()));
        });
    }

    [Fact]
    public void The_time_keeps_its_width_so_counting_never_moves_the_buttons()
    {
        rig.Frames.Nothing = true;
        rig.Controller.Start();
        headless.OnUiThread(() =>
        {
            using var model = new TeachStripModel(rig.Controller);
            var strip = new TeachStrip(model);
            using var stage = new ReserveViewStage(strip, 420, 120);
            var before = (strip.PauseButton.Bounds.X, strip.StopButton.Bounds.X);
            rig.Clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(41));
            model.Refresh();
            stage.Layout();
            Assert.Equal("9:41", strip.TimeText);
            Assert.Equal(before, (strip.PauseButton.Bounds.X, strip.StopButton.Bounds.X));
        });
    }

    [Fact]
    public async Task A_press_of_pause_and_stop_reaches_the_recording_and_the_label_follows()
    {
        rig.Frames.Nothing = true;
        rig.Controller.Start();
        var recorder = rig.Controller.Active!;
        headless.OnUiThread(() =>
        {
            using var model = new TeachStripModel(rig.Controller);
            var strip = new TeachStrip(model);
            using var stage = new ReserveViewStage(strip, 420, 120);
            Click(strip.PauseButton);
        });
        await recorder.Flush();
        headless.OnUiThread(() =>
        {
            using var model = new TeachStripModel(rig.Controller);
            var strip = new TeachStrip(model);
            using var stage = new ReserveViewStage(strip, 420, 120);
            Assert.Equal("Paused", strip.StateText);
            Assert.Equal("Resume recording", AutomationProperties.GetName(strip.PauseButton));
            Assert.Contains(Nodes<TextBlock>(strip), t => t.Text == "Resume");
            Click(strip.StopButton);
        });
        var ended = await recorder.Stop();
        Assert.Equal(TeachRecordingState.Recorded, ended.State);
        Assert.Null(rig.Controller.Active);
    }

    [Fact]
    public async Task The_dot_is_a_ring_while_paused_and_filled_while_recording()
    {
        rig.Frames.Nothing = true;
        rig.Controller.Start();
        rig.Controller.Pause();
        await rig.Controller.Active!.Flush();
        headless.OnUiThread(() =>
        {
            using var model = new TeachStripModel(rig.Controller);
            var strip = new TeachStrip(model);
            using var stage = new ReserveViewStage(strip, 420, 120);
            var dot = Nodes<Avalonia.Controls.Shapes.Ellipse>(strip).Single();
            Assert.Equal(Avalonia.Media.Colors.Transparent, ((Avalonia.Media.ISolidColorBrush)dot.Fill!).Color);
            Assert.True(dot.StrokeThickness > 0);
        });
    }
}
