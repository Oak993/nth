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
//     App/Captures/WebsiteCaptures.swift:87); a missing item is an error that the try? sites swallow
//     (Services/Store/Storage.swift:147, :372).
//   FileManager.moveItem(at:to:): an existing destination is an error, which is why App/AppUpdater.swift:515-516
//     removes the destination before moving.
//   FileManager.replaceItemAt(_:withItemAt:): the original takes the new item's bytes and the new item is gone
//     (Services/Providers/CommandCodeSession.swift:757-758).
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
    [Trait("Decision", "ED-068")]
    public void The_retry_schedule_is_a_bounded_backoff()
    {
        var delays = FileOps.RetryDelays;
        Assert.True(delays.Count >= 2, "a backoff needs at least two waits");
        Assert.All(delays, d => Assert.True(d > TimeSpan.Zero, "every wait is positive"));
        for (var i = 1; i < delays.Count; i++) Assert.True(delays[i] >= delays[i - 1], "waits never shrink");
        Assert.True(delays[^1] > delays[0], "the waits grow: a backoff, not a fixed interval");
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
        Assert.Equal(TimeSpan.Zero, runtime.Time.Elapsed);
        Assert.False(Directory.Exists(scratch.PathOf("attachments")));
        Assert.Empty(scratch.Names());
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-4")]
    public async Task An_atomic_write_over_a_read_only_file_succeeds_as_a_POSIX_rename_does()
    {
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
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-068")]
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
            // While it waits the reader still sees the old bytes, and no other entry in the folder ends in .json:
            // Services/Store/Storage.swift:125 loads every *.json document it finds in a folder.
            Assert.Equal(Old, ReadShared(path));
            Assert.DoesNotContain(scratch.Names(), n => n != "library.json" && n.EndsWith(".json", StringComparison.Ordinal));

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
    public async Task Removing_a_missing_item_throws_at_once()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => OffThread(() => FileOps.RemoveItem(scratch.PathOf("gone.json"))).WaitAsync(ManualClockWait.Guard));
        Assert.IsAssignableFrom<IOException>(error); // not TimeoutException: a missing item is not transient
        Assert.True(SwiftTry.IsThrownError(error), "Storage.swift:147 swallows it with try?");
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.Equal(TimeSpan.Zero, runtime.Time.Elapsed);
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
        Assert.True(SwiftTry.IsThrownError(error), "AppUpdater.swift:517-519 catches it and reports the failure");
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.Equal(Newer, File.ReadAllBytes(source));
        Assert.Equal(Old, File.ReadAllBytes(destination));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-068")]
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
