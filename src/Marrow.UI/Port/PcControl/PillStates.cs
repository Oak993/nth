// PORT-ONLY: Tactus pill v2, what the pill says (OD-0145, ED-NEW-pcctl-24; DESIGN.md 4.5 "States", 10.1 "Pill and pointer
// while paused", 10.3 "What the capsule shows", 10.4, 10.5 "Text priority", 10.6 "Accessibility"; canvas pc-control-ui
// render10). One pure function from the facts (the apps, their steps and pause phases, the asks, the stop, the transient
// notices, the chords) to the view: the two lines, the live glyph, the mark's state, the button group, whether Stop shows
// and folds its key cap, the badge, the hint row, the per-app rows and the polite announcement. Priority: the card, then
// Stop key taken, Stopped, the input check restarting, the hands-off family, an expired confirm and the other notices, the
// ask, Pause key taken, Pausing or Paused, Waiting while you type, Working. No Avalonia types: the control renders this.
namespace Marrow.UI.Port.PcControl;

/// <summary>Where one app's stream is (DESIGN.md 10.1).</summary>
public enum PillAppPhase
{
    Working,
    Pausing,
    Paused,
    Stopped,
}

/// <summary>
/// One app Tactus works in: its key and display name, its latest step's text ("Filling row 12 of 40"), its pause phase
/// and since when (seconds on the pill's clock), the atomic step a pause waits for ("dragging in Paint"), whether its
/// step waits on an ask, and whether its pointer works where the user cannot see it (ED-NEW-pcctl-21 item 5).
/// </summary>
public sealed record PillApp(string Key, string Name, string Step, PillAppPhase Phase = PillAppPhase.Working, double PhaseSince = 0, string? Busy = null, bool WaitsOnAsk = false, bool Behind = false)
{
    /// <summary>
    /// PORT-ONLY (web surfaces W11, ED-NEW-web-w11-1; DESIGN 5.10 "User takes over"): the user clicked or typed in the tab the
    /// app drives (Visus): the pill says "You took over Visus" with Resume while it still works elsewhere.
    /// </summary>
    public bool TookOver { get; init; }

    /// <summary>PORT-ONLY (web W11): its step waits on a card drawn in its own window (Visus's card), not in the pill: "Check the card".</summary>
    public bool OwnCard { get; init; }

    /// <summary>PORT-ONLY (web W11): the paused pill's line when it is the only app (Visus: "The page stays as it is").</summary>
    public string? PausedLine { get; init; }
}

/// <summary>The kinds of ask the pill carries (DESIGN.md 10.4).</summary>
public enum PillAskKind
{
    Approval,
    Confirm,
    ShowWindow,
    Question,
    Provider,
}

/// <summary>
/// One waiting ask: for a provider's own ask <paramref name="Title"/> is its title ("Run a command"); a withdrawn ask that
/// kept a draft stays until the draft is opened (canvas `q.withdrawn`).
/// </summary>
public sealed record PillAsk(string Id, PillAskKind Kind, string AppName, string Chat, string Model, string Title = "", bool Withdrawn = false)
{
    /// <summary>The Hydra head that asks ("Head 2"); null for the lead chat.</summary>
    public string? Head { get; init; }
}

/// <summary>The transient and blocking notices of DESIGN.md 4.5's table, each with its exact text.</summary>
public enum PillNoticeKind
{
    StopKeyTaken,
    InputRestarting,
    HandsOff,
    ProtectedForeground,
    KeysHeld,
    KeptKeys,
    Done,
    Borrowed,
    Expired,
    OpenedWindow,
    PauseKeyTaken,
}

/// <summary>
/// A notice: its lines, the hint row's sentence when it has one, its buttons, when it began and how long it lasts (0:
/// until it is cleared). Made only through the factories, which hold DESIGN's texts.
/// </summary>
public sealed record PillNotice(PillNoticeKind Kind, string Title, string Line, string? Hint, PillGroupKind Group, double Since, double Lasts, IReadOnlyList<PillHintPart>? HintParts = null)
{
    /// <summary>"Stop key is taken by another app" with Settings (critique C11).</summary>
    public static PillNotice StopKeyTaken(double now) => new(PillNoticeKind.StopKeyTaken, "Stop key is taken by another app", "", null, PillGroupKind.Settings, now, 0);

    /// <summary>"Pause key is taken by another app" with Settings, shown once (DESIGN.md 10.1).</summary>
    public static PillNotice PauseKeyTaken(double now) => new(PillNoticeKind.PauseKeyTaken, "Pause key is taken by another app", "", null, PillGroupKind.Settings, now, 0);

    /// <summary>The hook heartbeat failed (critique C2): DESIGN's one line split at its colon into the two lines.</summary>
    public static PillNotice InputRestarting(double now) => new(PillNoticeKind.InputRestarting, "Real mouse and keyboard off for a moment", "Restarting the input check", null, PillGroupKind.Run, now, 0);

    /// <summary>
    /// "Hands off for a moment": the step on the second line ("Drag the chart in Excel", "+1 more" when other apps wait),
    /// the whole sentence in the hint row ("Tactus needs the mouse for one step: drag the chart in Excel."), with "Esc stops
    /// this step" once the borrow runs, and "Release the mouse first" when Do it now meets a held button.
    /// </summary>
    public static PillNotice HandsOff(string step, string sentence, int more, bool running, bool releaseFirst, double now)
    {
        var line = releaseFirst ? "Release the mouse first" : more > 0 ? $"{step} +{more} more" : step;
        var hint = running ? $"{sentence} Esc stops this step" : sentence;
        // the canvas draws the key as a chip: "<kbd>Esc</kbd> stops this step"
        IReadOnlyList<PillHintPart> parts = running ? [new(sentence + " "), PillHintPart.Key("Esc"), new(" stops this step")] : [new(sentence)];
        return new(PillNoticeKind.HandsOff, "Hands off for a moment", line, hint, PillGroupKind.HandsOff, now, 0, parts);
    }

    /// <summary>"Done, all yours" for 1.2 s after the borrowed step, with what it did.</summary>
    public static PillNotice Done(string result, double now) => new(PillNoticeKind.Done, "Done, all yours", result, null, PillGroupKind.Run, now, 1.2);

    /// <summary>The borrow chip for 1 s ("Used the mouse for one click in Paint").</summary>
    public static PillNotice Borrowed(string chip, double now) => new(PillNoticeKind.Borrowed, chip, "", null, PillGroupKind.Run, now, 1);

    /// <summary>"Tactus kept 6 keys you typed" with "Put them back" (DESIGN.md 2.7 step 7).</summary>
    public static PillNotice KeptKeys(int keys, double now) => new(PillNoticeKind.KeptKeys, $"Tactus kept {keys} {(keys == 1 ? "key" : "keys")} you typed", "", null, PillGroupKind.PutBack, now, 0);

    /// <summary>"Click where you were working to keep typing" (keys held after a failed restore).</summary>
    public static PillNotice KeysHeld(double now) => new(PillNoticeKind.KeysHeld, "Click where you were working to keep typing", "", null, PillGroupKind.Run, now, 0);

    /// <summary>A focus borrow waits behind an admin or full-screen window (critique C1), with "Not now".</summary>
    public static PillNotice ProtectedForeground(bool admin, double now) =>
        new(PillNoticeKind.ProtectedForeground, admin ? "Waiting until you leave the admin window" : "Waiting until you leave the full-screen app", "", null, PillGroupKind.NotNow, now, 0);

    /// <summary>A confirm that expired, for 2 s: "Send in Outlook expired", "Nothing was sent. Ask in chat to try again".</summary>
    public static PillNotice Expired(string action, string app, double now) =>
        new(PillNoticeKind.Expired, $"{action} in {app} expired", "Nothing was sent. Ask in chat to try again", null, PillGroupKind.StopOnly, now, 2);

    /// <summary>A Tactus step opened a window that took the foreground, for 2 s ("Excel opened Save As").</summary>
    public static PillNotice OpenedWindow(string app, string title, double now) => new(PillNoticeKind.OpenedWindow, $"{app} opened {title}", "", null, PillGroupKind.Run, now, 2);

    /// <summary>True while the notice shows at <paramref name="now"/>.</summary>
    public bool Live(double now) => now >= Since && (Lasts <= 0 || now < Since + Lasts);

    /// <summary>The rank of DESIGN.md 10.5's priority (lower first).</summary>
    internal int Rank => Kind switch
    {
        PillNoticeKind.StopKeyTaken => 0,
        PillNoticeKind.InputRestarting => 2,
        PillNoticeKind.HandsOff or PillNoticeKind.ProtectedForeground => 3,
        PillNoticeKind.KeysHeld or PillNoticeKind.KeptKeys or PillNoticeKind.Done or PillNoticeKind.Borrowed => 4,
        PillNoticeKind.Expired => 5,
        PillNoticeKind.OpenedWindow => 6,
        _ => 8,
    };
}

/// <summary>Why Tactus stopped, and when (the Stopped state shows for 2 s).</summary>
public sealed record PillStop(double Since, bool EscThreeTimes = false)
{
    public const double Seconds = 2;
}

/// <summary>Waiting while the user types in a field (DESIGN.md 2.9): "Subject" in "Outlook".</summary>
public sealed record PillTyping(string Field, string App);

/// <summary>Everything the pill's text depends on.</summary>
public sealed record PillFacts
{
    public IReadOnlyList<PillApp> Apps { get; init; } = [];

    /// <summary>The key of the app whose step was latest (its step is the working line).</summary>
    public string? LatestApp { get; init; }

    public IReadOnlyList<PillAsk> Asks { get; init; } = [];

    /// <summary>The ask the card shows (and the collapsed pill names).</summary>
    public int OpenAsk { get; init; }

    /// <summary>The card is open.</summary>
    public bool Card { get; init; }

    public PillStop? Stopped { get; init; }

    public IReadOnlyList<PillNotice> Notices { get; init; } = [];

    public PillTyping? Typing { get; init; }

    /// <summary>The pause chord as shown ("Ctrl+Alt+Space"); empty when unset or refused.</summary>
    public string PauseChord { get; init; } = "Ctrl+Alt+Space";

    public string StopChord { get; init; } = "Ctrl+Alt+Esc";

    /// <summary>The rows are open (a click on the pill without asks).</summary>
    public bool RowsOpen { get; init; }

    public bool Dragging { get; init; }

    public bool ReduceMotion { get; init; }
}

/// <summary>The live glyph beside the mark (DESIGN.md 10.1): the dot, the pause bars (breathing while pausing), or nothing.</summary>
public enum PillGlyph
{
    None,
    Live,
    Pause,
    Pausing,
}

/// <summary>The state's button group (canvas `setGroups`); Stop has its own slot and is not one of these.</summary>
public enum PillGroupKind
{
    /// <summary>No group and no Stop (Stopped, an expired confirm).</summary>
    None,

    /// <summary>The 28 DIP Pause all icon button.</summary>
    Run,

    /// <summary>No group, Stop alone (canvas `stop`).</summary>
    StopOnly,

    /// <summary>Resume (and Stop).</summary>
    Paused,

    /// <summary>A pause stuck 5 s on one unit: Resume and "Stop this app".</summary>
    PausingStuck,

    /// <summary>"Do it now", "Not now".</summary>
    HandsOff,

    /// <summary>"Not now".</summary>
    NotNow,

    /// <summary>"Put them back".</summary>
    PutBack,

    /// <summary>"Settings".</summary>
    Settings,

    /// <summary>The card's header: the pager, Later and the app's stop (x); no Stop.</summary>
    Card,
}

/// <summary>How a piece of the hint row is drawn (canvas `.phint`: text, a key chip `kbd`, the separating dot).</summary>
public enum PillHintPartKind
{
    Text,
    Key,
    Dot,
}

/// <summary>One piece of the hint row.</summary>
public sealed record PillHintPart(string Text, PillHintPartKind Kind = PillHintPartKind.Text)
{
    public static PillHintPart Key(string key) => new(key, PillHintPartKind.Key);

    public static PillHintPart Dot { get; } = new("·", PillHintPartKind.Dot);

    /// <summary>A chord's keys as chips ("Ctrl+Alt+Space": Ctrl, Alt, Space).</summary>
    public static IEnumerable<PillHintPart> Keys(string chord) => chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Key);
}

/// <summary>One app's row under the pill.</summary>
public sealed record PillRowView(string Key, string Name, string Line, bool Paused, bool Off, bool Behind, string PauseName, string StopName);

/// <summary>What the pill shows.</summary>
public sealed record PillView(
    string Title,
    string Line,
    PillGlyph Glyph,
    TactusMarkState Mark,
    PillGroupKind Group,
    bool Ask,
    bool Working,
    int Badge,
    string Announcement,
    string Hint,
    bool HintShown,
    IReadOnlyList<PillRowView> Rows,
    bool RowsShown,
    string? Pager,
    string StopName,
    string PauseName,
    string ResumeName,
    bool Stopped,
    IReadOnlyList<PillHintPart>? HintParts = null)
{
    /// <summary>The hint row as drawn: its pieces, or the hint's text alone.</summary>
    public IReadOnlyList<PillHintPart> HintPieces => HintParts ?? [new PillHintPart(Hint)];

    /// <summary>Stop shows in every state but the card's and the empty one (canvas `noStop`).</summary>
    public bool ShowsStop => Group is not (PillGroupKind.None or PillGroupKind.Card);
}

public static class PillStates
{
    /// <summary>"Pausing after this step" shows when a unit still runs 2 s after Pause (DESIGN.md 10.1).</summary>
    public const double AfterThisStepSeconds = 2;

    /// <summary>5 s after Pause the pill offers "Stop this app".</summary>
    public const double StopThisAppSeconds = 5;

    public static PillView Compute(PillFacts facts, double now)
    {
        var apps = facts.Apps;
        var live = apps.Where(a => a.Phase != PillAppPhase.Stopped).ToList();
        var working = live.Where(a => a.Phase == PillAppPhase.Working && !a.TookOver).ToList();
        var tookOver = live.Where(a => a.Phase == PillAppPhase.Working && a.TookOver).ToList();
        var carded = working.Where(a => a.OwnCard).ToList();
        var paused = live.Where(a => a.Phase == PillAppPhase.Paused).ToList();
        var pausing = live.Where(a => a.Phase == PillAppPhase.Pausing).ToList();
        var asks = facts.Asks;
        var n = asks.Count;
        var allPaused = live.Count > 0 && paused.Count == live.Count;
        var stopped = facts.Stopped is { } stop && now < stop.Since + PillStop.Seconds;
        var notice = facts.Notices.Where(x => x.Live(now)).OrderBy(x => x.Rank).ThenByDescending(x => x.Since).FirstOrDefault();
        var glyph = stopped || live.Count == 0 ? PillGlyph.None : pausing.Count > 0 ? PillGlyph.Pausing : allPaused ? PillGlyph.Pause : PillGlyph.Live;
        var mark = stopped || live.Count == 0 ? TactusMarkState.Ready : pausing.Count > 0 || allPaused ? TactusMarkState.Paused : TactusMarkState.Working;
        var pauseName = Named("Pause all", facts.PauseChord);
        var resumeName = Named("Resume all", facts.PauseChord);
        var stopName = Named("Stop all", facts.StopChord);
        var workingLine = WorkingLine(facts, live, working);

        string title, line, announcement;
        PillGroupKind group;
        var ask = false;
        var isWorking = false;
        string? pager = null;
        var hint = Hint(facts, allPaused);
        var hintParts = HintParts(facts, allPaused);
        var hintShown = false;

        if (facts.Card && n > 0)
        {
            var q = asks[Math.Clamp(facts.OpenAsk, 0, n - 1)];
            title = AskTitle(q);
            line = q.Kind == PillAskKind.Provider ? $"{q.Model} · review it in the chat" : q.Head is { Length: > 0 } head ? $"{q.Chat} · {q.Model} · {head}" : $"{q.Chat} · {q.Model}";
            group = PillGroupKind.Card;
            pager = n > 1 ? $"{Math.Clamp(facts.OpenAsk, 0, n - 1) + 1} of {n}" : null;
            announcement = $"{title}. {n} waiting";
        }
        else if (notice is { Kind: PillNoticeKind.StopKeyTaken })
        {
            (title, line, group) = (notice.Title, workingLine, notice.Group);
            announcement = BaseAnnouncement(stopped, live, working, paused, pausing, allPaused, n);
        }
        else if (stopped || live.Count == 0)
        {
            title = "Stopped";
            line = facts.Stopped?.EscThreeTimes == true ? "Esc pressed 3 times. Ask in chat to continue." : "Ask in chat to continue";
            group = PillGroupKind.None;
            glyph = PillGlyph.None;
            announcement = "Tactus stopped";
        }
        else if (notice is not null && notice.Rank < 7)
        {
            title = notice.Title;
            line = notice.Line.Length > 0 ? notice.Line : workingLine;
            group = notice.Group;
            if (notice.Hint is { } sentence)
            {
                hint = sentence;
                hintParts = notice.HintParts ?? [new PillHintPart(sentence)];
                hintShown = !facts.Dragging;
            }
            isWorking = notice.Line.Length == 0;
            if (notice.Kind == PillNoticeKind.Expired)
            {
                // DESIGN.md 4.5: no buttons and no glyph; Stop keeps its own slot while other apps still work.
                group = PillGroupKind.StopOnly;
                glyph = PillGlyph.None;
            }
            announcement = BaseAnnouncement(stopped, live, working, paused, pausing, allPaused, n);
        }
        else if (n > 0)
        {
            var q = asks[Math.Clamp(facts.OpenAsk, 0, n - 1)];
            var go = working.Where(a => !a.WaitsOnAsk).ToList();
            title = n > 1 ? $"{AskTitle(q)} +{n - 1}" : AskTitle(q);
            line = q.Withdrawn ? "Your draft is kept · click to open" : go.Count > 0 ? $"Click to answer. {Names(go)} {(go.Count > 1 ? "keep" : "keeps")} working" : "Click to answer";
            group = pausing.Count > 0 ? PillGroupKind.Paused : allPaused ? PillGroupKind.Paused : PillGroupKind.Run;
            ask = true;
            announcement = $"Tactus: {n} waiting for you";
        }
        else if (carded.Count > 0)
        {
            // web W11: Visus's card hangs from its tab (the canvas's "nth needs your OK" / "Check the card")
            title = "nth needs your OK";
            line = "Check the card";
            group = pausing.Count > 0 || allPaused ? PillGroupKind.Paused : PillGroupKind.Run;
            announcement = $"nth needs your OK in {Names(carded)}";
        }
        else if (notice is { Kind: PillNoticeKind.PauseKeyTaken })
        {
            (title, line, group) = (notice.Title, workingLine, notice.Group);
            isWorking = true;
            announcement = BaseAnnouncement(stopped, live, working, paused, pausing, allPaused, n);
        }
        else if (tookOver.Count > 0 && pausing.Count == 0)
        {
            // web W11: "You took over Visus" / "Resume when you are done"; Resume hands the tab back
            title = $"You took over {Names(tookOver)}";
            line = "Resume when you are done";
            group = PillGroupKind.Paused;
            glyph = PillGlyph.Pause;
            mark = TactusMarkState.Paused;
            announcement = title;
        }
        else if (pausing.Count > 0)
        {
            var a = pausing.OrderBy(x => x.PhaseSince).First();
            title = "Pausing";
            line = a.Busy is { Length: > 0 } busy && now - a.PhaseSince >= AfterThisStepSeconds ? $"After this step: {busy}" : $"Finishing {a.Busy ?? "this step"}";
            group = now - a.PhaseSince >= StopThisAppSeconds ? PillGroupKind.PausingStuck : PillGroupKind.Paused;
            announcement = "Tactus pausing";
        }
        else if (allPaused)
        {
            var minutes = (int)Math.Floor((now - paused.Min(a => a.PhaseSince)) / 60);
            title = live.Count == 1 ? $"Paused in {paused[0].Name}" : "Paused";
            line = minutes >= 1 ? $"Paused {minutes} min"
                : live.Count == 1 && paused[0].PausedLine is { Length: > 0 } held ? held
                : $"{Names(paused)} {(paused.Count > 1 ? "hold" : "holds")} the next steps";
            group = PillGroupKind.Paused;
            announcement = "Tactus paused";
            hintShown = !facts.Dragging;
        }
        else if (paused.Count > 0)
        {
            title = $"Using {Names(working)}. {Names(paused)} paused";
            line = working.Count > 0 ? working[0].Step : "";
            group = PillGroupKind.Run;
            isWorking = true;
            announcement = $"Tactus paused in {Names(paused)}";
        }
        else if (facts.Typing is { } typing)
        {
            title = "Waiting while you type";
            line = $"{typing.Field} ({typing.App})";
            group = PillGroupKind.Run;
            announcement = "Tactus working";
        }
        else
        {
            title = $"Using {Names(working)}";
            line = workingLine;
            group = PillGroupKind.Run;
            isWorking = true;
            announcement = "Tactus working";
        }

        var rows = apps.Select(a => Row(a)).ToList();
        var rowsShown = facts.RowsOpen && !facts.Card && !facts.Dragging && live.Count > 0;
        hintShown = hintShown || rowsShown || (ask && facts.RowsOpen && !facts.Dragging);
        if (facts.Card || (stopped && group == PillGroupKind.None)) hintShown = false;
        return new PillView(title, line, glyph, mark, group, ask, isWorking, n, announcement, hint, hintShown, rows, rowsShown, pager, stopName, pauseName, resumeName, stopped || live.Count == 0, hintParts);
    }

    /// <summary>The collapsed pill's and the card header's title for an ask (DESIGN.md 4.5: "Needs your OK. Outlook").</summary>
    public static string AskTitle(PillAsk ask) => ask.Kind switch
    {
        PillAskKind.Question => $"Waiting for your answer. {ask.AppName}",
        PillAskKind.Provider => $"{ask.Title}. {ask.Chat}",
        _ => $"Needs your OK. {ask.AppName}",
    };

    /// <summary>The assertive notification a new ask raises (DESIGN.md 10.6: "Tactus needs your OK for Outlook").</summary>
    public static string NewAskNotice(PillAsk ask) => ask.Kind switch
    {
        PillAskKind.Question => $"Tactus is waiting for your answer in {ask.AppName}",
        PillAskKind.Provider => $"{ask.Title}. {ask.Chat}",
        _ => $"Tactus needs your OK for {ask.AppName}",
    };

    /// <summary>"Excel", "Excel and Outlook", "Excel, Outlook and Chrome" (canvas `listN`).</summary>
    public static string Names(IReadOnlyList<PillApp> apps) => apps.Count switch
    {
        0 => "",
        1 => apps[0].Name,
        _ => $"{string.Join(", ", apps.Take(apps.Count - 1).Select(a => a.Name))} and {apps[^1].Name}",
    };

    /// <summary>An accessible name with its chord ("Stop all, Ctrl+Alt+Esc"); the folded key cap never changes it.</summary>
    public static string Named(string action, string chord) => chord.Length > 0 ? $"{action}, {chord}" : action;

    private static string WorkingLine(PillFacts facts, List<PillApp> live, List<PillApp> working)
    {
        var latest = live.FirstOrDefault(a => a.Key == facts.LatestApp && a.Phase == PillAppPhase.Working) ?? working.FirstOrDefault();
        return latest is null ? "" : $"{latest.Name}: {latest.Step}";
    }

    // DESIGN.md 10.1 and 4.5: "Ctrl+Alt+Space pauses. Ctrl+Alt+Esc stops." (resumes while all are paused); without a pause
    // chord, "Ctrl+Alt+Esc stops all".
    private static string Hint(PillFacts facts, bool allPaused)
    {
        if (facts.PauseChord.Length == 0) return $"{facts.StopChord} stops all";
        return $"{facts.PauseChord} {(allPaused ? "resumes" : "pauses")}. {facts.StopChord} stops.";
    }

    // The canvas's hint row: "[Ctrl][Alt][Space] pauses · [Ctrl][Alt][Esc] stops" (render10 `swap(H10, ...)`); the
    // accessible text is DESIGN's sentence (Hint).
    private static IReadOnlyList<PillHintPart> HintParts(PillFacts facts, bool allPaused)
    {
        if (facts.PauseChord.Length == 0) return [.. PillHintPart.Keys(facts.StopChord), new(" stops all")];
        return [.. PillHintPart.Keys(facts.PauseChord), new(allPaused ? " resumes" : " pauses"), PillHintPart.Dot, .. PillHintPart.Keys(facts.StopChord), new(" stops")];
    }

    private static string BaseAnnouncement(bool stopped, List<PillApp> live, List<PillApp> working, List<PillApp> paused, List<PillApp> pausing, bool allPaused, int asks)
    {
        if (stopped || live.Count == 0) return "Tactus stopped";
        if (asks > 0) return $"Tactus: {asks} waiting for you";
        if (pausing.Count > 0) return "Tactus pausing";
        if (allPaused) return "Tactus paused";
        if (paused.Count > 0) return $"Tactus paused in {Names(paused)}";
        return "Tactus working";
    }

    private static PillRowView Row(PillApp a)
    {
        var pausedLike = a.Phase is PillAppPhase.Paused or PillAppPhase.Pausing || (a.Phase == PillAppPhase.Working && a.TookOver);
        var line = a.Phase switch
        {
            PillAppPhase.Stopped => "Stopped by you",
            PillAppPhase.Paused => "Paused",
            PillAppPhase.Pausing => $"Pausing after {a.Busy ?? "this step"}",
            PillAppPhase.Working when a.TookOver => "You took over",
            PillAppPhase.Working when a.OwnCard => "Check the card",
            _ => a.Step,
        };
        return new PillRowView(a.Key, a.Name, line, pausedLike, a.Phase == PillAppPhase.Stopped, a.Behind && a.Phase == PillAppPhase.Working,
            $"{(pausedLike ? "Resume" : "Pause")} {a.Name}", $"Stop {a.Name}");
    }
}
