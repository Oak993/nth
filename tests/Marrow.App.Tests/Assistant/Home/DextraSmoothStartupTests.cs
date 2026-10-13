using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Assistant.Home;

[Trait("Decision", "ED-NEW-dextra-smooth-7")]
public sealed class DextraSmoothStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Voice_construction_waits_for_a_frame_in_normal_and_hidden_start(bool hidden)
    {
        using var temp = new AssistantTempRoot();
        using var rig = new BackgroundLaunchRig(temp);
        var frames = new ManualFrameSource();
        var calls = 0;
        rig.OnMain(() =>
        {
            using var host = new DextraHost(rig.Model, rig.Model.AssistantHost.Settings,
                starter: (_, _, _) => rig.Model.AssistantHost.EnsureStarted(),
                voice: (_, _, _) => { calls++; return null; }, voiceFrames: frames);
            host.EnsureStarted(mode: hidden ? new StartupMode(true, null, null) : StartupMode.Normal);
            Assert.True(calls == 0, $"voice constructions before first frame={calls}");
            frames.Advance(TimeSpan.FromMilliseconds(17));
            Assert.Equal(0, calls);
            frames.Advance(TimeSpan.FromMilliseconds(17));
            Assert.Equal(1, calls);
            host.EnsureStarted(); frames.Advance(TimeSpan.FromMilliseconds(17));
            Assert.Equal(1, calls);
        });
    }

    [Fact]
    public void Stopping_before_the_frame_withdraws_voice_start()
    {
        using var temp = new AssistantTempRoot();
        using var rig = new BackgroundLaunchRig(temp);
        var frames = new ManualFrameSource(); var calls = 0;
        rig.OnMain(() =>
        {
            using var host = new DextraHost(rig.Model, rig.Model.AssistantHost.Settings,
                starter: (_, _, _) => rig.Model.AssistantHost.EnsureStarted(),
                voice: (_, _, _) => { calls++; return null; }, voiceFrames: frames);
            host.EnsureStarted(); host.Stop();
            frames.Advance(TimeSpan.FromMilliseconds(17));
            Assert.Equal(0, calls);
            Assert.Equal(0, frames.Pending);
        });
    }

    [Fact]
    public void Production_voice_start_yields_and_stop_cancels_its_pending_generation()
    {
        using var temp = new AssistantTempRoot();
        using var rig = new BackgroundLaunchRig(temp, Guid.NewGuid());
        var frames = new ManualFrameSource();
        var pending = new TaskCompletionSource<DextraVoice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0; CancellationToken token = default;
        var host = rig.OnMain(() => new DextraHost(rig.Model, rig.Model.AssistantHost.Settings,
            startup: rig.Seams, voiceFrames: frames, voiceAsync: (_, _, _, cancellation) => { calls++; token = cancellation; return pending.Task; }));
        try
        {
            rig.OnMain(() =>
            {
                Assert.True(host.EnsureStarted(mode: new StartupMode(true, null, null)) is not null, host.StartFault);
                Assert.Equal(0, calls);
                frames.Advance(TimeSpan.FromMilliseconds(17)); frames.Advance(TimeSpan.FromMilliseconds(17));
                Assert.Equal(1, calls);
                Assert.False(host.VoiceStartup.IsCompleted);
                host.Stop(); Assert.True(token.IsCancellationRequested);
            });
            pending.SetResult(null);
            rig.Run(() => host.VoiceStartup);
            Assert.Null(host.Voice);
            Assert.Null(host.StartFault);
        }
        finally { rig.OnMain(host.Dispose); }
    }

    [Fact]
    public void Chord_preparation_registration_and_cleanup_run_off_main_without_opening_capture()
    {
        using var doubles = new DextraVoiceRig();
        using var temp = new AssistantTempRoot();
        using var rig = new BackgroundLaunchRig(temp, Guid.NewGuid());
        var spy = new DextraSmoothWorkerChord(doubles.Chord);
        var use = doubles.Parts;
        var parts = new DextraVoiceParts
        {
            ChordHost = () => { spy.Observe(); return spy; },
            Capture = use.Capture, Vad = use.Vad, Resampler = use.Resampler, Engine = use.Engine,
            Microphone = use.Microphone, Synth = use.Synth, Player = use.Player, Model = use.Model,
            Typing = use.Typing, PreemptDictation = use.PreemptDictation, BringForward = use.BringForward,
            OtherChords = use.OtherChords, Post = use.Post,
        };
        DextraVoice? voice = null;
        try
        {
            rig.OnMain(() => Assert.NotNull(rig.Model.AssistantHost.EnsureStarted()));
            rig.Run(async () => voice = await DextraVoice.CreateDeferred(rig.Model, rig.Model.AssistantHost, new VoiceStopScope(), CancellationToken.None, parts));
            Assert.NotNull(voice);
            Assert.True(voice.Chord.Status.Works);
            Assert.False(doubles.Capture.IsRunning);
            Assert.False(spy.MainObserved, "native chord factory/validation/registration ran on main");
        }
        finally
        {
            if (voice is not null) { rig.OnMain(voice.Dispose); rig.Run(() => voice.NativeCleanup); }
        }
        Assert.False(spy.MainObserved, "native chord disposal ran on main");
    }

}
