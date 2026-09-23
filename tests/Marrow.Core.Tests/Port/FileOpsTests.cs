using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// Foundation unit F.FileOps (IMPLEMENTING.md 4.3, R-FS-1, PA-0015): every write, replace, rename and delete in the
// port goes through FileOps. The Swift APIs it stands in for, and what callers rely on:
//   Data.write(to:options: .atomic) and String.write(to:atomically: true, encoding: .utf8): the destination holds
//     exactly the new bytes or still the old ones; a missing parent folder is an error, never created
//     (Services/Store/Storage.swift:108, :384; Services/Providers/NativeFileTools.swift:88, :118).
//   FileManager.removeItem(at:): a folder is removed with everything in it (R-FS-1; App/AppModel+Hydra.swift:1110,
//     Services/Providers/CommandCodeSession.swift:261); a missing item is an error that the try? sites swallow
//     (Services/Store/Storage.swift:147, :372). Removing a link removes the link and never what it points at (POSIX
//     unlink; removeItem does not descend into a symbolic link): Services/Git/Git.swift:619 removes paths under the
//     doc comment at :600 "Nothing outside `paths` is touched", and a worktree folder (AppModel+Hydra.swift:1110)
//     can hold links. On Windows the link a test can make without privilege is a junction.
//   FileManager.moveItem(at:to:): an existing destination is an error, which is why App/AppUpdater.swift:515-516
//     removes the destination before moving.
//   FileManager.replaceItemAt(_:withItemAt:): the original takes the new item's bytes and the new item is gone
//     (Services/Providers/CommandCodeSession.swift:757-758).
// An .atomic write is a temporary file plus a replace (R-FS-1): a reader that already holds the destination keeps
// the old bytes, and the temporary file sits beside the destination (a replace needs the same volume) under a
// name that does not end in .json (Services/Store/Storage.swift:125 loads every *.json in a folder).
// Rulings this file rests on: ED-121 (only WriteAtomic verifies: the temporary file is flushed, read back and
// compared before the replace; a mismatch deletes it and counts as a failed attempt), ED-122 (RetryDelays 100 to
// 700 ms in 100 ms steps, 8 attempts, 2.8 s of waiting, the same for write, remove, move and replace), ED-123
// (replaceItemAt with a missing original throws at once, parked as oracle O-35), ED-124 (an atomic write keeps
// the destination's read-only state, parked as oracle O-36) and ED-125 (sharing, lock and access-denied errors are
// transient; everything else throws at once).
// PA-0015 adds the Windows part: a transient sharing violation (a handle a child process left open) is retried
// with backoff on SwiftClock (ED-068), a bounded number of times, then the real error is thrown; read-only is
// cleared where POSIX would ignore it. The API these tests fix: static FileOps with RetryDelays
// (IReadOnlyList<TimeSpan>, the wait before each retry), WriteAtomic(path, bytes), RemoveItem(path),
// MoveItem(source, destination), ReplaceItemAt(original, replacement), all synchronous like the Swift calls.
// Blocking calls run on the pool with a bounded real-time guard, so a wrong wait fails instead of hanging.
public sealed class FileOpsTests
{
    private static readonly byte[] Old = [0x7B, 0x22, 0x61, 0x22, 0x3A, 0x31, 0x7D];                // {"a":1}
    private static readonly byte[] Newer = [0xEF, 0xBB, 0xBF, 0x00, 0x0A, 0x0D, 0x0A, 0xFF, 0xFE, 0x7D]; // kept as is

    private static Task OffThread(Action operation) => Task.Run(operation);

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Decision", "ED-122")]
    public void The_retry_schedule_waits_100_to_700_ms_in_100_ms_steps_for_2_8_s_in_all()
    {
        // ED-122: the 100 ms step is negative_test.mjs:68 (100*(i+1) ms); the count keeps the total under the
        // HangWatchdog bound, 4 s (HangWatchdog.swift:24) minus one 0.5 s ping (:52). Eight attempts: the first,
        // then one after each of the seven waits.
        Assert.Equal(
            new[] { 100, 200, 300, 400, 500, 600, 700 }.Select(ms => TimeSpan.FromMilliseconds(ms)),
            FileOps.RetryDelays);
        Assert.Equal(TimeSpan.FromMilliseconds(2800), FileOps.RetryDelays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    public async Task An_atomic_write_creates_the_file_with_exactly_the_bytes_given_and_no_temporary_file()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.PathOf("library.json");
        await OffThread(() => FileOps.WriteAtomic(path, Newer)).WaitAsync(ManualClockWait.Guard);
        Assert.Equal(Newer, File.ReadAllBytes(path));
        Assert.Equal(["library.json"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    public async Task An_atomic_write_replaces_an_existing_file_and_leaves_no_temporary_file()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0.json", Old);
        await OffThread(() => FileOps.WriteAtomic(path, Newer)).WaitAsync(ManualClockWait.Guard);
        Assert.Equal(Newer, File.ReadAllBytes(path));
        Assert.Equal(["0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0.json"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    public async Task An_atomic_write_of_empty_data_leaves_an_empty_file()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("port", Old);
        await OffThread(() => FileOps.WriteAtomic(path, [])).WaitAsync(ManualClockWait.Guard);
        Assert.True(File.Exists(path));
        Assert.Empty(File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    public async Task An_atomic_write_of_a_large_payload_lands_intact()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var payload = new byte[3 * 1024 * 1024 + 17];
        for (var i = 0; i < payload.Length; i++) payload[i] = unchecked((byte)(i * 31 + (i >> 8)));
        var path = scratch.Seed("threads/big.json", Old);
        await OffThread(() => FileOps.WriteAtomic(path, payload)).WaitAsync(ManualClockWait.Guard);
        Assert.Equal(payload, File.ReadAllBytes(path));
        Assert.Equal(["big.json"], scratch.Names("threads"));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Decision", "ED-125")]
    public async Task An_atomic_write_into_a_missing_folder_throws_at_once_and_does_not_create_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.PathOf(Path.Combine("attachments", "photo.png"));
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => OffThread(() => FileOps.WriteAtomic(path, Newer)).WaitAsync(ManualClockWait.Guard));
        Assert.IsAssignableFrom<IOException>(error); // not TimeoutException: no retry was armed and waited on
        Assert.True(SwiftTry.IsThrownError(error), "a try? site swallows it, as Swift's try? does");
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.False(Directory.Exists(scratch.PathOf("attachments")));
        Assert.Empty(scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-124")]
    [Trait("Oracle", "O-36")]
    public async Task An_atomic_write_over_a_read_only_file_succeeds_as_a_POSIX_rename_does()
    {
        // The read-only state of the result is PARKED on oracle O-36 (ED-124, R-TEST-6): swift-foundation
        // writeToFileAux copies the destination's mode to the temporary file before the rename, so the result stays
        // read-only; whether macOS 26 behaves so is unverified. No Req trait until it is settled.
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("mcp-port", Old);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        // Negative control: the plain .NET replace refuses a read-only destination that rename(2) replaces.
        var control = scratch.Seed("control.tmp", Newer);
        Assert.Throws<UnauthorizedAccessException>(() => File.Move(control, path, overwrite: true));
        Assert.Equal(Old, File.ReadAllBytes(path));

        await OffThread(() => FileOps.WriteAtomic(path, Newer)).WaitAsync(ManualClockWait.Guard);
        Assert.Equal(Newer, File.ReadAllBytes(path));
        Assert.Equal(["control.tmp", "mcp-port"], scratch.Names());
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly), "O-36: the result keeps ReadOnly");
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Decision", "ED-124")]
    [Trait("Oracle", "O-36")]
    public async Task An_atomic_write_to_a_new_path_or_over_a_writable_file_does_not_make_it_read_only()
    {
        // PARKED on oracle O-36 (ED-124, R-TEST-6): only a read-only destination's mode is carried to the result.
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var created = scratch.PathOf("library.json");
        var replaced = scratch.Seed("0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0.json", Old);
        await OffThread(() => FileOps.WriteAtomic(created, Newer)).WaitAsync(ManualClockWait.Guard);
        await OffThread(() => FileOps.WriteAtomic(replaced, Newer)).WaitAsync(ManualClockWait.Guard);
        Assert.False(File.GetAttributes(created).HasFlag(FileAttributes.ReadOnly), "a new file is not made read-only");
        Assert.False(File.GetAttributes(replaced).HasFlag(FileAttributes.ReadOnly), "a writable file stays writable");
        Assert.Equal(Newer, File.ReadAllBytes(created));
        Assert.Equal(Newer, File.ReadAllBytes(replaced));
        Assert.Equal(["0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0.json", "library.json"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-121")]
    [Trait("Decision", "ED-125")]
    public async Task An_atomic_write_blocked_by_a_held_handle_retries_on_the_manual_clock_and_lands_once_released()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("library.json", Old);
        var held = ScratchFolder.HoldOpen(path);
        try
        {
            // Negative control: a single plain write fails on the lingering handle (the PA-0015 finding).
            Assert.Throws<IOException>(() => File.WriteAllBytes(path, Newer));

            var write = OffThread(() => FileOps.WriteAtomic(path, Newer));
            ManualClockWait.ForRetryTimer(runtime.Time, write);
            var first = FileOps.RetryDelays[0];
            runtime.Time.Advance(first - TimeSpan.FromTicks(1));
            Assert.False(write.IsCompleted, "no retry before the first wait has fully elapsed");
            Assert.Equal(1, runtime.Time.ActiveTimers);
            // While it waits the reader still sees the old bytes, and the temporary file waits beside the
            // destination under a name that does not end in .json (Storage.swift:125).
            Assert.Equal(Old, ReadShared(path));
            AssertOnlyATemporaryBeside(scratch, "", "library.json");

            held.Dispose();
            runtime.Time.Advance(TimeSpan.FromTicks(1));
            await write.WaitAsync(ManualClockWait.Guard);
        }
        finally
        {
            held.Dispose();
        }
        Assert.Equal(Newer, File.ReadAllBytes(path));
        Assert.Equal(["library.json"], scratch.Names());
        Assert.Equal(FileOps.RetryDelays[0], runtime.Time.Elapsed);
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-ERR-1")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-121")]
    [Trait("Decision", "ED-125")]
    public async Task An_atomic_write_still_blocked_after_every_retry_throws_the_real_error_and_keeps_the_old_file()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("library.json", Old);
        Exception error;
        using (ScratchFolder.HoldOpen(path))
        {
            var write = OffThread(() => FileOps.WriteAtomic(path, Newer));
            foreach (var delay in FileOps.RetryDelays)
            {
                ManualClockWait.ForRetryTimer(runtime.Time, write);
                runtime.Time.Advance(delay);
            }
            error = await Assert.ThrowsAnyAsync<Exception>(() => write.WaitAsync(ManualClockWait.Guard));
        }
        Assert.True(error is IOException or UnauthorizedAccessException, "the Windows error itself, not a timeout: " + error);
        Assert.True(SwiftTry.IsThrownError(error), "a thrown error, not a trap: try? sites swallow it");
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.Equal(FileOps.RetryDelays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d), runtime.Time.Elapsed);
        Assert.Equal(Old, File.ReadAllBytes(path));
        Assert.Equal(["library.json"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-ERR-1")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-124")]
    [Trait("Decision", "ED-125")]
    [Trait("Oracle", "O-36")]
    public async Task An_atomic_write_over_a_read_only_file_still_blocked_after_every_retry_keeps_it_read_only()
    {
        // PARKED on oracle O-36 (ED-124, R-TEST-6): when the retries run out the old file keeps its bytes and its
        // ReadOnly exactly as before, and the temporary file is gone.
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("mcp-port", Old);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Exception error;
        using (ScratchFolder.HoldOpen(path))
        {
            var write = OffThread(() => FileOps.WriteAtomic(path, Newer));
            foreach (var delay in FileOps.RetryDelays)
            {
                ManualClockWait.ForRetryTimer(runtime.Time, write);
                // ED-124: the destination's ReadOnly is set again before each wait.
                Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly), "ReadOnly is restored before the wait");
                runtime.Time.Advance(delay);
            }
            error = await Assert.ThrowsAnyAsync<Exception>(() => write.WaitAsync(ManualClockWait.Guard));
        }
        Assert.True(error is IOException or UnauthorizedAccessException, "the Windows error itself, not a timeout: " + error);
        Assert.True(SwiftTry.IsThrownError(error), "a thrown error, not a trap: try? sites swallow it");
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.Equal(Old, File.ReadAllBytes(path));
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly), "O-36: the old file keeps ReadOnly");
        Assert.Equal(["mcp-port"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TASK-2")]
    public async Task A_cancelled_task_does_not_cut_a_retry_short_because_the_Swift_write_ignores_cancellation()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("threads.json", Old);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = ScratchFolder.HoldOpen(path);
        try
        {
            var task = TaskHandle.DetachedThrowing(async () =>
            {
                await start.Task;
                var cancelled = SwiftTask.IsCancelled;
                FileOps.WriteAtomic(path, Newer);
                return cancelled;
            });
            task.Cancel();
            start.SetResult();
            ManualClockWait.ForRetryTimer(runtime.Time, task.Value);
            held.Dispose();
            runtime.Time.Advance(FileOps.RetryDelays[0]);
            Assert.True(await task.Value.WaitAsync(ManualClockWait.Guard), "the write ran inside a cancelled task");
        }
        finally
        {
            held.Dispose();
        }
        Assert.Equal(Newer, File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Removing_a_read_only_file_clears_the_attribute_and_deletes_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("pack-1.idx", Old);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        // Negative control: Windows refuses to delete a read-only file that POSIX unlink removes.
        Assert.Throws<UnauthorizedAccessException>(() => File.Delete(path));
        Assert.True(File.Exists(path));

        await OffThread(() => FileOps.RemoveItem(path)).WaitAsync(ManualClockWait.Guard);
        Assert.False(File.Exists(path));
        Assert.Empty(scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Removing_a_folder_removes_everything_in_it_including_read_only_files()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        // The shape PA-0015 cites: git loose objects are read-only files two levels down.
        var loose = scratch.Seed(Path.Combine("worktree", ".git", "objects", "ab", "cdef0123"), Old);
        var packed = scratch.Seed(Path.Combine("worktree", ".git", "objects", "pack", "pack-1.pack"), Newer);
        scratch.Seed(Path.Combine("worktree", "README.md"), Newer);
        Directory.CreateDirectory(scratch.PathOf(Path.Combine("worktree", "empty")));
        File.SetAttributes(loose, FileAttributes.ReadOnly);
        File.SetAttributes(packed, FileAttributes.ReadOnly);

        // Negative control: the plain recursive delete stops at the first read-only file.
        Assert.Throws<UnauthorizedAccessException>(() => Directory.Delete(scratch.PathOf("worktree"), recursive: true));
        Assert.True(Directory.Exists(scratch.PathOf("worktree")));

        await OffThread(() => FileOps.RemoveItem(scratch.PathOf("worktree"))).WaitAsync(ManualClockWait.Guard);
        Assert.False(Directory.Exists(scratch.PathOf("worktree")));
        Assert.Empty(scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task Removing_a_file_held_open_retries_and_deletes_it_once_released()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("call-7.request.json", Old);
        var held = ScratchFolder.HoldOpen(path);
        try
        {
            // Negative control: the plain delete fails on the lingering handle.
            Assert.Throws<IOException>(() => File.Delete(path));

            var remove = OffThread(() => FileOps.RemoveItem(path));
            ManualClockWait.ForRetryTimer(runtime.Time, remove);
            var first = FileOps.RetryDelays[0];
            runtime.Time.Advance(first - TimeSpan.FromTicks(1));
            Assert.False(remove.IsCompleted, "no retry before the first wait has fully elapsed");
            Assert.True(File.Exists(path));

            held.Dispose();
            runtime.Time.Advance(TimeSpan.FromTicks(1));
            await remove.WaitAsync(ManualClockWait.Guard);
        }
        finally
        {
            held.Dispose();
        }
        Assert.False(File.Exists(path));
        Assert.Empty(scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task Removing_a_folder_with_a_file_held_open_retries_and_removes_it_once_released()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var busy = scratch.Seed(Path.Combine("approvals", "call-1.request.json"), Old);
        scratch.Seed(Path.Combine("approvals", "call-1.reply.json"), Newer);
        var held = ScratchFolder.HoldOpen(busy);
        try
        {
            var remove = OffThread(() => FileOps.RemoveItem(scratch.PathOf("approvals")));
            ManualClockWait.ForRetryTimer(runtime.Time, remove);
            Assert.True(File.Exists(busy));
            held.Dispose();
            runtime.Time.Advance(FileOps.RetryDelays[0]);
            await remove.WaitAsync(ManualClockWait.Guard);
        }
        finally
        {
            held.Dispose();
        }
        Assert.False(Directory.Exists(scratch.PathOf("approvals")));
        Assert.Empty(scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-ERR-1")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task Removing_a_file_still_held_after_every_retry_throws_the_real_error_and_keeps_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("gate.json", Old);
        Exception error;
        using (ScratchFolder.HoldOpen(path))
        {
            var remove = OffThread(() => FileOps.RemoveItem(path));
            foreach (var delay in FileOps.RetryDelays)
            {
                ManualClockWait.ForRetryTimer(runtime.Time, remove);
                runtime.Time.Advance(delay);
            }
            error = await Assert.ThrowsAnyAsync<Exception>(() => remove.WaitAsync(ManualClockWait.Guard));
        }
        Assert.True(error is IOException or UnauthorizedAccessException, "the Windows error itself, not a timeout: " + error);
        Assert.True(SwiftTry.IsThrownError(error), "a thrown error, not a trap: try? sites swallow it");
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.Equal(FileOps.RetryDelays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d), runtime.Time.Elapsed);
        Assert.Equal(Old, File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-ERR-1")]
    [Trait("Decision", "ED-125")]
    public async Task Removing_a_missing_item_throws_at_once()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => OffThread(() => FileOps.RemoveItem(scratch.PathOf("gone.json"))).WaitAsync(ManualClockWait.Guard));
        Assert.IsAssignableFrom<IOException>(error); // not TimeoutException: a missing item is not transient
        Assert.True(SwiftTry.IsThrownError(error), "Storage.swift:147 swallows it with try?");
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    public async Task Moving_a_file_moves_its_bytes_and_removes_the_source()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var source = scratch.Seed(Path.Combine("downloads", "CFNetworkDownload_1.tmp"), Newer);
        Directory.CreateDirectory(scratch.PathOf("updates"));
        var destination = scratch.PathOf(Path.Combine("updates", "Marrow.zip"));
        await OffThread(() => FileOps.MoveItem(source, destination)).WaitAsync(ManualClockWait.Guard);
        Assert.False(File.Exists(source));
        Assert.Equal(Newer, File.ReadAllBytes(destination));
        Assert.Empty(scratch.Names("downloads"));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-ERR-1")]
    [Trait("Decision", "ED-125")]
    public async Task Moving_onto_an_existing_item_throws_at_once_and_changes_nothing()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        // FileManager.moveItem refuses an existing destination; App/AppUpdater.swift:515-516 removes it first.
        var source = scratch.Seed("download.tmp", Newer);
        var destination = scratch.Seed("Marrow.zip", Old);
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => OffThread(() => FileOps.MoveItem(source, destination)).WaitAsync(ManualClockWait.Guard));
        Assert.IsAssignableFrom<IOException>(error); // not TimeoutException: an existing destination is not transient
        Assert.True(SwiftTry.IsThrownError(error), "AppUpdater.swift:518-519 catches it and reports the failure");
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.Equal(Newer, File.ReadAllBytes(source));
        Assert.Equal(Old, File.ReadAllBytes(destination));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task Moving_a_file_held_open_retries_and_moves_it_once_released()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var source = scratch.Seed("download.tmp", Newer);
        var destination = scratch.PathOf("Marrow.zip");
        var held = ScratchFolder.HoldOpen(source);
        try
        {
            // Negative control: the plain rename fails on the lingering handle.
            Assert.Throws<IOException>(() => File.Move(source, destination));

            var move = OffThread(() => FileOps.MoveItem(source, destination));
            ManualClockWait.ForRetryTimer(runtime.Time, move);
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(destination));
            held.Dispose();
            runtime.Time.Advance(FileOps.RetryDelays[0]);
            await move.WaitAsync(ManualClockWait.Guard);
        }
        finally
        {
            held.Dispose();
        }
        Assert.False(File.Exists(source));
        Assert.Equal(Newer, File.ReadAllBytes(destination));
        Assert.Equal(["Marrow.zip"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    public async Task Replacing_an_item_puts_the_new_bytes_at_the_original_and_removes_the_replacement()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        // Services/Providers/CommandCodeSession.swift:753-758: <id>.reply.json.tmp replaces <id>.reply.json.
        var file = scratch.Seed("call-9.reply.json", Old);
        var temporary = scratch.Seed("call-9.reply.json.tmp", Newer);
        await OffThread(() => FileOps.ReplaceItemAt(file, temporary)).WaitAsync(ManualClockWait.Guard);
        Assert.Equal(Newer, File.ReadAllBytes(file));
        Assert.False(File.Exists(temporary));
        Assert.Equal(["call-9.reply.json"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task Replacing_an_item_held_open_retries_and_completes_once_released()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var file = scratch.Seed("call-9.reply.json", Old);
        var temporary = scratch.Seed("call-9.reply.json.tmp", Newer);
        var held = ScratchFolder.HoldOpen(file);
        try
        {
            // Negative control: the plain replace fails on the lingering handle.
            Assert.ThrowsAny<IOException>(() => File.Replace(temporary, file, null));
            Assert.True(File.Exists(temporary));

            var replace = OffThread(() => FileOps.ReplaceItemAt(file, temporary));
            ManualClockWait.ForRetryTimer(runtime.Time, replace);
            var first = FileOps.RetryDelays[0];
            runtime.Time.Advance(first - TimeSpan.FromTicks(1));
            Assert.False(replace.IsCompleted, "no retry before the first wait has fully elapsed");
            Assert.Equal(Old, ReadShared(file));
            // Nothing is staged anywhere else: the folder holds the two items and at most one entry not ending in .json.
            AssertAtMostOneTemporaryBeside(scratch, "", "call-9.reply.json", "call-9.reply.json.tmp");

            held.Dispose();
            runtime.Time.Advance(TimeSpan.FromTicks(1));
            await replace.WaitAsync(ManualClockWait.Guard);
        }
        finally
        {
            held.Dispose();
        }
        Assert.Equal(Newer, File.ReadAllBytes(file));
        Assert.False(File.Exists(temporary));
        Assert.Equal(["call-9.reply.json"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task An_atomic_write_is_a_replace_so_a_reader_sharing_everything_keeps_the_old_bytes()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("library.json", Old);

        // Negative control: this holder lets an in-place write straight through and then reads the new bytes, so
        // only a real replace (Storage.swift:384 .atomic, R-FS-1) leaves it reading the old ones.
        var control = scratch.Seed("control.bin", Old);
        using (var controlHolder = ScratchFolder.HoldOpenSharingAll(control))
        {
            File.WriteAllBytes(control, Newer);
            Assert.Equal(Newer, ScratchFolder.ReadFromStart(controlHolder));
        }
        File.Delete(control);

        var held = ScratchFolder.HoldOpenSharingAll(path);
        try
        {
            var write = OffThread(() => FileOps.WriteAtomic(path, Newer));
            var seen = SpinWait.SpinUntil(() => write.IsCompleted || runtime.Time.ActiveTimers == 1, ManualClockWait.Guard);
            Assert.True(seen, "the write neither finished nor armed a retry timer on the manual clock");
            Assert.Equal(Old, ScratchFolder.ReadFromStart(held));
            if (!write.IsCompleted)
            {
                // A retry is pending (a plain rename over this holder is refused): the temporary file waits beside
                // the destination, never staged elsewhere, and its name does not end in .json.
                AssertOnlyATemporaryBeside(scratch, "", "library.json");
                held.Dispose();
                await DriveToCompletion(runtime, write);
            }
            await write.WaitAsync(ManualClockWait.Guard);
        }
        finally
        {
            held.Dispose();
        }
        Assert.Equal(Newer, File.ReadAllBytes(path));
        Assert.Equal(["library.json"], scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task An_atomic_write_tries_again_after_the_last_wait_and_lands_when_released_just_before_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("library.json", Old);
        var held = ScratchFolder.HoldOpen(path);
        try
        {
            await ReleaseJustBeforeTheLastRetry(runtime, held, OffThread(() => FileOps.WriteAtomic(path, Newer)));
        }
        finally
        {
            held.Dispose();
        }
        Assert.Equal(Newer, File.ReadAllBytes(path));
        Assert.Equal(["library.json"], scratch.Names());
        Assert.Equal(TotalOfRetryDelays(), runtime.Time.Elapsed);
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task Removing_tries_again_after_the_last_wait_and_deletes_when_released_just_before_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var path = scratch.Seed("call-7.request.json", Old);
        var held = ScratchFolder.HoldOpen(path);
        try
        {
            await ReleaseJustBeforeTheLastRetry(runtime, held, OffThread(() => FileOps.RemoveItem(path)));
        }
        finally
        {
            held.Dispose();
        }
        Assert.False(File.Exists(path));
        Assert.Empty(scratch.Names());
        Assert.Equal(TotalOfRetryDelays(), runtime.Time.Elapsed);
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task Moving_tries_again_after_the_last_wait_and_moves_when_released_just_before_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var source = scratch.Seed("download.tmp", Newer);
        var destination = scratch.PathOf("Marrow.zip");
        var held = ScratchFolder.HoldOpen(source);
        try
        {
            await ReleaseJustBeforeTheLastRetry(runtime, held, OffThread(() => FileOps.MoveItem(source, destination)));
        }
        finally
        {
            held.Dispose();
        }
        Assert.False(File.Exists(source));
        Assert.Equal(Newer, File.ReadAllBytes(destination));
        Assert.Equal(["Marrow.zip"], scratch.Names());
        Assert.Equal(TotalOfRetryDelays(), runtime.Time.Elapsed);
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Decision", "ED-068")]
    [Trait("Decision", "ED-125")]
    public async Task Replacing_tries_again_after_the_last_wait_and_completes_when_released_just_before_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var file = scratch.Seed("call-9.reply.json", Old);
        var temporary = scratch.Seed("call-9.reply.json.tmp", Newer);
        var held = ScratchFolder.HoldOpen(file);
        try
        {
            await ReleaseJustBeforeTheLastRetry(runtime, held, OffThread(() => FileOps.ReplaceItemAt(file, temporary)));
        }
        finally
        {
            held.Dispose();
        }
        Assert.Equal(Newer, File.ReadAllBytes(file));
        Assert.False(File.Exists(temporary));
        Assert.Equal(["call-9.reply.json"], scratch.Names());
        Assert.Equal(TotalOfRetryDelays(), runtime.Time.Elapsed);
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Removing_a_folder_removes_a_junction_inside_it_and_nothing_it_points_at()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        // Git.swift:619 under :600 "Nothing outside `paths` is touched"; AppModel+Hydra.swift:1110 removes a worktree.
        var keep = scratch.Seed(Path.Combine("outside", "keep.txt"), Old);
        File.SetAttributes(keep, FileAttributes.ReadOnly);
        var own = scratch.Seed(Path.Combine("worktree", "README.md"), Newer);
        File.SetAttributes(own, FileAttributes.ReadOnly);
        scratch.Junction(Path.Combine("worktree", "node_modules"), "outside");

        // Negative control: a recursive walk of the folder goes through the junction and reaches the outside file,
        // so a walk that clears read-only or deletes what it enumerates would damage it.
        Assert.Contains(
            Directory.EnumerateFileSystemEntries(scratch.PathOf("worktree"), "*", SearchOption.AllDirectories),
            e => e.EndsWith(Path.Combine("node_modules", "keep.txt"), StringComparison.Ordinal));

        await OffThread(() => FileOps.RemoveItem(scratch.PathOf("worktree"))).WaitAsync(ManualClockWait.Guard);
        Assert.False(Directory.Exists(scratch.PathOf("worktree")));
        Assert.Equal(["outside"], scratch.Names());
        Assert.Equal(["keep.txt"], scratch.Names("outside"));
        Assert.Equal(Old, File.ReadAllBytes(keep));
        Assert.True(File.GetAttributes(keep).HasFlag(FileAttributes.ReadOnly), "the outside file keeps its read-only attribute");
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    public async Task Removing_a_junction_removes_only_the_link()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var keep = scratch.Seed(Path.Combine("outside", "keep.txt"), Old);
        File.SetAttributes(keep, FileAttributes.ReadOnly);
        Directory.CreateDirectory(scratch.PathOf("worktree"));
        var link = scratch.Junction(Path.Combine("worktree", "node_modules"), "outside");

        await OffThread(() => FileOps.RemoveItem(link)).WaitAsync(ManualClockWait.Guard);
        Assert.Empty(scratch.Names("worktree"));
        Assert.Equal(["outside", "worktree"], scratch.Names());
        Assert.Equal(["keep.txt"], scratch.Names("outside"));
        Assert.Equal(Old, File.ReadAllBytes(keep));
        Assert.True(File.GetAttributes(keep).HasFlag(FileAttributes.ReadOnly), "the target's file keeps its read-only attribute");
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-ERR-1")]
    [Trait("Decision", "ED-123")]
    [Trait("Decision", "ED-125")]
    [Trait("Oracle", "O-35")]
    public async Task Replacing_a_missing_original_throws_file_not_found_at_once_and_moves_nothing()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        // The common case at the only call site: CommandCodeSession.swift:754-758 replaces <id>.reply.json, which
        // does not exist before the first reply. This expectation is PARKED on oracle O-35 (ED-123, R-TEST-6):
        // swift-corelibs-foundation _replaceItem runs attributesOfItem on the original first, so it throws, and
        // CommandCodeSession.swift:759-760 then falls back to a direct write. No Req trait until it is settled.
        var file = scratch.PathOf("call-9.reply.json");
        var temporary = scratch.Seed("call-9.reply.json.tmp", Newer);
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => OffThread(() => FileOps.ReplaceItemAt(file, temporary)).WaitAsync(ManualClockWait.Guard));
        Assert.IsAssignableFrom<FileNotFoundException>(error); // an IOException; not TimeoutException: nothing waited
        Assert.True(SwiftTry.IsThrownError(error), "CommandCodeSession.swift:759-760 catches it and writes instead");
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.Equal(Newer, File.ReadAllBytes(temporary));
        Assert.Equal(["call-9.reply.json.tmp"], scratch.Names());
    }

    // Holds a real handle through every wait but the last, releases it after the last retry timer is armed and
    // before that wait elapses: a correct helper makes one more attempt after the last wait.
    private static async Task ReleaseJustBeforeTheLastRetry(TestRuntime runtime, FileStream held, Task operation)
    {
        var delays = FileOps.RetryDelays;
        for (var i = 0; i < delays.Count - 1; i++)
        {
            ManualClockWait.ForRetryTimer(runtime.Time, operation);
            runtime.Time.Advance(delays[i]);
        }
        ManualClockWait.ForRetryTimer(runtime.Time, operation);
        held.Dispose();
        runtime.Time.Advance(delays[^1]);
        await operation.WaitAsync(ManualClockWait.Guard);
    }

    // Fires each pending retry timer until the operation finishes; at most one attempt per wait in the schedule.
    private static async Task DriveToCompletion(TestRuntime runtime, Task operation)
    {
        for (var fired = 0; !operation.IsCompleted && fired <= FileOps.RetryDelays.Count; fired++)
        {
            var seen = SpinWait.SpinUntil(() => operation.IsCompleted || runtime.Time.ActiveTimers == 1, ManualClockWait.Guard);
            Assert.True(seen, "the operation neither finished nor armed a retry timer on the manual clock");
            if (!operation.IsCompleted) runtime.Time.Advance(FileOps.RetryDelays.Max());
        }
        await operation.WaitAsync(ManualClockWait.Guard);
    }

    private static TimeSpan TotalOfRetryDelays() => FileOps.RetryDelays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);

    // While a retry waits: the folder holds the items and exactly one more entry, the temporary file, whose name
    // does not end in .json (Storage.swift:125).
    private static void AssertOnlyATemporaryBeside(ScratchFolder scratch, string folder, params string[] items)
    {
        var names = scratch.Names(folder);
        Assert.All(items, item => Assert.Contains(item, names));
        var extra = Assert.Single(names.Except(items, StringComparer.Ordinal));
        Assert.False(extra.EndsWith(".json", StringComparison.Ordinal), "the temporary file ends in .json: " + extra);
    }

    // While a retry waits: the folder holds the items and at most one more entry, not ending in .json.
    private static void AssertAtMostOneTemporaryBeside(ScratchFolder scratch, string folder, params string[] items)
    {
        var names = scratch.Names(folder);
        Assert.All(items, item => Assert.Contains(item, names));
        var extra = names.Except(items, StringComparer.Ordinal).ToArray();
        Assert.True(extra.Length <= 1, "more than one staged entry: " + string.Join(", ", extra));
        Assert.DoesNotContain(extra, n => n.EndsWith(".json", StringComparison.Ordinal));
    }

    // Reads a file another handle holds open for reading (File.ReadAllBytes asks for FileShare.Read, which that
    // handle allows).
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
