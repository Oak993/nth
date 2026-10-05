using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeSummaryAdapterTests
{
    [Fact]
    public async Task A_nested_conversation_reads_its_untracked_file_using_the_repository_relative_path()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("same.txt", "ROOT-DECOY");
        repository.Write("sub/same.txt", "NESTED-CONTEXT");
        var before = repository.Git("status", "--porcelain=v1", "-z");
        var working = await FullWorkingContext.ReadAsync(repository.Child("sub"), CancellationToken.None);
        Assert.Contains("NESTED-CONTEXT", working.Diff, StringComparison.Ordinal);
        Assert.DoesNotContain("ROOT-DECOY", working.Diff, StringComparison.Ordinal);
        Assert.Contains("sub/same.txt", working.ChangedFiles);
        Assert.Equal(before, repository.Git("status", "--porcelain=v1", "-z"));
    }

    [Fact]
    public async Task The_summary_joins_the_full_working_diff_and_untracked_files_without_changing_the_checkout()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", "changed\n" + new string('x', 70_000) + "DIFF-TAIL\n");
        repository.Write("new file Café.txt", "untracked 17");
        var before = repository.Git("status", "--porcelain=v1", "-z");
        var working = await FullWorkingContext.ReadAsync(repository.Root, CancellationToken.None);
        Assert.Contains("DIFF-TAIL", working.Diff, StringComparison.Ordinal);
        Assert.Contains("untracked 17", working.Diff, StringComparison.Ordinal);
        Assert.Contains("new file Café.txt", working.ChangedFiles);
        Assert.Equal(before, repository.Git("status", "--porcelain=v1", "-z"));
        Assert.Equal("a\n", repository.Git("show", "HEAD:a.txt"));
    }

    [Fact]
    public async Task Facts_are_recorded_files_open_tasks_and_latest_user_instructions_not_guessed_meaning()
    {
        using var folder = new AccountTestFolder();
        var history = new ThreadDocument(Guid.NewGuid())
        {
            Items = new([new(null, new TimelineItem.Content.User(new() { Text = "Keep Café and 17 verbatim." }), "user"),
                new(null, new TimelineItem.Content.Todos(new([new() { Text = "Edit Config.cs", TodoStepStatus = TodoStep.Status.Pending }])), "todos")]),
        };
        var context = new HandoffContext(history, "Task", "full diff") { ChangedFiles = new(["Config.cs"]) };
        var adapter = new HandshakeSummaryAdapter((id, ct) => Task.FromResult(new HandshakeSummarySource(context, folder.Path)));
        var summary = await adapter.BuildAsync(history.ThreadID, CancellationToken.None);
        Assert.Contains("Keep Café and 17 verbatim.", summary.Text, StringComparison.Ordinal);
        Assert.Contains("Config.cs", summary.KeyFacts);
        Assert.Contains("Edit Config.cs", summary.KeyFacts);
        Assert.Contains("Keep Café and 17 verbatim.", summary.KeyFacts);
        Assert.Empty(adapter.FindGaps(summary, string.Join("\n", summary.KeyFacts)));
        Assert.Equal(summary.KeyFacts, adapter.FindGaps(summary, "Nothing relevant."));
        Assert.NotNull(summary.FilePath);
        var saved = System.Text.Json.JsonSerializer.Deserialize<HandoffContext>(File.ReadAllText(summary.FilePath))!;
        Assert.Equal("full diff", saved.CurrentDiff);
        Assert.Equal("Keep Café and 17 verbatim.", Assert.IsType<TimelineItem.Content.User>(saved.History.Items[0].TimelineItemContent).Value.Text);
    }
}
