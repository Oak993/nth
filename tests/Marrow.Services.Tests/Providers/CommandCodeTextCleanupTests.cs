using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

[Trait("Decision", "ED-NEW-dextra-open-12")]
public sealed class CommandCodeTextCleanupTests
{
    [Fact]
    public async Task A_second_start_does_not_replay_an_active_head_ownership_record()
    {
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new CommandCodeTextHeadFixture("good");
        first.OnStopAsync = async () =>
        {
            File.WriteAllText(Path.Combine(first.Environment["HOME"], "question.txt"), "PRIVATE-SENTINEL");
            stopping.SetResult();
            await release.Task;
        };
        var pending = CommandCodeTextHead.RunAsync(first.Configuration, "Q", () => true, CancellationToken.None, null, first.Make);
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            var next = new CommandCodeTextHeadFixture("good");
            await CommandCodeTextHead.RunAsync(next.Configuration, "Q", () =>
            {
                if (next.Started) Assert.Equal("PRIVATE-SENTINEL", File.ReadAllText(Path.Combine(first.Environment["HOME"], "question.txt")));
                return true;
            }, CancellationToken.None, null, next.Make);
        }
        finally { release.SetResult(); await pending; }
        Assert.False(Directory.Exists(first.Environment["HOME"]));
    }

    [Fact]
    public async Task A_previous_process_cleanup_record_is_replayed_but_unknown_versions_and_unregistered_folders_are_preserved()
    {
        var root = TestDataDirectory.Read()!;
        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(root, "commandcode-text-" + id);
        var record = Path.Combine(root, ".commandcode-text-" + id + ".cleanup-v1");
        var unknown = Path.Combine(root, ".commandcode-text-" + Guid.NewGuid().ToString("N") + ".cleanup-v2");
        var unregistered = Path.Combine(root, "commandcode-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "transcripts"));
        File.WriteAllText(Path.Combine(folder, "transcripts", "question.txt"), "SECRET-SENTINEL");
        File.WriteAllBytes(record, []); // Durable intent, with no live lease: a previous process died before recording outcome.
        File.WriteAllText(unknown, "NEWER-SENTINEL");
        Directory.CreateDirectory(unregistered);
        try
        {
            var next = new CommandCodeTextHeadFixture("good");
            await CommandCodeTextHead.RunAsync(next.Configuration, "Q", () => true, CancellationToken.None, null, next.Make);
            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(record));
            Assert.Equal("NEWER-SENTINEL", File.ReadAllText(unknown));
            Assert.True(Directory.Exists(unregistered));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            File.Delete(record); File.Delete(unknown); Directory.Delete(unregistered);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("refusal")]
    [InlineData("cancel")]
    [InlineData("stop-failure")]
    public async Task Every_exit_removes_nested_child_files_and_the_junction_but_preserves_its_outside_target(string outcome)
    {
        using var cancel = new CancellationTokenSource();
        var fixture = new CommandCodeTextHeadFixture("good", outcome == "refusal" ? "tool" : null);
        var outside = Path.Combine(TestDataDirectory.Read()!, "text-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "question.txt");
        File.WriteAllText(sentinel, "OUTSIDE-SENTINEL");
        fixture.OnExit = () => { if (outcome == "cancel") cancel.Cancel(); };
        fixture.OnStop = () =>
        {
            var nested = Path.Combine(fixture.Environment["HOME"], "nested", "transcripts");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "question.txt"), "QUESTION-SENTINEL");
            File.SetAttributes(Path.Combine(nested, "question.txt"), FileAttributes.ReadOnly);
            WindowsJunction.Create(TestDataDirectory.Read()!, Path.Combine(nested, "outside"), outside);
            if (outcome == "stop-failure") throw new IOException("fixture stop failure");
        };
        try
        {
            var error = await Record.ExceptionAsync(() => CommandCodeTextHead.RunAsync(fixture.Configuration, "QUESTION-SENTINEL", () => true, cancel.Token, null, fixture.Make));
            switch (outcome)
            {
                case "success": Assert.Null(error); break;
                case "refusal": Assert.IsType<InvalidOperationException>(error); break;
                case "cancel": Assert.IsAssignableFrom<OperationCanceledException>(error); break;
                case "stop-failure": Assert.IsType<IOException>(error); break;
            }
            Assert.False(Directory.Exists(fixture.Environment["HOME"]));
            Assert.Equal("OUTSIDE-SENTINEL", File.ReadAllText(sentinel));
        }
        finally
        {
            // Disposable fixture cleanup unlinks the junction before walking its own files.
            var link = Path.Combine(fixture.Environment["HOME"], "nested", "transcripts", "outside");
            if (Directory.Exists(link)) Directory.Delete(link);
            var privateFile = Path.Combine(fixture.Environment["HOME"], "nested", "transcripts", "question.txt");
            if (File.Exists(privateFile)) File.SetAttributes(privateFile, FileAttributes.Normal);
            if (Directory.Exists(fixture.Environment["HOME"])) Directory.Delete(fixture.Environment["HOME"], true);
            File.Delete(sentinel); Directory.Delete(outside);
        }
    }

    [Fact]
    public async Task A_locked_child_file_is_recorded_without_secrets_and_retried_before_the_next_child_starts()
    {
        var lines = new List<string>();
        using var sink = Logger.Sink.Override((line, _) => lines.Add(line));
        var first = new CommandCodeTextHeadFixture("good");
        FileStream? locked = null;
        first.OnStop = () =>
        {
            var file = Path.Combine(first.Environment["HOME"], "QUESTION-SENTINEL.txt");
            File.WriteAllText(file, "SECRET-SENTINEL");
            locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        };
        try
        {
            await CommandCodeTextHead.RunAsync(first.Configuration, "SECRET-SENTINEL", () => true, CancellationToken.None, null, first.Make);
            Assert.True(Directory.Exists(first.Environment["HOME"]));
            Assert.Contains(lines, line => line.Contains("commandcode-text-cleanup-failed", StringComparison.Ordinal));
            Assert.All(lines, line => { Assert.DoesNotContain("SECRET-SENTINEL", line, StringComparison.Ordinal); Assert.DoesNotContain("QUESTION-SENTINEL", line, StringComparison.Ordinal); });
            Assert.NotNull(locked);
            locked.Dispose(); locked = null;
            var next = new CommandCodeTextHeadFixture("good");
            await CommandCodeTextHead.RunAsync(next.Configuration, "Q", () =>
            {
                if (next.Started) Assert.False(Directory.Exists(first.Environment["HOME"]));
                return true;
            }, CancellationToken.None, null, next.Make);
            Assert.False(Directory.Exists(first.Environment["HOME"]));
            Assert.Contains(lines, line => line.Contains("commandcode-text-cleanup-retried", StringComparison.Ordinal));
        }
        finally
        {
            locked?.Dispose();
            if (first.Environment.TryGetValue("HOME", out var folder) && Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
