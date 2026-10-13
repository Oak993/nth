using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 1; ED-NEW-dextra-as19-8, -10): what the recording strip says. The state in a word, the time the recording has run (and the
// time left in the last five minutes), Pause that turns into Resume, the notes (narration off, frames held), and a spoken form of all of it. It
// reads the recording, so what it says is what the recorder knows; once the recording ends it says it is keeping it.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-10")]
public sealed class TeachStripModelTests : IDisposable
{
    private readonly TeachControllerRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    public async Task The_strip_says_recording_and_the_time_it_has_run_and_stays_the_same_until_the_time_moves()
    {
        using var model = new TeachStripModel(rig.Controller);
        Assert.Equal(("Starting", false), (model.StateWord, model.CanAct));
        Assert.True(rig.Controller.Start().Started);
        model.Refresh();
        Assert.Equal(("Recording", "0:00", "Pause", true), (model.StateWord, model.Time, model.PauseLabel, model.CanAct));
        rig.Frames.Nothing = true;
        rig.Clock.Advance(TimeSpan.FromSeconds(75));
        await rig.Controller.Active!.Flush();
        model.Refresh();
        Assert.Equal(("1:15", "Recording, 1 minute 15 seconds"), (model.Time, model.Spoken));
        var raised = 0;
        model.Changed += () => raised++;
        model.Refresh();
        model.Refresh();
        Assert.Equal(0, raised);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        await rig.Controller.Active!.Flush();
        model.Refresh();
        Assert.Equal(1, raised);
        await rig.Controller.Stop();
    }

    [Fact]
    public async Task Pause_turns_into_Resume_and_the_state_into_paused_and_back()
    {
        using var model = new TeachStripModel(rig.Controller);
        rig.Controller.Start();
        rig.Frames.Nothing = true;
        model.Refresh();
        model.TogglePause();
        await rig.Controller.Active!.Flush();
        model.Refresh();
        Assert.Equal(("Paused", "Resume", true), (model.StateWord, model.PauseLabel, model.Paused));
        Assert.Equal("Paused, 0 seconds", model.Spoken);
        model.TogglePause();
        await rig.Controller.Active!.Flush();
        model.Refresh();
        Assert.Equal(("Recording", "Pause", false), (model.StateWord, model.PauseLabel, model.Paused));
        await rig.Controller.Stop();
    }

    [Fact]
    public async Task In_the_last_five_minutes_the_strip_shows_the_time_left()
    {
        using var model = new TeachStripModel(rig.Controller);
        rig.Controller.Start();
        rig.Frames.Nothing = true;
        rig.Clock.Advance(TimeSpan.FromMinutes(56) + TimeSpan.FromSeconds(1));
        await rig.Controller.Active!.Flush();
        model.Refresh();
        Assert.Equal(("3:59 left", "Recording, 3 minutes 59 seconds left"), (model.Time, model.Spoken));
        await rig.Controller.Stop();
    }

    [Fact]
    public async Task Stop_ends_the_recording_and_the_strip_says_it_is_keeping_it()
    {
        using var model = new TeachStripModel(rig.Controller);
        rig.Controller.Start();
        model.Refresh();
        var ended = rig.Controller.Active!;
        model.Stop();
        await ended.Flush();
        await ended.Stop();
        model.Refresh();
        Assert.Equal(("Keeping the recording", false, true), (model.StateWord, model.CanAct, model.Ending));
        model.TogglePause();
        model.Stop();
        Assert.Equal(TeachRecordingState.Recorded, rig.Recordings.Info(ended.Id!)!.State);
    }

    [Fact]
    public async Task The_notes_of_the_recording_are_the_strips_notes()
    {
        using var model = new TeachStripModel(rig.Controller);
        rig.Controller.Start();
        model.Refresh();
        Assert.Empty(model.Notes);
        await rig.Controller.Stop();
    }

    [Theory]
    [InlineData(0, "0 seconds")]
    [InlineData(1, "1 second")]
    [InlineData(60, "1 minute")]
    [InlineData(134, "2 minutes 14 seconds")]
    [InlineData(3600, "1 hour")]
    [InlineData(3725, "1 hour 2 minutes 5 seconds")]
    public void A_time_is_said_in_words(int seconds, string said) => Assert.Equal(said, TeachStripModel.SpokenTime(TimeSpan.FromSeconds(seconds)));
}
