using Marrow.App.Safety;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-6: a stand-in for the thread the stop chord lives on. The test says which chords another app holds and which type a
/// character, and presses the chord itself; every Register and Unregister is recorded. Nothing is registered with Windows.
/// </summary>
internal sealed class FakeStopChordHost : IStopChordHost
{
    private readonly object gate = new();

    /// <summary>Chords another app holds: Register refuses them.</summary>
    public HashSet<PcHotkey> Taken { get; } = [];

    /// <summary>Chords that would type a character on some installed layout.</summary>
    public HashSet<PcHotkey> Types { get; } = [];

    public List<string> Calls { get; } = [];

    public PcHotkey? Registered { get; private set; }

    public int Registrations { get; private set; }

    public event Action<int>? Pressed;

    public bool Register(int id, PcHotkey chord)
    {
        lock (gate)
        {
            Calls.Add("register " + chord.Format());
            if (Taken.Contains(chord) || Registered is not null) return false;
            Registered = chord;
            Registrations++;
            return true;
        }
    }

    public void Unregister(int id)
    {
        lock (gate)
        {
            Calls.Add("unregister");
            Registered = null;
        }
    }

    public bool TypesCharacter(PcHotkey chord) => Types.Contains(chord);

    /// <summary>The owner presses the chord: raised on the caller's thread, like the host's own thread would.</summary>
    public void Press(int id = GlobalStop.StopId) => Pressed?.Invoke(id);

    public void Dispose()
    {
    }
}
