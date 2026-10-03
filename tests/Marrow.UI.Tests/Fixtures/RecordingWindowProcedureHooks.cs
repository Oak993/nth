using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

// What stands for Avalonia's window procedure hooks in a test (PopoverHost.WindowProcedure): a headless popup window has
// no Win32 window procedure, so the hooks a popover puts on its own window are recorded, and a test calls them as Windows
// would.
internal sealed class RecordingWindowProcedureHooks : PopoverHost.IWindowProcedureHooks
{
    public List<(TopLevel Window, Win32Properties.CustomWndProcHookCallback Hook)> Added { get; } = [];

    public List<(TopLevel Window, Win32Properties.CustomWndProcHookCallback Hook)> Removed { get; } = [];

    public void Add(TopLevel window, Win32Properties.CustomWndProcHookCallback hook) => Added.Add((window, hook));

    public void Remove(TopLevel window, Win32Properties.CustomWndProcHookCallback hook) => Removed.Add((window, hook));
}
