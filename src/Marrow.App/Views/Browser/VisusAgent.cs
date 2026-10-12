using System.Runtime.CompilerServices;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W11, Visus for the web tools (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "Model access (in the
// same safety model as Tactus)", 2.3 "Let the model use this tab" and agent tabs, 2.7 a crash during a model step;
// ED-NEW-web-w11-1). No Swift counterpart. The app's side of the seams WebAgentEngine drives: a chat's tabs the model may use
// (its agent tabs and the one tab the user shared, never a private tab), opening an agent tab (the pane opens when it is
// closed; the new tab is chosen only when the user's own tab would not be taken from them), the UI thread, the search engine,
// the addresses the user wrote in the chat, and the cards' names. The asks wait in VisusAgentAsks until the pane's card
// answers them. Tests replace the chats (IVisusAgentChats) and keep everything in memory.

namespace Marrow.App.Views.Browser;

/// <summary>The chats as the web tools see them: a chat's Visus session, its pane, its title and what the user wrote.</summary>
public interface IVisusAgentChats
{
    /// <summary>The chat's Visus session, or null when the chat has no runtime (it was closed).</summary>
    VisusSession? Session(string thread);

    bool IsPaneOpen(string thread);

    /// <summary>Opens the chat's Visus pane (nothing when it is open).</summary>
    void OpenPane(string thread);

    /// <summary>The chat's title, or null while it has none.</summary>
    string? Title(string thread);

    /// <summary>The messages the user sent in the chat.</summary>
    IReadOnlyList<string> UserMessages(string thread);

    /// <summary>The model's name as the cards show it, from its id; null when no provider knows the id.</summary>
    string? ModelName(string model) => null;
}

/// <summary>The app's chats: each thread's runtime, looked up on the UI thread.</summary>
public sealed class RuntimeVisusChats : IVisusAgentChats
{
    public VisusSession? Session(string thread) => Runtime(thread) is { } runtime ? VisusHost.SessionFor(runtime) : null;

    public bool IsPaneOpen(string thread) => Runtime(thread)?.Workspace.IsOpen(PaneKind.Browser) == true;

    public void OpenPane(string thread)
    {
        if (Runtime(thread) is not { } runtime || runtime.Workspace.IsOpen(PaneKind.Browser)) return;
        runtime.Workspace = runtime.Workspace.Open(PaneKind.Browser);
    }

    public string? Title(string thread) =>
        Runtime(thread)?.Thread?.Title is { Length: > 0 } title && title != ChatThread.Untitled ? title : null;

    public IReadOnlyList<string> UserMessages(string thread) => Runtime(thread)?.SentPrompts is { } sent ? [.. sent] : [];

    public string? ModelName(string model)
    {
        try
        {
            var providers = Marrow.App.Window.WindowManager.Shared.Model.Providers;
            foreach (var kind in Enum.GetValues<ProviderKind>())
                if (providers.Model(model, kind) is { } option) return option.ChipName;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
        }
        return null;
    }

    private static ThreadRuntime? Runtime(string thread) =>
        Guid.TryParse(thread, out var id) ? Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(id) : null;
}

/// <summary>Visus for the web tools (IWebAgentHost over the chats' sessions).</summary>
public sealed class VisusAgentHost(IVisusAgentChats chats) : IWebAgentHost
{
    private static readonly ConditionalWeakTable<VisusTab, VisusAgentTab> Adapters = new();

    public IVisusAgentChats Chats { get; } = chats;

    public IReadOnlyList<IWebAgentTab> Tabs(string thread)
    {
        if (Chats.Session(thread) is not { } session) return [];
        return [.. session.Tabs.Where(MayUse).Select(tab => (IWebAgentTab)Adapter(session, tab))];
    }

    /// <summary>
    /// A new agent tab after the chosen one. It is chosen when the pane was closed, or when the chosen tab is blank or is the
    /// model's own: a tab the user is reading stays in front.
    /// </summary>
    public Task<IWebAgentTab?> OpenAgentTab(string thread, string url, CancellationToken cancellationToken) => OnUi<IWebAgentTab?>(() =>
    {
        if (cancellationToken.IsCancellationRequested || Chats.Session(thread) is not { } session) return Task.FromResult<IWebAgentTab?>(null);
        var open = Chats.IsPaneOpen(thread);
        var front = session.Selected;
        var choose = !open || front is null || front.IsBlank || front.Kind == VisusTabKind.Agent;
        var tab = session.NewTab(VisusTabKind.Agent, url, choose);
        if (!open) Chats.OpenPane(thread);
        return Task.FromResult<IWebAgentTab?>(Adapter(session, tab));
    });

    public Task<T> OnUi<T>(Func<Task<T>> work) => MainActor.IsIsolated ? work() : MainActor.Invoke(work);

    public string SearchUrl(string query) => SearchEngines.QueryUrl(VisusHost.SearchEngine(), query);

    /// <summary>Only what the user wrote counts (ED-NEW-web-w11-1): an address in the model's own answer stays unknown.</summary>
    public bool InConversation(string thread, string url)
    {
        var wanted = WebAgentUrls.Normalize(url);
        foreach (var message in Chats.UserMessages(thread))
            foreach (var address in WebAgentUrls.FindAddresses(message))
                if (string.Equals(WebAgentUrls.Normalize(address), wanted, StringComparison.Ordinal)) return true;
        return false;
    }

    public string ModelName(string? model) =>
        string.IsNullOrWhiteSpace(model) ? "The model" : Chats.ModelName(model) is { Length: > 0 } name ? name : model;

    public string? ChatTitle(string thread) => Chats.Title(thread);

    /// <summary>The model may use the chat's agent tabs and the tab the user shared, never a private one.</summary>
    public static bool MayUse(VisusTab tab) =>
        tab.Kind == VisusTabKind.Agent || (tab.SharedWithModel && tab.Kind is VisusTabKind.Browse or VisusTabKind.Preview);

    /// <summary>The one adapter of a tab (the engine's hooks stay on it).</summary>
    public static VisusAgentTab Adapter(VisusSession session, VisusTab tab) => Adapters.GetValue(tab, t => new VisusAgentTab(session, t));
}

/// <summary>One Visus tab as the web tools drive it.</summary>
public sealed class VisusAgentTab : IWebAgentTab
{
    /// <summary>How long a step waits for the tab's page to come live (a restored or discarded tab, a new one).</summary>
    public static TimeSpan LiveTimeout { get; set; } = TimeSpan.FromSeconds(15);

    private readonly VisusSession session;
    private Action? userInput;
    private Action<string>? stepFailed;
    private WebSurface? watchedSurface;

    internal VisusAgentTab(VisusSession session, VisusTab tab)
    {
        this.session = session;
        Tab = tab;
    }

    public VisusTab Tab { get; }

    public string Id => Tab.Id;

    public VisusTabKind Kind => Tab.Kind;

    public bool Shared => Tab.SharedWithModel;

    public string Url => Tab.Url;

    public string Title => Tab.Title;

    public IWebEngineView? View => Tab.View?.Engine;

    public CdpBroker? Cdp => Tab.View?.Cdp;

    public bool IsClosed => !session.Tabs.Contains(Tab) || (Tab.Kind != VisusTabKind.Agent && !Tab.SharedWithModel);

    public bool Loading => Tab.IsLoading;

    public async Task<bool> MakeLiveAsync(CancellationToken cancellationToken)
    {
        if (IsClosed || EnsureView() is not { } view) return false;
        if (IsLive(view)) return true;
        if (view.Engine is null || view.Liveness is WebLiveness.Suspended or WebLiveness.Discarded) view.Activate();
        if (IsLive(view)) return true;
        var live = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check()
        {
            if (IsLive(view)) live.TrySetResult(true);
        }
        view.Changed += Check;
        try
        {
            Check();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(LiveTimeout);
            await using (limit.Token.Register(() => live.TrySetResult(false)))
                return await live.Task.ConfigureAwait(true) && !IsClosed;
        }
        finally
        {
            view.Changed -= Check;
        }
    }

    public void Navigate(string url) => Tab.Open(url);

    public void GoBack() => Tab.GoBack();

    public void GoForward() => Tab.GoForward();

    public void Reload() => Tab.Reload();

    public IDisposable BeginModelStep() => EnsureView()?.BeginModelStep() ?? Nothing.Instance;

    /// <summary>The user acted on the tab from Visus's chrome (an address, Back, Forward, Reload); the page's own input arrives through the overlay's binding.</summary>
    public event Action? UserInput
    {
        add
        {
            if (userInput is null) session.UserActed += OnActed;
            userInput += value;
        }
        remove
        {
            userInput -= value;
            if (userInput is null) session.UserActed -= OnActed;
        }
    }

    public event Action<string>? StepFailed
    {
        add
        {
            if (stepFailed is null && VisusHost.IsStarted)
            {
                watchedSurface = VisusHost.Surface;
                watchedSurface.StepFailed += OnStepFailed;
            }
            stepFailed += value;
        }
        remove
        {
            stepFailed -= value;
            if (stepFailed is not null || watchedSurface is null) return;
            watchedSurface.StepFailed -= OnStepFailed;
            watchedSurface = null;
        }
    }

    // A tab with an address and no view yet (restored, reopened) gets its view; a blank one has nothing to drive.
    private WebSurfaceView? EnsureView()
    {
        if (Tab.View is null && !Tab.IsBlank) Tab.Reopen();
        return Tab.View;
    }

    private static bool IsLive(WebSurfaceView view) => view.Liveness == WebLiveness.Live && view.Engine is { IsAttached: true } && view.Cdp is not null;

    private void OnActed(VisusTab tab)
    {
        if (ReferenceEquals(tab, Tab)) userInput?.Invoke();
    }

    private void OnStepFailed(WebSurfaceView view, string reason)
    {
        if (ReferenceEquals(view, Tab.View)) stepFailed?.Invoke(reason);
    }

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>What the user chose on an agent card.</summary>
public enum VisusAgentChoice
{
    /// <summary>Don't allow, Cancel, Esc, or the card went away.</summary>
    Deny,

    /// <summary>Allow once, or the guarded action's own button.</summary>
    Once,

    /// <summary>Always allow, Always for this site pair.</summary>
    Always,
}

public enum VisusAgentAskKind
{
    Site,
    Navigate,
    Guard,
    Eval,
}

/// <summary>One card waiting for the user.</summary>
public sealed class VisusAgentAsk
{
    private readonly Action<VisusAgentChoice> answer;
    private int done;

    internal VisusAgentAsk(VisusAgentAskKind kind, string thread, string? tabId, object request, Action<VisusAgentChoice> answer)
    {
        Kind = kind;
        Thread = thread;
        TabId = tabId;
        Request = request;
        this.answer = answer;
    }

    public VisusAgentAskKind Kind { get; }

    public string Thread { get; }

    /// <summary>The Visus tab the card belongs to; null for an address the model wants to open in a new tab.</summary>
    public string? TabId { get; }

    /// <summary>The engine's ask: a WebSiteAsk, WebNavigateAsk, WebGuardAsk or WebEvalAsk.</summary>
    public object Request { get; }

    public bool IsDone => Volatile.Read(ref done) != 0;

    /// <summary>The card's answer; the first one wins.</summary>
    public void Answer(VisusAgentChoice choice)
    {
        if (Interlocked.Exchange(ref done, 1) != 0) return;
        answer(choice);
    }

    internal void Withdraw() => Interlocked.Exchange(ref done, 1);
}

/// <summary>
/// The asks of every chat's web tools, oldest first, until their card answers them or their token withdraws them (a stop, the
/// turn's end). Changed and Added are raised on the thread that made the change (the pane posts to the UI thread).
/// </summary>
public sealed class VisusAgentAsks : IWebAgentAsker
{
    /// <summary>The process's asks.</summary>
    public static VisusAgentAsks Shared { get; private set; } = new();

    private readonly Lock gate = new();
    private readonly List<VisusAgentAsk> pending = [];

    /// <summary>The asks changed (one came, was answered or was withdrawn); on any thread.</summary>
    public event Action? Changed;

    /// <summary>An ask came (any thread).</summary>
    public event Action<VisusAgentAsk>? Added;

    public IReadOnlyList<VisusAgentAsk> Pending
    {
        get
        {
            lock (gate) return [.. pending];
        }
    }

    /// <summary>The oldest ask, if any (the pill's click opens its chat).</summary>
    public VisusAgentAsk? Oldest
    {
        get
        {
            lock (gate) return pending.Count > 0 ? pending[0] : null;
        }
    }

    public Task<WebSiteAnswer> AskSite(WebSiteAsk ask, CancellationToken cancellationToken) =>
        Add(VisusAgentAskKind.Site, ask.Thread, ask.TabId, ask, cancellationToken, choice => choice switch
        {
            VisusAgentChoice.Always when ask.AllowAlways => WebSiteAnswer.AlwaysAllow,
            VisusAgentChoice.Always or VisusAgentChoice.Once => WebSiteAnswer.AllowOnce,
            _ => WebSiteAnswer.Deny,
        });

    public Task<WebNavigateAnswer> AskNavigate(WebNavigateAsk ask, CancellationToken cancellationToken) =>
        Add(VisusAgentAskKind.Navigate, ask.Thread, ask.TabId, ask, cancellationToken, choice => choice switch
        {
            VisusAgentChoice.Always => WebNavigateAnswer.AlwaysForPair,
            VisusAgentChoice.Once => WebNavigateAnswer.AllowOnce,
            _ => WebNavigateAnswer.Deny,
        });

    public Task<bool> Confirm(WebGuardAsk ask, CancellationToken cancellationToken) =>
        Add(VisusAgentAskKind.Guard, ask.Thread, ask.TabId, ask, cancellationToken, choice => choice != VisusAgentChoice.Deny);

    public Task<bool> AskEval(WebEvalAsk ask, CancellationToken cancellationToken) =>
        Add(VisusAgentAskKind.Eval, ask.Thread, ask.TabId, ask, cancellationToken, choice => choice != VisusAgentChoice.Deny);

    /// <summary>Tests: a fresh process list.</summary>
    internal static VisusAgentAsks ResetForTests() => Shared = new VisusAgentAsks();

    private Task<T> Add<T>(VisusAgentAskKind kind, string thread, string? tabId, object request, CancellationToken cancellationToken, Func<VisusAgentChoice, T> map)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        VisusAgentAsk item = null!;
        item = new VisusAgentAsk(kind, thread, tabId, request, choice =>
        {
            Remove(item);
            result.TrySetResult(map(choice));
        });
        lock (gate) pending.Add(item);
        var withdrawn = cancellationToken.Register(() =>
        {
            item.Withdraw();
            Remove(item);
            result.TrySetCanceled(cancellationToken);
        });
        _ = result.Task.ContinueWith(_ => withdrawn.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Added?.Invoke(item);
        RaiseChanged();
        return result.Task;
    }

    private void Remove(VisusAgentAsk item)
    {
        bool removed;
        lock (gate) removed = pending.Remove(item);
        if (removed) RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();
}
