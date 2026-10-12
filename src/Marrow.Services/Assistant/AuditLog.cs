// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-3: the single writer of the keyed audit chain.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Marrow.Services.Assistant;

/// <summary>Where an appended line landed: its number, its HMAC and its day file.</summary>
public sealed record AuditReceipt(long Sequence, string Hmac, string File);

/// <summary>A second writer on the same audit folder: refused (12.5, "there is a single writer").</summary>
public sealed class AuditLogBusyException(string directory, Exception inner)
    : IOException($"The audit log at \"{directory}\" already has a writer.", inner);

/// <summary>
/// The audit log's one writer (12.5). Every component logs through the one instance the app holds. Each action becomes
/// one line of <c>&lt;audit&gt;\yyyy-mm-dd.jsonl</c> (the local day; a clock that steps back never reopens an earlier
/// day), carrying an HMAC-SHA256 over the line and the previous line's HMAC, keyed by a 256-bit key held only in
/// Credential Manager (<see cref="AuditKeyStore"/>). The writer holds <c>writer.lock</c> with no sharing and the day's
/// file with write access for itself and read-only sharing for others, so no second writer, in this process or another,
/// can open either. After each write batch the lines are flushed to disk and the head is copied into Credential Manager
/// as the anchor.
/// <para>At open it repairs, in the chain and never by deleting a byte: a last line a crash cut off is closed with a
/// newline and followed by a <c>tornLine</c> record naming its SHA-256; a key that is gone while lines exist is replaced
/// and a <c>keyReplaced</c> record starts a new chain (the earlier lines no longer verify); and an anchor the files do
/// not reach, or that names a different HMAC, is kept as a <c>logCut</c> record, so a cut log never heals silently.</para>
/// <para>Limit, stated plainly (12.5): this detects edits by agents and tools; it cannot stop malware running as the
/// owner, which can read the same Credential Manager entry.</para>
/// </summary>
public sealed class AuditLog : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly AuditEntry SystemEntry = new() { Assistant = "nth", Tool = "audit", ActionClass = "Audit" };

    private readonly Lock gate = new();
    private readonly AuditKeyStore keys;
    private readonly TimeProvider time;
    private byte[] key;
    private FileStream? lockHandle;
    private FileStream? dayStream;
    private DateOnly dayOpen;
    private long headSequence;
    private string headHmac;
    private string headFile;

    private AuditLog(string directory, AuditKeyStore keys, TimeProvider time, FileStream lockHandle, byte[] key,
        long headSequence, string headHmac, string headFile)
    {
        Directory = directory;
        this.keys = keys;
        this.time = time;
        this.lockHandle = lockHandle;
        this.key = key;
        this.headSequence = headSequence;
        this.headHmac = headHmac;
        this.headFile = headFile;
    }

    public string Directory { get; }

    /// <summary>The last line's number (0 before the first line).</summary>
    public long HeadSequence
    {
        get { lock (gate) return headSequence; }
    }

    /// <summary>The last line's HMAC (64 zeros before the first line).</summary>
    public string HeadHmac
    {
        get { lock (gate) return headHmac; }
    }

    /// <summary>True when the last anchor copy failed; the next batch tries again.</summary>
    public bool AnchorBehind { get; private set; }

    /// <summary>The system records this open wrote (tornLine, keyReplaced, logCut), for the lead's start-up report.</summary>
    public IReadOnlyList<string> OpenRecords { get; private set; } = [];

    public static AuditLog Open(IAssistantStorageRoots roots, AuditKeyStore keys, TimeProvider? time = null) =>
        Open(roots.AuditDirectory, keys, time);

    /// <summary>
    /// Opens the writer: takes the lock (else <see cref="AuditLogBusyException"/>), reads the key (making one when none
    /// is held; <see cref="AuditKeyUnavailableException"/> when Credential Manager refuses), finds the chain's head in
    /// the day files and writes the repair records described on the type.
    /// </summary>
    public static AuditLog Open(string directory, AuditKeyStore keys, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        time ??= TimeProvider.System;
        directory = Path.GetFullPath(directory);
        System.IO.Directory.CreateDirectory(directory);
        FileStream handle;
        try
        {
            handle = new FileStream(Path.Combine(directory, AuditLine.LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new AuditLogBusyException(directory, error);
        }
        try
        {
            var head = FindHead(directory);
            var key = keys.ReadKey();
            var replaced = false;
            if (key is null)
            {
                key = keys.CreateKey();
                replaced = head.Sequence > 0 || head.Torn is not null;
            }
            var log = new AuditLog(directory, keys, time, handle, key, head.Sequence, head.Hmac, head.File ?? "");
            log.Repair(head, replaced);
            return log;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Appends one action, flushed to disk and anchored before this returns.</summary>
    public AuditReceipt Append(AuditEntry entry) => AppendBatch([entry])[0];

    /// <summary>Appends actions in order as one batch: one flush to disk and one anchor copy for all of them.</summary>
    public IReadOnlyList<AuditReceipt> AppendBatch(IReadOnlyList<AuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(lockHandle is null, this);
            var receipts = new List<AuditReceipt>(entries.Count);
            foreach (var entry in entries)
            {
                ArgumentNullException.ThrowIfNull(entry);
                receipts.Add(Write(entry, null));
            }
            Commit();
            return receipts;
        }
    }

    /// <summary>The final HMAC of a day (printed in the next morning's digest, 12.5), or null when the day has no line.</summary>
    public string? DayFinal(DateOnly day) => AuditVerifier.DayFinal(Directory, day);

    public void Dispose()
    {
        lock (gate)
        {
            if (lockHandle is null) return;
            dayStream?.Flush(flushToDisk: true);
            dayStream?.Dispose();
            dayStream = null;
            CryptographicOperations.ZeroMemory(key);
            key = [];
            lockHandle.Dispose();
            lockHandle = null;
        }
    }

    // ---- Writing ----

    private AuditReceipt Write(AuditEntry entry, Action<Utf8JsonWriter>? systemFields)
    {
        var now = time.GetUtcNow();
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).DateTime);
        var stream = StreamFor(day);
        var sequence = headSequence + 1;
        var previous = systemFields is not null && IsChainStart ? AuditLine.ZeroHmac : headHmac;
        var core = AuditLine.Core(sequence, now, entry, previous, systemFields);
        var hmac = AuditLine.Mac(key, core, previous);
        stream.Write(Utf8.GetBytes(AuditLine.Seal(core, hmac) + "\n"));
        headSequence = sequence;
        headHmac = hmac;
        headFile = Path.GetFileName(stream.Name);
        IsChainStart = false;
        return new AuditReceipt(sequence, hmac, headFile);
    }

    /// <summary>Set while a keyReplaced record is being written: it starts a new chain from 32 zero bytes.</summary>
    private bool IsChainStart { get; set; }

    private void Commit()
    {
        dayStream?.Flush(flushToDisk: true);
        if (headSequence == 0) return;
        try
        {
            keys.WriteAnchor(new AuditAnchor(headSequence, headHmac, headFile));
            AnchorBehind = false;
        }
        catch (AuditKeyUnavailableException)
        {
            AnchorBehind = true; // the lines are on disk; the next batch copies the head again
        }
    }

    private FileStream StreamFor(DateOnly day)
    {
        // A clock that steps back stays on the latest day file, so the chain's files stay in order.
        if (headFile.Length > 0 && DateOnly.TryParseExact(headFile[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var headDay) && headDay > day) day = headDay;
        if (dayStream is not null && dayOpen == day) return dayStream;
        dayStream?.Flush(flushToDisk: true);
        dayStream?.Dispose();
        dayStream = new FileStream(Path.Combine(Directory, AuditLine.DayFileName(day)), FileMode.Append, FileAccess.Write, FileShare.Read);
        dayOpen = day;
        return dayStream;
    }

    // ---- Open: head and repairs ----

    private sealed record Head(long Sequence, string Hmac, string? File, byte[]? Torn, string? TornFile);

    private static Head FindHead(string directory)
    {
        var files = AuditLine.DayFiles(directory);
        long sequence = 0;
        var hmac = AuditLine.ZeroHmac;
        byte[]? torn = null;
        foreach (var path in files)
        {
            var (lines, tail) = AuditLine.SplitLines(AuditLine.ReadShared(path));
            foreach (var raw in lines)
            {
                if (!AuditLine.TryOpen(Utf8.GetString(raw), out var core, out var mac)) continue;
                if (AuditLine.ParseCore(core) is not { } fields || AuditLine.Long(fields, "seq") is not { } seq) continue;
                if (seq <= sequence) continue;
                sequence = seq;
                hmac = mac;
            }
            // Only the last file can hold a piece a crash cut off: every open closes it before a later file starts.
            if (path == files[^1] && tail.Length > 0) torn = tail;
        }
        var last = files.Count > 0 ? files[^1] : null;
        return new Head(sequence, hmac, last is null ? null : Path.GetFileName(last), torn, torn is null ? null : last);
    }

    private void Repair(Head head, bool keyReplaced)
    {
        var records = new List<string>();
        var anchor = keys.ReadAnchor();
        lock (gate)
        {
            if (head.Torn is not null && head.TornFile is not null)
            {
                // Close the cut piece with a newline so it is a line of its own, never merged with the next one.
                using (var close = new FileStream(head.TornFile, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    close.Write("\n"u8);
                    close.Flush(flushToDisk: true);
                }
                headFile = Path.GetFileName(head.TornFile);
            }
            var tornHash = head.Torn is null ? null : AuditLine.Sha256(head.Torn);
            if (keyReplaced)
            {
                IsChainStart = true;
                Write(SystemEntry, w =>
                {
                    w.WriteString("event", AuditLine.KeyReplaced);
                    if (tornHash is not null) w.WriteString("torn", tornHash);
                });
                records.Add(AuditLine.KeyReplaced);
            }
            else if (tornHash is not null)
            {
                Write(SystemEntry, w =>
                {
                    w.WriteString("event", AuditLine.TornLine);
                    w.WriteString("torn", tornHash);
                });
                records.Add(AuditLine.TornLine);
            }
            if (anchor is not null && !AnchorFound(anchor, head))
            {
                Write(SystemEntry, w =>
                {
                    w.WriteString("event", AuditLine.LogCut);
                    w.WriteNumber("anchorSeq", anchor.Sequence);
                    w.WriteString("anchorHmac", AuditLine.IsHmac(anchor.Hmac) ? anchor.Hmac : AuditLine.ZeroHmac);
                    w.WriteNumber("foundSeq", head.Sequence);
                });
                records.Add(AuditLine.LogCut);
            }
            if (records.Count > 0) Commit();
            OpenRecords = records;
        }
    }

    /// <summary>Whether the files hold the anchor's line with the anchor's HMAC.</summary>
    private bool AnchorFound(AuditAnchor anchor, Head head)
    {
        if (anchor.Sequence > head.Sequence) return false;
        if (anchor.Sequence == head.Sequence) return string.Equals(anchor.Hmac, head.Hmac, StringComparison.Ordinal);
        return AuditVerifier.HmacOf(Directory, anchor.Sequence) is { } found && string.Equals(found, anchor.Hmac, StringComparison.Ordinal);
    }
}
