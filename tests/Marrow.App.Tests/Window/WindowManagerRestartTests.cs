using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Window;

// Source: DroppyCode/App/Window/WindowManager.swift (B3 52bc792), :24, :32-55, :67-70, :80-84.
// Two disposable testhosts share one production SettingsStore file. This is a process restart of
// the window/store components; Program, AppShell and the installed application's lifetime are not started.
[Collection("WindowManager process proof")]
public sealed class WindowManagerRestartTests
{
    [Fact]
    [Trait("Req", "PR-WIN-006")] // WindowManager.swift:24, :67-70; restart preserves main placement through ED-006/ED-156.
    [Trait("Req", "PR-WIN-010")] // WindowManager.swift:21, :32-55, :80-84; Settings has no persisted placement.
    public void Restart_restores_the_main_frame_from_SettingsStore_and_recomputes_Settings_placement()
    {
        WindowManagerRestartProof.Execute(typeof(WindowManagerRestartTests).FullName + "." +
            nameof(Restart_restores_the_main_frame_from_SettingsStore_and_recomputes_Settings_placement));
    }
}
