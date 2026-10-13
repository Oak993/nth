// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.2 "The morning digest is pinned at the top as the day's first card", 5.2, 6; canvas "Morning digest"; unit AS-11; ED-NEW-dextra-as11-11).
// No Swift twin. What the digest card shows and does, apart from how it draws: the newest digest, its Suggestions lane as the ledger holds it now (a suggestion
// the brain adds later shows too, one the owner answers turns into the line that took its place), Accept and Dismiss, what a tap on an item opens, and Read aloud
// (offered only when the voice has bound itself: nothing speaks unprompted, and there is no voice yet to ask). Changed is raised on the main thread.
using Avalonia.Threading;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant.Views;

/// <summary>One row of the lane: the suggestion, how it stands, and the line that replaced it once answered (or why Accept did not work).</summary>
public sealed record SuggestionRow(Suggestion Suggestion, string? Answered, string? Problem)
{
    public bool IsOpen => Suggestion.State == SuggestionState.Open && Answered is null;
}

public sealed class DigestModel : IDisposable
{
    private readonly DigestService service;
    private readonly Func<TimeZoneInfo> zone;
    private readonly Func<bool> onMainThread;
    private readonly Action<Action> postToMain;
    private readonly Dictionary<string, string> answered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> problems = new(StringComparer.Ordinal);
    private bool disposed;
    private bool quiet;

    /// <param name="service">The digest service the card shows.</param>
    /// <param name="zone">The PC's time zone; null: the system's.</param>
    /// <param name="onMainThread">Whether the caller is on the main thread; null: Avalonia's UI thread. A test gives its own.</param>
    /// <param name="postToMain">Runs work on the main thread later; null: Avalonia's UI thread.</param>
    public DigestModel(DigestService service, Func<TimeZoneInfo>? zone = null, Func<bool>? onMainThread = null, Action<Action>? postToMain = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.zone = zone ?? (() => TimeZoneInfo.Local);
        this.onMainThread = onMainThread ?? (() => Dispatcher.UIThread.CheckAccess());
        this.postToMain = postToMain ?? (work => Dispatcher.UIThread.Post(work));
        service.Changed += OnServiceChanged;
    }

    private Action<DigestLink>? openLink;
    private Action<string>? readAloud;

    /// <summary>What a tap on an item opens (the card, the mission, the duty, the account, the chat). Null: items are not links, and the card draws them as text.</summary>
    public Action<DigestLink>? OpenLink
    {
        get => openLink;
        set
        {
            openLink = value;
            Raise();
        }
    }

    /// <summary>The voice's: reads the digest's spoken text aloud (local voices only). Null while no voice is bound, and the card shows no Read aloud.</summary>
    public Action<string>? ReadAloud
    {
        get => readAloud;
        set
        {
            readAloud = value;
            Raise();
        }
    }

    /// <summary>The newest digest, or null before the first.</summary>
    public Digest? Digest => service.Latest;

    public bool CanReadAloud => ReadAloud is not null && Digest is { Spoken.Length: > 0 };

    /// <summary>The digest of today, as the lane below it is live; an older digest's lane is the record of what it offered, without buttons.</summary>
    public bool IsToday => Digest is { } digest && DayOf(digest.At) == service.Book.Ledger.Day;

    /// <summary>The lane: today's suggestions as the ledger holds them (for today's digest), or what an older digest offered.</summary>
    public IReadOnlyList<SuggestionRow> Lane
    {
        get
        {
            if (Digest is not { } digest) return [];
            var ledger = service.Book.Ledger;
            var live = IsToday ? ledger.Today : new Marrow.Core.Port.ValueList<Suggestion>(digest.Suggestions);
            return [.. live.Select(s => new SuggestionRow(s, AnsweredLine(s, ledger), problems.GetValueOrDefault(s.Key)))];
        }
    }

    /// <summary>"Suggestions · 2 of at most 3 today", from the ledger for today's digest; empty when the lane has nothing.</summary>
    public string LaneTitle => Lane.Count == 0 ? "" : IsToday ? SuggestionWords.LaneTitle(service.Book.Ledger.OfferedToday) : Digest!.LaneTitle;

    public event Action? Changed;

    private DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone()).DateTime);

    private string? AnsweredLine(Suggestion suggestion, SuggestionLedger ledger)
    {
        if (suggestion.State == SuggestionState.Open) return answered.GetValueOrDefault(suggestion.Key);
        return answered.TryGetValue(suggestion.Key, out var said) ? said
            : suggestion.State == SuggestionState.Accepted ? "Accepted." : SuggestionWords.Dismissed(ledger.IsOff(suggestion.Kind));
    }

    /// <summary>Accept: the service acts; the row becomes the line it returned, or keeps its buttons with the reason Accept did not work.</summary>
    public void Accept(string key)
    {
        problems.Remove(key);
        // The service says the ledger changed while it acts; the row is drawn once, when this has what to say in its place.
        quiet = true;
        SuggestionAccept result;
        try { result = service.Accept(key); }
        finally { quiet = false; }
        if (result.Ok) answered[key] = result.Message;
        else problems[key] = result.Message;
        Raise();
    }

    public void Dismiss(string key)
    {
        problems.Remove(key);
        quiet = true;
        string? said;
        try { said = service.Dismiss(key); }
        finally { quiet = false; }
        if (said is not null) answered[key] = said;
        Raise();
    }

    /// <summary>The owner taps an item: what it leads to opens.</summary>
    public void Open(DigestLink link)
    {
        if (link.Kind != DigestLinkKind.None) OpenLink?.Invoke(link);
    }

    public void Read()
    {
        if (CanReadAloud) ReadAloud!(Digest!.Spoken);
    }

    // The service raises Changed on whichever thread changed it (a digest composed on the pool, a tap on the main thread); the card draws on the main thread.
    private void OnServiceChanged()
    {
        if (onMainThread()) Raise();
        else postToMain(Raise);
    }

    private void Raise()
    {
        if (!disposed && !quiet) Changed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        service.Changed -= OnServiceChanged;
    }
}
