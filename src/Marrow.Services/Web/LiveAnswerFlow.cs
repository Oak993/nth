// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3, 7.4: "the renderer builds the UI while tokens arrive"; ED-NEW-web-w24-5).
// Between a chat's session and its timeline: what turns a model's live answer into a timeline row, whichever way the model made it.
//
//   - A fenced ```nth-ui or ```nth-html block in the answer text. The text before it is completed as a message of its own, the block
//     becomes a live-answer row that grows as its lines arrive, and the text after it goes on in a new message below the row.
//   - A call of ui_show, ui_update or html_widget. The provider's own tool row for the call becomes the live-answer row (the same row
//     id, so the provider's later events find it), grows as the arguments stream when the provider streams them, and settles on what the
//     tool recorded. A ui_update row is hidden: the view it names changes in place.
//   - A change the tools recorded in the LiveAnswerHost (the nth-ui server on its relay thread, the native tool loop): it binds to the
//     oldest provider row waiting for it, or to the view's row when it is a replacement or an update, or makes a row of its own when no
//     provider row is waiting (a provider whose row titles nth cannot recognise).
//
// A live-answer row is an ordinary tool row (ToolStarted and ToolUpdated) whose payload holds the ToolLiveView, so the timeline, the
// save file and the restart need nothing new. Every other event passes through untouched; an answer with no block comes out
// byte for byte as it went in. No Swift counterpart.
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Web;

/// <summary>What the flow asks the chat about the rows it already holds (read on the main actor).</summary>
public sealed class LiveEntryLookup
{
    /// <summary>The id of the row showing the view with this id (not a superseded one), or null.</summary>
    public required Func<string, string?> EntryOf { get; init; }

    /// <summary>The live view a row holds now, with the reader's states in it, or null.</summary>
    public required Func<string, ToolLiveView?> Current { get; init; }

    public static LiveEntryLookup None { get; } = new() { EntryOf = _ => null, Current = _ => null };
}

public sealed class LiveAnswerFlow(Guid thread, LiveAnswerHost host, bool lite, LiveEntryLookup? entries = null)
{
    internal enum Tool { Show, Update, Html }

    private sealed class Row(string entryId, Tool tool)
    {
        public string EntryId { get; } = entryId;
        public Tool Tool { get; } = tool;
        public string? Json { get; set; }
        public string? ViewId { get; set; }
        public bool Bound { get; set; }
        public string? Title { get; set; }
        public string? Shown { get; set; }
        public bool Complete { get; set; }
        public bool Finished { get; set; }
    }

    private sealed class Block(string entryId, LiveFenceHeader header)
    {
        public string EntryId { get; } = entryId;
        public LiveFenceHeader Header { get; } = header;
        public System.Text.StringBuilder Content { get; } = new();
        public string? Title { get; set; }
        public string? ViewId { get; set; }
    }

    private sealed class Message(string id)
    {
        public string OriginalId { get; } = id;
        public LiveFenceParser Parser { get; } = new();
        public string SegmentId { get; set; } = id;
        public int Segment { get; set; }
        public System.Text.StringBuilder SegmentText { get; } = new();
        public bool SegmentStarted { get; set; }
        public bool TrimLeading { get; set; }
        public bool HasBlock { get; set; }
        public Dictionary<int, Block> Blocks { get; } = [];
    }

    private readonly LiveEntryLookup lookup = entries ?? LiveEntryLookup.None;
    private readonly Dictionary<string, Message> messages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Row> rows = new(StringComparer.Ordinal);
    private readonly HashSet<string> hidden = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> entryOfView = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Tool> orphans = [];

    /// <summary>The chat whose answers this flow reads.</summary>
    public Guid Thread { get; } = thread;

    /// <summary>The title a provider's tool row for <paramref name="tool"/> carries: the server's name and the tool's, with a middle dot between.</summary>
    public static string RowTitle(string tool) => UiMcpServer.ServerName + " · " + tool;

    // ------------------------------------------------------------------------------------------------------------------
    // The session's events.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The events the chat should see for one event of the session.</summary>
    public IReadOnlyList<ProviderEvent> Filter(ProviderEvent @event)
    {
        switch (@event)
        {
            case ProviderEvent.MessageDelta delta:
                return Delta(delta);
            case ProviderEvent.MessageCompleted completed:
                return Completed(completed);
            case ProviderEvent.ToolStarted started when Match(started.Call.Title) is { } tool:
                return Started(started, tool);
            case ProviderEvent.ToolUpdated updated when hidden.Contains(updated.Id):
                return [];
            case ProviderEvent.ToolUpdated updated when rows.TryGetValue(updated.Id, out var row):
                return Updated(updated, row);
            case ProviderEvent.ToolOutput output when hidden.Contains(output.Id) || rows.ContainsKey(output.Id):
                return [];
            case ProviderEvent.TurnCompleted or ProviderEvent.Exited:
            {
                var output = new List<ProviderEvent>();
                Finish(output);
                output.Add(@event);
                return output;
            }
            default:
                return [@event];
        }
    }

    /// <summary>The tool a provider's row title names, or null for any other row.</summary>
    internal static Tool? Match(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        foreach (var (name, tool) in new[] { (LiveAnswerTools.ShowName, Tool.Show), (LiveAnswerTools.UpdateName, Tool.Update), (LiveAnswerTools.HtmlName, Tool.Html) })
        {
            if (string.Equals(title.Trim(), name, StringComparison.Ordinal)) return tool;
            if (title.Contains(UiMcpServer.ServerName, StringComparison.Ordinal) && title.Contains(name, StringComparison.Ordinal)) return tool;
        }
        return null;
    }

    // ---- the tool rows ----

    private List<ProviderEvent> Started(ProviderEvent.ToolStarted started, Tool tool)
    {
        var id = started.Id;
        if (tool == Tool.Update)
        {
            hidden.Add(id);
            return [];
        }
        if (rows.TryGetValue(id, out var known))
            return Updated(new ProviderEvent.ToolUpdated(id, new ToolUpdate { Detail = started.Call.Detail }), known);
        // A row of its own was already made for a view this call made (the tools were quicker than the provider's report): this one is the same view.
        if (orphans.IndexOf(tool) is var made and >= 0)
        {
            orphans.RemoveAt(made);
            hidden.Add(id);
            return [];
        }
        var row = new Row(id, tool) { Json = started.Call.Detail };
        rows[id] = row;
        var view = Preview(row);
        row.Shown = view.Text;
        return [new ProviderEvent.ToolStarted(id, Call(view))];
    }

    private List<ProviderEvent> Updated(ProviderEvent.ToolUpdated updated, Row row)
    {
        var update = updated.Update;
        var output = new List<ProviderEvent>();
        if (update.Detail is { Length: > 0 } json) row.Json = json;
        var finished = update.Status is { } status && status != ToolCall.Status.Running;
        if (finished) row.Finished = true;
        if (row.Bound)
        {
            // The tools recorded the view; what the provider says now is only how its call ended.
            if (finished) output.Add(new ProviderEvent.ToolUpdated(row.EntryId, new ToolUpdate { Status = update.Status }));
            return output;
        }
        var view = Preview(row);
        if (update.Status == ToolCall.Status.Failed || update.Status == ToolCall.Status.Declined)
        {
            view = view with { Complete = true, Problem = Problem(update.Output) };
            row.Complete = true;
            output.Add(new ProviderEvent.ToolUpdated(row.EntryId, new ToolUpdate { Payload = new ToolPayload { LiveView = view }, Status = update.Status }));
            return output;
        }
        if (finished && view.Text.Length > 0) view = view with { Complete = true };
        if (view.Text != row.Shown || finished || view.Complete != row.Complete)
        {
            row.Shown = view.Text;
            row.Complete = view.Complete;
            output.Add(new ProviderEvent.ToolUpdated(row.EntryId, new ToolUpdate
            {
                Payload = new ToolPayload { LiveView = view },
                Status = finished ? update.Status : null,
            }));
        }
        return output;
    }

    /// <summary>The view as far as the call's arguments have arrived.</summary>
    private ToolLiveView Preview(Row row)
    {
        var members = PartialJson.Members(row.Json);
        string? Member(params string[] keys) => keys.Select(key => members.TryGetValue(key, out var member) && member.IsString ? member.Value : null).FirstOrDefault(value => value is not null);
        var html = row.Tool == Tool.Html;
        var text = html ? Member("html", "content") ?? "" : NthUiPatch.Clean(Member("ui", "view", "content") ?? "").TrimStart('\n', '\r');
        var complete = html
            ? members.TryGetValue("html", out var h) && h.Complete
            : (members.TryGetValue("ui", out var u) && u.Complete) || (members.TryGetValue("view", out var v) && v.Complete) || (members.TryGetValue("content", out var c) && c.Complete);
        var id = row.ViewId ?? LiveViewIds.Normalize(Member("id")) ?? "";
        row.Title ??= TitleOf(html, text, Member("title"), complete);
        int? height = html ? (members.TryGetValue("height", out var hm) && int.TryParse(hm.Value, out var parsed) ? Math.Clamp(parsed, LiveAnswerTools.MinHeight, LiveAnswerTools.MaxHeight) : LiveAnswerTools.DefaultHeight) : null;
        return new ToolLiveView { Id = id, Kind = html ? LiveViewKind.Html : LiveViewKind.Ui, Title = row.Title, Text = text, Complete = complete, Lite = lite && !html, Height = height };
    }

    private static string? TitleOf(bool html, string text, string? given, bool complete)
    {
        if (given is { Length: > 0 }) return given;
        if (html) return null;
        return text.Contains('\n', StringComparison.Ordinal) || complete ? NthUiPatch.TitleOf(NthUiParser.Parse(text)) : null;
    }

    private static string Problem(string? output)
    {
        var line = (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        line = line.StartsWith("Error: ", StringComparison.Ordinal) ? line[7..] : line;
        return line.Length == 0 ? "The live answer could not be shown." : line.Length <= 240 ? line : line[..239] + "…";
    }

    private static ToolCall Call(ToolLiveView view) => new()
    {
        ToolCallKind = ToolCall.Kind.Other,
        Title = view.Title is { Length: > 0 } title ? LiveAnswerTools.Title + " · " + title : LiveAnswerTools.Title,
        Payload = new ToolPayload { LiveView = view },
    };

    // ------------------------------------------------------------------------------------------------------------------
    // What the tools recorded.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The events the chat should see for a change the tools recorded (called on the main actor).</summary>
    public IReadOnlyList<ProviderEvent> Apply(LiveViewChange change)
    {
        var output = new List<ProviderEvent>();
        var view = change.View;
        var known = entryOfView.TryGetValue(view.Id, out var mapped) && lookup.Current(mapped) is not null ? mapped : lookup.EntryOf(view.Id); // a row the chat no longer holds (rewound) is not the view's row
        if (change.Kind == LiveChangeKind.Updated)
        {
            if (known is not null)
            {
                var current = lookup.Current(known);
                var shown = Merge(current, view, change.StateSets);
                entryOfView[view.Id] = known;
                output.Add(new ProviderEvent.ToolUpdated(known, new ToolUpdate { Title = Called(shown), Payload = new ToolPayload { LiveView = shown } }));
                return output;
            }
            // The row is gone from the chat (it was never saved): a row of its own shows the view as it is now.
            Create(output, view, Merge(null, view, change.StateSets) with { Complete = true });
            return output;
        }

        var tool = view.Kind == LiveViewKind.Html ? Tool.Html : Tool.Show;
        var waiting = rows.Values.FirstOrDefault(row => !row.Bound && row.Tool == tool);
        var fresh = new ToolLiveView
        {
            Id = view.Id, Kind = view.Kind, Title = view.Title, Text = view.Text, Version = view.Version, Complete = true, Lite = view.Lite, Height = view.Height,
        };
        if (known is not null && (waiting is null || waiting.EntryId != known)) Supersede(output, known);
        if (waiting is not null)
        {
            waiting.Bound = true;
            waiting.ViewId = view.Id;
            waiting.Complete = true;
            entryOfView[view.Id] = waiting.EntryId;
            output.Add(new ProviderEvent.ToolUpdated(waiting.EntryId, new ToolUpdate { Title = Called(fresh), Payload = new ToolPayload { LiveView = fresh } }));
            return output;
        }
        Create(output, view, fresh);
        orphans.Add(tool);
        return output;
    }

    private void Supersede(List<ProviderEvent> output, string entryId)
    {
        var current = lookup.Current(entryId);
        if (current is null || current.Superseded) return;
        output.Add(new ProviderEvent.ToolUpdated(entryId, new ToolUpdate { Payload = new ToolPayload { LiveView = current with { Superseded = true } } }));
    }

    private void Create(List<ProviderEvent> output, LiveViewRecord record, ToolLiveView view)
    {
        var id = $"live-{record.Id}-{record.Version}";
        entryOfView[record.Id] = id;
        output.Add(new ProviderEvent.ToolStarted(id, Call(view)));
        output.Add(new ProviderEvent.ToolUpdated(id, new ToolUpdate { Status = ToolCall.Status.Completed }));
    }

    private static string Called(ToolLiveView view) => view.Title is { Length: > 0 } title ? LiveAnswerTools.Title + " · " + title : LiveAnswerTools.Title;

    /// <summary>The row's view after an update: the new text and title on the reader's states, with the states the patch set laid over them.</summary>
    private static ToolLiveView Merge(ToolLiveView? current, LiveViewRecord view, string? sets)
    {
        var state = current?.State;
        if (sets is not null)
        {
            var merged = new Dictionary<string, NthUiValue>(StringComparer.Ordinal);
            foreach (var json in new[] { state, sets })
            {
                if (json is null) continue;
                var value = NthUiValue.FromJson(json);
                if (value.IsObject) foreach (var (name, setting) in value.Fields) merged[name] = setting;
            }
            state = NthUiValue.Object(merged).ToJson();
        }
        return (current ?? new ToolLiveView { Id = view.Id, Kind = view.Kind }) with
        {
            Id = view.Id, Title = view.Title, Text = view.Text, Version = view.Version, Complete = true, State = state, Height = view.Height ?? current?.Height, Lite = view.Lite, Problem = null,
        };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The answer's text: fenced blocks.
    // ------------------------------------------------------------------------------------------------------------------

    private List<ProviderEvent> Delta(ProviderEvent.MessageDelta delta)
    {
        if (!messages.TryGetValue(delta.Id, out var message)) messages[delta.Id] = message = new Message(delta.Id);
        var output = new List<ProviderEvent>();
        Take(message, message.Parser.Feed(delta.Text), output);
        // An answer with no block is passed on as it came (a held-back line is the only difference, and it comes out later).
        return output;
    }

    private List<ProviderEvent> Completed(ProviderEvent.MessageCompleted completed)
    {
        var output = new List<ProviderEvent>();
        if (!messages.Remove(completed.Id, out var message))
        {
            // The answer arrived whole, with no deltas: read it at once.
            var parts = LiveFenceParser.Parse(completed.Text);
            if (!parts.Any(part => part is LiveFencePart.Opened))
            {
                output.Add(completed);
                return output;
            }
            message = new Message(completed.Id);
            Take(message, parts, output);
            EndMessage(message, output);
            return output;
        }
        Take(message, message.Parser.Finish(), output);
        if (!message.HasBlock)
        {
            output.Add(completed); // an answer with no block comes out as the session wrote it
            return output;
        }
        EndMessage(message, output);
        return output;
    }

    /// <summary>The last segment of a message that held blocks ends: its text is completed (nothing is sent for an empty one).</summary>
    private static void EndMessage(Message message, List<ProviderEvent> output)
    {
        if (message.SegmentStarted) output.Add(new ProviderEvent.MessageCompleted(message.SegmentId, message.SegmentText.ToString()));
    }

    private void Take(Message message, IReadOnlyList<LiveFencePart> parts, List<ProviderEvent> output)
    {
        foreach (var part in parts)
        {
            switch (part)
            {
                case LiveFencePart.Text text:
                {
                    var value = text.Value;
                    if (message.TrimLeading)
                    {
                        value = value.TrimStart('\r', '\n');
                        if (value.Length == 0) break;
                        message.TrimLeading = false;
                    }
                    message.SegmentStarted = true;
                    message.SegmentText.Append(value);
                    output.Add(new ProviderEvent.MessageDelta(message.SegmentId, value));
                    break;
                }
                case LiveFencePart.Opened opened:
                {
                    // The text before the block is a message of its own; the row follows it and the rest of the answer follows the row.
                    if (message.SegmentStarted) output.Add(new ProviderEvent.MessageCompleted(message.SegmentId, message.SegmentText.ToString()));
                    message.HasBlock = true;
                    var block = new Block($"live-{message.OriginalId}-{opened.Index}", opened.Header) { ViewId = opened.Header.Id };
                    message.Blocks[opened.Index] = block;
                    if (block.ViewId is { } named && (entryOfView.TryGetValue(named, out var old) ? old : lookup.EntryOf(named)) is { } earlier) Supersede(output, earlier);
                    output.Add(new ProviderEvent.ToolStarted(block.EntryId, Call(BlockView(block, complete: false))));
                    message.Segment++;
                    message.SegmentId = $"{message.OriginalId}~{message.Segment}";
                    message.SegmentText.Clear();
                    message.SegmentStarted = false;
                    message.TrimLeading = true;
                    break;
                }
                case LiveFencePart.Content content when message.Blocks.TryGetValue(content.Index, out var block):
                {
                    block.Content.Append(content.Value);
                    var text = block.Content.ToString();
                    block.Title ??= TitleOf(block.Header.Kind == LiveFenceKind.Html, text, block.Header.Title, complete: false);
                    output.Add(new ProviderEvent.ToolUpdated(block.EntryId, new ToolUpdate { Payload = new ToolPayload { LiveView = BlockView(block, complete: false) } }));
                    break;
                }
                case LiveFencePart.Closed closed when message.Blocks.TryGetValue(closed.Index, out var block):
                    CloseBlock(block, output);
                    break;
            }
        }
    }

    private ToolLiveView BlockView(Block block, bool complete)
    {
        var html = block.Header.Kind == LiveFenceKind.Html;
        var text = block.Content.ToString();
        if (!html) text = NthUiPatch.Clean(text);
        return new ToolLiveView
        {
            Id = block.ViewId ?? "",
            Kind = html ? LiveViewKind.Html : LiveViewKind.Ui,
            Title = block.Title ?? block.Header.Title,
            Text = html ? text.TrimEnd('\n', '\r') : text,
            Complete = complete,
            Lite = !html && (lite || block.Header.Lite),
            Height = html ? Math.Clamp(block.Header.Height ?? LiveAnswerTools.DefaultHeight, LiveAnswerTools.MinHeight, LiveAnswerTools.MaxHeight) : null,
        };
    }

    private void CloseBlock(Block block, List<ProviderEvent> output)
    {
        var html = block.Header.Kind == LiveFenceKind.Html;
        var view = BlockView(block, complete: true);
        block.Title = html ? view.Title ?? HtmlTitle(view.Text) : NthUiPatch.TitleOf(NthUiParser.Parse(view.Text, view.Lite ? NthUiProfile.Lite : NthUiProfile.Full));
        view = view with { Title = block.Title };
        if (view.Text.Trim().Length == 0)
        {
            output.Add(new ProviderEvent.ToolUpdated(block.EntryId, new ToolUpdate
            {
                Payload = new ToolPayload { LiveView = view with { Problem = "The live answer was empty." } },
                Status = ToolCall.Status.Failed,
            }));
            return;
        }
        var id = block.ViewId ?? host.NewId(Thread);
        var record = host.Show(Thread, new LiveViewRecord(id, view.Kind, view.Text, view.Title, 1, view.Lite, view.Height), raise: false).View;
        entryOfView[id] = block.EntryId;
        output.Add(new ProviderEvent.ToolUpdated(block.EntryId, new ToolUpdate
        {
            Title = Called(view),
            Payload = new ToolPayload { LiveView = view with { Id = id, Version = record.Version } },
            Status = ToolCall.Status.Completed,
        }));
    }

    private static string? HtmlTitle(string html)
    {
        var open = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var start = html.IndexOf('>', open);
        var end = start < 0 ? -1 : html.IndexOf("</title", start, StringComparison.OrdinalIgnoreCase);
        return start < 0 || end < 0 || end - start <= 1 ? null : System.Net.WebUtility.HtmlDecode(html[(start + 1)..end]).Trim();
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The end of the turn.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The turn ended: text still held back is shown, a block still open ends as cut off, and a row still waiting for its view settles.</summary>
    private void Finish(List<ProviderEvent> output)
    {
        foreach (var message in messages.Values.ToList())
        {
            Take(message, message.Parser.Finish(), output);
            if (message.HasBlock) EndMessage(message, output);
        }
        messages.Clear();
        foreach (var row in rows.Values.Where(row => !row.Finished))
        {
            row.Finished = true;
            if (row.Bound)
            {
                output.Add(new ProviderEvent.ToolUpdated(row.EntryId, new ToolUpdate { Status = ToolCall.Status.Completed }));
                continue;
            }
            var view = Preview(row);
            var empty = view.Text.Length == 0;
            row.Complete = true;
            output.Add(new ProviderEvent.ToolUpdated(row.EntryId, new ToolUpdate
            {
                Payload = new ToolPayload { LiveView = empty ? view with { Complete = true, Problem = "The live answer was not completed." } : view with { Complete = true } },
                Status = empty ? ToolCall.Status.Failed : ToolCall.Status.Completed,
            }));
        }
        rows.Clear();
        hidden.Clear();
    }
}
