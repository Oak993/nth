using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Window;

// Source: DroppyCode/App/Window/WindowManager.swift (B3 52bc792), :15-16, :26-38, :98.
// The main-close process-exit behaviour is PORT-ONLY OD-0008 C1/C2, AppShell.cs:50-75.
// This test uses factory Close(), not an injected pointer or the real Marrow executable.
[Collection("WindowManager process proof")]
public sealed class WindowManagerProcessTests
{
    [Fact]
    [Trait("Req", "PR-WIN-003")] // WindowManager.swift:15, :26-30; OD-0008 C1 main-close exit
    [Trait("Req", "PR-WIN-008")] // WindowManager.swift:98; OD-0008 C1/C2 retained Settings, main-close handshake
    [Trait("Decision", "OD-0151")] // Settings is a panel inside the main window: the one panel and its content are retained
    public void Closing_main_after_FinishLaunching_exits_the_child_process_and_Settings_is_retained()
    {
        WindowManagerProcessProof.Execute(typeof(WindowManagerProcessTests).FullName + "." +
            nameof(Closing_main_after_FinishLaunching_exits_the_child_process_and_Settings_is_retained));
    }
}
