using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W11 test helpers (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10, 13.2; ED-NEW-web-w11-1). No Swift
// counterpart. Fake chats for Visus as the web tools see it, a recorder for the turns' ends, the bound set a test drives, and
// a Visus with no tabs (kept outside the test classes, so each test file holds one class, R-TEST-2).

namespace Marrow.App.Tests.Fixtures;

/// <summary>The chats the web tools see: one chat (<paramref name="thread"/>) with its Visus session.</summary>
internal sealed class VisusAgentChatsFake(VisusSession session, string thread) : IVisusAgentChats
{
    public bool PaneOpen { get; set; }
    public int PaneOpened { get; private set; }
    public List<string> Wrote { get; } = [];
    public string? ChatTitle { get; set; } = "Invite Dana";

    public VisusSession? Session(string asked) => asked == thread ? session : null;

    public bool IsPaneOpen(string asked) => PaneOpen;

    public void OpenPane(string asked)
    {
        PaneOpen = true;
        PaneOpened++;
    }

    public string? Title(string asked) => ChatTitle;

    public IReadOnlyList<string> UserMessages(string asked) => Wrote;

    public string? ModelName(string model) => model == "claude-opus-5-5" ? "Opus 5.5" : null;
}

/// <summary>The turn-end watches a binding asked for, by chat.</summary>
internal sealed class VisusAgentTurns
{
    public Dictionary<string, Action> Watched { get; } = [];

    public void Watch(string thread, Action ended) => Watched[thread] = ended;
}

/// <summary>Visus, its fake chats, the hub, the cards, Background tasks and the binding a W11 test drives.</summary>
internal sealed record VisusAgentBound(VisusRig Visus, VisusAgentChatsFake Chats, WebAgentHub Hub, VisusAgentAsks Asks, PublishedJobSource Jobs, VisusAgentTurns Turns,
    VisusAgentBinding Binding) : IDisposable
{
    public void Dispose()
    {
        Binding.Dispose();
        Visus.Dispose();
    }
}

/// <summary>Visus with no tabs: a search reports its step, then finds no tab to open.</summary>
internal sealed class NoWebTabs : IWebAgentHost
{
    public IReadOnlyList<IWebAgentTab> Tabs(string thread) => [];

    public Task<IWebAgentTab?> OpenAgentTab(string thread, string url, CancellationToken cancellationToken) => Task.FromResult<IWebAgentTab?>(null);

    public Task<T> OnUi<T>(Func<Task<T>> work) => work();
}
