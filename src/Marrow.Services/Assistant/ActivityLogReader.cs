// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-1 (what the view reads and how the chain check is used), ED-NEW-dextra-as21-2 (rows, hostile lines).
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

/// <summary>
/// Where the Activity view reads from: the audit folder (12.5), the key store the chain check needs, and, once the morning
/// digest exists (AS-11), the day finals it printed (<see cref="PrintedDayFinals"/>), so a rewritten day no longer matches
/// the digest either.
/// </summary>
public sealed record ActivityLogSource(string Directory, AuditKeyStore Keys, Func<IReadOnlyDictionary<DateOnly, string>?>? PrintedDayFinals = null)
{
    public static ActivityLogSource For(IAssistantStorageRoots roots, AuditKeyStore keys, Func<IReadOnlyDictionary<DateOnly, string>?>? printedDayFinals = null) =>
        new(roots.AuditDirectory, keys, printedDayFinals);
}

/// <summary>A value that appears in the log, how many rows carry it and the number of the last one (for newest first).</summary>
public sealed record ActivityFacet(string Value, int Count, long LastSequence);

/// <summary>A day that has lines, and how many.</summary>
public sealed record ActivityDayFacet(DateOnly Day, int Count);

/// <summary>Every value a filter can pick from, over all the rows listed.</summary>
public sealed record ActivityFacets(
    IReadOnlyList<ActivityFacet> Assistants,
    IReadOnlyList<ActivityFacet> Missions,
    IReadOnlyList<ActivityFacet> Agents,
    IReadOnlyList<ActivityFacet> Classes,
    IReadOnlyList<ActivityFacet> Grants,
    IReadOnlyList<ActivityDayFacet> Days,
    int NoMission,
    int NoGrant)
{
    public static ActivityFacets Empty { get; } = new([], [], [], [], [], [], 0, 0);
}

/// <summary>What the Activity view holds after it opens: the chain check's result and the rows, oldest first.</summary>
public sealed class ActivitySnapshot
{
    /// <summary>"Verified" or the first line that fails (12.5).</summary>
    public required AuditVerification Verification { get; init; }

    /// <summary>The newest lines of the log in chain order (oldest first), at most <see cref="ActivityLogReader.MaxRows"/>.</summary>
    public required IReadOnlyList<ActivityRow> Rows { get; init; }

    /// <summary>Every complete line the log holds.</summary>
    public int TotalLines { get; init; }

    /// <summary>Lines older than the rows listed (a very long log lists its newest lines; the exports still cover every line).</summary>
    public int NotListed => TotalLines - Rows.Count;

    public required ActivityFacets Facets { get; init; }

    public DateTimeOffset LoadedAt { get; init; }

    /// <summary>The row the chain check names as the first that fails, when it is listed.</summary>
    public ActivityRow? FirstFailureRow
    {
        get
        {
            if (Verification.FirstFailure is not { Line: > 0 } at) return null;
            foreach (var row in Rows)
                if (string.Equals(row.At.File, at.File, StringComparison.Ordinal) && row.At.Line == at.Line) return row;
            return null;
        }
    }
}

/// <summary>
/// Reads the audit log for the Activity view (12.5). Opening runs the chain check (<see cref="AuditVerifier"/>) first and
/// then reads every complete line of every day file in order, one line at a time and never more than
/// <see cref="MaxLineBytes"/> of one, so a huge or planted line cannot exhaust memory. A line that is not a sealed audit
/// line is not dropped: it becomes an <see cref="ActivityRowKind.Unreadable"/> row, so the first line that fails is always
/// something the owner can see. A piece after the last newline of a file (a line being written right now) is not a line yet.
/// </summary>
public static class ActivityLogReader
{
    /// <summary>The most rows a snapshot lists: the newest. 250,000 lines is about a year of 700 actions a day.</summary>
    public const int MaxRows = 250_000;

    /// <summary>The longest line read; a longer one is shown as unreadable. The writer keeps a line under about 600 KB (64 inputs of 8,192 characters).</summary>
    public const int MaxLineBytes = 1 << 20;

    private const int ExcerptChars = 300;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Checks the chain, then lists the log. Throws only for what cannot be read at all (the folder, a locked file).</summary>
    public static ActivitySnapshot Load(ActivityLogSource source, CancellationToken cancellation = default, int maxRows = MaxRows)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        var verification = AuditVerifier.Verify(source.Directory, source.Keys, source.PrintedDayFinals?.Invoke());
        cancellation.ThrowIfCancellationRequested();
        var kept = new Queue<ActivityRow>();
        var total = 0;
        foreach (var row in Stream(source.Directory, verification, cancellation))
        {
            total++;
            kept.Enqueue(row);
            if (kept.Count > maxRows) kept.Dequeue();
        }
        var rows = kept.ToList();
        return new ActivitySnapshot
        {
            Verification = verification,
            Rows = rows,
            TotalLines = total,
            Facets = FacetsOf(rows),
            LoadedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Every complete line of the log in chain order, one at a time (the exports use this, so they cover lines older than a
    /// snapshot lists). <paramref name="verification"/> sets each row's <see cref="ActivityRow.Trust"/>; null leaves every
    /// row <see cref="ActivityTrust.Unchecked"/>.
    /// </summary>
    public static IEnumerable<ActivityRow> Stream(string directory, AuditVerification? verification, CancellationToken cancellation = default)
    {
        var policy = new TrustPolicy(verification);
        var ordinal = 0;
        foreach (var path in AuditLine.DayFiles(directory))
        {
            var name = Path.GetFileName(path);
            var day = AuditLine.DayOf(name) ?? default;
            var number = 0;
            foreach (var raw in CompleteLines(path))
            {
                cancellation.ThrowIfCancellationRequested();
                number++;
                var recovered = policy.IsRecovered(name, number);
                yield return Parse(raw, new AuditLineRef(name, number, null), day, policy.Of(name, number, ordinal++, recovered), recovered);
            }
        }
    }

    // ---- reading ----------------------------------------------------------------------------------------------------

    private readonly record struct RawLine(string Text, long Length, bool TooLong);

    private static FileStream? OpenShared(string path)
    {
        try
        {
            // Read-write sharing (and delete): the writer holds the day's file for writing and refuses nothing else.
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static IEnumerable<RawLine> CompleteLines(string path)
    {
        using var stream = OpenShared(path);
        if (stream is null) yield break;
        var buffer = new byte[65536];
        var line = new MemoryStream();
        long length = 0;
        var tooLong = false;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var start = 0;
            while (start < read)
            {
                var newline = Array.IndexOf(buffer, (byte)'\n', start, read - start);
                var end = newline < 0 ? read : newline;
                var count = end - start;
                length += count;
                if (!tooLong)
                {
                    if (length > MaxLineBytes)
                    {
                        tooLong = true;
                        line.SetLength(0);
                    }
                    else
                    {
                        line.Write(buffer, start, count);
                    }
                }
                if (newline < 0) break;
                yield return tooLong ? new RawLine("", length, true) : new RawLine(Utf8.GetString(line.GetBuffer(), 0, (int)line.Length), length, false);
                line.SetLength(0);
                length = 0;
                tooLong = false;
                start = newline + 1;
            }
        }
        // What is left has no newline: a line the writer is writing right now (or one a crash cut off), not a line yet.
    }

    // ---- parsing ----------------------------------------------------------------------------------------------------

    private static ActivityRow Parse(RawLine raw, AuditLineRef at, DateOnly day, ActivityTrust trust, bool recovered)
    {
        if (raw.TooLong)
            return Unreadable(at, day, trust, $"The line is longer than {MaxLineBytes / 1024 / 1024} MiB, so it was not read.", null);
        if (!AuditLine.TryOpen(raw.Text, out var core, out var hmac))
            return Unreadable(at, day, trust, recovered ? CutNote : "It is not a sealed audit line.", raw.Text);
        if (AuditLine.ParseCore(core) is not { } fields)
            return Unreadable(at, day, trust, "It does not parse.", raw.Text);
        if (AuditLine.Sequence(fields) is not { } sequence || !TryTime(AuditLine.Text(fields, "time"), out var time))
            return Unreadable(at, day, trust, "It has no line number or no time.", raw.Text);
        var evt = AuditLine.Text(fields, "event");
        return new ActivityRow
        {
            At = at with { Sequence = sequence },
            Kind = evt is null ? ActivityRowKind.Action : ActivityRowKind.Record,
            Day = day,
            Time = time,
            Event = evt is null ? null : ActivityText.Safe(evt),
            Assistant = Text(fields, "assistant") ?? "",
            Mission = Text(fields, "mission"),
            Agent = Text(fields, "agent"),
            Tool = Text(fields, "tool") ?? "",
            Class = Text(fields, "class") ?? "",
            Target = Text(fields, "target"),
            Inputs = Inputs(fields),
            InputsNotLogged = (int)Math.Clamp(AuditLine.Long(fields, "inputsNotLogged") ?? 0, 0, int.MaxValue),
            Result = Text(fields, "result"),
            CostUsd = fields["costUsd"] is JsonValue cost && cost.TryGetValue<decimal>(out var usd) ? usd : null,
            Tokens = AuditLine.Long(fields, "tokens"),
            AllowedBy = Text(fields, "allowedBy"),
            Note = evt is null ? null : RecordNote(evt, fields),
            Prev = AuditLine.Text(fields, "prev") ?? "",
            Hmac = hmac,
            Trust = trust,
        };
    }

    private const string CutNote = "A line a crash cut off. The log closed it and recorded its fingerprint, so it is not part of the chain.";

    private static ActivityRow Unreadable(AuditLineRef at, DateOnly day, ActivityTrust trust, string why, string? text)
    {
        var excerpt = text is null ? "" : AuditRedactor.Redact(ActivityText.Safe(text.Length > 4096 ? text[..4096] : text));
        if (excerpt.Length > ExcerptChars) excerpt = excerpt[..ExcerptChars] + "…";
        return new ActivityRow
        {
            At = at,
            Kind = ActivityRowKind.Unreadable,
            Day = day,
            Note = excerpt.Length == 0 ? why : why + " " + excerpt,
            Trust = trust,
        };
    }

    private static string? Text(JsonObject fields, string name) => AuditLine.Text(fields, name) is { } text ? ActivityText.Safe(text) : null;

    private static bool TryTime(string? text, out DateTimeOffset time)
    {
        time = default;
        return text is not null && DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time);
    }

    private static IReadOnlyList<ActivityInput> Inputs(JsonObject fields)
    {
        if (fields["inputs"] is not JsonArray array || array.Count == 0) return [];
        var inputs = new List<ActivityInput>(Math.Min(array.Count, AuditLine.MaxInputs));
        foreach (var node in array)
        {
            if (inputs.Count >= AuditLine.MaxInputs) break;
            if (node is not JsonObject input || Text(input, "name") is not { } name) continue;
            if (input["text"] is JsonValue text && text.TryGetValue<string>(out var value))
                inputs.Add(new ActivityInput(name, AuditInputKind.Text, ActivityText.Safe(value)));
            else if (AuditLine.Long(input, "typedLength") is { } typed)
                inputs.Add(new ActivityInput(name, AuditInputKind.Typed, TypedLength: (int)Math.Clamp(typed, 0, int.MaxValue)));
            else if (input.ContainsKey("secret"))
                inputs.Add(new ActivityInput(name, AuditInputKind.Secret));
            else if (input["number"] is JsonValue number && number.TryGetValue<double>(out var numeric))
                inputs.Add(new ActivityInput(name, AuditInputKind.Number, Number: numeric));
            else if (input["number"] is JsonValue words && words.TryGetValue<string>(out var spelled))
            {
                // NaN and Infinity are written as words; they read back as the numbers they are, anything else as the text it is.
                inputs.Add(double.TryParse(spelled, NumberStyles.Float, CultureInfo.InvariantCulture, out var special)
                    ? new ActivityInput(name, AuditInputKind.Number, Number: special)
                    : new ActivityInput(name, AuditInputKind.Text, ActivityText.Safe(spelled)));
            }
            else if (input["flag"] is JsonValue flag && flag.TryGetValue<bool>(out var on))
                inputs.Add(new ActivityInput(name, AuditInputKind.Flag, Flag: on));
        }
        return inputs;
    }

    private static string RecordNote(string evt, JsonObject fields) => evt switch
    {
        AuditLine.TornLine => "A line a crash cut off. The log closed it and recorded its fingerprint" + Fingerprint(fields) + ".",
        AuditLine.KeyReplaced => "The log's key was gone, so a new key and a new chain begin here; the lines before it cannot be verified"
            + (AuditLine.Text(fields, "torn") is not null ? ". A cut line was recorded with it" : "") + ".",
        AuditLine.LogCut => $"The log was cut: line {AuditLine.Long(fields, "anchorSeq")?.ToString(CultureInfo.InvariantCulture) ?? "?"} had been written, "
            + $"but the files ended at line {AuditLine.Long(fields, "foundSeq")?.ToString(CultureInfo.InvariantCulture) ?? "?"}.",
        _ => "A record the log wrote about itself: " + ActivityText.Safe(evt) + ".",
    };

    private static string Fingerprint(JsonObject fields) =>
        AuditLine.Text(fields, "torn") is { Length: >= 12 } torn && AuditLine.IsHmac(torn) ? " (" + torn[..12] + ")" : "";

    // ---- trust ------------------------------------------------------------------------------------------------------

    /// <summary>Which rows the chain check vouches for (ED-NEW-dextra-as21-1).</summary>
    private sealed class TrustPolicy
    {
        private readonly AuditVerification? verification;
        private readonly HashSet<(string File, int Line)> recovered = [];

        public TrustPolicy(AuditVerification? verification)
        {
            this.verification = verification;
            if (verification is null) return;
            foreach (var at in verification.Recovered) recovered.Add((at.File, at.Line));
        }

        public bool IsRecovered(string file, int line) => recovered.Contains((file, line));

        public ActivityTrust Of(string file, int line, int ordinal, bool isRecovered)
        {
            // A cut piece the log repaired is not a link of the chain: nothing vouches for it, and nothing fails because of it.
            if (verification is null || isRecovered) return ActivityTrust.Unchecked;
            switch (verification.Status)
            {
                case AuditVerificationStatus.Verified:
                    // The check read the files once. Lines written since are past what it covered.
                    return ordinal < verification.LinesVerified + verification.Recovered.Count ? ActivityTrust.Verified : ActivityTrust.Unchecked;
                case AuditVerificationStatus.Failed:
                    if (verification.FirstFailure is not { } failure) return ActivityTrust.NotVerified; // the log as a whole does not verify
                    var order = string.CompareOrdinal(file, failure.File);
                    return order < 0 || (order == 0 && line < failure.Line) ? ActivityTrust.Verified : ActivityTrust.NotVerified;
                default:
                    return ActivityTrust.Unchecked; // empty, no key, or Credential Manager refused: nothing was checked
            }
        }
    }

    // ---- facets -----------------------------------------------------------------------------------------------------

    private sealed class Tally
    {
        public int Count;
        public long Last;
    }

    private static ActivityFacets FacetsOf(List<ActivityRow> rows)
    {
        var assistants = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var missions = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var agents = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var classes = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var grants = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var days = new SortedDictionary<DateOnly, int>(Comparer<DateOnly>.Create((a, b) => b.CompareTo(a)));
        var noMission = 0;
        var noGrant = 0;
        foreach (var row in rows)
        {
            if (row.Kind == ActivityRowKind.Unreadable)
            {
                days[row.Day] = days.GetValueOrDefault(row.Day) + 1;
                continue;
            }
            Add(assistants, row.Assistant, row.Sequence);
            if (row.Mission is { Length: > 0 } mission) Add(missions, mission, row.Sequence);
            else noMission++;
            if (row.Agent is { Length: > 0 } agent) Add(agents, agent, row.Sequence);
            Add(classes, row.Class, row.Sequence);
            if (row.AllowedBy is { Length: > 0 } grant) Add(grants, grant, row.Sequence);
            else noGrant++;
            days[row.Day] = days.GetValueOrDefault(row.Day) + 1;
        }
        return new ActivityFacets(Sorted(assistants, byRecent: false), Sorted(missions, byRecent: true), Sorted(agents, byRecent: true),
            Sorted(classes, byRecent: false), Sorted(grants, byRecent: true), days.Select(d => new ActivityDayFacet(d.Key, d.Value)).ToList(),
            noMission, noGrant);
    }

    private static void Add(Dictionary<string, Tally> tallies, string value, long sequence)
    {
        if (!tallies.TryGetValue(value, out var tally)) tallies[value] = tally = new Tally();
        tally.Count++;
        tally.Last = Math.Max(tally.Last, sequence);
    }

    private static List<ActivityFacet> Sorted(Dictionary<string, Tally> tallies, bool byRecent)
    {
        var facets = tallies.Select(t => new ActivityFacet(t.Key, t.Value.Count, t.Value.Last));
        return (byRecent
            ? facets.OrderByDescending(f => f.LastSequence).ThenBy(f => f.Value, StringComparer.Ordinal)
            : facets.OrderBy(f => f.Value, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Value, StringComparer.Ordinal)).ToList();
    }
}
