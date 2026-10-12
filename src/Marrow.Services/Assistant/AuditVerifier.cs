// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-3: verifying the keyed chain and its anchors.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

public enum AuditVerificationStatus
{
    /// <summary>Every line verifies and the anchors match.</summary>
    Verified,

    /// <summary>A line fails, or an anchor does not match; <see cref="AuditVerification.FirstFailure"/> names it.</summary>
    Failed,

    /// <summary>No line yet.</summary>
    Empty,

    /// <summary>Lines exist but no key is held, so nothing can be verified.</summary>
    KeyMissing,
}

/// <summary>A line of the log: its day file's name, its line number in that file (from 1) and its number in the chain.</summary>
public sealed record AuditLineRef(string File, int Line, long? Sequence);

/// <summary>The result the Activity view shows on open (12.5: "Verified" or the first line that fails).</summary>
public sealed record AuditVerification(
    AuditVerificationStatus Status,
    int LinesVerified,
    AuditLineRef? FirstFailure,
    string? Reason,
    IReadOnlyList<AuditLineRef> Recovered)
{
    public string Summary => Status switch
    {
        AuditVerificationStatus.Verified => "Verified",
        AuditVerificationStatus.Empty => "No activity yet",
        AuditVerificationStatus.KeyMissing => "Not verified: the log's key is missing",
        _ => FirstFailure is { } at
            ? $"Line {at.Line.ToString(CultureInfo.InvariantCulture)} of {at.File} fails: {Reason}"
            : $"Fails: {Reason}",
    };
}

/// <summary>One line as read back for the Activity view (AS-21): where it is, its number, its fields and its HMAC.</summary>
public sealed record AuditLogLine(AuditLineRef At, JsonObject Fields, string Hmac);

/// <summary>
/// Reads and verifies the audit log (12.5) while its writer may hold it (files are opened with read-write sharing).
/// Verification walks every complete line of every day file in order and checks the line's shape, its number (one more
/// than the line before, from 1), its <c>prev</c> (the line before's HMAC) and its HMAC under the key; then that the
/// Credential Manager anchor's line is present with the anchor's HMAC; then that each day final the digest printed is
/// still the day's final HMAC. A line that fails is passed over only when the next line is a valid <c>tornLine</c> (or
/// <c>keyReplaced</c>) record naming its SHA-256: the writer's own repair of a line a crash cut off. A <c>logCut</c>
/// record fails verification at its line, every time. A piece after the last newline of the last file (a line being
/// written right now) is not counted.
/// </summary>
public static class AuditVerifier
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static AuditVerification Verify(IAssistantStorageRoots roots, AuditKeyStore keys,
        IReadOnlyDictionary<DateOnly, string>? printedDayFinals = null) =>
        Verify(roots.AuditDirectory, keys, printedDayFinals);

    public static AuditVerification Verify(string directory, AuditKeyStore keys,
        IReadOnlyDictionary<DateOnly, string>? printedDayFinals = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        // The anchor first: the writer flushes its lines before it copies the anchor, so files read after it hold at
        // least the anchored line, even while the writer is appending.
        var anchor = keys.ReadAnchor();
        var lines = Collect(directory, out var earlyTail);
        if (lines.Count == 0 && earlyTail is null)
            return anchor is null
                ? new(AuditVerificationStatus.Empty, 0, null, null, [])
                : Fail(0, null, $"The log is empty, but the anchor in Credential Manager names line {anchor.Sequence}.", []);
        var key = keys.ReadKey();
        if (key is null) return new(AuditVerificationStatus.KeyMissing, 0, null, null, []);
        try
        {
            return Walk(lines, earlyTail, key, anchor, directory, printedDayFinals);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>The final HMAC of a day file, or null when it holds no complete line.</summary>
    public static string? DayFinal(string directory, DateOnly day)
    {
        var path = Path.Combine(directory, AuditLine.DayFileName(day));
        if (!File.Exists(path)) return null;
        string? last = null;
        foreach (var raw in AuditLine.SplitLines(AuditLine.ReadShared(path)).Lines)
            if (AuditLine.TryOpen(Utf8.GetString(raw), out _, out var hmac)) last = hmac;
        return last;
    }

    /// <summary>Every line that opens, verified or not, in chain order (the Activity view's rows; AS-21 filters them).</summary>
    public static IReadOnlyList<AuditLogLine> ReadLines(string directory)
    {
        var rows = new List<AuditLogLine>();
        foreach (var line in Collect(directory, out _))
        {
            if (!AuditLine.TryOpen(line.Text, out var core, out var hmac) || AuditLine.ParseCore(core) is not { } fields) continue;
            rows.Add(new AuditLogLine(line.At with { Sequence = AuditLine.Long(fields, "seq") }, fields, hmac));
        }
        return rows;
    }

    /// <summary>The HMAC of the line numbered <paramref name="sequence"/>, or null when no such line opens.</summary>
    internal static string? HmacOf(string directory, long sequence)
    {
        foreach (var line in Collect(directory, out _))
            if (AuditLine.TryOpen(line.Text, out var core, out var hmac) && AuditLine.ParseCore(core) is { } fields
                && AuditLine.Long(fields, "seq") == sequence) return hmac;
        return null;
    }

    private sealed record RawLine(AuditLineRef At, byte[] Bytes, string Text);

    /// <summary>Every complete line in order; <paramref name="earlyTail"/> is a cut piece in a file that is not the last.</summary>
    private static List<RawLine> Collect(string directory, out AuditLineRef? earlyTail)
    {
        earlyTail = null;
        var lines = new List<RawLine>();
        var files = AuditLine.DayFiles(directory);
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var (raw, tail) = AuditLine.SplitLines(AuditLine.ReadShared(path));
            for (var i = 0; i < raw.Count; i++) lines.Add(new RawLine(new AuditLineRef(name, i + 1, null), raw[i], Utf8.GetString(raw[i])));
            if (tail.Length > 0 && path != files[^1]) earlyTail ??= new AuditLineRef(name, raw.Count + 1, null);
        }
        return lines;
    }

    private static AuditVerification Walk(List<RawLine> lines, AuditLineRef? earlyTail, byte[] key, AuditAnchor? anchor,
        string directory, IReadOnlyDictionary<DateOnly, string>? printedDayFinals)
    {
        var recovered = new List<AuditLineRef>();
        var hmacs = new Dictionary<long, string>();
        var expectedPrevious = AuditLine.ZeroHmac;
        long expectedSequence = 1;
        var verified = 0;
        var keyReplacedAt = lines.FindLastIndex(l => Event(l) == AuditLine.KeyReplaced);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // A cut line is passed over whether or not it happens to verify (a crash may cut just before its newline).
            if (i + 1 < lines.Count && RepairsCut(lines[i + 1], line, key, expectedPrevious, expectedSequence))
            {
                recovered.Add(line.At);
                continue;
            }
            var problem = Check(line, key, expectedPrevious, expectedSequence, i == keyReplacedAt, out var fields, out var hmac);
            if (problem is not null)
            {
                if (i < keyReplacedAt) problem = "it was written under an earlier key that is gone (a new key starts at line "
                    + lines[keyReplacedAt].At.Line.ToString(CultureInfo.InvariantCulture) + " of " + lines[keyReplacedAt].At.File + ").";
                return Fail(verified, line.At with { Sequence = fields is null ? null : AuditLine.Long(fields, "seq") }, problem, recovered);
            }
            var sequence = AuditLine.Long(fields!, "seq")!.Value;
            if (AuditLine.Text(fields!, "event") == AuditLine.LogCut)
                return Fail(verified, line.At with { Sequence = sequence },
                    $"the log was cut: line {AuditLine.Long(fields!, "anchorSeq")} had been written, but the files ended at line {AuditLine.Long(fields!, "foundSeq")}.",
                    recovered);
            hmacs[sequence] = hmac;
            expectedPrevious = hmac;
            expectedSequence = sequence + 1;
            verified++;
        }
        if (earlyTail is not null) return Fail(verified, earlyTail, "the line was cut off and never repaired.", recovered);
        if (anchor is not null)
        {
            if (!hmacs.TryGetValue(anchor.Sequence, out var held))
                return Fail(verified, null, $"lines after {expectedSequence - 1} are missing: the anchor in Credential Manager names line {anchor.Sequence}.", recovered);
            if (!string.Equals(held, anchor.Hmac, StringComparison.Ordinal))
                return Fail(verified, Find(lines, anchor.Sequence), "the line does not match the anchor kept in Credential Manager.", recovered);
        }
        if (printedDayFinals is not null)
        {
            foreach (var (day, printed) in printedDayFinals.OrderBy(p => p.Key))
            {
                if (string.Equals(DayFinal(directory, day), printed, StringComparison.Ordinal)) continue;
                return Fail(verified, new AuditLineRef(AuditLine.DayFileName(day), 0, null),
                    $"the day no longer ends with the HMAC the digest printed for {day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.", recovered);
            }
        }
        return new(AuditVerificationStatus.Verified, verified, null, null, recovered);
    }

    private static string? Check(RawLine line, byte[] key, string expectedPrevious, long expectedSequence, bool chainStart,
        out JsonObject? fields, out string hmac)
    {
        fields = null;
        if (!AuditLine.TryOpen(line.Text, out var core, out hmac)) return "it is not a sealed audit line.";
        fields = AuditLine.ParseCore(core);
        if (fields is null) return "it does not parse.";
        if (AuditLine.Long(fields, "v") != AuditLine.Version) return "its format version is unknown.";
        if (AuditLine.Long(fields, "seq") is not { } sequence) return "it has no line number.";
        var previous = AuditLine.Text(fields, "prev");
        if (!AuditLine.IsHmac(previous)) return "it names no previous HMAC.";
        var start = chainStart && AuditLine.Text(fields, "event") == AuditLine.KeyReplaced;
        if (!start && sequence != expectedSequence)
            return $"it is numbered {sequence}, but line {expectedSequence} comes next (a line was removed, added or moved).";
        if (!string.Equals(previous, start ? AuditLine.ZeroHmac : expectedPrevious, StringComparison.Ordinal))
            return "its previous HMAC is not the line before's (a line was removed, added or moved).";
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(AuditLine.Mac(key, core, previous!)), Convert.FromHexString(hmac)))
            return "its HMAC does not match its contents (it was edited, or sealed without the key).";
        return null;
    }

    /// <summary>Whether <paramref name="next"/> is the writer's repair of <paramref name="cut"/>: a valid record naming its hash.</summary>
    private static bool RepairsCut(RawLine next, RawLine cut, byte[] key, string expectedPrevious, long expectedSequence)
    {
        if (!next.Text.Contains("\"torn\":\"", StringComparison.Ordinal)) return false; // cheap: most lines are actions
        var kind = Event(next);
        if (kind is not (AuditLine.TornLine or AuditLine.KeyReplaced)) return false;
        if (Check(next, key, expectedPrevious, expectedSequence, kind == AuditLine.KeyReplaced, out var fields, out _) is not null) return false;
        return string.Equals(AuditLine.Text(fields!, "torn"), AuditLine.Sha256(cut.Bytes), StringComparison.Ordinal);
    }

    private static string? Event(RawLine line) =>
        AuditLine.TryOpen(line.Text, out var core, out _) && AuditLine.ParseCore(core) is { } fields ? AuditLine.Text(fields, "event") : null;

    private static AuditLineRef? Find(List<RawLine> lines, long sequence)
    {
        foreach (var line in lines)
            if (AuditLine.TryOpen(line.Text, out var core, out _) && AuditLine.ParseCore(core) is { } fields
                && AuditLine.Long(fields, "seq") == sequence) return line.At with { Sequence = sequence };
        return null;
    }

    private static AuditVerification Fail(int verified, AuditLineRef? at, string reason, IReadOnlyList<AuditLineRef> recovered) =>
        new(AuditVerificationStatus.Failed, verified, at, reason, recovered);
}
