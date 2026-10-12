using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY test fixture (web surfaces W24, ED-NEW-web-w24-5). A timeline as the chat's runtime builds it from events, small enough to
/// read: messages (a delta appends, a completion sets), and tool rows (a start upserts, an update sets what it carries, a payload
/// replaces the row's payload). It answers the flow's questions about rows the way the runtime does, so a test can run a whole
/// answer through the flow and read the rows it left.
/// </summary>
internal sealed class LiveTimelineSim
{
    public sealed class Row
    {
        public string Id { get; init; } = "";
        public string Title { get; set; } = "";
        public ToolCall.Status Status { get; set; } = ToolCall.Status.Running;
        public ToolLiveView? View { get; set; }
        public string Output { get; set; } = "";
        public string? Detail { get; set; }
    }

    private readonly List<string> order = [];
    private readonly Dictionary<string, string> messages = new(StringComparer.Ordinal);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Row> rows = new(StringComparer.Ordinal);

    public List<ProviderEvent> Seen { get; } = [];

    /// <summary>Entry ids in creation order: messages and tool rows.</summary>
    public IReadOnlyList<string> Order => order;

    public IReadOnlyDictionary<string, Row> Rows => rows;

    public string Message(string id) => messages[id];

    public bool IsComplete(string messageId) => completed.Contains(messageId);

    public IEnumerable<string> MessageIds => order.Where(messages.ContainsKey);

    /// <summary>The texts of the messages with something in them, in order.</summary>
    public IReadOnlyList<string> Texts => [.. MessageIds.Select(Message)];

    public IReadOnlyList<Row> LiveRows => [.. order.Where(rows.ContainsKey).Select(id => rows[id]).Where(row => row.View is not null)];

    public LiveEntryLookup Lookup => new()
    {
        EntryOf = viewId => order.Where(rows.ContainsKey).Reverse().FirstOrDefault(id => rows[id].View is { Superseded: false } view && string.Equals(view.Id, viewId, StringComparison.OrdinalIgnoreCase)),
        Current = id => rows.TryGetValue(id, out var row) ? row.View : null,
    };

    public void Apply(IEnumerable<ProviderEvent> events)
    {
        foreach (var @event in events)
        {
            Seen.Add(@event);
            switch (@event)
            {
                case ProviderEvent.MessageDelta delta:
                    if (!messages.ContainsKey(delta.Id)) { messages[delta.Id] = ""; order.Add(delta.Id); }
                    messages[delta.Id] += delta.Text;
                    break;
                case ProviderEvent.MessageCompleted done:
                    if (!messages.ContainsKey(done.Id)) order.Add(done.Id);
                    messages[done.Id] = done.Text;
                    completed.Add(done.Id);
                    break;
                case ProviderEvent.ToolStarted started:
                    if (!rows.TryGetValue(started.Id, out var row))
                    {
                        rows[started.Id] = row = new Row { Id = started.Id };
                        order.Add(started.Id);
                    }
                    if (started.Call.Title.Length > 0) row.Title = started.Call.Title;
                    row.Detail = started.Call.Detail ?? row.Detail;
                    row.View = started.Call.Payload?.LiveView ?? row.View;
                    break;
                case ProviderEvent.ToolUpdated updated:
                    if (!rows.TryGetValue(updated.Id, out var target))
                    {
                        rows[updated.Id] = target = new Row { Id = updated.Id, Title = updated.Update.Title ?? "Tool" };
                        order.Add(updated.Id);
                    }
                    if (updated.Update.Title is { Length: > 0 } title) target.Title = title;
                    if (updated.Update.Detail is { } detail) target.Detail = detail;
                    if (updated.Update.Output is { } output) target.Output = output;
                    if (updated.Update.Payload is { } payload) target.View = payload.LiveView ?? target.View;
                    if (updated.Update.Status is { } status) target.Status = status;
                    break;
            }
        }
    }

    /// <summary>Feeds one session event through the flow and applies what comes out.</summary>
    public IReadOnlyList<ProviderEvent> Feed(LiveAnswerFlow flow, ProviderEvent @event)
    {
        var output = flow.Filter(@event);
        Apply(output);
        return output;
    }

    /// <summary>Applies a change the tools recorded.</summary>
    public IReadOnlyList<ProviderEvent> Feed(LiveAnswerFlow flow, LiveViewChange change)
    {
        var output = flow.Apply(change);
        Apply(output);
        return output;
    }
}
