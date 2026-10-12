// PORT-ONLY: Tactus, the chat mirror (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.1, 10.2, 10.4 "Resolution", 10.7). What each
// chat hears from Tactus: a row for every pause, stop and answer that reached it, a resume it may continue from, and its
// strip's state. A Pause or Resume reaches each chat that used one of its apps; a stop reaches each chat its scope covers
// that had Tactus in use (its apps of this turn, or kept paused); an answer reaches the chat that asked. The chat's own Stop
// right after a stop that already reached it adds no second row (the pill's Stop interrupts the turns too). The words come
// from PcChatMirror; the chat runtime writes the rows into its timeline and decides the resume.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public enum PcChatRowKind
{
    Paused,
    Stopped,
    Answered,
}

/// <summary>A row for one chat's timeline.</summary>
public sealed record PcChatRow(string Thread, PcChatRowKind Kind, string Text);

/// <summary>A resume that reached a chat; <see cref="Row"/> is its row while the turn runs ("Resumed (from the pill)").</summary>
public sealed record PcChatResume(string Thread, IReadOnlyList<string> Apps, PcPauseSource Source, string Row, string Chip, string Message);

public enum PcStripAppState
{
    Working,
    Pausing,
    Paused,
}

/// <summary>One app chip of the strip: its icon and name, its state dot, and its x.</summary>
public sealed record PcStripApp(string Key, string Name, PcStripAppState State);

/// <summary>The chat's Tactus strip (DESIGN.md 10.7): its line, its app chips, Pause or Resume, and the asks badge.</summary>
public sealed record PcChatStrip(string Line, IReadOnlyList<PcStripApp> Apps, int Asks)
{
    /// <summary>Every app is paused or pausing: the strip's button reads Resume.</summary>
    public bool Paused => Apps.Count > 0 && Apps.All(a => a.State != PcStripAppState.Working);

    public string Action => Paused ? "Resume" : "Pause";
}

public sealed class PcChatFeed : IDisposable
{
    /// <summary>A chat's own Stop within this time after a stop row reached it writes no second row.</summary>
    public const long StopEchoMs = 2_000;

    private readonly Lock gate = new();
    private readonly PcPauseControl pause;
    private readonly PcStopControl stop;
    private readonly PcAskBroker asks;
    private readonly IPcClock clock;
    private readonly Func<string, string> name;
    private readonly Func<string?> pauseChord;
    private readonly Func<string?> stopChord;
    private readonly Dictionary<string, long> stopped = new(StringComparer.Ordinal);

    public PcChatFeed(PcPauseControl pause, PcStopControl stop, PcAskBroker asks, IPcClock clock, Func<string, string>? name = null,
        Func<string?>? pauseChord = null, Func<string?>? stopChord = null)
    {
        this.pause = pause;
        this.stop = stop;
        this.asks = asks;
        this.clock = clock;
        this.name = name ?? PcChatMirror.NameOf;
        this.pauseChord = pauseChord ?? (() => null);
        this.stopChord = stopChord ?? (() => null);
        pause.Moved += OnMoved;
        pause.Changed += OnChanged;
        stop.StoppedIn += OnStopped;
        asks.Added += OnAsk;
        asks.Resolved += OnResolved;
        asks.Withdrawn += OnWithdrawn;
    }

    /// <summary>A row for a chat's timeline (raised on the thread that made the change).</summary>
    public event Action<PcChatRow>? Row;

    /// <summary>The user resumed apps a chat used: the chat continues, says "Ask to continue", or shows the row.</summary>
    public event Action<PcChatResume>? Resumed;

    /// <summary>Anything a strip shows may have changed.</summary>
    public event Action? Changed;

    /// <summary>The chat's strip, or null when it has no working or paused stream; its badge counts the Tactus asks (a provider's own keep their chat UI).</summary>
    public PcChatStrip? Strip(string thread)
    {
        if (!pause.Chats(PcScope.Thread(thread)).TryGetValue(thread, out var used) || used.Count == 0) return null;
        var apps = used.Select(app => new PcStripApp(app, name(app), State(app))).ToList();
        return new PcChatStrip(PcChatMirror.StripLine(apps), apps, asks.Waiting(thread).Count(a => a.Kind is not (PcAskKind.ProviderApproval or PcAskKind.ProviderQuestion)));
    }

    /// <summary>The strip's Pause: that chat's apps.</summary>
    public void Pause(string thread) => pause.Pause(PcScope.Thread(thread), PcPauseSource.Chat);

    /// <summary>The strip's Resume: that chat's apps.</summary>
    public void Resume(string thread) => pause.Resume(PcScope.Thread(thread), PcPauseSource.Chat);

    /// <summary>A chip's x: that app, for every chat (stop path 7).</summary>
    public void StopApp(string app) => stop.StripRow(app);

    /// <summary>The chat's Stop (stop path 6).</summary>
    public void ChatStop(string thread) => stop.ChatStop(thread);

    public void Dispose()
    {
        pause.Moved -= OnMoved;
        pause.Changed -= OnChanged;
        stop.StoppedIn -= OnStopped;
        asks.Added -= OnAsk;
        asks.Resolved -= OnResolved;
        asks.Withdrawn -= OnWithdrawn;
    }

    private PcStripAppState State(string app) => pause.Status(app) switch
    {
        { Phase: PcPausePhase.Pausing } => PcStripAppState.Pausing,
        { } => PcStripAppState.Paused,
        null => PcStripAppState.Working,
    };

    private void OnMoved(PcPauseMove move)
    {
        foreach (var (thread, apps) in move.Chats)
        {
            var names = apps.Select(name).ToList();
            if (move.Paused)
            {
                Row?.Invoke(new PcChatRow(thread, PcChatRowKind.Paused, PcChatMirror.PausedRow(PcChatMirror.And(names), move.Source, pauseChord())));
                continue;
            }
            Resumed?.Invoke(new PcChatResume(thread, apps, move.Source, PcChatMirror.ResumedRow(move.Source, pauseChord()),
                PcChatMirror.ResumedChip(move.Source, pauseChord()), PcChatMirror.ContinueMessage(PcChatMirror.And(names))));
        }
    }

    private void OnStopped(PcStopPath path, PcScope scope)
    {
        var now = clock.NowMs;
        var rows = new List<PcChatRow>();
        var named = scope.Kind == PcScopeKind.App ? name(scope.Key!) : null;
        lock (gate)
        {
            foreach (var (thread, apps) in pause.Chats(scope))
            {
                if (apps.Count == 0) continue;
                // the pill's Stop interrupts the turn, and that turn's Stop comes back here: one row is enough
                if (path == PcStopPath.ChatStop && stopped.TryGetValue(thread, out var at) && now - at < StopEchoMs) continue;
                stopped[thread] = now;
                rows.Add(new PcChatRow(thread, PcChatRowKind.Stopped, PcChatMirror.StoppedRow(path, stopChord(), named)));
            }
        }
        foreach (var row in rows) Row?.Invoke(row);
    }

    private void OnResolved(PcPendingAsk ask, object answer, PcAnswerSource source)
    {
        if (PcChatMirror.AnswerRow(ask, answer, source) is { } text) Row?.Invoke(new PcChatRow(ask.Thread, PcChatRowKind.Answered, text));
        Changed?.Invoke();
    }

    private void OnWithdrawn(PcPendingAsk ask, PcWithdrawal reason)
    {
        if (reason == PcWithdrawal.Dismissed) Row?.Invoke(new PcChatRow(ask.Thread, PcChatRowKind.Answered, PcChatMirror.DismissedRow));
        Changed?.Invoke();
    }

    private void OnAsk(PcPendingAsk ask) => Changed?.Invoke();

    private void OnChanged() => Changed?.Invoke();
}
