// PORT-ONLY: Tactus pointer, where Tactus acts (OD-0144, ED-NEW-pcctl-21; DESIGN.md 4.5 "The Tactus pointer", 2.10
// "Pointers"). The engine tells the pointer host, once per action and before it runs, which app acts at which screen point
// and how (a move, a press, typing), and whether the user can see that point: the window is not minimized and its own
// root window is the one under the point (not covered). A point the user cannot see parks the app's pointer on its pill
// row ("working behind"). Nothing here waits: the pointer is drawn beside the action, never before it, so it costs a step
// no time. With "Show Tactus pointer" off nothing is raised.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>How Tactus acts at the point.</summary>
public enum PcPointerAct
{
    /// <summary>The pointer goes there (a scroll, a drag's way, a focus).</summary>
    Move,

    /// <summary>A click or a command lands there: the ripple shows when the pointer arrives.</summary>
    Press,

    /// <summary>Text or keys go into the field there.</summary>
    Type,
}

/// <summary>One action of <paramref name="App"/> (its approval key) at <paramref name="Point"/> (physical screen pixels).</summary>
public sealed record PcPointerAim(string App, string AppName, string ExePath, nint Window, PcPoint Point, PcPointerAct Act, bool Visible);

public sealed class PcPointerFeed
{
    /// <summary>Raised on the engine's thread before each action that has a point; handlers must not block.</summary>
    public event Action<PcPointerAim>? Aimed;

    public void Aim(PcPointerAim aim) => Aimed?.Invoke(aim);
}
