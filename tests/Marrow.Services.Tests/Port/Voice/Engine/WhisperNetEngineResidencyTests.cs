using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-7.9: "The model is unloaded after 5 minutes without a session." The engine sees calls, not sessions: 5 minutes after the last load or decode, on the
// injected clock. No test waits on a wall clock.
public sealed class WhisperNetEngineResidencyTests : IDisposable
{
    private readonly EngineRig rig = new();

    public void Dispose() => rig.Dispose();

    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private async Task Load() => await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));

    private async Task Decode() =>
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), new DecodeRequest("en", "", DecodeMode.Live, TimeSpan.Zero), CancellationToken.None));

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_idle_time_is_five_minutes()
    {
        Assert.Equal(Five, WhisperNetEngine.IdleUnloadAfter);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task The_model_is_released_five_minutes_after_the_load_and_not_a_moment_before()
    {
        await Load();
        rig.Runtime.Advance(Five - TimeSpan.FromMilliseconds(1));
        Assert.True(rig.Engine.IsLoaded);
        rig.Runtime.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(rig.Engine.IsLoaded);
        Assert.True(rig.Backend.Models[0].Disposed);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task A_decode_restarts_the_five_minutes_from_its_end()
    {
        await Load();
        rig.Runtime.Advance(TimeSpan.FromMinutes(4));
        await Decode();
        rig.Runtime.Advance(TimeSpan.FromMinutes(4));
        Assert.True(rig.Engine.IsLoaded);
        rig.Runtime.Advance(TimeSpan.FromMinutes(1));
        Assert.False(rig.Engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task A_load_of_the_model_already_loaded_restarts_the_five_minutes_too()
    {
        await Load();
        rig.Runtime.Advance(TimeSpan.FromMinutes(4));
        await Load();
        rig.Runtime.Advance(TimeSpan.FromMinutes(4));
        Assert.True(rig.Engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task A_decode_in_flight_is_never_unloaded_under_and_the_clock_starts_when_it_ends()
    {
        await Load();
        var release = rig.Backend.HoldDecodes();
        var pass = rig.Engine.TranscribeAsync(EngineRig.Audio(1), new DecodeRequest("en", "", DecodeMode.Final, TimeSpan.Zero), CancellationToken.None);
        rig.Runtime.Advance(Five + TimeSpan.FromMinutes(1));
        Assert.True(rig.Engine.IsLoaded);
        Assert.False(rig.Backend.Models[0].Disposed);
        release();
        await EngineRig.Guard(pass);
        rig.Runtime.Advance(Five - TimeSpan.FromSeconds(1));
        Assert.True(rig.Engine.IsLoaded);
        rig.Runtime.Advance(TimeSpan.FromSeconds(1));
        Assert.False(rig.Engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task After_the_idle_release_a_decode_reloads_the_model_it_had()
    {
        await Load();
        rig.Runtime.Advance(Five);
        Assert.False(rig.Engine.IsLoaded);
        await Decode();
        Assert.Equal(2, rig.Backend.Loads.Count);
        Assert.Equal(rig.Backend.Loads[0].ModelPath, rig.Backend.Loads[1].ModelPath);
        Assert.Equal(rig.Backend.Loads[0].RuntimeOrder, rig.Backend.Loads[1].RuntimeOrder);
        Assert.Equal(rig.Backend.Loads[0].UseGpu, rig.Backend.Loads[1].UseGpu);
        Assert.True(rig.Engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task An_explicit_unload_cancels_the_timer_and_no_second_release_happens()
    {
        await Load();
        rig.Engine.Unload();
        Assert.Equal(0, rig.Runtime.Time.ActiveTimers);
        rig.Runtime.Advance(Five);
        Assert.Equal(1, rig.Backend.Log.Count(l => l.StartsWith("dispose:", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task A_failed_load_leaves_no_timer_behind()
    {
        rig.Backend.FailLoads(new IOException("x"));
        await Assert.ThrowsAsync<IOException>(() => rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        Assert.Equal(0, rig.Runtime.Time.ActiveTimers);
    }
}
