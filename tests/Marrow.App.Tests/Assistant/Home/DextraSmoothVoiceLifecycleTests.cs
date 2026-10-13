using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant.Home;

[Trait("Decision", "ED-NEW-dextra-smooth-7")]
public sealed class DextraSmoothVoiceLifecycleTests
{
    [Fact]
    public async Task Status_snapshot_remains_readable_while_registration_is_held()
    {
        var native = new DextraSmoothChordGate { HoldRegister = true };
        using var chord = new VoiceChord(native, () => null);
        var registration = Task.Run(chord.Register);
        await native.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var reading = Task.Run(() => chord.Status);
        var finishedBeforeRelease = false;
        try
        {
            try { await reading.WaitAsync(TimeSpan.FromSeconds(2)); finishedBeforeRelease = true; }
            catch (TimeoutException) { /* deadlock guard: registration still owns its gate */ }
        }
        finally { native.Release.Set(); await registration; await reading; }
        Assert.True(finishedBeforeRelease, "Status waited for native registration to be released");
        Assert.True(chord.Status.Works);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restart_waits_for_actual_release_and_refuses_a_failed_release(bool failRelease)
    {
        using var doubles = new DextraVoiceRig();
        using var temp = new AssistantTempRoot();
        using var rig = new BackgroundLaunchRig(temp, Guid.NewGuid());
        var native = new DextraSmoothChordGate { HoldDispose = true, FailDispose = failRelease };
        var frames = new Marrow.UI.Port.ManualFrameSource();
        var calls = 0;
        var host = rig.OnMain(() => new DextraHost(rig.Model, rig.Model.AssistantHost.Settings,
            startup: rig.Seams, voiceFrames: frames, voiceAsync: (app, owner, scope, token) =>
            {
                calls++;
                return calls == 1 ? DextraVoice.CreateDeferred(app, owner, scope, token, native.Parts(doubles.Parts))
                    : Task.FromResult<DextraVoice?>(null);
            }));
        Task previous = Task.CompletedTask;
        try
        {
            rig.OnMain(() => { Assert.NotNull(host.EnsureStarted()); FrameTwice(frames); });
            rig.Run(() => host.VoiceStartup);
            rig.OnMain(() =>
            {
                var old = Assert.IsType<DextraVoice>(host.Voice);
                host.Stop(); previous = old.NativeCleanup;
                Assert.False(previous.IsCompleted);
                Assert.NotNull(host.EnsureStarted()); FrameTwice(frames);
                Assert.Equal(1, calls); // replacement may not even prepare against the old reservation
            });
            native.Release.Set();
            rig.Run(() => host.VoiceStartup);
            Assert.Equal(failRelease ? 1 : 2, calls);
            if (failRelease) Assert.Contains("fake chord release failed", host.StartFault, StringComparison.Ordinal);
            else Assert.Null(host.StartFault);
        }
        finally
        {
            native.Release.Set();
            rig.Run(async () => { try { await previous; } catch (InvalidOperationException) when (failRelease) { /* asserted by restart */ } });
            rig.Run(() => host.VoiceStartup);
            rig.OnMain(host.Dispose);
        }
    }

    [Fact]
    public void Activation_reconciles_a_chord_changed_after_preparation()
    {
        using var doubles = new DextraVoiceRig();
        using var temp = new AssistantTempRoot();
        using var rig = new BackgroundLaunchRig(temp, Guid.NewGuid());
        DextraVoice? voice = null;
        var latest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            rig.OnMain(() => Assert.NotNull(rig.Model.AssistantHost.EnsureStarted()));
            rig.Run(async () => voice = await DextraVoice.CreateDeferred(rig.Model, rig.Model.AssistantHost,
                new VoiceStopScope(), CancellationToken.None, doubles.Parts));
            voice!.Chord.StatusChanged += status => { if (status.Chord == "Ctrl+Alt+Home" && status.Works) latest.TrySetResult(); };
            rig.OnMain(() =>
            {
                rig.Model.AssistantHost.Settings.Update(value => value with { TalkChord = "Ctrl+Alt+Home" });
                voice.Activate();
            });
            rig.Run(() => latest.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("Ctrl+Alt+Home", voice.Chord.Status.Chord);
            Assert.False(doubles.Capture.IsRunning);
        }
        finally { if (voice is not null) { rig.OnMain(voice.Dispose); rig.Run(() => voice.NativeCleanup); } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Canceling_preparation_remembers_failed_release_before_retry(bool retryBeforeRelease)
    {
        using var doubles = new DextraVoiceRig();
        using var temp = new AssistantTempRoot();
        using var rig = new BackgroundLaunchRig(temp, Guid.NewGuid());
        var native = new DextraSmoothChordGate { HoldRegister = true, FailDispose = true };
        var frames = new Marrow.UI.Port.ManualFrameSource();
        var calls = 0;
        var host = rig.OnMain(() => new DextraHost(rig.Model, rig.Model.AssistantHost.Settings,
            startup: rig.Seams, voiceFrames: frames, voiceAsync: (app, owner, scope, token) =>
            {
                calls++;
                return calls == 1 ? DextraVoice.CreateDeferred(app, owner, scope, token, native.Parts(doubles.Parts))
                    : Task.FromResult<DextraVoice?>(null);
            }));
        Task abandoned = Task.CompletedTask;
        try
        {
            rig.OnMain(() => { Assert.NotNull(host.EnsureStarted()); FrameTwice(frames); });
            rig.Run(() => native.Entered.Task);
            rig.OnMain(() => { abandoned = host.VoiceStartup; host.Stop(); });
            if (retryBeforeRelease) rig.OnMain(() => { Assert.NotNull(host.EnsureStarted()); FrameTwice(frames); });
            native.Release.Set();
            rig.Run(() => abandoned);
            if (!retryBeforeRelease) rig.OnMain(() => { Assert.NotNull(host.EnsureStarted()); FrameTwice(frames); });
            rig.Run(() => host.VoiceStartup);
            Assert.Equal(1, calls);
            Assert.Contains("fake chord release failed", host.StartFault, StringComparison.Ordinal);
        }
        finally
        {
            native.Release.Set();
            rig.Run(() => abandoned);
            rig.Run(() => host.VoiceStartup);
            rig.OnMain(host.Dispose);
        }
    }

    private static void FrameTwice(Marrow.UI.Port.ManualFrameSource frames)
    {
        frames.Advance(TimeSpan.FromMilliseconds(17)); frames.Advance(TimeSpan.FromMilliseconds(17));
    }
}
