// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.2, 6, 12.5, 15.3; unit AS-11; ED-NEW-dextra-as11-6). No Swift twin.
// What the digest keeps in the store (digest.json): the last fourteen digests (the day's is pinned in the home), the activity log's final HMAC
// of each day a digest printed (so a later rewrite of that day no longer matches, 12.5), the suggestion ledger (today's offers, what the owner taught),
// and whether the Morning digest duty has been made. The format carries a version; a file with a newer version, or one that exists and cannot be
// read, is shown from memory and never written over; fields this build does not know are written back; nothing grows without bound.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed class DigestBook : IDisposable
{
    public const int FormatVersion = 1;

    /// <summary>Digests kept, newest first.</summary>
    public const int DigestsKept = 14;

    /// <summary>Days whose printed final is kept: the activity log is checked against them (AuditVerifier takes them as printed finals).</summary>
    public const int FinalsKept = 90;

    private readonly object gate = new();
    private readonly object writing = new();      // one write at a time, so an older document never lands after a newer one
    private List<Digest> digests = [];
    private SortedDictionary<DateOnly, string> finals = [];
    private SuggestionLedger ledger = SuggestionLedger.Empty;
    private bool dutyMade;
    private JsonObject? unknown;
    private AssistantStore? store;
    private bool dirty;
    private bool disposed;

    /// <summary>Why the file is shown and not written (a newer version's, or one that could not be read); null when it is written as it changes.</summary>
    public string? Problem { get; private set; }

    public bool ReadOnly => Problem is not null;

    /// <summary>Something changed (a digest, the ledger). Raised outside the book's lock.</summary>
    public event Action? Changed;

    /// <summary>A write failed (the line for the board). The next change tries again.</summary>
    public event Action<string>? Faulted;

    // ---- reading ----

    /// <summary>The digests, newest first.</summary>
    public IReadOnlyList<Digest> Digests
    {
        get { lock (gate) return [.. digests]; }
    }

    public Digest? Latest
    {
        get { lock (gate) return digests.Count > 0 ? digests[0] : null; }
    }

    /// <summary>The newest digest composed on this local day, or null.</summary>
    public Digest? OfDay(DateOnly day, TimeZoneInfo zone)
    {
        lock (gate) return digests.FirstOrDefault(d => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(d.At, zone).DateTime) == day);
    }

    public SuggestionLedger Ledger
    {
        get { lock (gate) return ledger; }
    }

    /// <summary>Whether the Morning digest duty was made once (the owner may delete it; it is not made again).</summary>
    public bool DutyMade
    {
        get { lock (gate) return dutyMade; }
    }

    /// <summary>The day finals the digests printed, for the activity log's check; null when none was printed (AuditVerifier then checks nothing against them).</summary>
    public IReadOnlyDictionary<DateOnly, string>? PrintedDayFinals()
    {
        lock (gate) return finals.Count == 0 ? null : new Dictionary<DateOnly, string>(finals);
    }

    // ---- changing ----

    /// <summary>
    /// Keeps a digest as the newest. If it printed a day's final, that final is remembered, the first one printed for the day: a day whose log
    /// has changed since is not given a new final that would hide it.
    /// </summary>
    public void Add(Digest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        lock (gate)
        {
            digests.RemoveAll(d => d.Id == digest.Id);
            digests.Insert(0, digest);
            if (digests.Count > DigestsKept) digests.RemoveRange(DigestsKept, digests.Count - DigestsKept);
            if (digest.Seal is { } seal && !finals.ContainsKey(seal.Day))
            {
                finals[seal.Day] = seal.Hmac;
                while (finals.Count > FinalsKept) finals.Remove(finals.Keys.First());
            }
            Touch();
        }
        Raise();
    }

    /// <summary>The first final printed for a day, if one was.</summary>
    public string? PrintedFinal(DateOnly day)
    {
        lock (gate) return finals.GetValueOrDefault(day);
    }

    /// <summary>Changes the ledger. <paramref name="change"/> returns the ledger after, or null when nothing changed. Returns what the ledger is now.</summary>
    public SuggestionLedger Update(Func<SuggestionLedger, SuggestionLedger?> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        SuggestionLedger now;
        bool changed;
        lock (gate)
        {
            var next = change(ledger);
            changed = next is not null && !ReferenceEquals(next, ledger) && !next.Equals(ledger);
            if (changed) ledger = next!;
            now = ledger;
            if (changed) Touch();
        }
        if (changed) Raise();
        return now;
    }

    public void MarkDutyMade()
    {
        lock (gate)
        {
            if (dutyMade) return;
            dutyMade = true;
            Touch();
        }
        Raise();
    }

    // ---- the store ----

    /// <summary>Loads what the store holds and writes there from now on. A file the book may not write is read and left alone.</summary>
    public void Attach(AssistantStore target)
    {
        ArgumentNullException.ThrowIfNull(target);
        JsonObject? document;
        bool damaged;
        try { document = target.DigestDocument(out damaged); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            lock (gate)
            {
                store = target;
                Problem = "The digest file could not be read (" + error.Message + "), so nth keeps today's digest in memory only.";
            }
            return;
        }
        lock (gate)
        {
            store = target;
            Load(document, damaged);
        }
    }

    // Under the lock.
    private void Load(JsonObject? document, bool damaged)
    {
        digests = [];
        finals = [];
        ledger = SuggestionLedger.Empty;
        dutyMade = false;
        unknown = null;
        Problem = null;
        if (damaged)
        {
            Problem = "The digest file could not be read, so nth keeps today's digest in memory only and leaves the file as it is.";
            return;
        }
        if (document is null) return;
        if (AssistantJson.Number(document["version"]) is { } version && version > FormatVersion)
        {
            Problem = "The digest file is from a newer nth, so nth shows nothing from it and leaves it as it is.";
            return;
        }
        unknown = (JsonObject)document.DeepClone();
        foreach (var known in new[] { "version", "digests", "finals", "ledger", "dutyMade" }) unknown.Remove(known);
        digests = [.. (document["digests"] as JsonArray ?? []).Select(DigestJson.ReadDigest).OfType<Digest>().Take(DigestsKept)];
        foreach (var entry in (document["finals"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (DigestJson.Text(entry["day"], 10) is { } dayText && DateOnly.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && DigestJson.Text(entry["hmac"], 128) is { Length: > 0 } hmac && !finals.ContainsKey(day)) finals[day] = hmac;
        }
        while (finals.Count > FinalsKept) finals.Remove(finals.Keys.First());
        ledger = DigestJson.ReadLedger(document["ledger"]);
        dutyMade = document["dutyMade"] is JsonValue made && made.TryGetValue<bool>(out var yes) && yes;
    }

    /// <summary>The document to store, or null when the file is one the book may not write.</summary>
    public JsonObject? ToJson()
    {
        lock (gate) return ToJsonLocked();
    }

    private JsonObject? ToJsonLocked()
    {
        if (Problem is not null) return null;
        var document = unknown is null ? new JsonObject() : (JsonObject)unknown.DeepClone();
        document["version"] = FormatVersion;
        document["dutyMade"] = dutyMade;
        document["digests"] = new JsonArray([.. digests.Select(d => (JsonNode)DigestJson.ToJson(d))]);
        document["finals"] = new JsonArray([.. finals.Select(f => (JsonNode)new JsonObject { ["day"] = f.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["hmac"] = f.Value })]);
        document["ledger"] = DigestJson.ToJson(ledger);
        return document;
    }

    // Under the lock: a change is written soon, off the thread that made it (a tap on Accept is a click).
    private void Touch()
    {
        if (disposed || store is null || Problem is not null) return;
        dirty = true;
        _ = Task.Run(Write);
    }

    private void Write()
    {
        lock (writing)
        {
            JsonObject? document;
            AssistantStore? target;
            lock (gate)
            {
                if (!dirty || store is null) return;
                dirty = false;
                target = store;
                document = ToJsonLocked();
            }
            if (document is null) return;
            try { target.SaveDigestDocument(document); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                lock (gate) dirty = true;
                Faulted?.Invoke("The digest could not be saved: " + error.Message);
            }
        }
    }

    /// <summary>Writes what changed now, and returns when it is written.</summary>
    public void Flush() => Write();

    private void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Faulted?.Invoke("A listener of the digest failed: " + error.Message);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
        }
        Flush();
        lock (gate) disposed = true;
    }
}
