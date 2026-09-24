using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A fake IAlertPresenter (App/Port/AlertPresenter.cs) that records every Show call and lets a test
/// choose when each alert "closes" (its Task completes), for AppUpdaterFailureAlertTests (PR-UPDATE-057,
/// UI-UPDATE-034). Avalonia 12.1.2 headless cannot open a real Window (RecordingPresenter's own comment,
/// HeadlessFixtures.cs), so this stands in for WindowsAlertPresenter the same way RecordingPresenter stands
/// in for AvaloniaPopoverPresenter.</summary>
public sealed class RecordingAlertPresenter : IAlertPresenter
{
    public sealed record Recorded(string Title, string InformativeText, AlertStyle Style, IReadOnlyList<AlertButton> Buttons);

    public List<Recorded> Shown { get; } = [];

    private readonly List<TaskCompletionSource> pending = [];

    /// <summary>True (the default): Show's Task completes as soon as it is recorded, for a test that does
    /// not care about the alert's own blocking behaviour. A test proving PR-UPDATE-057/UI-UPDATE-034's own
    /// "runs modally" behaviour sets this false and calls Dismiss explicitly.</summary>
    public bool AutoDismiss { get; set; } = true;

    public Task Show(string title, string informativeText, AlertStyle style, IReadOnlyList<AlertButton> buttons)
    {
        Shown.Add(new Recorded(title, informativeText, style, buttons));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Add(completion);
        if (AutoDismiss) completion.TrySetResult();
        return completion.Task;
    }

    /// <summary>Simulates the user clicking the most recently shown alert's given button, closing it.</summary>
    public void Dismiss(int alertIndex = 0, int buttonIndex = 0)
    {
        Shown[alertIndex].Buttons[buttonIndex].Perform();
        pending[alertIndex].TrySetResult();
    }
}
