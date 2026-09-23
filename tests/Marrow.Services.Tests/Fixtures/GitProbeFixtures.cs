using System.Text;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin: probes for Services.Git.Git part 3 (Git.swift:121-129, :244-257, :431-716).
// The code under test runs the system git with an argv it builds itself, so what it did is read back through git's own
// extension points, each configured inside the throwaway repository only (R-TEST-9, R-TEST-10):
// - a clean filter (gitattributes "filter") runs for every file `git add` or `git apply` hashes, inheriting the
//   process environment, so it can record GIT_INDEX_FILE, the private index the Swift names (Git.swift:249, :474, :699);
// - a reference-transaction hook sees every ref transaction, so one `update-ref --stdin` batch is one transaction;
// - a clean filter, a diff textconv or a post-checkout hook that sleeps holds a git process open, so the deadline of
//   that one process can be measured on the manual clock.
// Every script is run by Git for Windows' own sh, with forward-slashed paths.

/// <summary>The probes, installed into a <see cref="GitRepository"/>.</summary>
public static class GitProbes
{
    /// <summary>A path as Git for Windows' sh reads it.</summary>
    public static string ShellPath(string path) => path.Replace('\\', '/');

    /// <summary>
    /// A clean filter on every path that appends "GIT_INDEX_FILE|path" to a log and passes the content through
    /// unchanged; returns the log's path. Blob ids are not changed by it.
    /// </summary>
    public static string InstallIndexProbe(GitRepository repository)
    {
        var log = repository.Child(".git", "index-probe.log");
        var script = repository.Child(".git", "index-probe.sh");
        WriteScript(script, "printf '%s|%s\\n' \"$GIT_INDEX_FILE\" \"$1\" >> \"" + ShellPath(log) + "\"\ncat\n");
        repository.Git("config", "filter.probe.clean", "sh \"" + ShellPath(script) + "\" %f");
        AppendAttributes(repository, "* filter=probe");
        return log;
    }

    /// <summary>The GIT_INDEX_FILE values the probe recorded, in order (empty when it never ran).</summary>
    public static IReadOnlyList<string> ProbedIndexFiles(string log) =>
        File.Exists(log)
            ? Encoding.UTF8.GetString(File.ReadAllBytes(log)).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line[..line.IndexOf('|', StringComparison.Ordinal)]).ToList()
            : [];

    /// <summary>A clean filter on every path that writes a marker and then sleeps; returns the marker's path.</summary>
    public static string InstallStallFilter(GitRepository repository)
    {
        var marker = repository.Child(".git", "stall-started");
        var script = repository.Child(".git", "stall-filter.sh");
        WriteScript(script, "echo started > \"" + ShellPath(marker) + "\"\nsleep 600\ncat\n");
        repository.Git("config", "filter.stall.clean", "sh \"" + ShellPath(script) + "\"");
        AppendAttributes(repository, "* filter=stall");
        return marker;
    }

    /// <summary>A diff textconv on *.txt that writes a marker and then sleeps; returns the marker's path.</summary>
    public static string InstallStallTextconv(GitRepository repository)
    {
        var marker = repository.Child(".git", "stall-started");
        var script = repository.Child(".git", "stall-textconv.sh");
        WriteScript(script, "echo started > \"" + ShellPath(marker) + "\"\nsleep 600\ncat \"$1\"\n");
        repository.Git("config", "diff.stall.textconv", "sh \"" + ShellPath(script) + "\"");
        AppendAttributes(repository, "*.txt diff=stall");
        return marker;
    }

    /// <summary>A post-checkout hook that writes a marker and then sleeps; returns the marker's path.</summary>
    public static string InstallStallPostCheckout(GitRepository repository)
    {
        var marker = repository.Child(".git", "stall-started");
        WriteScript(repository.Child(".git", "hooks", "post-checkout"),
            "#!/bin/sh\necho started > \"" + ShellPath(marker) + "\"\nsleep 600\n");
        return marker;
    }

    /// <summary>
    /// A reference-transaction hook that logs each state line and the transaction's "old new ref" lines; with
    /// <paramref name="refusePrepared"/> it exits 1 in the "prepared" state, which aborts the transaction.
    /// </summary>
    public static string InstallTransactionLog(GitRepository repository, bool refusePrepared = false)
    {
        var log = repository.Child(".git", "transactions.log");
        var refuse = refusePrepared ? "if [ \"$1\" = prepared ]; then exit 1; fi\n" : "";
        WriteScript(repository.Child(".git", "hooks", "reference-transaction"),
            "#!/bin/sh\necho \"$1\" >> \"" + ShellPath(log) + "\"\ncat >> \"" + ShellPath(log) + "\"\n" + refuse);
        return log;
    }

    /// <summary>The transactions a <see cref="InstallTransactionLog"/> log holds: each state with its ref lines.</summary>
    public static IReadOnlyList<(string State, IReadOnlyList<string> Lines)> Transactions(string log)
    {
        var result = new List<(string State, IReadOnlyList<string> Lines)>();
        if (!File.Exists(log)) return result;
        foreach (var line in Encoding.UTF8.GetString(File.ReadAllBytes(log)).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains(' ', StringComparison.Ordinal)) result.Add((line, new List<string>()));
            else ((List<string>)result[^1].Lines).Add(line);
        }
        return result;
    }

    /// <summary>
    /// A tree built by git itself through an index of the test's own: optionally seeded with a byte copy of
    /// <paramref name="seed"/>, then each step run with GIT_INDEX_FILE set, then write-tree.
    /// </summary>
    public static string IndexTree(GitRepository repository, string? seed, params string[][] steps)
    {
        var index = repository.Child(".git", "expected-index-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (seed is not null) File.Copy(seed, index);
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = index };
            foreach (var step in steps) GitRepository.GitIn(repository.Root, environment, step);
            return GitRepository.GitIn(repository.Root, environment, "write-tree").Trim();
        }
        finally
        {
            File.Delete(index);
        }
    }

    /// <summary>
    /// Waits for <paramref name="marker"/> (the stalled process has started), then checks that the process's deadline
    /// is <paramref name="seconds"/>: one tick short the call still runs; at the deadline the process is ended and the
    /// call throws the failure as a ShellError.
    /// </summary>
    public static async Task AssertBudgetOnceStalled(TestRuntime runtime, Task call, int seconds, string marker)
    {
        Assert.True(GitWaits.Eventually(() => File.Exists(marker) || call.IsCompleted), "the stall never started");
        Assert.False(call.IsCompleted, "the call ended before its stall");
        runtime.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromTicks(1));
        Assert.False(SpinWait.SpinUntil(() => call.IsCompleted, TimeSpan.FromMilliseconds(300)), "ended before the budget");
        runtime.Advance(TimeSpan.FromTicks(1));
        for (var step = 0; step < 30 && !SpinWait.SpinUntil(() => call.IsCompleted, TimeSpan.FromMilliseconds(500)); step++)
            runtime.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<Marrow.Core.Support.ShellError>(() => call.WaitAsync(GitWaits.Bound));
    }

    /// <summary>Steps the manual clock 100 ms at a time until <paramref name="call"/> ends (FileOps retries wait on it).</summary>
    public static async Task Drive(TestRuntime runtime, Task call)
    {
        for (var step = 0; step < 600 && !SpinWait.SpinUntil(() => call.IsCompleted, TimeSpan.FromMilliseconds(20)); step++)
            runtime.Advance(TimeSpan.FromMilliseconds(100));
        await call.WaitAsync(GitWaits.Bound);
    }

    private static void AppendAttributes(GitRepository repository, string line)
    {
        var path = repository.Child(".git", "info", "attributes");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
    }

    private static void WriteScript(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }
}
