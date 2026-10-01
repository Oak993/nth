using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Xunit;
using static Marrow.App.Tests.Fixtures.DiffInspectorTree;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Diff;

// Source: DroppyCode/App/Views/Diff/DiffInspector.swift (B3 52bc792), :110-155 (the load task and what it decides).
[Collection("Headless")]
public sealed class DiffInspectorLoadTests(HeadlessSession headless)
{
    private static string[] CardPaths(DiffInspector inspector) =>
        Descendants<DiffFileCard>(inspector).Select(card => (string)card.Tag!).ToArray();

    private static string[] ExpandedPaths(DiffInspector inspector) =>
        Descendants<DiffFileCard>(inspector)
            .Where(card => Descendants<DiffLinesView>(card).Any())
            .Select(card => (string)card.Tag!)
            .ToArray();

    private static void Click(DiffInspector inspector, string path) =>
        Assert.Single(Descendants<Button>(Assert.Single(Descendants<DiffFileCard>(inspector), card => (string)card.Tag! == path)))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string[] Paths(int count) => Enumerable.Range(1, count).Select(index => "f" + index + ".txt").ToArray();

    [Fact]
    [Trait("Req", "PR-GIT-132")] // App/Views/Diff/DiffInspector.swift:110-120 (the wait is 180 ms when no diff is cached)
    public void An_uncached_diff_is_read_only_after_a_180_millisecond_wait() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));
        rig.Store.Main.OnMain(rig.Runtime.NoteDiffChanged);

        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(179));
        rig.Store.Main.Loop.RunUntilIdle();
        Assert.False(rig.Store.Main.OnMain(() => rig.Runtime.HasCachedDiff(null)));
        Assert.Empty(CardPaths(inspector));

        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(rig.PumpUntil(() => CardPaths(inspector).Length == 1));
        Assert.True(rig.Store.Main.OnMain(() => rig.Runtime.HasCachedDiff(null)));
    });

    [Fact]
    [Trait("Req", "PR-GIT-132")] // App/Views/Diff/DiffInspector.swift:115-119 (a diff the runtime already parsed skips the wait)
    public void A_diff_the_runtime_already_parsed_loads_without_waiting() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var turn = rig.Turns[0].Id;
        rig.Store.Main.Loop.Run(() => rig.Runtime.ParsedDiff(turn), AppModelHarness.Bound);
        Assert.True(rig.Store.Main.OnMain(() => rig.Runtime.HasCachedDiff(turn)));
        var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));

        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = turn);

        // The manual clock never moves: the card can only appear if no wait was taken.
        Assert.True(rig.PumpUntil(() => CardPaths(inspector).Length == 1));
    });

    [Theory]
    [InlineData("revision")]
    [InlineData("selection")]
    [InlineData("focus")]
    [Trait("Req", "PR-GIT-132")] // App/Views/Diff/DiffInspector.swift:18-20, :110-120 (any of the three key parts restarts the task and its wait)
    public void A_change_of_the_diff_revision_the_selection_or_the_focus_restarts_the_wait(string part) => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var turn = rig.Turns[0].Id;
        var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));
        rig.Store.Main.OnMain(rig.Runtime.NoteDiffChanged);
        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(100));

        rig.Store.Main.OnMain(() =>
        {
            switch (part)
            {
                case "revision": rig.Runtime.NoteDiffChanged(); break;
                case "selection": rig.Runtime.DiffSelection = turn; break;
                default: rig.Runtime.ShowDiff(new Border(), turn: null, focusEdits: [new FileEdit("a.txt", "old\n", "new\n")]); break;
            }
        });
        var selection = part == "selection" ? (Guid?)turn : null;

        // 200 ms after the first change, only 100 after the second: the first wait was dropped, the second not over.
        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(100));
        rig.Store.Main.Loop.RunUntilIdle();
        Assert.False(rig.Store.Main.OnMain(() => rig.Runtime.HasCachedDiff(selection)));

        rig.Store.Main.Advance(TimeSpan.FromMilliseconds(80));
        Assert.True(rig.PumpUntil(() => rig.Runtime.HasCachedDiff(selection)));
    });

    [Fact]
    [Trait("Req", "PR-GIT-132")] // App/Views/Diff/DiffInspector.swift:110-120 (the task is cancelled when the popover closes; a cancelled sleep abandons the load)
    public void Closing_the_popover_during_the_wait_abandons_the_load() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));
        var window = new AvaloniaWindow { Width = 700, Height = 600, Content = inspector };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // Attaching to the tree is the popover opening: the first load, under the same wait.
            rig.Store.Main.Advance(TimeSpan.FromMilliseconds(100));
            window.Close();
            Dispatcher.UIThread.RunJobs();

            rig.Store.Main.Advance(TimeSpan.FromMilliseconds(500));
            rig.Store.Main.Loop.RunUntilIdle();
            Assert.False(rig.Store.Main.OnMain(() => rig.Runtime.HasCachedDiff(null)));
            Assert.Empty(CardPaths(inspector));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-GIT-133")] // App/Views/Diff/DiffInspector.swift:130-138, :162-173 (focus patches fill in what the diff cannot show, ahead of it)
    public void A_focused_edit_the_diff_lacks_is_prepended_and_one_it_has_is_not_repeated() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt", "src/b.txt"));
        var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));
        const string patch = "@@ -1,1 +1,1 @@\n-old\n+new\n";
        FileEdit[] edits =
        [
            new("z/new.txt", patch, 1, 1),        // not in the diff: kept
            new("src/b.txt", patch, 1, 1),        // the diff's own path: skipped
            new("pkg/src/b.txt", patch, 1, 1),    // the diff's path is a suffix of this one: skipped (:57 both directions)
            new("a.txt", patch, 1, 1),            // the diff's own path: skipped
            new("new.txt", patch, 1, 1),          // suffix of the file already collected: skipped
            new("other.txt", "", 0, 0),           // an empty patch: nothing
            new("nopatch.txt", Diff: null, Additions: 0, Deletions: 0), // no patch: nothing
            new("hunkless.txt", "not a diff at all\n", 0, 0), // a patch with no hunk: nothing
        ];

        rig.Store.Main.OnMain(() => rig.Runtime.ShowDiff(new Border(), turn: null, focusEdits: edits));
        rig.FinishLoad(inspector);

        Assert.Equal(new[] { "z/new.txt", "a.txt", "src/b.txt" }, CardPaths(inspector));
    });

    [Fact]
    [Trait("Req", "PR-GIT-133")] // App/Views/Diff/DiffInspector.swift:162-173 (an edit with no path is skipped)
    public void A_focused_edit_without_a_path_contributes_nothing() => headless.OnUiThread(() =>
    {
        const string patch = "@@ -1,1 +1,1 @@\n-old\n+new\n";
        var files = DiffInspector.FocusedFiles([new FileEdit("", patch, 1, 1), new FileEdit("kept.txt", patch, 1, 1)], ValueList<DiffFile>.Empty);

        Assert.Equal(new[] { "kept.txt" }, files.Select(file => file.Path));
    });

    [Fact]
    [Trait("Req", "PR-GIT-134")] // App/Views/Diff/DiffInspector.swift:149-151 (more than six files: all but the first collapse)
    public void Seven_files_open_with_only_the_first_expanded_and_six_open_all() => headless.OnUiThread(() =>
    {
        foreach (var count in new[] { 6, 7 })
        {
            using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches(Paths(count)));
            var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));
            rig.Store.Main.OnMain(rig.Runtime.NoteDiffChanged);
            rig.FinishLoad(inspector);

            Assert.Equal(Paths(count), CardPaths(inspector));
            Assert.Equal(count == 6 ? Paths(6) : new[] { "f1.txt" }, ExpandedPaths(inspector));
        }
    });

    [Fact]
    [Trait("Req", "PR-GIT-134")] // App/Views/Diff/DiffInspector.swift:149 (the collapse rule applies only while nothing is collapsed)
    public void A_reload_of_seven_files_keeps_what_the_reader_expanded() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches(Paths(7)));
        var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));
        rig.Store.Main.OnMain(rig.Runtime.NoteDiffChanged);
        rig.FinishLoad(inspector);
        rig.Store.Main.OnMain(() => Click(inspector, "f2.txt"));
        Assert.Equal(new[] { "f1.txt", "f2.txt" }, ExpandedPaths(inspector));

        rig.Store.Main.OnMain(rig.Runtime.NoteDiffChanged);
        rig.FinishLoad(inspector);

        Assert.Equal(new[] { "f1.txt", "f2.txt" }, ExpandedPaths(inspector));
    });

    [Fact]
    [Trait("Req", "PR-GIT-134")] // App/Views/Diff/DiffInspector.swift:152 (otherwise the collapsed set is intersected with the present files)
    public void A_reload_keeps_the_collapse_of_files_still_present_and_forgets_those_that_left() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("f1.txt", "f2.txt"), DiffInspectorRig.Patches("f3.txt"));
        var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));
        rig.Store.Main.OnMain(rig.Runtime.NoteDiffChanged);
        rig.FinishLoad(inspector);
        Assert.Equal(new[] { "f1.txt", "f2.txt", "f3.txt" }, ExpandedPaths(inspector));
        rig.Store.Main.OnMain(() => { Click(inspector, "f2.txt"); });
        rig.Store.Main.OnMain(() => { Click(inspector, "f3.txt"); });
        Assert.Equal(new[] { "f1.txt" }, ExpandedPaths(inspector));

        // Turn 1 shows f1 and f2 only: f2 is still collapsed, f3 is gone from the set.
        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[0].Id);
        rig.FinishLoad(inspector);
        Assert.Equal(new[] { "f1.txt", "f2.txt" }, CardPaths(inspector));
        Assert.Equal(new[] { "f1.txt" }, ExpandedPaths(inspector));

        // Back to all changes: f3 returns expanded, because the set was cut down to the files present.
        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = null);
        rig.FinishLoad(inspector);
        Assert.Equal(new[] { "f1.txt", "f2.txt", "f3.txt" }, CardPaths(inspector));
        Assert.Equal(new[] { "f1.txt", "f3.txt" }, ExpandedPaths(inspector));
    });

    [Fact]
    [Trait("Req", "PR-GIT-134")] // App/Views/Diff/DiffInspector.swift:143-148 (focused files open, every other card collapses, the first focused is the scroll target)
    public void A_focused_file_opens_alone_and_becomes_the_scroll_target_each_time() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt", "b.txt", "c.txt", "d.txt"));
        var inspector = rig.Store.Main.OnMain(() => new DiffInspector(rig.Runtime));
        string? Target() => (string?)typeof(DiffInspector).GetField("scrollTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inspector);
        long Request() => (long)typeof(DiffInspector).GetField("scrollRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inspector)!;
        Assert.Null(Target());
        Assert.Equal(0, Request());

        rig.Store.Main.OnMain(() => rig.Runtime.ShowDiff(new Border(), turn: null, focusEdits:
        [
            new FileEdit("c.txt", "old\n", "new\n"),
            new FileEdit("b.txt", "old\n", "new\n"),
        ]));
        rig.FinishLoad(inspector);

        Assert.Equal(new[] { "b.txt", "c.txt" }, ExpandedPaths(inspector));
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt", "d.txt" }, CardPaths(inspector));
        Assert.Equal("b.txt", Target()); // the first focused file in list order
        Assert.Equal(1, Request());

        // The reader collapses the focused card; focusing it again opens it (the collapsed set is rebuilt, not added to).
        rig.Store.Main.OnMain(() => Click(inspector, "b.txt"));
        Assert.Equal(new[] { "c.txt" }, ExpandedPaths(inspector));

        // The same focus asked for again (a new focus revision, the same target) still bumps the counter.
        rig.Store.Main.OnMain(() => rig.Runtime.ShowDiff(new Border(), turn: null, focusEdits:
        [
            new FileEdit("b.txt", "old\n", "new\n"),
        ]));
        rig.FinishLoad(inspector);
        Assert.Equal(new[] { "b.txt" }, ExpandedPaths(inspector));
        Assert.Equal("b.txt", Target());
        Assert.Equal(2, Request());
    });

    [Fact]
    [Trait("Req", "PR-GIT-132")] // App/Views/Diff/DiffInspector.swift:128-129 (a load cancelled while it parses abandons its result and leaves the previous content in place)
    public void A_load_cancelled_while_it_parses_leaves_the_previous_content_in_place() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"), DiffInspectorRig.Patches("b.txt"));
        var inspector = rig.NewInspector();
        rig.Reload(inspector);
        Assert.Equal(new[] { "a.txt", "b.txt" }, CardPaths(inspector));
        var pending = new TaskCompletionSource<ValueList<DiffFile>>();
        rig.SeedDiff(pending.Task, rig.Turns[0].Id);
        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[0].Id); // cached, so the load starts at once and waits on the parse
        rig.Store.Main.Loop.RunUntilIdle();

        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[1].Id); // a new key: the first load is cancelled
        pending.SetResult(new ValueList<DiffFile>([new DiffFile("stale.txt", DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty)]));
        rig.Store.Main.Loop.RunUntilIdle();

        Assert.Equal(new[] { "a.txt", "b.txt" }, CardPaths(inspector));
    });
}
