using System.Text;
using Avalonia.Controls;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Runtime;

// Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B2 1fbf81e), :405-412, :698-722, :1254-1281,
// :1765-1813, :1820-1936, :1960-2049, :2346-2353.
public sealed class ThreadRuntimePart1GitTests
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-026")] // App/Runtime/ThreadRuntime.swift:698-722
    public void Take_back_with_a_nonempty_draft_keeps_the_existing_turn()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h, AppModelGitRepo.Empty(h.NewFolder("project")));
        var turnID = h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("keep this turn");
            runtime.Draft = new ComposerDraft { Text = "typed while stopping" };
            var id = Assert.Single(runtime.Turns).Id;

            runtime.TakeBackRunningTurn();

            return id;
        });

        Assert.Equal(turnID, Assert.Single(runtime.Turns).Id);
        Assert.Equal("typed while stopping", runtime.Draft.Text);
    }

    [Fact]
    [Trait("Req", "PR-GIT-060")] // App/Runtime/ThreadRuntime.swift:1254-1281
    [Trait("Req", "PR-GIT-061")] // App/Runtime/ThreadRuntime.swift:2346-2353
    [Trait("Req", "PR-GIT-112")] // App/Runtime/ThreadRuntime.swift:1254
    public void Start_and_end_checkpoints_are_captured_in_the_thread_worktree()
    {
        using var h = new AppModelHarness();
        var projectRepo = AppModelGitRepo.Empty(h.NewFolder("project"));
        var threadRepo = AppModelGitRepo.WithCommit(h.NewFolder("thread-worktree"));
        var (app, thread, runtime) = OpenRuntime(h, projectRepo, threadRepo);
        h.PrepareBootstrap(app);

        h.Main.OnMain(() =>
        {
            runtime.Draft = new ComposerDraft { Text = "checkpoint boundary" };
            runtime.Send();
        });
        h.Main.Loop.RunUntil(
            () => runtime.Phase == RuntimePhase.Idle && runtime.Turns.Count == 1 && runtime.Turns[0].Status != TurnStatus.Running,
            AppModelHarness.Bound);

        var turn = Assert.Single(runtime.Turns);
        Assert.Equal(Git.CheckpointRef(thread.Id, turn.Index, "start"), turn.BaseCheckpoint);
        Assert.Equal(Git.CheckpointRef(thread.Id, turn.Index, "end"), turn.EndCheckpoint);
        Assert.Contains(turn.BaseCheckpoint!, threadRepo.Checkpoints(thread.Id));
        Assert.Contains(turn.EndCheckpoint!, threadRepo.Checkpoints(thread.Id));
        Assert.Empty(projectRepo.Checkpoints(thread.Id));
    }

    [Fact]
    [Trait("Req", "PR-GIT-112")] // App/Runtime/ThreadRuntime.swift:1812
    [Trait("Req", "PR-GIT-114")] // App/Runtime/ThreadRuntime.swift:1804-1813
    public void Revert_preview_uses_the_thread_worktree_and_sorted_paths_from_the_selected_suffix()
    {
        using var h = new AppModelHarness();
        var projectRepo = AppModelGitRepo.Empty(h.NewFolder("project"));
        var threadRepo = AppModelGitRepo.WithCommit(h.NewFolder("thread-worktree"));
        var threadID = Guid.NewGuid();
        var initial = threadRepo.Git("rev-parse", "HEAD").Trim();
        var before = CommitFile(threadRepo, "before.txt", "before\n", "before selected turn");
        var selectedBase = before;
        var selectedEnd = CommitFile(threadRepo, "zeta.txt", "zeta\n", "selected turn");
        var lastEnd = CommitFile(threadRepo, "beta.txt", "beta\n", "later turn");
        var turn0 = TurnWithUser(0, "before", touchedPaths: ["before.txt"],
            baseCheckpoint: Git.CheckpointRef(threadID, 0, "start"), endCheckpoint: Git.CheckpointRef(threadID, 0, "end"));
        var turn1 = TurnWithUser(1, "selected", touchedPaths: ["zeta.txt", "unused.txt"],
            baseCheckpoint: Git.CheckpointRef(threadID, 1, "start"), endCheckpoint: Git.CheckpointRef(threadID, 1, "end"));
        var turn2 = TurnWithUser(2, "later", touchedPaths: ["beta.txt", "zeta.txt"],
            baseCheckpoint: Git.CheckpointRef(threadID, 2, "start"), endCheckpoint: Git.CheckpointRef(threadID, 2, "end"));
        SetCheckpoint(threadRepo, threadID, 0, "start", initial);
        SetCheckpoint(threadRepo, threadID, 0, "end", before);
        SetCheckpoint(threadRepo, threadID, 1, "start", selectedBase);
        SetCheckpoint(threadRepo, threadID, 1, "end", selectedEnd);
        SetCheckpoint(threadRepo, threadID, 2, "start", selectedEnd);
        SetCheckpoint(threadRepo, threadID, 2, "end", lastEnd);

        var (_, _, runtime) = OpenRuntime(h, projectRepo, threadRepo, threadID,
            Document(threadID, turn0, turn1, turn2));
        var selectedID = turn1.Turn.Id;
        var handle = h.Main.OnMain(() => runtime.RevertPreview(selectedID));
        var preview = h.Main.Loop.Run(() => handle.Value, AppModelHarness.Bound);

        Assert.Equal(new[] { "beta.txt", "zeta.txt" }, preview.Files.Select(file => file.Path));
        Assert.All(preview.Files, file => Assert.False(file.IsKept));
    }

    [Fact]
    [Trait("Req", "PR-GIT-113")] // App/Runtime/ThreadRuntime.swift:1765-1789
    public void Revert_preview_shares_tasks_until_revision_or_ten_second_window_changes()
    {
        using var h = new AppModelHarness();
        var projectRepo = AppModelGitRepo.Empty(h.NewFolder("project"));
        var (_, _, runtime) = OpenRuntime(h, projectRepo);
        var turnID = h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("no checkpoint");
            return Assert.Single(runtime.Turns).Id;
        });

        var first = h.Main.OnMain(() => runtime.RevertPreview(turnID));
        var withinWindow = h.Main.OnMain(() => runtime.RevertPreview(turnID));
        Assert.Same(first, withinWindow);
        Assert.ThrowsAny<Exception>(() => h.Main.Loop.Run(() => first.Value, AppModelHarness.Bound));
        Assert.Same(first.Value, h.Main.OnMain(() => runtime.CachedRevertPreview(turnID)));

        h.Main.Advance(TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(1));
        Assert.Null(h.Main.OnMain(() => runtime.CachedRevertPreview(turnID)));
        var afterWindow = h.Main.OnMain(() => runtime.RevertPreview(turnID));
        Assert.NotSame(first, afterWindow);
        Assert.ThrowsAny<Exception>(() => h.Main.Loop.Run(() => afterWindow.Value, AppModelHarness.Bound));

        var revision = runtime.DiffRevision;
        h.Main.OnMain(runtime.NoteDiffChanged);
        Assert.Equal(revision + 1, runtime.DiffRevision);
        Assert.Null(h.Main.OnMain(() => runtime.CachedRevertPreview(turnID)));
    }

    [Fact]
    [Trait("Req", "PR-GIT-117")] // App/Runtime/ThreadRuntime.swift:1857-1902
    [Trait("Req", "PR-GIT-118")] // App/Runtime/ThreadRuntime.swift:1904-1913
    [Trait("Req", "PR-GIT-119")] // App/Runtime/ThreadRuntime.swift:1915-1935
    public void Revert_clears_nonrewindable_session_state_restores_files_and_trims_runtime_state()
    {
        using var h = new AppModelHarness();
        var projectRepo = AppModelGitRepo.Empty(h.NewFolder("project"));
        var threadRepo = AppModelGitRepo.WithCommit(h.NewFolder("thread-worktree"));
        var threadID = Guid.NewGuid();
        var baseHash = threadRepo.Git("rev-parse", "HEAD").Trim();
        var before = CommitFile(threadRepo, "changed.txt", "before\n", "before turn");
        var after = CommitFile(threadRepo, "changed.txt", "after\n", "selected turn");
        SetCheckpoint(threadRepo, threadID, 0, "start", baseHash);
        SetCheckpoint(threadRepo, threadID, 0, "end", before);
        SetCheckpoint(threadRepo, threadID, 1, "start", before);
        SetCheckpoint(threadRepo, threadID, 1, "end", after);
        var prior = TurnWithUser(0, "prior", touchedPaths: ["changed.txt"],
            baseCheckpoint: Git.CheckpointRef(threadID, 0, "start"), endCheckpoint: Git.CheckpointRef(threadID, 0, "end"));
        var selected = TurnWithUser(1, "selected", status: TurnStatus.Failed, touchedPaths: ["changed.txt"],
            baseCheckpoint: Git.CheckpointRef(threadID, 1, "start"), endCheckpoint: Git.CheckpointRef(threadID, 1, "end"));
        var (app, _, runtime) = OpenRuntime(h, projectRepo, threadRepo, threadID,
            Document(threadID, prior, selected), provider: ProviderKind.Cursor,
            providerSessionID: "old-session", providerResumeAt: "old-resume", lastStatus: TurnStatus.Failed);
        var (selection, priorEntryIDs) = h.Main.OnMain(() =>
        {
            runtime.DiffSelection = selected.Turn.Id;
            return (runtime.DiffRevision,
                runtime.Entries.Where(entry => entry.TurnID == prior.Turn.Id).Select(entry => entry.Id).ToArray());
        });

        var restored = h.Main.Loop.Run(() => runtime.Revert(selected.Turn.Id, restoreFiles: true), AppModelHarness.Bound);

        Assert.True(restored);
        Assert.Equal("before\n", File.ReadAllText(Path.Combine(threadRepo.Root, "changed.txt")));
        Assert.Equal(new[] { prior.Turn.Id }, runtime.Turns.Select(turn => turn.Id));
        Assert.Equal(priorEntryIDs, runtime.Entries.Select(entry => entry.Id));
        Assert.DoesNotContain(runtime.Entries, entry => entry.TurnID == selected.Turn.Id);
        Assert.Null(runtime.Usage);
        Assert.Null(runtime.DiffSelection);
        Assert.Equal(selection + 1, runtime.DiffRevision);
        Assert.Equal(TurnStatus.Completed, app.Thread(threadID)!.LastStatus);
        Assert.Null(app.Thread(threadID)!.ProviderSessionID);
        Assert.Null(app.Thread(threadID)!.ProviderResumeAt);
        Assert.DoesNotContain(runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Notice);
    }

    [Fact]
    [Trait("Req", "PR-GIT-118")] // App/Runtime/ThreadRuntime.swift:1904-1913
    [Trait("Req", "PR-GIT-119")] // App/Runtime/ThreadRuntime.swift:1915-1935
    [Trait("Req", "PR-GIT-120")] // App/Runtime/ThreadRuntime.swift:1820-1831
    public void Failed_file_restore_keeps_the_edit_in_the_composer_and_appends_a_notice_after_trimming()
    {
        using var h = new AppModelHarness();
        var projectRepo = AppModelGitRepo.Empty(h.NewFolder("project"));
        var threadRepo = AppModelGitRepo.WithCommit(h.NewFolder("thread-worktree"));
        var threadID = Guid.NewGuid();
        var baseHash = threadRepo.Git("rev-parse", "HEAD").Trim();
        var before = CommitFile(threadRepo, "changed.txt", "before\n", "before turn");
        var after = CommitFile(threadRepo, "changed.txt", "after\n", "selected turn");
        SetCheckpoint(threadRepo, threadID, 0, "start", baseHash);
        SetCheckpoint(threadRepo, threadID, 0, "end", before);
        SetCheckpoint(threadRepo, threadID, 1, "start", before);
        SetCheckpoint(threadRepo, threadID, 1, "end", after);
        var prior = TurnWithUser(0, "prior", touchedPaths: ["changed.txt"],
            baseCheckpoint: Git.CheckpointRef(threadID, 0, "start"), endCheckpoint: Git.CheckpointRef(threadID, 0, "end"));
        var selected = TurnWithUser(1, "selected", status: TurnStatus.Failed, touchedPaths: ["changed.txt"],
            baseCheckpoint: Git.CheckpointRef(threadID, 1, "start"), endCheckpoint: Git.CheckpointRef(threadID, 1, "end"));
        var (_, _, runtime) = OpenRuntime(h, projectRepo, threadRepo, threadID,
            Document(threadID, prior, selected), provider: ProviderKind.Cursor);
        var editedAttachment = new Attachment
        {
            Name = "draft.png",
            Path = @"C:\uploads\draft.png",
            MimeType = "image/png",
        };
        File.WriteAllText(Path.Combine(threadRepo.Root, ".git", "index.lock"), "hold index lock");
        try
        {
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "earlier draft" });
            h.Main.Loop.Run(
                () => runtime.Resend(selected.Turn.Id, "edited text", [editedAttachment], restoreFiles: true),
                AppModelHarness.Bound);

            Assert.Equal("after\n", File.ReadAllText(Path.Combine(threadRepo.Root, "changed.txt")));
            Assert.Equal("edited text", runtime.Draft.Text);
            Assert.Equal(new[] { editedAttachment }, runtime.Draft.Attachments);
            Assert.Equal(new[] { prior.Turn.Id }, runtime.Turns.Select(turn => turn.Id));
            var notice = Assert.IsType<TimelineItem.Content.Notice>(runtime.Entries.Last().Item.TimelineItemContent);
            Assert.Equal(Notice.Level.Error, notice.Value.NoticeLevel);
            Assert.StartsWith("Conversation reverted, but files could not be restored: ", notice.Value.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(Path.Combine(threadRepo.Root, ".git", "index.lock"));
        }
    }

    [Fact]
    [Trait("Req", "PR-GIT-140")] // App/Runtime/ThreadRuntime.swift:392-412
    public void Toggling_diff_clears_file_focus_and_the_anchor_only_when_opening()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h, AppModelGitRepo.Empty(h.NewFolder("project")));
        var anchor = new Control();
        var edit = new FileEdit("changed.txt", "old", "new");
        h.Main.OnMain(() =>
        {
            runtime.ShowDiff(anchor, Guid.NewGuid(), focusEdits: [edit]);
            Assert.Same(anchor, runtime.DiffAnchor!.Value);
            Assert.Equal(new[] { edit }, runtime.DiffFocusEdits);
            var focusRevision = runtime.DiffFocusRevision;

            runtime.ToggleDiff();

            Assert.False(runtime.IsDiffVisible);
            Assert.Empty(runtime.DiffFocusEdits);
            Assert.Equal(focusRevision + 1, runtime.DiffFocusRevision);
            Assert.Same(anchor, runtime.DiffAnchor!.Value);
            var clearedRevision = runtime.DiffFocusRevision;

            runtime.ToggleDiff();

            Assert.True(runtime.IsDiffVisible);
            Assert.Null(runtime.DiffAnchor?.Value);
            Assert.Equal(clearedRevision, runtime.DiffFocusRevision);
        });
    }

    [Fact]
    [Trait("Req", "PR-GIT-144")] // App/Runtime/ThreadRuntime.swift:1960-2049
    public void Finished_turn_diff_cache_survives_a_diff_revision_change_and_callers_share_its_task()
    {
        using var h = new AppModelHarness();
        var projectRepo = AppModelGitRepo.Empty(h.NewFolder("project"));
        var threadID = Guid.NewGuid();
        var turnID = Guid.NewGuid();
        var turn = new TurnRecord
        {
            Id = turnID,
            Index = 0,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            ProviderDiff = "diff --git a/a.txt b/a.txt\n",
        };
        var (_, _, runtime) = OpenRuntime(h, projectRepo, threadID: threadID,
            document: new ThreadDocument(threadID) { Turns = new ValueList<TurnRecord>([turn]) });
        var file = new DiffFile("a.txt", DiffFile.Change.Modified, false,
            new ValueList<DiffHunk>([new DiffHunk(1, "@@ -1 +1 @@", new ValueList<DiffLine>([
                new DiffLine(1, DiffLine.Kind.Deletion, "old", OldNumber: 1),
                new DiffLine(2, DiffLine.Kind.Addition, "new", NewNumber: 1),
            ]))]));

        var first = h.Main.OnMain(() => runtime.ParsedDiff(turnID, [file]));
        var second = h.Main.OnMain(() => runtime.ParsedDiff(turnID, [file]));
        Assert.Same(first, second);
        h.Main.Loop.Run(() => first, AppModelHarness.Bound);
        Assert.True(h.Main.OnMain(() => runtime.HasCachedDiff(turnID)));

        h.Main.OnMain(runtime.NoteDiffChanged);
        Assert.True(h.Main.OnMain(() => runtime.HasCachedDiff(turnID)));
        var afterRevision = h.Main.OnMain(() => runtime.ParsedDiff(turnID, [file]));
        Assert.Same(first, afterRevision);
    }

    [Fact]
    [Trait("Decision", "ED-164")] // App.Runtime.ThreadRuntime part 1 ruling: use FileChangeSummaryDiff.Create.
    public void File_change_summary_diff_factory_sums_parsed_files()
    {
        var files = new ValueList<DiffFile>([
            new DiffFile("one.txt", DiffFile.Change.Modified, false,
                new ValueList<DiffHunk>([new DiffHunk(1, "@@ -1 +1,2 @@", new ValueList<DiffLine>([
                    new DiffLine(1, DiffLine.Kind.Deletion, "old", OldNumber: 1),
                    new DiffLine(2, DiffLine.Kind.Addition, "new", NewNumber: 1),
                    new DiffLine(3, DiffLine.Kind.Addition, "extra", NewNumber: 2),
                ]))])),
            new DiffFile("two.bin", DiffFile.Change.Modified, true, ValueList<DiffHunk>.Empty,
                reportedAdditions: 0, reportedDeletions: 0),
        ]);

        var summary = FileChangeSummaryDiff.Create(files);

        Assert.Equal(2, summary.Additions);
        Assert.Equal(1, summary.Deletions);
        Assert.Equal(new[] { "one.txt", "two.bin" }, summary.Files.Select(file => file.Path));
        Assert.True(summary.Files[1].IsBinary);
    }

    private static (AppModel App, ChatThread Thread, ThreadRuntime Runtime) OpenRuntime(
        AppModelHarness h,
        AppModelGitRepo projectRepo,
        AppModelGitRepo? threadRepo = null,
        Guid? threadID = null,
        ThreadDocument? document = null,
        ProviderKind provider = ProviderKind.Cursor,
        string? providerSessionID = null,
        string? providerResumeAt = null,
        TurnStatus? lastStatus = null)
    {
        var project = AppModelSamples.Project("P", projectRepo.Root);
        var thread = AppModelSamples.Thread(project) with
        {
            Id = threadID ?? Guid.NewGuid(),
            Provider = provider,
            WorktreePath = threadRepo?.Root,
            ProviderSessionID = providerSessionID,
            ProviderResumeAt = providerResumeAt,
            LastStatus = lastStatus,
        };
        h.WriteLibrary([project], [thread]);
        DiskWriter.Shared.EncodeAndWrite(document ?? new ThreadDocument(thread.Id), Storage.ThreadURL(thread.Id));
        var app = h.MakeModel();
        var runtime = h.Main.OnMain(() => app.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return (app, thread, runtime);
    }

    private static (TurnRecord Turn, TimelineItem Item) TurnWithUser(
        long index,
        string text,
        TurnStatus status = TurnStatus.Completed,
        IReadOnlyList<string>? touchedPaths = null,
        string? baseCheckpoint = null,
        string? endCheckpoint = null)
    {
        var id = Guid.NewGuid();
        var item = new TimelineItem(id, new TimelineItem.Content.User(new UserMessage { Text = text }));
        return (new TurnRecord
        {
            Id = id,
            Index = index,
            Status = status,
            CompletedAt = SwiftDate.Now,
            UserItemID = item.Id,
            TouchedPaths = touchedPaths is null ? null : new ValueList<string>(touchedPaths),
            BaseCheckpoint = baseCheckpoint,
            EndCheckpoint = endCheckpoint,
        }, item);
    }

    private static ThreadDocument Document(Guid threadID, params (TurnRecord Turn, TimelineItem Item)[] turns) =>
        new(threadID)
        {
            Turns = new ValueList<TurnRecord>(turns.Select(value => value.Turn)),
            Items = new ValueList<TimelineItem>(turns.Select(value => value.Item)),
            Usage = new ContextUsage { UsedTokens = 987, WindowTokens = 1000 },
        };

    private static string CommitFile(AppModelGitRepo repository, string path, string text, string message)
    {
        var fullPath = Path.Combine(repository.Root, path.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(parent);
        File.WriteAllText(fullPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", message);
        return repository.Git("rev-parse", "HEAD").Trim();
    }

    private static void SetCheckpoint(AppModelGitRepo repository, Guid threadID, long index, string phase, string commit) =>
        repository.Git("update-ref", Git.CheckpointRef(threadID, index, phase), commit);
}
