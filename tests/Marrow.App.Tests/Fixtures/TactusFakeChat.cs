using Marrow.App.Port.PcControl;
using Marrow.Core.Models;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A chat runtime as the Tactus chat mirror sees it: records rows, continues, asks and provider answers.</summary>
public sealed class TactusFakeChat(string thread) : ITactusChat
{
    public string Thread { get; } = thread;

    public List<string> Rows { get; } = [];

    public List<(string Message, string Chip)> Continued { get; } = [];

    public IReadOnlyList<PcPendingAsk> Shown { get; private set; } = [];

    public List<(string Request, PcAnswer Answer)> Answered { get; } = [];

    public List<QuestionRequest> Questions { get; } = [];

    public List<ApprovalRequest> Approvals { get; } = [];

    public PcResumeOutcome Outcome { get; set; } = PcResumeOutcome.None;

    public bool? AskedToContinue { get; private set; }

    public IReadOnlyList<QuestionRequest> ProviderQuestions => Questions;

    public IReadOnlyList<ApprovalRequest> ProviderApprovals => Approvals;

    public event Action? ProviderAsksChanged;

    public void Row(string text) => Rows.Add(text);

    public PcResumeOutcome Resume(bool continuesChat, DateTimeOffset now)
    {
        AskedToContinue = continuesChat;
        return Outcome;
    }

    public void Continue(string message, string chip) => Continued.Add((message, chip));

    public void Asks(IReadOnlyList<PcPendingAsk> asks) => Shown = asks;

    public void AnswerProvider(string requestID, PcAnswer answer)
    {
        Answered.Add((requestID, answer));
        Questions.RemoveAll(q => q.Id == requestID);
        ProviderAsksChanged?.Invoke();
    }

    public void Changed() => ProviderAsksChanged?.Invoke();
}
