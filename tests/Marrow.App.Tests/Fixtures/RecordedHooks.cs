using Avalonia.Controls;

namespace Marrow.App.Tests.Fixtures;

// What stands for Avalonia's window procedure hook registration in a test (AppShell.AddWindowProcedureHook): a headless window
// has no Win32 procedure, so the hooks put on a window are recorded, with whether the window was shown at that moment, and a
// test calls them as Windows would.
internal sealed class RecordedHooks
{
    public List<(TopLevel Window, Win32Properties.CustomWndProcHookCallback Hook, bool VisibleWhenHooked)> Added { get; } = [];

    public void Add(TopLevel window, Win32Properties.CustomWndProcHookCallback hook) => Added.Add((window, hook, window.IsVisible));
}
