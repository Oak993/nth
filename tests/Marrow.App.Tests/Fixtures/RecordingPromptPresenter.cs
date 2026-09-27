using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A fake IPromptPresenter (App/Port/PromptPresenter.cs) that records every Show call and lets a test
/// choose when each alert "closes", the same way RecordingAlertPresenter stands in for WindowsAlertPresenter
/// (Avalonia 12.1.2 headless cannot open a real Window).</summary>
public sealed class RecordingPromptPresenter : IPromptPresenter
{
    public sealed record Recorded(string Title, string Placeholder, string InitialText, IReadOnlyList<PromptButton> Buttons);

    public List<Recorded> Shown { get; } = [];

    private readonly List<TaskCompletionSource> pending = [];

    public Task Show(string title, string placeholder, string initialText, IReadOnlyList<PromptButton> buttons)
    {
        Shown.Add(new Recorded(title, placeholder, initialText, buttons));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Add(completion);
        return completion.Task;
    }

    /// <summary>Simulates the user typing <paramref name="text"/> and clicking the given button, closing the alert.</summary>
    public void Dismiss(int alertIndex, int buttonIndex, string text)
    {
        var button = Shown[alertIndex].Buttons[buttonIndex];
        if (!button.IsCancelRole) button.Perform(text);
        pending[alertIndex].TrySetResult();
    }
}
