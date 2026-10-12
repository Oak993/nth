using System.ComponentModel;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W11, the web tools bound in the app (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "Tools (web_*),
// exposed on the nth-pc MCP server for CLI providers and as native tools for API providers ... listed when Settings › Visus ›
// 'Let models use Visus' is on (default on), independent of Tactus's own switch", "Pause", "Stop", "Log: ... the full log is
// in Background tasks", 2.3 "Let the model use this tab"; 3.10 the Background tasks jobs; ED-NEW-web-w11-1). No Swift
// counterpart. Program.Main's one Bind line: the hub gets Visus (VisusAgentHost), the cards (VisusAgentAsks), the standing
// grants in the web data folder and the settings; a chat's turn end lets its next turn use Visus again and the chat's Stop
// ends its web steps; an ask opens the chat's Visus pane when it is closed; each chat's web session is a Background tasks job
// with Pause and Stop (with Tactus off this and the chat are Visus's only controls; with Tactus on the pill covers it as the
// app "visus"); and the ⋮ menu shares a tab with the model.

namespace Marrow.App.Views.Browser;

public static class VisusAgentStartup
{
    /// <summary>The standing grants' file in Visus's data folder.</summary>
    public const string GrantsFile = "agent-grants.json";

    private static VisusAgentBinding? bound;

    /// <summary>Program.Main: binds the web tools to Visus once.</summary>
    public static void Bind()
    {
        if (bound is not null) return;
        var grants = new WebAgentGrants(VisusHost.DataFolder is { } folder ? Path.Combine(folder, GrantsFile) : null);
        bound = new VisusAgentBinding(new RuntimeVisusChats(), WebAgentHub.Shared.Current, VisusAgentAsks.Shared,
            () => Marrow.App.Window.WindowManager.Shared.Model.PublishedJobs, grants, watchTurnEnd: WatchRuntimeTurnEnd);
        ThreadRuntime.TactusInterrupted += bound.ChatStopped;
    }

    /// <summary>The settings Visus's web tools read (Settings › Visus's own page adds the switches; until then the defaults).</summary>
    public static WebAgentSettings Settings() => WebAgentSettings.Default with
    {
        ReduceMotion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion,
    };

    // The turn ends when the chat's runtime goes idle (the Tactus composition watches its own chats the same way).
    private static void WatchRuntimeTurnEnd(string thread, Action ended)
    {
        if (!Guid.TryParse(thread, out var id)) return;
        MainActor.Post(() =>
        {
            if (Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(id) is not { } runtime)
            {
                ended();
                return;
            }
            void Changed(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName != nameof(runtime.Phase) || runtime.Phase != RuntimePhase.Idle) return;
                runtime.PropertyChanged -= Changed;
                ended();
            }
            runtime.PropertyChanged += Changed;
            if (runtime.Phase == RuntimePhase.Idle) Changed(runtime, new PropertyChangedEventArgs(nameof(runtime.Phase))); // it ended meanwhile
        });
    }
}

/// <summary>The hub bound to the app's Visus, with the turn, the chat's Stop, the ask's pane and the Background tasks jobs.</summary>
public sealed class VisusAgentBinding : IDisposable
{
    /// <summary>A Visus job's id: this prefix and the chat's thread id.</summary>
    public const string JobPrefix = "visus:";

    private readonly IVisusAgentChats chats;
    private readonly WebAgentHub hub;
    private readonly VisusAgentAsks asks;
    private readonly Func<PublishedJobSource?> jobs;
    private readonly Action<string, Action> watchTurnEnd;
    private readonly Func<DateTimeOffset> now;
    private readonly Lock gate = new();
    private readonly HashSet<string> watching = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Session> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<VisusSession, VisusTab?, IEnumerable<VisusMenuEntry>> menu;

    public VisusAgentBinding(IVisusAgentChats chats, WebAgentHub hub, VisusAgentAsks asks, Func<PublishedJobSource?> jobs, WebAgentGrants? grants = null,
        Func<WebAgentSettings>? settings = null, Action<string, Action>? watchTurnEnd = null, Func<DateTimeOffset>? now = null)
    {
        this.chats = chats;
        this.hub = hub;
        this.asks = asks;
        this.jobs = jobs;
        this.watchTurnEnd = watchTurnEnd ?? ((_, _) => { });
        this.now = now ?? (() => DateTimeOffset.Now);
        Host = new VisusAgentHost(chats);
        hub.Bind(Host, asks, grants, settings ?? VisusAgentStartup.Settings);
        hub.Activity += OnActivity;
        asks.Added += OnAsk;
        menu = ShareEntries;
        VisusToolbar.MenuProviders.Add(menu);
    }

    public VisusAgentHost Host { get; }

    /// <summary>The chat's Stop interrupted its turn: its web steps end, and none starts again until the turn is over.</summary>
    public void ChatStopped(string thread)
    {
        if (!hub.Engines.Any(e => string.Equals(e.Thread, thread, StringComparison.OrdinalIgnoreCase))) return;
        hub.StopTurn(thread);
        WatchTurn(thread);
    }

    /// <summary>"Let the model use this tab" / "Stop sharing with the model" (DESIGN 2.3), for a browse or preview tab.</summary>
    public IEnumerable<VisusMenuEntry> ShareEntries(VisusSession session, VisusTab? tab)
    {
        if (tab is not { Kind: VisusTabKind.Browse or VisusTabKind.Preview } || !hub.Settings().Enabled) yield break;
        var share = !tab.SharedWithModel;
        yield return new VisusMenuEntry(share ? "Let the model use this tab" : "Stop sharing with the model", () => session.ShareWithModel(tab, share), IsEnabled: share ? !tab.IsBlank : true);
    }

    public void Dispose()
    {
        hub.Activity -= OnActivity;
        asks.Added -= OnAsk;
        VisusToolbar.MenuProviders.Remove(menu);
    }

    // A call worked, asked, held or stopped: its turn's end is watched, and its job says so.
    private void OnActivity(WebAgentActivity activity)
    {
        if (activity.Phase is not (WebAgentPhase.Idle or WebAgentPhase.Stopped)) WatchTurn(activity.Thread);
        MainActor.Post(() => Publish(activity)); // in the order they came, whatever thread reported them
    }

    // An ask the user cannot see waits for nothing: the chat's Visus pane opens (its card hangs there).
    private void OnAsk(VisusAgentAsk ask) => MainActor.Post(() =>
    {
        if (!ask.IsDone && !chats.IsPaneOpen(ask.Thread)) chats.OpenPane(ask.Thread);
    });

    private void WatchTurn(string thread)
    {
        lock (gate)
            if (!watching.Add(thread)) return;
        watchTurnEnd(thread, () =>
        {
            lock (gate) watching.Remove(thread);
            hub.EndTurn(thread);
        });
    }

    private void Publish(WebAgentActivity activity)
    {
        if (jobs() is not { } source) return;
        var at = now();
        BackgroundJob job;
        lock (gate)
        {
            if (!sessions.TryGetValue(activity.Thread, out var session) || (session.Finished && activity.Phase is not (WebAgentPhase.Idle or WebAgentPhase.Stopped)))
                sessions[activity.Thread] = session = new Session(at);
            if (session.Finished) return; // a turn end after a stop keeps "Stopped"
            if (activity.Phase == WebAgentPhase.Working) session.Steps++;
            if (activity.Step.Length > 0) session.Step = activity.Step;
            session.State = activity.Phase switch
            {
                WebAgentPhase.Stopped => BackgroundJobState.Stopped,
                WebAgentPhase.Idle => BackgroundJobState.Done,
                WebAgentPhase.Paused or WebAgentPhase.TookOver => BackgroundJobState.Paused,
                _ => hub.IsPaused ? BackgroundJobState.Paused : BackgroundJobState.Running,
            };
            if (session.Finished) session.FinishedAt = at;
            job = new BackgroundJob
            {
                Id = JobPrefix + activity.Thread,
                Kind = BackgroundJobKind.BrowserSession,
                Title = chats.Title(activity.Thread) ?? "Visus",
                KindLabel = "Visus",
                Subject = "agent tab",
                ThreadId = Guid.TryParse(activity.Thread, out var id) ? id : null,
                TranscriptThreadId = Guid.TryParse(activity.Thread, out var transcript) ? transcript : null,
                State = session.State,
                StartedAt = session.StartedAt,
                FinishedAt = session.FinishedAt,
                ToolUses = session.Steps,
                Activity = session.Finished ? null : session.Step,
                CanPause = !session.Finished,
                CanStop = !session.Finished,
            };
        }
        var thread = activity.Thread;
        source.Publish(job, new BackgroundJobControl(
            Stop: () =>
            {
                hub.StopTurn(thread);
                return true;
            },
            Pause: () =>
            {
                hub.Pause();
                return true;
            },
            Resume: () =>
            {
                hub.Resume();
                return true;
            }));
    }

    private sealed class Session(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;
        public DateTimeOffset? FinishedAt { get; set; }
        public BackgroundJobState State { get; set; } = BackgroundJobState.Running;
        public string Step { get; set; } = "";
        public long Steps { get; set; }
        public bool Finished => State.IsFinished();
    }
}
