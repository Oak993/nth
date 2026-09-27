using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A fake IPromptPresenter (App/Port/PromptPresenter.cs) that records every Show call and lets a test
/// choose when each alert "closes", the same way RecordingAlertPresenter stands in for WindowsAlertPresenter
/// (Avalonia 12.1.2 headless cannot open a real Window).</summary>
public sealed class RecordingPromptPresenter : IPromptPresenter, IAlertPresenter
{
    public sealed record Recorded(string Title, string Placeholder, string InitialText, IReadOnlyList<PromptButton> Buttons);

    public List<Recorded> Shown { get; } = [];

    private readonly List<TaskCompletionSource> pending = [];

    // ED-216: preserve the original field/button assertions through the new shared request override.
    Task IAlertPresenter.Show(AlertRequest request)
    {
        var field = Assert.Single(request.TextFields!);
        return Show(request.Title, field.Label, field.Value.Value, request.Buttons.Select(button =>
            new PromptButton(button.Title, button.Role == AlertButtonRole.Cancel, text =>
            {
                field.Value.Value = text;
                button.Perform();
            })).ToArray());
    }

    Task IAlertPresenter.Show(string title, string message, AlertStyle style, IReadOnlyList<AlertButton> buttons) =>
        throw new InvalidOperationException("Expected an ED-216 text-field request.");

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
