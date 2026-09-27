using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: ED-216 override uses the shared session and window keyboard handlers without showing a window.
internal sealed class ChatAlertFixture : IAlertPresenter
{
    public List<AlertRequest> Shown { get; } = [];
    public List<Avalonia.Controls.Window> Windows { get; } = [];
    public List<Task> Completions { get; } = [];
    private readonly List<AlertSession> sessions = [];

    public Task Show(string title, string message, AlertStyle style, IReadOnlyList<AlertButton> buttons) =>
        throw new InvalidOperationException("Expected a complete ED-216 dialog request.");

    public Task Show(AlertRequest request)
    {
        Shown.Add(request);
        AlertSession? session = null;
        session = new AlertSession(request, () => session!.Closed());
        session.BindLifetime(SwiftTask.CancellationToken);
        sessions.Add(session);
        Windows.Add(WindowsAlertPresenter.BuildWindow(request, session));
        Completions.Add(session.Completion);
        return session.Completion;
    }

    public void Key(Key key, int index = 0) => Windows[index].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    public void CancelAll() { foreach (var session in sessions) session.Cancel(); }
}
