// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-3: the single writer of the keyed audit chain.
// ED-NEW-dextra-as3-6: the head is taken only from a line sealed under the key, the key is replaced only when it is
// gone, and no line is ever appended onto a piece.
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
/// <para>The chain's head is the highest-numbered line that is sealed under the key (<see cref="AuditLine.Seals"/>), so
/// a line planted without the key, in any day file, never becomes the head and never names the HMAC the next line
/// chains to. The anchor matches only its own line, sealed, with its HMAC, in the day file it names.</para>
/// <para>At open it repairs, in the chain and never by deleting a byte: a last line a crash cut off is closed with a
/// newline and followed by a <c>tornLine</c> record naming its SHA-256; a key that is gone (no such item, or an item that
/// is not 32 bytes) while lines exist is replaced and a <c>keyReplaced</c> record starts a new chain (the earlier lines no
/// longer verify); and an anchor the files do not reach, or that names a different HMAC or day file, is kept as a
/// <c>logCut</c> record, so a cut log never heals silently. A key or anchor Credential Manager refuses to read fails the
/// open (<see cref="AuditKeyUnavailableException"/>) and replaces nothing.</para>
/// <para>No line is ever appended onto a piece: whenever the writer opens a day file, a piece after its last newline
/// (a crash, a failed write, or anything else) is closed and recorded the same way first. A write that throws leaves
/// the head unknown, so the writer drops the file and reads the head back from the files before its next line; lines
/// of that batch that reached the disk stay in the chain (an audit log keeps a line twice rather than lose one).</para>
/// <para>Limit, stated plainly (12.5): this detects edits by agents and tools; it cannot stop malware running as the
/// owner, which can read the same Credential Manager entry.</para>
/// </summary>
public sealed class AuditLog : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly AuditEntry SystemEntry = new() { Assistant = "nth", Tool = "audit", ActionClass = "Audit" };

    /// <summary>
    /// A time-zone change moves the local day by at most two days (UTC-12 to UTC+14), so a day file named further ahead
    /// than that, holding no line sealed under the key, was never this writer's: it never becomes the head's file.
    /// </summary>
    private const int ZoneMarginDays = 2;

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
    private bool headUnknown;

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

    /// <summary>
    /// A test's fault hook: given the number of the line about to be written, how many of its bytes reach the disk
    /// before the write throws an <see cref="IOException"/> (null: no fault). It stands in for a disk that fills up.
    /// </summary>
    internal Func<long, int?>? FailWriteForTests { get; set; }

    public static AuditLog Open(IAssistantStorageRoots roots, AuditKeyStore keys, TimeProvider? time = null) =>
        Open(roots.AuditDirectory, keys, time);

    /// <summary>
    /// Opens the writer: takes the lock (else <see cref="AuditLogBusyException"/>), reads the key (making one only when
    /// Credential Manager holds none; <see cref="AuditKeyUnavailableException"/> when it refuses a read or a write),
    /// finds the chain's head in the day files and writes the repair records described on the type.
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
        byte[]? key = null;
        AuditLog? log = null;
        try
        {
            key = keys.ReadKey(out var state); // a refused read throws here: nothing is replaced, nothing is written
            var anchor = keys.ReadAnchor();
            var head = FindHead(directory, key, LocalDay(time, time.GetUtcNow()), anchor);
            var replaced = false;
            if (key is null)
            {
                replaced = head.Sequence > 0 || head.Torn is not null;
                key = keys.CreateKey(state);
            }
            log = new AuditLog(directory, keys, time, handle, key, head.Sequence, head.Hmac, head.File ?? "");
            log.Repair(head, replaced, anchor);
            log.OpenRecordsFrom(head, replaced, anchor);
            return log;
        }
        catch
        {
            if (log is not null)
            {
                log.Dispose(); // closes the day file, zeroes the key and frees the folder
            }
            else
            {
                if (key is not null) CryptographicOperations.ZeroMemory(key);
                handle.Dispose();
            }
            throw;
        }
    }

    /// <summary>Appends one action, flushed to disk and anchored before this returns.</summary>
    public AuditReceipt Append(AuditEntry entry) => AppendBatch([entry])[0];

    /// <summary>
    /// Appends actions in order as one batch: one flush to disk and one anchor copy for all of them. When it throws, the
    /// batch is not confirmed: the writer reads its head back from the files before the next line, and any line of the
    /// batch that reached the disk stays in the chain.
    /// </summary>
    public IReadOnlyList<AuditReceipt> AppendBatch(IReadOnlyList<AuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries) ArgumentNullException.ThrowIfNull(entry); // before any byte is written
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(lockHandle is null, this);
            try
            {
                if (headUnknown) ReadHeadBack();
                var receipts = new List<AuditReceipt>(entries.Count);
                foreach (var entry in entries) receipts.Add(Write(entry, null));
                Commit();
                return receipts;
            }
            catch
            {
                // What reached the disk is unknown: never chain the next line to a guess.
                DropStream();
                headUnknown = true;
                throw;
            }
        }
    }

    /// <summary>The final HMAC of a day (printed in the next morning's digest, 12.5), or null when the day has no line.</summary>
    public string? DayFinal(DateOnly day) => AuditVerifier.DayFinal(Directory, day);

    public void Dispose()
    {
        lock (gate)
        {
            if (lockHandle is null) return;
            try
            {
                dayStream?.Flush(flushToDisk: true);
            }
            catch (IOException)
            {
                // the lines that did not reach the disk are recorded as a piece at the next open
            }
            finally
            {
                DropStream();
                CryptographicOperations.ZeroMemory(key);
                key = [];
                lockHandle.Dispose();
                lockHandle = null;
            }
        }
    }

    // ---- Writing ----

    private static DateOnly LocalDay(TimeProvider time, DateTimeOffset at) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).DateTime);

    private AuditReceipt Write(AuditEntry entry, Action<Utf8JsonWriter>? systemFields)
    {
        var now = time.GetUtcNow();
        return WriteTo(StreamFor(LocalDay(time, now), now), now, entry, systemFields);
    }

    private AuditReceipt WriteTo(FileStream stream, DateTimeOffset now, AuditEntry entry, Action<Utf8JsonWriter>? systemFields)
    {
        var sequence = headSequence + 1;
        var previous = systemFields is not null && IsChainStart ? AuditLine.ZeroHmac : headHmac;
        var core = AuditLine.Core(sequence, now, entry, previous, systemFields);
        var hmac = AuditLine.Mac(key, core, previous);
        var bytes = Utf8.GetBytes(AuditLine.Seal(core, hmac) + "\n");
        if (FailWriteForTests?.Invoke(sequence) is { } reached)
        {
            stream.Write(bytes, 0, Math.Clamp(reached, 0, bytes.Length));
            stream.Flush(flushToDisk: true);
            throw new IOException("Simulated write failure (a test's fault hook).");
        }
        stream.Write(bytes);
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

    private FileStream StreamFor(DateOnly day, DateTimeOffset now)
    {
        // A clock that steps back stays on the head's day file, so the chain's files stay in order. The head's file is
        // only ever one holding a line sealed under the key (or, in range, one the writer itself repaired).
        if (AuditLine.DayOf(headFile) is { } headDay && headDay > day) day = headDay;
        if (dayStream is not null && dayOpen == day) return dayStream;
        DropStream(flushFirst: true);
        var path = Path.Combine(Directory, AuditLine.DayFileName(day));
        dayStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        dayOpen = day;
        // Never append onto a piece: one left after the file's last newline is closed and recorded before our line.
        if (AuditLine.PieceHash(path) is { } piece)
        {
            dayStream.Write("\n"u8);
            WriteTo(dayStream, now, SystemEntry, w =>
            {
                w.WriteString("event", AuditLine.TornLine);
                w.WriteString("torn", piece);
            });
        }
        return dayStream;
    }

    private void DropStream(bool flushFirst = false)
    {
        var stream = dayStream;
        dayStream = null;
        if (stream is null) return;
        try
        {
            if (flushFirst) stream.Flush(flushToDisk: true);
        }
        finally
        {
            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
                // the handle is closed; what did not reach the disk is read back as a piece
            }
        }
    }

    /// <summary>After a write that threw: the head as the files hold it, the anchor checked, a piece recorded.</summary>
    private void ReadHeadBack()
    {
        var anchor = keys.ReadAnchor();
        var head = FindHead(Directory, key, LocalDay(time, time.GetUtcNow()), anchor);
        headSequence = head.Sequence;
        headHmac = head.Hmac;
        headFile = head.File ?? "";
        Repair(head, keyReplaced: false, anchor);
        headUnknown = false;
    }

    // ---- Open: head and repairs ----

    /// <summary>
    /// The chain's head as the files hold it: the highest-numbered line sealed under the key (with no key: the highest
    /// number on any line that parses, used only to number the keyReplaced record), its HMAC and day file; the piece after
    /// the last newline of the writer's last file; and whether the anchor's own line is there.
    /// </summary>
    private sealed record Head(long Sequence, string Hmac, string? File, byte[]? Torn, string? TornFile, bool AnchorMatched);

    private static Head FindHead(string directory, byte[]? key, DateOnly today, AuditAnchor? anchor)
    {
        var files = AuditLine.DayFiles(directory);
        long sequence = 0;
        var hmac = AuditLine.ZeroHmac;
        string? file = null;
        var anchorMatched = false;
        var tails = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var (lines, tail) = AuditLine.SplitLines(AuditLine.ReadShared(path));
            if (tail.Length > 0) tails[name] = tail;
            foreach (var raw in lines)
            {
                if (!AuditLine.TryOpen(Utf8.GetString(raw), out var core, out var mac)) continue;
                if (AuditLine.ParseCore(core) is not { } fields || AuditLine.Sequence(fields) is not { } seq) continue;
                // A line not sealed under the key was not written by this writer: it never becomes the head.
                if (key is not null && !AuditLine.Seals(key, core, fields, mac)) continue;
                if (anchor is not null && seq == anchor.Sequence && string.Equals(mac, anchor.Hmac, StringComparison.Ordinal)
                    && string.Equals(name, anchor.File, StringComparison.Ordinal)) anchorMatched = true;
                if (seq <= sequence) continue; // a copy of a line never moves the head to the copy's file
                sequence = seq;
                hmac = mac;
                file = name;
            }
        }
        // With no key nothing is sealed, so a far-future file a writer never used cannot pin the new chain's files.
        if (key is null && file is not null && AuditLine.DayOf(file) is { } unsealedDay && unsealedDay > today.AddDays(ZoneMarginDays)) file = null;
        // The writer's last file: the head's or a later one, never past the day it writes now (today, or the head's day
        // when the clock stepped back). A piece in a file past that was never the writer's and is never adopted.
        var limit = AuditLine.DayOf(file ?? "") is { } headDay && headDay > today ? headDay : today;
        var last = files.Select(Path.GetFileName).OfType<string>()
            .LastOrDefault(name => (file is null || string.CompareOrdinal(name, file) >= 0) && AuditLine.DayOf(name) is { } day && day <= limit);
        var torn = last is not null && tails.TryGetValue(last, out var piece) ? piece : null;
        return new Head(sequence, hmac, file, torn, torn is null ? null : Path.Combine(directory, last!), anchorMatched);
    }

    private void Repair(Head head, bool keyReplaced, AuditAnchor? anchor)
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
        var wrote = false;
        if (keyReplaced)
        {
            IsChainStart = true;
            Write(SystemEntry, w =>
            {
                w.WriteString("event", AuditLine.KeyReplaced);
                if (tornHash is not null) w.WriteString("torn", tornHash);
            });
            wrote = true;
        }
        else if (tornHash is not null)
        {
            Write(SystemEntry, w =>
            {
                w.WriteString("event", AuditLine.TornLine);
                w.WriteString("torn", tornHash);
            });
            wrote = true;
        }
        if (anchor is not null && !head.AnchorMatched)
        {
            Write(SystemEntry, w =>
            {
                w.WriteString("event", AuditLine.LogCut);
                w.WriteNumber("anchorSeq", anchor.Sequence);
                w.WriteString("anchorHmac", AuditLine.IsHmac(anchor.Hmac) ? anchor.Hmac : AuditLine.ZeroHmac);
                w.WriteNumber("foundSeq", head.Sequence);
            });
            wrote = true;
        }
        if (wrote) Commit();
    }

    private void OpenRecordsFrom(Head head, bool keyReplaced, AuditAnchor? anchor)
    {
        var records = new List<string>();
        if (keyReplaced) records.Add(AuditLine.KeyReplaced);
        else if (head.Torn is not null) records.Add(AuditLine.TornLine);
        if (anchor is not null && !head.AnchorMatched) records.Add(AuditLine.LogCut);
        OpenRecords = records;
    }
}
