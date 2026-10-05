using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Port.MultiAccount;

public sealed class HandshakeSummaryAdapter(Func<Guid, CancellationToken, Task<HandshakeSummarySource>> source) : IHandoffSummary
{
    private readonly Func<Guid, CancellationToken, Task<HandshakeSummarySource>> read = source;
    public Task<HandoffSummary> BuildAsync(Guid threadId, CancellationToken ct) => ReadAsync(threadId, ct);
    private async Task<HandoffSummary> ReadAsync(Guid id, CancellationToken ct)
    {
        var source = await read(id, ct);
        ct.ThrowIfCancellationRequested();
        var saved = HandoffStore.Save(source.Context, source.ThreadFolder);
        var history = source.Context.History;
        var files = source.Context.ChangedFiles.Concat(history.Turns.SelectMany(turn => turn.TouchedPaths ?? ValueList<string>.Empty))
            .Concat(history.Items.SelectMany(item => item.TimelineItemContent is TimelineItem.Content.Tool tool ? tool.Value.Edits.Select(edit => edit.Path) : []));
        var todos = history.Items.LastOrDefault(item => item.TimelineItemContent is TimelineItem.Content.Todos)?.TimelineItemContent is TimelineItem.Content.Todos state
            ? state.Value.Where(todo => todo.TodoStepStatus != TodoStep.Status.Done).Select(todo => todo.Text) : [];
        var latest = history.Items.LastOrDefault(item => item.TimelineItemContent is TimelineItem.Content.User)?.TimelineItemContent is TimelineItem.Content.User user ? user.Value.Text : "";
        var facts = files.Concat(todos).Append(latest).Concat(history.FollowUps.Select(prompt => prompt.Text))
            .Where(fact => !string.IsNullOrWhiteSpace(fact)).Distinct(StringComparer.Ordinal).ToArray();
        return new(saved.Text, saved.HandoffFile, facts);
    }

    public IReadOnlyList<string> FindGaps(HandoffSummary summary, string readBack) =>
        summary.KeyFacts.Where(fact => !readBack.Contains(fact, StringComparison.OrdinalIgnoreCase)).ToArray();
}
