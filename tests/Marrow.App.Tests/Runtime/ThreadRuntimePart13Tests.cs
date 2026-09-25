using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePart13Tests
{
    [Fact]
    [Trait("Req", "UI-RUNTIME-038")] // App/Runtime/ThreadRuntime.swift:1904-1911, 1931-1935
    public async Task A_file_restore_failure_leaves_the_exact_error_notice_in_the_timeline()
    {
        using var h = new AppModelHarness();
        var projectPath = h.NewFolder("revert error notice");
        var repository = AppModelGitRepo.WithCommit(projectPath);
        var project = AppModelSamples.Project("P", projectPath);
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude };
        h.WriteLibrary([project], [thread]);

        const string baseCheckpoint = "refs/port-test/valid-base";
        repository.Git("update-ref", baseCheckpoint, "HEAD");
        const string endCheckpoint = "refs/port-test/missing-end";
        var turn = new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Completed,
            BaseCheckpoint = baseCheckpoint,
            EndCheckpoint = endCheckpoint,
            TouchedPaths = new ValueList<string>(["a.txt"]),
        };
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(thread.Id)
            {
                Turns = new ValueList<TurnRecord>([turn]),
                Items = new ValueList<TimelineItem>(
                [
                    new TimelineItem(
                        turn.Id,
                        new TimelineItem.Content.User(new UserMessage { Text = "Change a.txt." })),
                ]),
            },
            Storage.ThreadURL(thread.Id));

        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        var previewError = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            _ = await new Git(projectPath).PreviewRestore(baseCheckpoint, endCheckpoint, ["a.txt"]);
        });
        var expectedMessage = "Conversation reverted, but files could not be restored: "
            + SwiftErrors.LocalizedDescription(previewError);

        var restored = h.Main.Loop.Run(
            () => runtime.Revert(turn.Id, restoreFiles: true),
            AppModelHarness.Bound);

        Assert.False(restored);
        var errorEntry = Assert.Single(runtime.Entries, entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice);
        var notice = Assert.IsType<TimelineItem.Content.Notice>(errorEntry.Item.TimelineItemContent).Value;
        Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
        Assert.Equal(expectedMessage, notice.Message);
    }
}
