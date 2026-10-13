using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-dextra-smooth-7: a causal native wait, released by the test, with a deadlock guard only.
internal sealed class DextraSmoothChordGate : IStopChordHost
{
    public ManualResetEventSlim Release { get; } = new(false);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool HoldRegister { get; init; }
    public bool HoldDispose { get; init; }
    public bool FailDispose { get; init; }
    public bool Register(int id, PcHotkey chord) { if (HoldRegister) Hold(); return true; }
    public bool TypesCharacter(PcHotkey chord) => false;
    public void Unregister(int id) { }
    public event Action<int>? Pressed { add { } remove { } }
    public void Dispose() { if (HoldDispose) Hold(); if (FailDispose) throw new InvalidOperationException("fake chord release failed"); }
    private void Hold()
    {
        Entered.TrySetResult();
        if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test did not release fake native wait.");
    }

    public DextraVoiceParts Parts(DextraVoiceParts use) => new()
    {
        ChordHost = () => this, Capture = use.Capture, Vad = use.Vad, Resampler = use.Resampler,
        Engine = use.Engine, Microphone = use.Microphone, Synth = use.Synth, Player = use.Player,
        Model = use.Model, Typing = use.Typing, PreemptDictation = use.PreemptDictation,
        BringForward = use.BringForward, OtherChords = use.OtherChords, Post = use.Post,
    };
}
