using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Views.Composer;

// ED-238 (unit/effort-title-fidelity): the frames of the effort title that the side-by-sides with 2-chat-use.mov are made
// from (EffortTitleFramesRig; EffortSlider.swift:573-574 the title, :631 the slider's withAnimation(.snappy(duration: 0.2))).
// With MARROW_STUDY_DIR and MARROW_STUDY_SCHEDULE set it renders that study there (the study's tools/probe.sh). Without them
// it renders the widening of 34.84 s (High -> Extra High) into a throwaway folder under MARROW_PORT_DATA_DIR and checks the
// tool itself: a frame every step, each the whole window, the title moving while the change runs and still once it is over.
[Collection("Headless")]
public sealed class EffortSliderTitleFramesTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-238")]
    public void The_titles_frames_for_the_side_by_sides_render_every_step_of_a_schedule() => headless.OnUiThread(() =>
    {
        if (EffortTitleFramesRig.FromEnvironment() is { } asked)
        {
            Assert.NotEmpty(EffortTitleFramesRig.Render(asked).Times);
            return;
        }
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must point at a throwaway folder (R-TEST-9)");
        var folder = Path.Combine(root!, "effort-title-frames-" + Guid.NewGuid().ToString("N"));
        try
        {
            var frames = EffortTitleFramesRig.Render(new EffortTitleFramesRig.Study(folder, "high;0:xhigh", 20, 600, false, null));
            Assert.Equal(Enumerable.Range(0, 31).Select(i => i * 20), frames.Times);
            Assert.Equal((330, 140), (frames.Width, frames.Height)); // the rig's window, at the headless scale of 1
            foreach (var ms in frames.Times)
                Assert.Equal(frames.RowBytes * frames.Height, new FileInfo(Path.Combine(folder, $"t{ms:0000}.raw")).Length);
            Assert.True(frames.Title.Width > 0 && frames.Title.Height > 0);
            byte[] Title(int ms) => EffortTitleFramesRig.Crop(folder, ms, frames, frames.Title);
            Assert.NotEqual(Title(0), Title(100)); // the title moves while the change runs
            Assert.NotEqual(Title(380), Title(400)); // the last letters still arrive at the approved snappy pace
            Assert.Equal(Title(480), Title(500)); // after the last letter settles, a plain colour holds still
            Assert.Equal(Title(500), Title(600)); // including a further five independently rendered frames
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });
}
