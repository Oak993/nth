// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.2, 6, 12.5, 13.1, 13.2; unit AS-11; ED-NEW-dextra-as11-1, -5, -7, -8, -9, -10). No Swift twin.
// The morning digest as the app runs it: gathers the facts off nth's own state on the main thread (never a transcript), asks the spotter for what is worth
// a suggestion, lets the ledger say which are offered today (at most three), lets the cheap model reword them if one is bound, composes the digest, prints
// the last day's activity-log seal in it, keeps it in the store, and says it changed. It sends no toast and raises no decision card by itself: the digest is
// read where it is pinned (13.1: notifications only for decisions). The scheduler runs it as a duty (DigestDuty); the owner can ask for one at any time.
// Accept starts a mission or raises the usual duty card, inside the usual rules; Dismiss teaches the spotter.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>Where the brain's <c>suggest</c> tool puts a line (5.2, 15.5).</summary>
public interface ISuggestionIntake
{
    AssistantToolResult Suggest(string assistantId, string line, string? action);
}

public sealed class DigestServiceOptions
{
    public required AssistantController Controller { get; init; }

    public required AssistantScheduler Scheduler { get; init; }

    public required DigestBook Book { get; init; }

    /// <summary>Runs work on the app's main thread (the runner and the queue live there).</summary>
    public required Action<Action> OnMain { get; init; }

    public required Func<IReadOnlyList<MissionProject>> Projects { get; init; }

    /// <summary>The ids of the assistants whose duties are loaded (the first is Dextra when the setup names no main assistant).</summary>
    public required Func<IReadOnlyList<string>> AssistantIds { get; init; }

    public DigestSetup Setup { get; init; } = new();

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>A line for the board when something about the digest went wrong. The digest goes on.</summary>
    public Action<string>? Faulted { get; init; }
}

public sealed partial class DigestService : ISuggestionIntake, IDisposable
{
    /// <summary>A digest tells what happened since the last morning digest, but never reaches back past this.</summary>
    public static readonly TimeSpan MaxLookback = TimeSpan.FromDays(3);

    /// <summary>With no earlier digest, a day back.</summary>
    public static readonly TimeSpan FirstLookback = TimeSpan.FromDays(1);

    private readonly DigestServiceOptions o;
    private readonly DigestSetup setup;
    private readonly SemaphoreSlim composing = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private bool disposed;

    public DigestService(DigestServiceOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        setup = o.Setup;
        Book.Changed += RaiseChanged;
        Book.Faulted += Fault;
        o.Controller.Intake = this;
    }

    public DigestBook Book => o.Book;

    /// <summary>The newest digest, the one pinned at the top of the home.</summary>
    public Digest? Latest => Book.Latest;

    /// <summary>The last dry run's digest (7.3): shown on the duty's card, never pinned or kept.</summary>
    public Digest? LastPreview { get; private set; }

    /// <summary>The suggestions still open today, for the digest's lane and the home's composer chips (5.2).</summary>
    public IReadOnlyList<Suggestion> Open => Book.Ledger.Open;

    /// <summary>A digest was composed, a suggestion was answered, or the ledger changed. Raised outside any lock.</summary>
    public event Action? Changed;

    private void RaiseChanged() => Changed?.Invoke();

    private void Fault(string message)
    {
        try { o.Faulted?.Invoke(message); }
        catch (Exception error) when (error is not OutOfMemoryException) { o.Controller.Runner.ReportFault("A listener of the digest failed: " + error.Message); }
    }

    private TimeZoneInfo Zone => setup.Zone?.Invoke() ?? TimeZoneInfo.Local;

    private static DateOnly DayOf(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    // ---- asking for one ----

    /// <summary>The owner asks for the digest (6: "by voice or text at any time"): composed now, pinned in place of today's.</summary>
    public Task<Digest?> Ask(CancellationToken cancellationToken = default) => Compose(DigestKind.Asked, preview: false, cancellationToken);

    // ---- composing ----

    /// <summary>
    /// Composes a digest. <paramref name="preview"/> (a dry run, 7.3) reads the same things and composes the same digest but keeps nothing: it is not
    /// stored or pinned, uses none of today's three suggestion places, and the activity log's seal is not remembered. Null when it could not be composed.
    /// </summary>
    public async Task<Digest?> Compose(DigestKind kind, bool preview = false, CancellationToken cancellationToken = default)
    {
        if (disposed) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        var token = linked.Token;
        await composing.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var now = o.Time.GetUtcNow();
            var zone = Zone;
            var today = DayOf(now, zone);
            var gathered = await OnMainAsync(() => Gather(kind, now, zone)).ConfigureAwait(false);
            var facts = gathered.Facts;

            // The activity log's last closed day: its final HMAC is printed so a later rewrite of that day no longer matches (12.5). Read off the main thread.
            var yesterday = today.AddDays(-1);
            var seal = await Task.Run(() => SealOf(yesterday), token).ConfigureAwait(false);

            IReadOnlyList<Suggestion> lane;
            int offered;
            var added = 0;
            if (preview)
            {
                var (ledger, _) = Book.Ledger.Admit(gathered.Candidates, now, today);
                lane = ledger.Open;
                offered = ledger.OfferedToday;
            }
            else
            {
                IReadOnlyList<Suggestion> fresh = [];
                var after = Book.Update(l =>
                {
                    var next = l.Admit(gathered.Candidates, now, today);
                    fresh = next.Offered;
                    return next.Ledger;
                });
                added = fresh.Count;
                if (fresh.Count > 0) after = await RewordAsync(fresh, after, token).ConfigureAwait(false);
                lane = after.Open;
                offered = after.OfferedToday;
            }

            var digest = DigestComposer.Compose(facts with { Suggestions = lane, OfferedToday = offered, Seal = seal });
            if (preview)
            {
                LastPreview = digest;
                RaiseChanged();
                return digest;
            }
            Book.Add(digest);
            o.Controller.AuditDigest(MainAssistantId(), "compose", "ReadLocal",
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{kind}: {digest.Sections.Count} sections, {lane.Count} suggestions in the lane, {added} newly offered"));
            if (kind == DigestKind.Scheduled) FoldNotes();
            return digest;
        }
        finally
        {
            composing.Release();
        }
    }

    private DigestSeal? SealOf(DateOnly day)
    {
        if (setup.DayFinal is not { } read) return null;
        try { return read(day) is { Length: > 0 } hmac ? new DigestSeal(day, hmac) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Fault("The activity log's last day could not be read for the digest's seal: " + error.Message);
            return null;
        }
    }

    // The cheap model rewords the lines nth wrote (5.2). It gets a time limit on nth's own clock; a model that is slow, fails or writes something the check
    // refuses leaves the lines as they were.
    private async Task<SuggestionLedger> RewordAsync(IReadOnlyList<Suggestion> added, SuggestionLedger ledger, CancellationToken token)
    {
        if (setup.Rewriter is not { } model) return ledger;
        IReadOnlyList<WordedSuggestion> worded;
        using (var limit = new CancellationTokenSource(setup.ModelTimeout, o.Time))
        using (var both = CancellationTokenSource.CreateLinkedTokenSource(token, limit.Token))
        {
            worded = await SuggestionWriter.Reword(model, added, both.Token).ConfigureAwait(false);
        }
        if (worded.Count == 0) return ledger;
        return Book.Update(l => worded.Aggregate(l, (current, w) => current.Reworded(w.Key, w.Line, w.Cost)));
    }

    private void FoldNotes()
    {
        if (setup.FoldNotes is not { } fold) return;
        // The day's folding of the notes (8.2) runs in the morning with the digest; it is idempotent for the day and never holds the digest up.
        _ = Task.Run(() =>
        {
            try { fold(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
            {
                Fault("The notes could not be folded: " + error.Message);
            }
        });
    }

    private Task<T> OnMainAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        o.OnMain(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception error) when (error is not OutOfMemoryException) { done.SetException(error); }
        });
        return done.Task;
    }

    // ---- pinned in the home ----

    /// <summary>
    /// The digest is pinned at the top of a home chat (AS-8 calls this when it mounts the card): what the digest was made from that nth cannot vouch for
    /// (a duty's finding, an Astur's paragraph) taints that chat, as anything made from it does (12.4). A clean digest changes nothing.
    /// </summary>
    public void PinnedIn(Guid homeThread)
    {
        if (Latest is { Taint.IsTainted: true } digest) o.Controller.AbsorbHome(homeThread, digest.Taint);
    }

    // ---- the Morning digest duty ----

    /// <summary>
    /// Makes the Morning digest duty once (6: 08:00 by default). A duty the owner deleted afterwards is not made again, and a duty with its id that is
    /// already there is left as it is. Returns why it was not made, or null.
    /// </summary>
    public string? EnsureDuty()
    {
        if (Book.DutyMade) return null;
        if (Book.ReadOnly) return "The digest file could not be written, so the Morning digest is not scheduled.";
        if (o.Scheduler.Find(DigestDuty.Id) is not null)
        {
            Book.MarkDutyMade();
            return null;
        }
        var main = MainAssistantId();
        if (main is null) return "There is no assistant to own the Morning digest yet.";
        var (saved, problem) = o.Scheduler.Add(DigestDuty.Make(main, Zone.Id));
        if (saved is null) return problem;
        Book.MarkDutyMade();
        return null;
    }

    private string? MainAssistantId() => setup.MainAssistantId?.Invoke() ?? o.Controller.MainAssistantId ?? o.AssistantIds().FirstOrDefault();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stopping.Cancel();
        Book.Changed -= RaiseChanged;
        Book.Faulted -= Fault;
        if (ReferenceEquals(o.Controller.Intake, this)) o.Controller.Intake = null;
        stopping.Dispose();
    }

    // The brain's own line: its key names its words, so the same line is never offered twice.
    internal static string BrainKey(string line) => "brain:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line.Trim().ToLowerInvariant())))[..16];
}
