using System.Collections.Concurrent;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Chat;

// DroppyCode/App/Views/Chat/ChatView.swift:1270-1307 GitStatusModel.refresh(_:force:) (B3 52bc792): the reads it makes, counted as the processes it starts. The seam is
// ShellObserver (ED-207): it reports every Shell.Run call of the flow as the call is entered, before the process starts, and the observer may hold the call there, so
// "how many processes" and "all three in flight before any one returns" are observed rather than inferred from the branch the code takes (PR-GIT-126's verification method).
[Collection("Headless")]
public sealed class ChatViewGitReadsTests(HeadlessSession headless)
{
    private static string? ReadOf(ShellInvocation call)
    {
        if (call.Arguments.Contains("status")) return "status";
        if (call.Arguments.Contains("for-each-ref")) return "branches";
        if (call.Arguments.Contains("remote")) return "web URL";
        return null;
    }

    [Fact]
    [Trait("Req", "PR-GIT-126")] // ChatView.swift:1289-1292: a read within 30 seconds, when not forced, is answered from the shared snapshot and starts no process; at 30 seconds it is stale
    public void A_refresh_that_is_not_forced_within_thirty_seconds_starts_no_process_and_one_at_thirty_reads_again() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
        var first = new GitStatusModel();
        h.Main.Loop.Run(() => first.Refresh(directory));
        Assert.Equal("main", first.Status!.Branch);
        var calls = new ConcurrentQueue<ShellInvocation>();
        var second = new GitStatusModel();
        h.Main.Advance(TimeSpan.FromSeconds(29));
        h.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(calls.Enqueue);
            await second.Refresh(directory, force: false);
        });
        Assert.True(second.IsRepository); // the cached snapshot, applied at once (:1291)
        Assert.Empty(calls); // 29 seconds old: fresh, no git at all
        h.Main.Advance(TimeSpan.FromSeconds(1));
        h.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(calls.Enqueue);
            await second.Refresh(directory, force: false);
        });
        Assert.NotEmpty(calls); // exactly 30 is stale (:1292 strictly under): git runs again
        Assert.Contains(calls, call => call.Arguments.Contains("--is-inside-work-tree"));
    });

    [Fact]
    [Trait("Req", "PR-GIT-126")] // ChatView.swift:1294-1296: a directory that is no repository costs exactly one probe (isRepository) and leaves status, branches and the web URL empty
    public void A_refresh_of_a_directory_that_is_not_a_repository_starts_exactly_one_probe() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        var directory = h.NewFolder("not-a-repository");
        var calls = new ConcurrentQueue<ShellInvocation>();
        var model = new GitStatusModel();
        h.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(calls.Enqueue);
            await model.Refresh(directory);
        });
        var only = Assert.Single(calls);
        Assert.Contains("--is-inside-work-tree", only.Arguments);
        Assert.False(model.IsRepository);
        Assert.Null(model.Status);
        Assert.Empty(model.Branches);
        Assert.Null(model.RemoteURL);
    });

    [Fact]
    [Trait("Req", "PR-GIT-126")] // ChatView.swift:1297-1299 `async let status = git.status()`, `branches = git.branches()`, `remoteURL = git.remoteWebURL()`: three concurrent children
    public void A_refresh_of_a_repository_reads_the_status_the_branches_and_the_web_URL_concurrently() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
        ChatGitFixtures.Run(directory, "remote", "add", "origin", "https://example.invalid/group/project.git");
        // Each read's first call is held at its entry until all three have been entered: three reads in flight at once pass; reads awaited one after the other never do
        // (the first waits for the others, which are not started until it has returned), and time out here.
        using var gate = new Barrier(3);
        var entered = new ConcurrentDictionary<string, bool>();
        var timedOut = 0;
        var model = new GitStatusModel();
        h.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(call =>
            {
                if (ReadOf(call) is not { } read || !entered.TryAdd(read, true)) return;
                if (!gate.SignalAndWait(TimeSpan.FromSeconds(8))) Interlocked.Increment(ref timedOut);
            });
            await model.Refresh(directory);
        });
        Assert.Equal(0, timedOut);
        Assert.Equal(["branches", "status", "web URL"], entered.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal("main", model.Status!.Branch);
        Assert.Equal("main", Assert.Single(model.Branches).Name);
        Assert.Equal("https://example.invalid/group/project", model.RemoteURL!.AbsoluteString);
    });
}
