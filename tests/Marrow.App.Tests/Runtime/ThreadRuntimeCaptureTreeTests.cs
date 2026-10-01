using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// captureTree(_:) (App/Runtime/ThreadRuntime.swift:3458-3468, B3): working-tree snapshots for one thread run strictly one
// after another, each waiting for the one before it, because they are heavy and a turn's commands would otherwise run
// several at once. Git runs locally against a real checkout; nothing else is touched.
public sealed class ThreadRuntimeCaptureTreeTests
{
    private static Task<string?> Capture(ThreadRuntimeTurnRig rig, Marrow.Services.Git.Git git) =>
        ThreadRuntimeReflection.Call<Task<string?>>(rig.Runtime, "CaptureTree", git)!;

    [Fact]
    [Trait("Req", "PR-RUNTIME-194")] // :3459-3466 each capture awaits the previous one before its own work, forming a chain per runtime
    public async Task A_capture_waits_for_the_one_before_it_and_the_chain_is_per_runtime()
    {
        AppModelGitRepo repo = null!;
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude,
            projectFactory: harness => (repo = AppModelGitRepo.WithCommit(harness.NewFolder("project"))).Root);
        var git = new Marrow.Services.Git.Git(repo.Root);
        var earlier = new TaskCompletionSource<string?>();
        ThreadRuntimeReflection.Set(rig.Runtime, "treeCaptures", earlier.Task); // a capture that is still running

        Task<string?> first = null!, second = null!;
        rig.H.Main.OnMain(() =>
        {
            first = Capture(rig, git);
            second = Capture(rig, git);
        });
        rig.H.Main.Loop.RunUntilIdle();
        Thread.Sleep(300); // long enough for a snapshot of this checkout to have finished, were it not held back

        rig.H.Main.Loop.RunUntilIdle();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Same(second, ThreadRuntimeReflection.Get<Task<string?>>(rig.Runtime, "treeCaptures")); // the next one chains onto this one

        rig.H.Main.OnMain(() => earlier.SetResult("earlier-tree"));
        rig.H.Main.Loop.RunUntil(() => second.IsCompleted, AppModelHarness.Bound);

        var firstTree = await first;
        Assert.NotNull(firstTree);
        Assert.Equal(firstTree, await second); // the checkout did not change in between
        Assert.Equal(repo.Git("rev-parse", "HEAD^{tree}").Trim(), firstTree); // the snapshot is of the working tree as git sees it
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-194")] // :3466-3467 a failed capture yields nil
    public async Task A_capture_that_fails_yields_no_tree()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var git = new Marrow.Services.Git.Git(rig.H.NewFolder("not-a-repository"));
        Task<string?> capture = null!;
        rig.H.Main.OnMain(() => { capture = Capture(rig, git); });
        rig.H.Main.Loop.RunUntil(() => capture.IsCompleted, AppModelHarness.Bound);

        Assert.Null(await capture);
    }
}
