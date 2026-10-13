using Marrow.App.Safety;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-6: a chord host whose Register waits until the test lets it go, so a press can be sent while a registration is in
/// flight on another thread (a press never waits on the registration).
/// </summary>
internal sealed class BlockingRegisterHost : IStopChordHost
{
    public ManualResetEventSlim InRegister { get; } = new();

    public ManualResetEventSlim Finish { get; } = new();

    public event Action<int>? Pressed;

    public bool Register(int id, PcHotkey chord)
    {
        InRegister.Set();
        return Finish.Wait(TimeSpan.FromSeconds(10));
    }

    public void Unregister(int id)
    {
    }

    public bool TypesCharacter(PcHotkey chord) => false;

    public void Press(int id = GlobalStop.StopId) => Pressed?.Invoke(id);

    public void Dispose()
    {
        InRegister.Dispose();
        Finish.Dispose();
    }
}
