using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: legacy cloud Show helpers left their windows alive after the owning TestRuntime was disposed.
// Bound their lifetime to the test. This preserves every assertion, and prevents a later RunJobs from rendering
// a prior test's detached glyph runs or reading a prior test's disposed observable scopes.
internal static class ChatHeadlessFixtures
{
    private static readonly List<Avalonia.Controls.Window> windows = [];

    public static void Track(Avalonia.Controls.Window window) => windows.Add(window);

    public static void Run(HeadlessSession session, Action body) => session.OnUiThread(() =>
    {
        // A chat view that opens a popover on its own (the Hydra mark's first-chat intro, HydraButton.swift:93) must not try
        // the real Popup, which headless cannot create; a test that reads what was presented overrides this with its own.
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        try { body(); }
        finally
        {
            foreach (var window in windows.ToArray()) window.Close();
            windows.Clear();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
