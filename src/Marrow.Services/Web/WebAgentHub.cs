// PORT-ONLY: web surfaces W11, the web tools' shared controls (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "The pill:
// 'Using Visus' with the step line, Pause and Stop. Stop is the Tactus chord ... it ends every web step at once"; "Pause: the
// model's queue holds"; "User takes over"; ED-NEW-web-w11-1). No Swift counterpart. One hub per process: the bound Visus seams,
// one engine per chat, and the controls every chat shares with Tactus. Stop goes through Tactus's run control (its chord, its
// pill, the chat's Stop) when Tactus is bound, else through the hub's own; Pause is the Tactus pause of the app "visus" when bound
// (so the pill's Pause and Pause all cover Visus), else the hub's own. The pill reads the activity feed.
using System.Collections.Concurrent;
using Marrow.Core.Port;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Web;

/// <summary>What the pill shows for the web tools of one chat.</summary>
public enum WebAgentPhase
{
    /// <summary>A step runs ("Reading the page").</summary>
    Working,

    /// <summary>A card waits for the user ("nth needs your OK").</summary>
    Asking,

    /// <summary>The user paused Visus ("Paused", "The page stays as it is").</summary>
    Paused,

    /// <summary>The user clicked or typed in the driven tab ("You took over Visus", "Resume when you are done").</summary>
    TookOver,

    /// <summary>The user stopped the web steps ("Stopped").</summary>
    Stopped,

    /// <summary>The turn ended; nothing runs.</summary>
    Idle,
}

/// <summary>One change of a chat's web activity: its phase, the step line, and the tab it is in.</summary>
public sealed record WebAgentActivity(string Thread, WebAgentPhase Phase, string Step, string? Tab = null)
{
    /// <summary>The Visus tab's own id (<see cref="IWebAgentTab.Id"/>), when the step is in one.</summary>
    public string? TabId { get; init; }
}

/// <summary>Who calls a web tool: the chat, the model (for the cards) and whether it sees images.</summary>
public sealed record WebAgentCaller(string Thread, string? Model, bool Vision);

public sealed class WebAgentHub
{
    /// <summary>The app key of Visus in the Tactus run and pause controls (and the pill's row).</summary>
    public const string App = "visus";

    /// <summary>The process's hub; tests make their own.</summary>
    public static Ambient<WebAgentHub> Shared { get; } = new(new Func<WebAgentHub>(() => new WebAgentHub()));

    private readonly ConcurrentDictionary<string, WebAgentEngine> engines = new(StringComparer.Ordinal);
    private readonly PcRunControl ownRun = new();
    private readonly Func<PcRunControl?> tactusRun;
    private readonly Func<PcPauseControl?> tactusPause;
    private readonly Func<string?> stopChord;
    private readonly Lock gate = new();
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PcPauseControl? watchedPause;
    private bool paused;

    /// <summary>
    /// A hub over Tactus's controls when they are bound (<paramref name="run"/>, <paramref name="pause"/>; by default those of the
    /// bound Tactus parts), on <paramref name="clock"/>.
    /// </summary>
    public WebAgentHub(Func<PcRunControl?>? run = null, Func<PcPauseControl?>? pause = null, IPcClock? clock = null, Func<string?>? stopChord = null)
    {
        tactusRun = run ?? (() => PcControlBridge.Parts.Current()?.Control);
        tactusPause = pause ?? (() => PcControlBridge.Parts.Current()?.Pause);
        this.stopChord = stopChord ?? (() => PcControlBridge.Parts.Current()?.Settings().StopChord);
        Clock = clock ?? new SystemPcClock();
    }

    public IPcClock Clock { get; }

    public IWebAgentHost? Host { get; private set; }

    public IWebAgentAsker? Asker { get; private set; }

    public WebAgentGrants Grants { get; private set; } = new();

    public Func<WebAgentSettings> Settings { get; private set; } = () => WebAgentSettings.Default;

    /// <summary>A pause, resume or take-over changed.</summary>
    public event Action? Changed;

    /// <summary>The pill's feed: a chat's web phase and step line.</summary>
    public event Action<WebAgentActivity>? Activity;

    /// <summary>The app binds Visus (its tabs, its cards, the grants file and Settings › Visus).</summary>
    public void Bind(IWebAgentHost host, IWebAgentAsker asker, WebAgentGrants? grants = null, Func<WebAgentSettings>? settings = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Asker = asker ?? throw new ArgumentNullException(nameof(asker));
        if (grants is not null) Grants = grants;
        if (settings is not null) Settings = settings;
    }

    /// <summary>Whether the web tools are offered: Visus is bound and "Let models use Visus" is on.</summary>
    public bool Offered => Host is not null && Asker is not null && Settings().Enabled;

    /// <summary>The run control the web steps answer to: Tactus's when bound (its chord and pill stop Visus too), else the hub's.</summary>
    public PcRunControl Run => tactusRun() ?? ownRun;

    /// <summary>Tactus's stop chord while Tactus is bound ("Ctrl+Alt+Esc"); null without Tactus, which has no chord then.</summary>
    public string? StopChord => tactusRun() is null ? null : stopChord() is { Length: > 0 } chord ? chord : null;

    /// <summary>Tactus's pause control when bound.</summary>
    public PcPauseControl? TactusPause => tactusPause();

    /// <summary>The chat's engine, made on first use; null while Visus is unbound.</summary>
    public WebAgentEngine? Engine(string thread)
    {
        ArgumentException.ThrowIfNullOrEmpty(thread);
        if (Host is null || Asker is null) return null;
        return engines.GetOrAdd(thread, t => new WebAgentEngine(this, t));
    }

    /// <summary>The engines made so far (for the pill and tests).</summary>
    public IReadOnlyCollection<WebAgentEngine> Engines => [.. engines.Values];

    /// <summary>Whether the user took over a tab a model drives, in any chat ("You took over Visus" with Resume).</summary>
    public bool AnyTookOver => engines.Values.Any(e => e.AnyTookOver);

    /// <summary>Whether Visus is paused now (the hub's own pause or Tactus's pause of "visus").</summary>
    public bool IsPaused
    {
        get
        {
            WatchPause();
            lock (gate)
                if (paused) return true;
            return TactusPause?.IsPaused(App) == true;
        }
    }

    /// <summary>The pill's Pause: the web steps hold at their next boundary; the page stays as it is.</summary>
    public void Pause()
    {
        if (TactusPause is { } pause)
        {
            pause.Pause(PcScope.App(App), PcPauseSource.Pill);
            return;
        }
        lock (gate) paused = true;
        RaiseChanged();
    }

    /// <summary>The pill's Resume: paused steps go on, and every tab the user took over is handed back.</summary>
    public void Resume()
    {
        if (TactusPause is { } pause) pause.Resume(PcScope.App(App), PcPauseSource.Pill);
        lock (gate) paused = false;
        foreach (var engine in engines.Values) engine.HandBack();
        RaiseChanged();
    }

    /// <summary>The pill's Stop and the chord: every web step of every chat ends at once.</summary>
    public void Stop(PcStopSource source = PcStopSource.Pill) => Run.Stop(PcScope.App(App), source);

    /// <summary>The chat's Stop: its steps end and none starts again this turn.</summary>
    public void StopTurn(string thread)
    {
        if (engines.TryGetValue(thread, out var engine)) engine.StopTurn(PcStopSource.Chat);
    }

    /// <summary>The chat's turn ended: its asks are withdrawn, the overlay leaves its tabs, and the next turn may use Visus again.</summary>
    public void EndTurn(string thread)
    {
        if (engines.TryGetValue(thread, out var engine)) engine.EndTurn();
    }

    /// <summary>The chat closed: its engine goes.</summary>
    public void Forget(string thread)
    {
        if (engines.TryRemove(thread, out var engine)) engine.Dispose();
    }

    /// <summary>Completes at the next pause, resume or take-over change.</summary>
    internal Task NextChange()
    {
        WatchPause();
        lock (gate) return changed.Task;
    }

    internal void Report(WebAgentActivity activity) => Activity?.Invoke(activity);

    internal void RaiseChanged()
    {
        TaskCompletionSource done;
        lock (gate)
        {
            done = changed;
            changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        done.TrySetResult();
        Changed?.Invoke();
    }

    // Tactus's pause changes reach the web steps' holds, and its Resume of Visus (the pill's row, Resume all, the chord, the
    // tray) hands back the tabs the user took over, as the hub's own Resume does.
    private void WatchPause()
    {
        var pause = TactusPause;
        lock (gate)
        {
            if (ReferenceEquals(pause, watchedPause)) return;
            if (watchedPause is not null)
            {
                watchedPause.Changed -= RaiseChanged;
                watchedPause.AppResumed -= OnTactusResumed;
            }
            watchedPause = pause;
            if (pause is not null)
            {
                pause.Changed += RaiseChanged;
                pause.AppResumed += OnTactusResumed;
            }
        }
    }

    private void OnTactusResumed(string app, PcPauseSource source)
    {
        if (!string.Equals(app, App, StringComparison.Ordinal)) return;
        foreach (var engine in engines.Values) engine.HandBack();
        RaiseChanged();
    }

    /// <summary>
    /// Resume from a Tactus control while Visus is not paused there (the pill's Resume on "You took over Visus"): the tabs the
    /// user took over go back to the model.
    /// </summary>
    public void HandBack()
    {
        foreach (var engine in engines.Values) engine.HandBack();
        RaiseChanged();
    }
}
