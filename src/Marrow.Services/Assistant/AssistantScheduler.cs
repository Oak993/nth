// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.1 to 7.4, 12.6; unit AS-10; ED-NEW-dextra-as10-11). No Swift twin.
// The scheduler of standing duties. It keeps each duty's next time on the injected clock (the manual-clock pattern of the
// Ignis and AutoContinue timers), watches the folders and pages its duties name, hears the app events they wait for, and hands
// each run to an IDutyLauncher. It never starts a run while Stop all holds it, while "Pause duties" is on, or outside a duty's
// active hours: such a run is held and, when the hold ends, runs once (catch up once) or is skipped (skip), whatever started it.
// A duty whose last run is still going does not run again on a time (the next time comes), and an event that arrives meanwhile
// waits for the run to end. One timer sleeps at most MaxWait, so a sleeping PC or a changed clock is noticed. Nothing here
// touches a window: it runs the same with nth's window closed.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed class AssistantScheduler : IDisposable
{
    private const int HeldDetailLimit = 2_000;

    private enum Block { None, Stop, Pause, Hours }

    private sealed class Runtime(Duty duty, string assistantId, int sequence)
    {
        public Duty Duty = duty;
        public readonly string AssistantId = assistantId;
        public readonly int Sequence = sequence;
        public bool ReadOnly;
        public DateTimeOffset? NextDue;
        public DateTimeOffset? NextCheck;
        public bool Checking;
        public bool Launching;
        public Guid? Running;
        public DateTimeOffset RunningSince;
        public HeldRun? Held;
        public FolderTrigger? Folder;
        public JsonObject? Page;
        public string? Status;
        public DateTimeOffset LastEvent = DateTimeOffset.MinValue;
        public bool AboveFired;
    }

    private sealed record HeldRun(DutyCause Cause, string? Detail, ContentOrigin? Untrusted, DateTimeOffset Since, bool Missed);

    private sealed class Doc(DutyDocument document, bool writesAllowed)
    {
        public DutyDocument Document = document;
        public readonly bool WritesAllowed = writesAllowed;
    }

    private readonly Lock gate = new();
    private readonly AssistantSchedulerOptions o;
    private readonly TimeProvider time;
    private readonly Dictionary<string, Runtime> runtimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Doc> docs = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stopping = new();
    private ITimer? timer;
    private bool started;
    private bool disposed;
    private int sequence;
    private DateTimeOffset? pausedUntil;

    public AssistantScheduler(AssistantSchedulerOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        time = o.Time;
    }

    /// <summary>The duty list changed (a duty added, edited, run, held or ended). Raised outside the scheduler's lock.</summary>
    public event Action? Changed;

    /// <summary>A duty was added; the second value is true for the very first duty (its card offers the two background switches, 7.4).</summary>
    public event Action<Duty, bool>? DutyAdded;

    // ---- start ----

    /// <summary>Loads every assistant's duties, starts the watchers, and runs what came due while nth was closed (catch up once or skip).</summary>
    public IReadOnlyList<string> Start()
    {
        var notes = new List<string>();
        var after = new List<Action>();
        lock (gate)
        {
            if (started || disposed) return notes;
            started = true;
            foreach (var assistantId in o.AssistantIds())
            {
                var file = o.Store.Load(assistantId);
                docs[assistantId] = new Doc(file.Document, file.WritesAllowed);
                foreach (var problem in file.Document.Problems) notes.Add($"{assistantId}: {problem}");
                foreach (var duty in file.Document.Duties)
                {
                    var runtime = new Runtime(duty, assistantId, sequence++) { ReadOnly = !file.WritesAllowed };
                    runtimes[duty.Id] = runtime;
                }
            }
            var now = time.GetUtcNow();
            foreach (var runtime in runtimes.Values.OrderBy(r => r.Sequence)) Arm(runtime, now, after);
            Tick(now, after);
        }
        Run(after);
        Raise();
        return notes;
    }

    // Under the lock. Sets a runtime up from its duty: its next time, its watcher, its first page look.
    private void Arm(Runtime r, DateTimeOffset now, List<Action> after)
    {
        r.NextDue = null;
        r.NextCheck = null;
        r.Status = null;
        DisposeWatchers(r);
        if (r.ReadOnly || !r.Duty.Enabled) return;
        switch (r.Duty.Trigger)
        {
            case TimeTrigger time:
                if (DutyZones.Find(time.TimeZoneId) is not { } zone) r.Status = "nth does not know this duty's time zone.";
                else r.NextDue = ScheduleMath.Next(time.Schedule, zone, r.Duty.Handled ?? r.Duty.Since, r.Duty.Since);
                break;
            case FolderChangeTrigger folder:
                after.Add(() => StartFolder(r, folder));
                break;
            case PageWatchTrigger page:
                if (o.Web is null) r.Status = "Watching pages needs the web reader, which is not available.";
                else
                {
                    r.Page = o.Store.LoadWatch(r.AssistantId, r.Duty.Id);
                    var every = TimeSpan.FromMinutes(page.EveryMinutes);
                    r.NextCheck = r.Page?["checked"]?.GetValue<string>() is { } checkedAt
                                  && DateTimeOffset.TryParse(checkedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
                                  && last + every > now ? last + every : now;
                }
                break;
        }
    }

    private void DisposeWatchers(Runtime r)
    {
        r.Folder?.Dispose();
        r.Folder = null;
    }

    // ---- the duty list ----

    /// <summary>The duties as the duty list shows them, in the order they were made.</summary>
    public IReadOnlyList<DutyView> Duties
    {
        get
        {
            lock (gate)
            {
                return [.. runtimes.Values.OrderBy(r => r.Sequence)
                    .Select(r => new DutyView(r.Duty, r.NextDue ?? r.NextCheck, r.Running is not null || r.Launching, r.Held is not null, r.Status, r.ReadOnly))];
            }
        }
    }

    public Duty? Find(string id)
    {
        lock (gate) return runtimes.TryGetValue(id, out var r) ? r.Duty : null;
    }

    /// <summary>Until when "Pause duties" holds them; null when it is not on.</summary>
    public DateTimeOffset? PausedUntil
    {
        get
        {
            lock (gate) return pausedUntil is { } until && until > time.GetUtcNow() ? until : null;
        }
    }

    /// <summary>The problems the files had when they were loaded.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (gate) return [.. docs.SelectMany(d => d.Value.Document.Problems.Select(p => $"{d.Key}: {p}"))];
        }
    }

    // ---- changing duties ----

    /// <summary>
    /// Saves a new duty (the owner's own Save, or a card he approved, 7.1). Returns it as saved, or why it could not be.
    /// </summary>
    public (Duty? Duty, string? Problem) Add(Duty duty)
    {
        ArgumentNullException.ThrowIfNull(duty);
        Duty saved;
        bool first;
        var after = new List<Action>();
        lock (gate)
        {
            if (disposed) return (null, "The scheduler is stopped.");
            var now = time.GetUtcNow();
            saved = duty with
            {
                Id = string.IsNullOrWhiteSpace(duty.Id) ? Duty.NewId() : duty.Id,
                Since = now,
                Handled = null,
                LastRun = null,
                History = ValueList<DutyRun>.Empty,
            };
            if (saved.Problem() is { } problem) return (null, problem);
            if (runtimes.ContainsKey(saved.Id)) return (null, "A duty with that id exists already.");
            if (Writable(saved.AssistantId) is { } blocked) return (null, blocked);
            first = runtimes.Count == 0;
            var runtime = new Runtime(saved, saved.AssistantId, sequence++);
            runtimes[saved.Id] = runtime;
            Persist(saved.AssistantId);
            Arm(runtime, now, after);
            Rearm(now);
        }
        Run(after);
        DutyAdded?.Invoke(saved, first);
        Raise();
        return (saved, null);
    }

    /// <summary>Replaces a duty by id. A changed trigger starts a fresh schedule (no catch-up for what it did not yet do).</summary>
    public (Duty? Duty, string? Problem) Update(Duty duty)
    {
        ArgumentNullException.ThrowIfNull(duty);
        Duty saved;
        var after = new List<Action>();
        lock (gate)
        {
            if (disposed) return (null, "The scheduler is stopped.");
            if (!runtimes.TryGetValue(duty.Id, out var r)) return (null, "There is no such duty.");
            if (Writable(r.AssistantId) is { } blocked) return (null, blocked);
            var now = time.GetUtcNow();
            var triggerChanged = !Equals(r.Duty.Trigger, duty.Trigger);
            saved = duty with
            {
                AssistantId = r.AssistantId,
                Since = triggerChanged ? now : r.Duty.Since,
                Handled = triggerChanged ? null : r.Duty.Handled,
                LastRun = r.Duty.LastRun,
                History = r.Duty.History,
                Unknown = r.Duty.Unknown,
                CreatedBy = r.Duty.CreatedBy,
            };
            if (saved.Problem() is { } problem) return (null, problem);
            r.Duty = saved;
            if (triggerChanged) o.Store.DeleteWatch(r.AssistantId, saved.Id);
            if (triggerChanged) r.Page = null;
            Persist(r.AssistantId);
            Arm(r, now, after);
            Rearm(now);
        }
        Run(after);
        Raise();
        return (saved, null);
    }

    public string? Remove(string id)
    {
        lock (gate)
        {
            if (!runtimes.TryGetValue(id, out var r)) return "There is no such duty.";
            if (Writable(r.AssistantId) is { } blocked) return blocked;
            DisposeWatchers(r);
            runtimes.Remove(id);
            o.Store.DeleteWatch(r.AssistantId, id);
            Persist(r.AssistantId);
            Rearm(time.GetUtcNow());
        }
        Raise();
        return null;
    }

    /// <summary>Switches a duty on or off. Switching on starts from now: the days it was off are not caught up.</summary>
    public string? SetEnabled(string id, bool enabled)
    {
        var after = new List<Action>();
        lock (gate)
        {
            if (!runtimes.TryGetValue(id, out var r)) return "There is no such duty.";
            if (Writable(r.AssistantId) is { } blocked) return blocked;
            if (r.Duty.Enabled == enabled) return null;
            var now = time.GetUtcNow();
            r.Duty = enabled ? r.Duty with { Enabled = true, Handled = now } : r.Duty with { Enabled = false };
            if (!enabled) r.Held = null;
            Persist(r.AssistantId);
            Arm(r, now, after);
            Rearm(now);
        }
        Run(after);
        Raise();
        return null;
    }

    private string? Writable(string assistantId)
    {
        if (!docs.TryGetValue(assistantId, out var doc))
        {
            var file = o.Store.Load(assistantId);
            doc = docs[assistantId] = new Doc(file.Document, file.WritesAllowed);
        }
        return doc.WritesAllowed ? null : "This assistant's duties file is from a newer nth or could not be read, so nth does not change it.";
    }

    // Under the lock. Writes one assistant's duties back, with whatever the file held that this build does not know.
    private void Persist(string assistantId)
    {
        if (!docs.TryGetValue(assistantId, out var doc) || !doc.WritesAllowed) return;
        var duties = runtimes.Values.Where(r => r.AssistantId == assistantId).OrderBy(r => r.Sequence).Select(r => r.Duty).ToList();
        doc.Document = doc.Document with { Duties = duties };
        try { o.Store.Save(assistantId, doc.Document); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            o.Faulted?.Invoke("The duties could not be saved: " + error.Message);
        }
    }

    // ---- running ----

    /// <summary>Run now (7.2): always available. Refuses while Stop all holds the scheduler or the last run is still going.</summary>
    public string? RunNow(string id, bool dry = false)
    {
        var after = new List<Action>();
        lock (gate)
        {
            if (!runtimes.TryGetValue(id, out var r)) return "There is no such duty.";
            if (!o.MayFire()) return "Stop all is on. Tap Resume all first.";
            if (r.Running is not null || r.Launching) return "The last run is still going.";
            Begin(r, DutyCause.Manual, null, null, dry, time.GetUtcNow(), after);
        }
        Run(after);
        return null;
    }

    /// <summary>
    /// Task Scheduler woke the PC for this duty (<c>nth --duty id</c>, 7.4): it runs now, whatever its missed-run policy, unless it
    /// already ran for this time (a running nth, or the catch-up at start, got there first).
    /// </summary>
    public string? WakeRun(string id)
    {
        var after = new List<Action>();
        lock (gate)
        {
            if (!runtimes.TryGetValue(id, out var r)) return "There is no such duty.";
            var now = time.GetUtcNow();
            if (!r.Duty.Enabled || r.ReadOnly) return "The duty is off.";
            if (r.Running is not null || r.Launching) return "It is running already.";
            if (r.Duty.LastRun is { } last && now - last < WakeDuplicate) return "It already ran a moment ago.";
            if (!o.MayFire()) return "Stop all is on.";
            r.NextDue = r.Duty.Trigger is TimeTrigger t && DutyZones.Find(t.TimeZoneId) is { } zone ? ScheduleMath.Next(t.Schedule, zone, now, r.Duty.Since) : r.NextDue;
            Begin(r, DutyCause.Wake, null, null, false, now, after);
        }
        Run(after);
        return null;
    }

    /// <summary>A wake that arrives within this time of a run is the same run.</summary>
    public static readonly TimeSpan WakeDuplicate = TimeSpan.FromMinutes(5);

    // ---- holds ----

    /// <summary>Pauses every duty for a while ("Pause duties for 1 hour", 7.4). What comes due meanwhile is held and runs once after, or is skipped.</summary>
    public void PauseFor(TimeSpan span)
    {
        lock (gate)
        {
            if (disposed) return;
            var now = time.GetUtcNow();
            pausedUntil = now + span;
            Rearm(now);
        }
        Raise();
    }

    public void ResumeDuties()
    {
        lock (gate) pausedUntil = null;
        Reevaluate();
        Raise();
    }

    /// <summary>The holds may have changed (Resume all, a Pause that ended, the clock): looks at everything now.</summary>
    public void Reevaluate()
    {
        var after = new List<Action>();
        lock (gate)
        {
            if (!started || disposed) return;
            Tick(time.GetUtcNow(), after);
        }
        Run(after);
    }

    // ---- the timer ----

    private void OnTimer()
    {
        var after = new List<Action>();
        lock (gate)
        {
            if (!started || disposed) return;
            Tick(time.GetUtcNow(), after);
        }
        Run(after);
        Raise();
    }

    // Under the lock. Deals with everything that is due, then sets the next wake.
    private void Tick(DateTimeOffset now, List<Action> after)
    {
        if (pausedUntil is { } until && until <= now) pausedUntil = null;
        foreach (var r in runtimes.Values.OrderBy(r => r.Sequence).ToList())
        {
            if (r.Running is not null && now - r.RunningSince > o.MaxRun)
            {
                r.Running = null;
                Note(r, DutyFeedKind.Problem, $"{r.Duty.Name}: its last run never reported back, so it may run again.");
            }
            if (r.ReadOnly || !r.Duty.Enabled) continue;
            if (r.NextDue is { } due && due <= now) HandleDue(r, due, now, after);
            if (r.NextCheck is { } check && check <= now && !r.Checking) StartCheck(r, now, after);
            if (r.Held is not null) TryRelease(r, now, after);
        }
        Rearm(now);
    }

    // Under the lock.
    private void Rearm(DateTimeOffset now)
    {
        if (!started || disposed) return;
        DateTimeOffset? next = null;
        void Consider(DateTimeOffset? at)
        {
            if (at is { } moment && (next is null || moment < next)) next = moment;
        }
        var live = false;
        foreach (var r in runtimes.Values)
        {
            if (r.ReadOnly || !r.Duty.Enabled) continue;
            live = true;
            Consider(r.NextDue);
            if (!r.Checking) Consider(r.NextCheck);
            if (r.Running is not null) Consider(r.RunningSince + o.MaxRun);
            if (r.Held is not null)
            {
                Consider(now + o.MaxWait); // a held run is looked at again within the cap, whatever lifted what held it
                if (Blocker(r, now) is Block.Hours && r.Duty.Hours?.NextOpen(now, ZoneOf(r.Duty)) is { } opens) Consider(opens);
                if (pausedUntil is { } until) Consider(until);
            }
        }
        // Nothing to wait for (no duty on, or only duties that start on an event or when asked): no timer at all, so an idle app is idle. The
        // 60 s cap below is for a time that is coming (a sleep or a clock change must not delay it).
        if (!live || next is null)
        {
            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }
        var delay = next.Value - now;
        if (delay > o.MaxWait) delay = o.MaxWait;
        if (delay < TimeSpan.FromMilliseconds(1)) delay = TimeSpan.FromMilliseconds(1);
        if (timer is null) timer = time.CreateTimer(_ => OnTimer(), null, delay, Timeout.InfiniteTimeSpan);
        else timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    // ---- deciding ----

    private TimeZoneInfo ZoneOf(Duty duty) =>
        duty.Trigger is TimeTrigger t && DutyZones.Find(t.TimeZoneId) is { } zone ? zone : time.LocalTimeZone;

    private Block Blocker(Runtime r, DateTimeOffset now)
    {
        if (!o.MayFire()) return Block.Stop;
        if (pausedUntil is { } until && until > now) return Block.Pause;
        if (r.Duty.Hours is { } hours && !hours.Contains(now, ZoneOf(r.Duty))) return Block.Hours;
        return Block.None;
    }

    // Under the lock. A time came.
    private void HandleDue(Runtime r, DateTimeOffset due, DateTimeOffset now, List<Action> after)
    {
        r.NextDue = r.Duty.Trigger is TimeTrigger t && DutyZones.Find(t.TimeZoneId) is { } zone
            ? ScheduleMath.Next(t.Schedule, zone, now, r.Duty.Since)
            : null;
        var cause = now - due > o.Late ? DutyCause.CatchUp : DutyCause.Time;
        Decide(r, cause, null, null, now, after);
    }

    // Under the lock. Something wants this duty to run: hold it, skip it, or begin it.
    private void Decide(Runtime r, DutyCause cause, string? detail, ContentOrigin? untrusted, DateTimeOffset now, List<Action> after)
    {
        if (Blocker(r, now) != Block.None)
        {
            Hold(r, cause, detail, untrusted, now, missed: true);
            return;
        }
        if (r.Running is not null || r.Launching)
        {
            if (cause is DutyCause.Time or DutyCause.CatchUp) Skip(r, cause, "The last run is still going, so this time is skipped.", now);
            else Hold(r, cause, detail, untrusted, now, missed: false);
            return;
        }
        if (cause == DutyCause.CatchUp && r.Duty.Missed == MissedRunPolicy.Skip)
        {
            Skip(r, cause, "Missed while nth was closed or held; this duty skips missed runs.", now);
            return;
        }
        r.Held = null;
        Begin(r, cause, detail, untrusted, dry: false, now, after);
    }

    private void Hold(Runtime r, DutyCause cause, string? detail, ContentOrigin? untrusted, DateTimeOffset now, bool missed)
    {
        r.Held = r.Held is null
            ? new HeldRun(cause, Cap(detail), untrusted, now, missed)
            : r.Held with
            {
                Detail = Cap(string.Join("\n", new[] { r.Held.Detail, detail }.Where(d => !string.IsNullOrEmpty(d)))),
                Untrusted = r.Held.Untrusted ?? untrusted,
                Missed = r.Held.Missed || missed,
            };
    }

    private static string? Cap(string? text) => text is { Length: > HeldDetailLimit } ? text[..HeldDetailLimit] : text;

    // Under the lock. A hold may have ended.
    private void TryRelease(Runtime r, DateTimeOffset now, List<Action> after)
    {
        if (r.Held is not { } held || Blocker(r, now) != Block.None) return;
        if (r.Running is not null || r.Launching) return;
        r.Held = null;
        var cause = held.Missed ? DutyCause.CatchUp : held.Cause;
        if (cause == DutyCause.CatchUp && r.Duty.Missed == MissedRunPolicy.Skip)
        {
            Skip(r, cause, "Missed while nth was closed or held; this duty skips missed runs.", now);
            return;
        }
        Begin(r, cause, held.Detail, held.Untrusted, dry: false, now, after);
    }

    // Under the lock. The run did not happen, and the schedule moves on.
    private void Skip(Runtime r, DutyCause cause, string why, DateTimeOffset now)
    {
        // Skipped times in a row for the same reason are one line that counts them, so a long run cannot fill the history.
        var last = r.Duty.History.Count > 0 ? r.Duty.History[0] : null;
        var repeat = last is { Outcome: DutyRunOutcome.Skipped } && last.Note == why;
        r.Duty = repeat
            ? r.Duty with { History = new ValueList<DutyRun>(r.Duty.History.Select((h, i) => i == 0 ? h with { At = now, Count = h.Count + 1 } : h)) }
            : r.Duty.WithHistory(new DutyRun(now, cause, DutyRunOutcome.Skipped) { Note = why });
        r.Duty = r.Duty with { Handled = now, Enabled = r.Duty.Enabled && !IsOnce(r.Duty) };
        Persist(r.AssistantId);
        if (!repeat) Note(r, DutyFeedKind.Skipped, $"{r.Duty.Name}: skipped. {why}");
        if (!r.Duty.Enabled) Disarm(r);
    }

    private static bool IsOnce(Duty duty) => duty.Trigger is TimeTrigger { Schedule.Kind: ScheduleKind.Once };

    private void Disarm(Runtime r)
    {
        r.NextDue = null;
        r.NextCheck = null;
        DisposeWatchers(r);
    }

    // Under the lock. Starts a run: the launcher is asked outside the lock.
    private void Begin(Runtime r, DutyCause cause, string? detail, ContentOrigin? untrusted, bool dry, DateTimeOffset now, List<Action> after)
    {
        r.Launching = true;
        var fire = new DutyFire(r.Duty, cause, dry, detail, o.DesktopLocked()) { Untrusted = untrusted };
        after.Add(() => _ = LaunchAsync(r, fire, now));
    }

    private async Task LaunchAsync(Runtime r, DutyFire fire, DateTimeOffset at)
    {
        try
        {
            DutyLaunch result;
            try { result = await o.Launcher.Launch(fire).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                result = DutyLaunch.Refusal("nth could not start it: " + error.Message);
            }
            lock (gate)
            {
                r.Launching = false;
                if (disposed) return;
                var outcome = result.MissionId is null ? DutyRunOutcome.Refused : DutyRunOutcome.Started;
                var run = new DutyRun(at, fire.Cause, outcome) { MissionId = result.MissionId, Note = fire.Dry ? "Dry run" + (result.Refused is null ? "" : ": " + result.Refused) : result.Refused };
                if (result.MissionId is { } mission)
                {
                    r.Running = mission;
                    r.RunningSince = time.GetUtcNow();
                }
                // Only a real run that started counts as the last that ran; a dry run and a refused start are lines in the history.
                r.Duty = fire.Dry || result.MissionId is null ? r.Duty.WithHistory(run) : r.Duty.WithRun(run);
                if (!fire.Dry) r.Duty = r.Duty with { Handled = at, Enabled = r.Duty.Enabled && !IsOnce(r.Duty) };
                if (!r.Duty.Enabled) Disarm(r);
                Persist(r.AssistantId);
                Note(r, result.MissionId is null ? DutyFeedKind.Problem : DutyFeedKind.Fired,
                    result.MissionId is null ? $"{r.Duty.Name}: not started. {result.Refused}" : $"{r.Duty.Name}: {(fire.Dry ? "dry run" : "ran")} ({CauseWords(fire.Cause)}).", result.MissionId);
                Rearm(time.GetUtcNow());
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            o.Faulted?.Invoke("A duty run could not be recorded: " + error.Message);
        }
        Raise();
    }

    private static string CauseWords(DutyCause cause) => cause switch
    {
        DutyCause.Time => "on time",
        DutyCause.Folder => "a folder changed",
        DutyCause.Page => "a page changed",
        DutyCause.AppEvent => "an app event",
        DutyCause.Manual => "run now",
        DutyCause.CatchUp => "catching up",
        _ => "the PC woke for it",
    };

    /// <summary>
    /// A duty's mission ended (the app tells the scheduler): the run's line in the history gets its outcome. A report that ends with
    /// the nothing-changed line is a quiet run (7.1); a run that failed or was stopped is never quiet.
    /// </summary>
    public void MissionEnded(Guid mission, DutyRunOutcome outcome, string? report)
    {
        var after = new List<Action>();
        lock (gate)
        {
            var r = runtimes.Values.FirstOrDefault(x => x.Duty.History.Any(h => h.MissionId == mission));
            if (r is null) return;
            var ended = outcome == DutyRunOutcome.Done && DutyResult.NothingChanged(report) ? DutyRunOutcome.NothingChanged : outcome;
            r.Duty = r.Duty.WithRunUpdated(mission, run => run.Outcome == DutyRunOutcome.Started ? run with { Outcome = ended } : run);
            if (r.Running == mission) r.Running = null;
            Persist(r.AssistantId);
            if (ended == DutyRunOutcome.NothingChanged) Note(r, DutyFeedKind.Quiet, $"{r.Duty.Name}: nothing changed.", mission);
            if (r.Held is not null) TryRelease(r, time.GetUtcNow(), after);
            Rearm(time.GetUtcNow());
        }
        Run(after);
        Raise();
    }

    // ---- folder triggers ----

    private void StartFolder(Runtime r, FolderChangeTrigger trigger)
    {
        FolderTrigger watch;
        lock (gate)
        {
            if (disposed || r.ReadOnly || !r.Duty.Enabled || !ReferenceEquals(r.Duty.Trigger, trigger)) return;
            watch = new FolderTrigger(new FolderTriggerOptions
            {
                Path = trigger.Path,
                Pattern = trigger.Pattern,
                Events = trigger.Events,
                Subfolders = trigger.Subfolders,
                Time = time,
                Granted = path => o.FolderGranted?.Invoke(path) == true,
                Source = o.FolderSource,
            }, change => OnFolderChange(r, change), status => OnWatcherStatus(r, status));
            r.Folder = watch;
        }
        var refused = watch.Start(o.Store.LoadWatch(r.AssistantId, r.Duty.Id));
        if (refused is not null)
        {
            lock (gate)
            {
                if (ReferenceEquals(r.Folder, watch))
                {
                    r.Folder = null;
                    SetStatus(r, refused);
                }
            }
            watch.Dispose();
            Raise();
        }
    }

    private void OnFolderChange(Runtime r, FolderChange change)
    {
        var after = new List<Action>();
        lock (gate)
        {
            if (disposed || r.ReadOnly || !r.Duty.Enabled) return;
            try { o.Store.SaveWatch(r.AssistantId, r.Duty.Id, r.Folder?.State() ?? new JsonObject()); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
            {
                o.Faulted?.Invoke("A folder watch could not be saved: " + error.Message);
            }
            var detail = FolderWords(r.Duty.Trigger as FolderChangeTrigger, change);
            Note(r, DutyFeedKind.WatcherChanged, $"{r.Duty.Name}: {detail}");
            Decide(r, DutyCause.Folder, detail, ContentOrigin.OutsideFile, time.GetUtcNow(), after);
        }
        Run(after);
        Raise();
    }

    internal static string FolderWords(FolderChangeTrigger? trigger, FolderChange change)
    {
        var parts = new List<string>();
        if (change.New.Count > 0) parts.Add("New: " + string.Join(", ", change.New));
        if (change.Changed.Count > 0) parts.Add("Changed: " + string.Join(", ", change.Changed));
        if (change.Removed.Count > 0) parts.Add("Removed: " + string.Join(", ", change.Removed));
        return (trigger is null ? "" : "In " + trigger.Path + " - ") + string.Join("; ", parts);
    }

    private void OnWatcherStatus(Runtime r, string? status)
    {
        lock (gate)
        {
            if (disposed) return;
            SetStatus(r, status);
        }
        Raise();
    }

    // Under the lock. The owner hears of a watcher's problem when it begins and again when it ends.
    private void SetStatus(Runtime r, string? status)
    {
        if (r.Status == status) return;
        r.Status = status;
        Note(r, status is null ? DutyFeedKind.WatcherChanged : DutyFeedKind.Problem, status is null ? $"{r.Duty.Name}: the source is back." : $"{r.Duty.Name}: {status}");
    }

    // ---- page watches ----

    // Under the lock.
    private void StartCheck(Runtime r, DateTimeOffset now, List<Action> after)
    {
        if (r.Duty.Trigger is not PageWatchTrigger page || o.Web is null) return;
        r.Checking = true;
        var state = r.Page;
        after.Add(() => _ = CheckAsync(r, page, state, now));
    }

    private async Task CheckAsync(Runtime r, PageWatchTrigger page, JsonObject? state, DateTimeOffset now)
    {
        PageCheck? check = null;
        try
        {
            check = await o.Web!.Check(page, state, now, stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            o.Faulted?.Invoke("A page watch failed: " + error.Message);
        }
        var after = new List<Action>();
        lock (gate)
        {
            r.Checking = false;
            if (disposed || r.ReadOnly || !r.Duty.Enabled || !ReferenceEquals(r.Duty.Trigger, page)) return;
            var at = time.GetUtcNow();
            r.NextCheck = at + TimeSpan.FromMinutes(page.EveryMinutes);
            if (check is null) check = new PageCheck(false, null, "The look at the page did not finish.", state ?? new JsonObject());
            SetStatus(r, check.Problem);
            if (!check.Failed)
            {
                r.Page = check.State;
                try { o.Store.SaveWatch(r.AssistantId, r.Duty.Id, check.State); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
                {
                    o.Faulted?.Invoke("A page watch could not be saved: " + error.Message);
                }
            }
            if (check.Fired)
            {
                var detail = check.Detail + (check.Diff is null ? "" : "\n" + check.Diff);
                Note(r, DutyFeedKind.WatcherChanged, $"{r.Duty.Name}: {check.Detail}");
                Decide(r, DutyCause.Page, detail, ContentOrigin.Web, at, after);
            }
            Rearm(at);
        }
        Run(after);
        Raise();
    }

    // ---- app events ----

    /// <summary>An app event happened (7.2). Every duty that waits for it runs, with a short cooldown, and never from its own missions' events.</summary>
    public void OnEvent(DutyEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var after = new List<Action>();
        lock (gate)
        {
            if (!started || disposed) return;
            var now = time.GetUtcNow();
            foreach (var r in runtimes.Values.OrderBy(x => x.Sequence).ToList())
            {
                if (r.ReadOnly || !r.Duty.Enabled || r.Duty.Trigger is not AppEventTrigger trigger) continue;
                if (!Matches(r, trigger, e)) continue;
                if (now - r.LastEvent < o.EventCooldown) continue;
                r.LastEvent = now;
                Decide(r, DutyCause.AppEvent, e.Summary, null, now, after);
            }
        }
        Run(after);
        Raise();
    }

    private static bool Matches(Runtime r, AppEventTrigger trigger, DutyEvent e)
    {
        if (trigger.Event != e.Kind) return false;
        if (e.Mission is { } mission && r.Duty.History.Any(h => h.MissionId == mission)) return false;
        if (e.Kind is AppEventKind.ChatFinished or AppEventKind.HeadFailed && e.Mission is not null) return false;
        if (!string.IsNullOrWhiteSpace(trigger.Account) && e.Account?.Contains(trigger.Account.Trim(), StringComparison.OrdinalIgnoreCase) != true) return false;
        if (!string.IsNullOrWhiteSpace(trigger.Window) && e.Window?.Contains(trigger.Window.Trim(), StringComparison.OrdinalIgnoreCase) != true) return false;
        if (trigger.Event != AppEventKind.AccountAbove) return true;
        if (e.Percent is not { } percent || trigger.AbovePercent is not { } limit) return false;
        if (percent < limit)
        {
            r.AboveFired = false;
            return false;
        }
        if (r.AboveFired) return false;
        r.AboveFired = true;
        return true;
    }

    // ---- plumbing ----

    // Under the lock: the owner is told once per change, not at every look.
    private void Note(Runtime r, DutyFeedKind kind, string text, Guid? mission = null)
    {
        try { o.Feed?.Invoke(new DutyFeedLine(kind, text, r.Duty.Id, r.AssistantId, mission)); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            o.Faulted?.Invoke("The feed could not take a duty's line: " + error.Message);
        }
    }

    private static void Run(List<Action> actions)
    {
        foreach (var action in actions) action();
    }

    private void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            o.Faulted?.Invoke("A listener of the duty list failed: " + error.Message);
        }
    }

    public void Dispose()
    {
        List<Runtime> all;
        ITimer? stopped;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            all = [.. runtimes.Values];
            stopped = timer;
            timer = null;
        }
        stopping.Cancel();
        stopped?.Dispose();
        foreach (var r in all) r.Folder?.Dispose();
    }
}
