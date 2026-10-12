using Marrow.App.Port.PcControl.Workspace;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: cloak and focus for WorkspaceView tests (unit P18, ED-NEW-pcws-7): records every cloak with whether the window
// was already on screen, and where focus was given back. Touches no window of Windows'.
internal sealed class FakeWorkspaceWindowing : IWorkspaceWindowing
{
    public List<(bool On, bool WasVisible)> Cloaks { get; } = [];

    public nint Front { get; set; } = 0x77;

    public List<nint> FocusGiven { get; } = [];

    public void Cloak(Avalonia.Controls.Window window, bool on) => Cloaks.Add((on, window.IsVisible));

    public nint Foreground() => Front;

    public void GiveFocus(nint window) => FocusGiven.Add(window);
}
