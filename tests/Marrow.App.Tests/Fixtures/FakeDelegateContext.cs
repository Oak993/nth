using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-22: the delegate's context, faked: the project's decisions and a chat's last turns, as the test gives them.</summary>
internal sealed class FakeDelegateContext : IDelegateContext
{
    public string Decisions { get; set; } = "";

    public string PastAnswers { get; set; } = "";

    public List<DelegateTurn> Turns { get; } = [];

    public Func<Guid, ChatThread?> Chat { get; set; } = _ => null;

    public int Views { get; private set; }

    public int Packs { get; private set; }

    public DelegateChatView? View(Guid thread)
    {
        Views++;
        return Chat(thread) is { } chat ? new DelegateChatView(thread, chat.Title, chat.ProjectID, null, [.. Turns]) : null;
    }

    public Task<DelegatePack> Pack(DelegateChatView view, QuestionRequest request, CancellationToken cancellationToken)
    {
        Packs++;
        return Task.FromResult(new DelegatePack(Decisions, PastAnswers, view.Recent));
    }
}
