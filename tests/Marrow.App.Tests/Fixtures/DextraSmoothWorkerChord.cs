using Marrow.App.Safety;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Fixtures;

internal sealed class DextraSmoothWorkerChord(IStopChordHost inner) : IStopChordHost
{
    public bool MainObserved { get; private set; }
    public void Observe() => MainObserved |= MainActor.IsIsolated;
    public bool Register(int id, PcHotkey chord) { Observe(); return inner.Register(id, chord); }
    public bool TypesCharacter(PcHotkey chord) { Observe(); return inner.TypesCharacter(chord); }
    public void Unregister(int id) { Observe(); inner.Unregister(id); }
    public void Dispose() { Observe(); inner.Dispose(); }
    public event Action<int>? Pressed { add => inner.Pressed += value; remove => inner.Pressed -= value; }
}
