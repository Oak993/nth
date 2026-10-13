// PORT-ONLY: PA-0015 and R-FS-1. Windows refuses a write, rename, replace or delete while another handle holds the
// file, and refuses to delete or replace a read-only file, where the POSIX calls behind Foundation succeed. Every
// write, replace, rename and delete in the port goes through this file (IMPLEMENTING.md 4.3).
using System.Buffers;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;

namespace Marrow.Core.Port;

/// <summary>
/// The Foundation file calls the Swift makes, with PA-0015's retry: <c>Data.write(to:options: .atomic)</c> and
/// <c>String.write(to:atomically: true, encoding: .utf8)</c> (Services/Store/Storage.swift:108, :384;
/// Services/Providers/NativeFileTools.swift:88, :118) are <see cref="WriteAtomic"/>; <c>FileManager.removeItem</c>
/// (Services/Store/Storage.swift:147, :372; App/AppModel+Hydra.swift:1110; Services/Git/Git.swift:619) is
/// <see cref="RemoveItem"/>; <c>FileManager.moveItem</c> (App/AppUpdater.swift:516) is <see cref="MoveItem"/>;
/// <c>FileManager.replaceItemAt</c> (Services/Providers/CommandCodeSession.swift:758) is <see cref="ReplaceItemAt"/>.
/// Every call is synchronous like the Swift one. A transient Windows error (ED-125) is retried after each
/// <see cref="RetryDelays"/> wait on <see cref="SwiftClock"/> (ED-068, ED-122); the error of the last attempt is thrown
/// as it is, a thrown error that <c>try?</c> sites swallow (R-ERR-1). Cancellation is not observed: the Swift calls
/// ignore it.
/// </summary>
public static class FileOps
{
    // ED-125: ERROR_ACCESS_DENIED, ERROR_SHARING_VIOLATION and ERROR_LOCK_VIOLATION.
    private const int ErrorAccessDenied = 0x5;
    private const int ErrorSharingViolation = 0x20;
    private const int ErrorLockViolation = 0x21;

    /// <summary>
    /// ED-122: the wait before each retry, 100 to 700 ms in 100 ms steps (negative_test.mjs:68), 2.8 s in all, which
    /// stays under the HangWatchdog bound of 4 s (HangWatchdog.swift:24) less one 0.5 s ping (:52). Eight attempts:
    /// the first, then one after each wait. The same schedule serves every operation here.
    /// </summary>
    public static IReadOnlyList<TimeSpan> RetryDelays { get; } = new ReadOnlyCollection<TimeSpan>(
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(600),
        TimeSpan.FromMilliseconds(700),
    ]);

    /// <summary>
    /// <c>data.write(to: url, options: .atomic)</c> (Services/Store/Storage.swift:384): the destination holds exactly
    /// <paramref name="bytes"/> or still its old bytes. A missing parent folder is an error and is never created.
    /// Each attempt (ED-121) writes a temporary file beside the destination, flushes it to disk, reads it back and
    /// replaces the destination only when the bytes match; a destination that was read-only stays read-only (ED-124).
    /// </summary>
    public static void WriteAtomic(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);
        // ED-121: a verified temporary file waits beside the destination through a failed replace and is used by the
        // next attempt; a mismatch or a failed read-back discards it and the next attempt writes a fresh one.
        string? staged = null;
        for (var attempt = 0; ; attempt++)
        {
            Exception failure;
            try
            {
                if (staged is null)
                {
                    var temporary = WriteTemporary(path, bytes);
                    if (ReadsBackAs(temporary, bytes)) staged = temporary;
                    else Discard(temporary); // ED-121 (d): a mismatch fails the attempt.
                }
                if (staged is not null)
                {
                    ReplaceKeepingReadOnly(staged, path);
                    return;
                }
                // ED-121: an IOException naming the file, thrown if this was the last attempt.
                failure = new IOException($"The bytes written for \"{path}\" did not read back as written.");
            }
            catch (Exception error) when (IsTransient(error))
            {
                failure = error;
            }
            catch
            {
                if (staged is not null) Discard(staged);
                throw;
            }
            if (attempt == RetryDelays.Count)
            {
                // ED-121: the destination is untouched, no temporary file is left, and the last attempt's error is thrown.
                if (staged is not null) Discard(staged);
                ExceptionDispatchInfo.Throw(failure);
            }
            Wait(RetryDelays[attempt]);
        }
    }

    /// <summary>
    /// <c>FileManager.default.removeItem(at:)</c>: a folder goes with everything in it (R-FS-1), a link goes as itself
    /// and nothing it points at is touched (POSIX unlink; Services/Git/Git.swift:600, :619), and a read-only file is
    /// deleted as POSIX unlink deletes it (PA-0015). A missing item throws at once (ED-125).
    /// </summary>
    public static void RemoveItem(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Retrying(() => Remove(path));
    }

    /// <summary>
    /// <c>FileManager.default.moveItem(at:to:)</c> (App/AppUpdater.swift:516): an existing destination is an error
    /// (which is why :515 removes it first) and throws at once (ED-125).
    /// </summary>
    public static void MoveItem(string source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        Retrying(() =>
        {
            if (File.GetAttributes(source).HasFlag(FileAttributes.Directory)) Directory.Move(source, destination);
            else File.Move(source, destination, overwrite: false);
        });
    }

    /// <summary>
    /// <c>FileManager.default.replaceItemAt(file, withItemAt: temporary)</c>
    /// (Services/Providers/CommandCodeSession.swift:758, whose result is discarded): the original takes the
    /// replacement's bytes and the replacement is gone. A missing original throws <see cref="FileNotFoundException"/>
    /// at once and moves nothing (ED-123, oracle O-35): the replace primitive's own error, never an existence check
    /// followed by a move.
    /// </summary>
    public static void ReplaceItemAt(string original, string replacement)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        // UNVERIFIED O-35: Foundation's replaceItemAt throws when the original is missing (ED-123).
        Retrying(() => File.Replace(replacement, original, destinationBackupFileName: null));
    }

    // ED-122: the first attempt, then one after each wait; the last attempt's error goes out unchanged.
    private static void Retrying(Action attempt)
    {
        for (var index = 0; ; index++)
        {
            try
            {
                attempt();
                return;
            }
            catch (Exception error) when (index < RetryDelays.Count && IsTransient(error))
            {
            }
            Wait(RetryDelays[index]);
        }
    }

    // ED-068: the wait is a SwiftClock timer, so a test drives it with the manual clock. It blocks the calling
    // thread as the synchronous Swift call does.
    private static void Wait(TimeSpan delay)
    {
        using var elapsed = new ManualResetEventSlim();
        using (SwiftClock.Provider.Current.CreateTimer(_ => elapsed.Set(), null, delay, Timeout.InfiniteTimeSpan))
            elapsed.Wait();
    }

    // ED-125: sharing, lock and access-denied errors are transient; everything else (not found, already exists, an
    // invalid name, a full disk and the rest) is not.
    private static bool IsTransient(Exception error)
    {
        if (error is not (IOException or UnauthorizedAccessException)) return false;
        var result = error.HResult;
        if ((result & unchecked((int)0xFFFF0000)) != unchecked((int)0x80070000)) return false;
        return (result & 0xFFFF) is ErrorAccessDenied or ErrorSharingViolation or ErrorLockViolation;
    }

    // ED-121 (a): the temporary file sits beside the destination (a rename needs the same volume) under a name that
    // does not end in .json (Services/Store/Storage.swift:125 loads every *.json in a folder), and is flushed to disk
    // (swift-foundation Data+Writing.swift calls fsync before the rename). A missing folder throws here, and nothing
    // is created.
    private static string WriteTemporary(string destination, byte[] bytes)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(destination))
            ?? throw new IOException($"\"{destination}\" has no parent folder.");
        var temporary = Path.Combine(folder, "." + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray(), 0, 8) + ".tmp");
        try
        {
            using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            Discard(temporary);
            throw;
        }
        return temporary;
    }

    // ED-121 (b): the read-back. A failure to read (a sharing violation is transient, ED-125) discards the file, so
    // the next attempt writes a fresh one.
    // ED-NEW-perf-memory-5: compared in 64 KB pieces, not by reading the whole file into a second array of the file's size.
    private static bool ReadsBackAs(string temporary, byte[] bytes)
    {
        try
        {
            using var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
            if (stream.Length != bytes.Length) return false;
            var piece = ArrayPool<byte>.Shared.Rent(1 << 16);
            try
            {
                var compared = 0;
                while (compared < bytes.Length)
                {
                    var read = stream.Read(piece, 0, Math.Min(piece.Length, bytes.Length - compared));
                    if (read <= 0 || !piece.AsSpan(0, read).SequenceEqual(bytes.AsSpan(compared, read))) return false;
                    compared += read;
                }
                return stream.ReadByte() < 0;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(piece);
            }
        }
        catch (Exception)
        {
            Discard(temporary);
            throw;
        }
    }

    // ED-124: swift-foundation writeToFileAux copies the destination's mode to the temporary file before the rename.
    // ReadOnly is the one attribute that stands for a mode bit. The rename is File.Move with overwrite (MoveFileEx),
    // since ReplaceFileW refuses a read-only file. A failed replace sets the destination's ReadOnly again before the
    // retry wait.
    // UNVERIFIED O-36: an .atomic write over a read-only file succeeds and leaves the result read-only (ED-124).
    private static void ReplaceKeepingReadOnly(string temporary, string destination)
    {
        var existing = AttributesOrNull(destination);
        if (existing is not { } attributes || !attributes.HasFlag(FileAttributes.ReadOnly))
        {
            File.Move(temporary, destination, overwrite: true);
            return;
        }
        File.SetAttributes(temporary, File.GetAttributes(temporary) | FileAttributes.ReadOnly);
        File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
        try
        {
            File.Move(temporary, destination, overwrite: true);
        }
        catch (Exception)
        {
            SwiftTry.Ignore(() => File.SetAttributes(destination, attributes));
            throw;
        }
    }

    private static FileAttributes? AttributesOrNull(string path)
    {
        try
        {
            return File.GetAttributes(path);
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

    // ED-121 (d), ED-124: a temporary file that will not be used is deleted, its ReadOnly cleared first.
    private static void Discard(string temporary) => SwiftTry.Ignore(() =>
    {
        var attributes = File.GetAttributes(temporary);
        if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(temporary, attributes & ~FileAttributes.ReadOnly);
        File.Delete(temporary);
    });

    // removeItem: a link (a reparse point) is removed as itself, never followed; a folder is emptied one level at a
    // time with the non-recursive enumeration, so no walk passes through a link; ReadOnly is cleared before a delete
    // (PA-0015) and set again if that delete fails, so a failed attempt leaves the item as it found it.
    private static void Remove(string path)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            if (attributes.HasFlag(FileAttributes.Directory)) Directory.Delete(path);
            else File.Delete(path);
            return;
        }
        if (attributes.HasFlag(FileAttributes.Directory))
        {
            foreach (var entry in Directory.GetFileSystemEntries(path)) Remove(entry);
            DeleteClearingReadOnly(path, attributes, () => Directory.Delete(path));
            return;
        }
        DeleteClearingReadOnly(path, attributes, () => File.Delete(path));
    }

    private static void DeleteClearingReadOnly(string path, FileAttributes attributes, Action delete)
    {
        if (!attributes.HasFlag(FileAttributes.ReadOnly))
        {
            delete();
            return;
        }
        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        try
        {
            delete();
        }
        catch (Exception)
        {
            SwiftTry.Ignore(() => File.SetAttributes(path, attributes));
            throw;
        }
    }
}
