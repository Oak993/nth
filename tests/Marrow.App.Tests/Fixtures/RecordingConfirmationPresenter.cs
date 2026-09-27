using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

// ED-216: records complete dialog requests and drives the same action/close state without opening a window.
public sealed class RecordingConfirmationPresenter : IAlertPresenter
{
    public List<AlertRequest> Shown { get; } = [];
    private readonly List<AlertSession> sessions = [];

    public Task Show(string title, string informativeText, AlertStyle style, IReadOnlyList<AlertButton> buttons) =>
        Show(new AlertRequest(title, informativeText, buttons));

    public Task Show(AlertRequest request)
    {
        Shown.Add(request);
        AlertSession? session = null;
        session = new AlertSession(request, () => session!.Closed());
        sessions.Add(session);
        return session.Completion;
    }

    public void Press(string title, int index = 0) => sessions[index].Press(title);
    public void Cancel(int index = 0) => sessions[index].Cancel();
}
