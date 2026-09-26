using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Runtime;

// Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B3 52bc792), :2138-2150, :3342-3366, :3383-3404, :3425-3454
// (the command edits a finished command's row takes from the working tree).
public sealed class ThreadRuntimeCommandEditsTests
{
    private const string CommandID = "command-1";

    [Fact]
    [Trait("Req", "PR-STORE-175")] // App/Runtime/ThreadRuntime.swift:2144-2150, :3425-3448
    public void A_command_row_whose_reported_edit_has_no_diff_takes_the_edits_of_the_working_tree()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("project"));
        var (app, runtime) = OpenRuntime(h, repository);
        // The edit SimpleDiff.unified makes of two identical texts: FileEdit.Stats(diff: nil, additions: 0,
        // deletions: 0) (Core/Support/Text.swift:105-115). Built as that value, so the test pins settleCommand's
        // reading of a nil diff whatever SimpleDiff port is merged; SimpleDiffTests pins the producer.
        var unchanged = new FileEdit(Path.Combine(repository.Root, "a.txt"), Diff: null, Additions: 0, Deletions: 0);

        var edits = RunCommand(h, repository, runtime, unchanged);

        // :3447 counts the nil-diff edit as unreported, so :3448 puts the working tree's edit in its place.
        var edit = Assert.Single(edits);
        Assert.NotEqual(unchanged, edit);
        Assert.NotNull(edit.Diff);
        Assert.Contains("\n+after", edit.Diff, StringComparison.Ordinal);
        Assert.Equal((1L, 0L), (edit.Additions, edit.Deletions));
        Assert.EndsWith("a.txt", edit.Path, StringComparison.Ordinal);
        GC.KeepAlive(app);
    }

    [Fact]
    [Trait("Req", "PR-STORE-175")] // App/Runtime/ThreadRuntime.swift:3446-3447
    public void A_command_row_whose_reported_edit_carries_a_patch_keeps_it()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("project"));
        var (app, runtime) = OpenRuntime(h, repository);
        // The negative control of the test above: the same command and the same working-tree change, but the provider
        // reported the edit with a patch, so "A provider that did report its edits keeps them" (:3446).
        var reported = new FileEdit(Path.Combine(repository.Root, "a.txt"), "a\n", "a\nreported\n");
        Assert.NotNull(reported.Diff);

        var edits = RunCommand(h, repository, runtime, reported);

        Assert.Equal(reported, Assert.Single(edits));
        GC.KeepAlive(app);
    }

    // The runtime holds its model weakly (ThreadRuntime.swift:409), so each test keeps the returned model alive until
    // it is done: a collected model leaves no repository to watch (:3386) and the command is never settled.
    private static (AppModel App, ThreadRuntime Runtime) OpenRuntime(AppModelHarness h, AppModelGitRepo repository)
    {
        var project = AppModelSamples.Project("P", repository.Root);
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(thread.Id), Storage.ThreadURL(thread.Id));
        var app = h.MakeModel();
        var runtime = h.Main.OnMain(() => app.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return (app, runtime);
    }

    // One command of a running turn that appends a line to a.txt and finishes reporting `reported` as its edit; returns
    // the row's edits once the command has settled.
    private static ValueList<FileEdit> RunCommand(
        AppModelHarness h,
        AppModelGitRepo repository,
        ThreadRuntime runtime,
        FileEdit reported)
    {
        var committedTree = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("Append a line to a.txt.");
            // treeBeforeCommand (:3399-3404) takes, with no settled tree, the last snapshot a command of the turn
            // finished; the committed tree stands in for it, so the watch starts (:3383-3391) without a live session.
            typeof(ThreadRuntime).GetField("latestTree", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(runtime, committedTree);
            runtime.Rehearse(new ProviderEvent.ToolStarted(CommandID, new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Command,
                Title = "printf 'after\\n' >> a.txt",
            }));
        });

        File.WriteAllBytes(Path.Combine(repository.Root, "a.txt"), Encoding.UTF8.GetBytes("a\nafter\n"));

        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.ToolUpdated(CommandID, new ToolUpdate
        {
            Status = ToolCall.Status.Completed,
            Edits = new ValueList<FileEdit>([reported]),
        })));
        // The settle task of :3428 (commandSettles), read by reflection so the test waits for it to end rather than
        // for a change the control case must never see.
        var settles = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TaskHandle>>(typeof(ThreadRuntime)
            .GetField("commandSettles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime));
        var settle = h.Main.OnMain(() => settles[CommandID]);
        h.Main.Loop.RunUntil(() => settle.Completion.IsCompleted, AppModelHarness.Bound);
        Assert.True(settle.Completion.IsCompletedSuccessfully);

        return h.Main.OnMain(() =>
        {
            var row = Assert.Single(runtime.Entries, entry => entry.Id == CommandID);
            var call = Assert.IsType<TimelineItem.Content.Tool>(row.Item.TimelineItemContent).Value;
            Assert.Equal(ToolCall.Status.Completed, call.ToolCallStatus);
            runtime.CancelPersistence();
            return call.Edits;
        });
    }
}
