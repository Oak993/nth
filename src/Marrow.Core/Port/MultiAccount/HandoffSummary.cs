using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount;

// PORT-ONLY: OD-0114, multi-account-NOTES "Marrow's own digest" / "Nothing missed"; ED-407.
// This is mechanical selection, never AI summarization. All recorded information remains in the archive.
public sealed record HandoffApproval(string Id, string Title, string Outcome, string? Detail = null);

[JsonConverter(typeof(HandoffContextConverter))]
public sealed record HandoffContext(ThreadDocument History, string Task, string CurrentDiff)
{
    public bool IsQuiescent { get; init; }
    public ValueList<string> ChangedFiles { get; init; } = ValueList<string>.Empty;
    public ValueList<HandoffApproval> Approvals { get; init; } = ValueList<HandoffApproval>.Empty;
    // Provider events captured before Timeline.ToolCall.OutputLimit. Also holds question answers and approvals.
    public ValueList<string> EventJournal { get; init; } = ValueList<string>.Empty;
}

public sealed record HandoffSize(long FullBytes, long SummaryBytes);
public sealed record HandoffOptions(int RecentTurns = 3, int ReReadableCharacters = 512, int RetainedCharacters = 8192);

public sealed record HandoffSummary(string Text, string FullConversation, string HandoffFile, HandoffSize Size)
{
    public static HandoffSummary Build(HandoffContext context, string handoffFile, HandoffOptions? options = null)
    {
        options ??= new();
        if (!Path.IsPathFullyQualified(handoffFile)) throw new ArgumentException("The handoff file must have an absolute path.", nameof(handoffFile));
        if (options.RecentTurns < 0 || options.ReReadableCharacters < 2 || options.RetainedCharacters < 2)
            throw new ArgumentOutOfRangeException(nameof(options));

        var full = JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var text = new StringBuilder();
        text.Append("Marrow handoff: recorded context for continuing this task.\n")
            .Append("Full conversation, including all shortened output: ").Append(handoffFile).Append('\n')
            .Append("Use the archive to look up any missing detail. No tool should be repeated merely because its output was shortened.\n")
            .Append("Task:\n").Append(context.Task).Append('\n');

        if (context.History.Continuation is { } inherited)
            text.Append("Earlier forwarded context:\n").Append(inherited.Context).Append('\n');
        text.Append("Changed files:\n");
        var paths = context.ChangedFiles
            .Concat(context.History.Turns.SelectMany(turn => turn.TouchedPaths ?? ValueList<string>.Empty))
            .Concat(context.History.Items.SelectMany(item => item.TimelineItemContent is TimelineItem.Content.Tool tool
                ? tool.Value.Edits.Select(edit => edit.Path) : []));
        foreach (var path in paths.Distinct(StringComparer.Ordinal)) text.Append(path).Append('\n');
        text.Append("Current diff:\n").Append(context.CurrentDiff).Append('\n');
        text.Append("Approvals (recorded scope only; no new permission is implied):\n");
        foreach (var approval in context.Approvals)
            text.Append(approval.Id).Append(": ").Append(approval.Title).Append("\n").Append(approval.Outcome).Append('\n').Append(approval.Detail).Append('\n');
        foreach (var entry in context.EventJournal)
            if (entry.Contains("\"Approval\"", StringComparison.Ordinal) || entry.Contains("\"Question\"", StringComparison.Ordinal)
                || entry.Contains("\"Resolution\"", StringComparison.Ordinal)) text.Append(entry).Append('\n');

        // Use item order, not timestamps or the presence of TurnRecord: older recordings can omit records.
        var orderedTurns = context.History.Items.Where(item => item.TurnID is not null).Select(item => item.TurnID!.Value).Distinct().ToArray();
        var recent = orderedTurns.TakeLast(options.RecentTurns).ToHashSet();
        string? lastError = null;
        text.Append("Conversation and tool history (chronological):\n");
        foreach (var item in context.History.Items)
        {
            text.Append("\n[").Append(item.Id).Append("; turn ").Append(item.TurnID).Append("]\n");
            var keepFull = item.TurnID is { } turn && recent.Contains(turn);
            switch (item.TimelineItemContent)
            {
                case TimelineItem.Content.User user:
                    text.Append("User:\n").Append(user.Value.Text).Append('\n');
                    foreach (var attachment in user.Value.Attachments)
                        text.Append("Attachment: ").Append(attachment.Name).Append(" · ").Append(attachment.Path).Append('\n');
                    break;
                case TimelineItem.Content.Assistant assistant:
                    text.Append("Assistant:\n").Append(assistant.Value.Text).Append('\n');
                    break;
                case TimelineItem.Content.Plan plan:
                    text.Append("Plan: ").Append(plan.Value.ProposedPlanState).Append('\n').Append(plan.Value.Markdown).Append('\n');
                    break;
                case TimelineItem.Content.Todos todos:
                    text.Append("Todo state:\n");
                    foreach (var todo in todos.Value) text.Append(todo.TodoStepStatus).Append(": ").Append(todo.Text).Append('\n');
                    break;
                case TimelineItem.Content.Tool tool:
                    var call = tool.Value;
                    var failed = call.ToolCallStatus == ToolCall.Status.Failed || call.ExitCode is { } code && code != 0;
                    var status = failed ? "FAIL" : call.ToolCallStatus == ToolCall.Status.Completed ? "PASS" : call.ToolCallStatus.ToString();
                    text.Append(call.ToolCallKind).Append(": ").Append(call.Title).Append('\n').Append(call.Detail).Append('\n')
                        .Append(status).Append(call.ExitCode is { } exit ? " · exit " + exit.ToString(CultureInfo.InvariantCulture) : "").Append('\n');
                    foreach (var edit in call.Edits) text.Append("File: ").Append(edit.Path).Append('\n').Append(edit.Diff).Append('\n');
                    var reread = !failed && call.ToolCallKind is ToolCall.Kind.Read or ToolCall.Kind.Search;
                    text.Append(keepFull ? call.Output : Shorten(call.Output, reread ? options.ReReadableCharacters : options.RetainedCharacters,
                        reread ? "re-readable output shortened" : "long recorded output: head and tail", item.Id, handoffFile)).Append('\n');
                    if (failed) lastError = call.Title + "\n" + call.Output;
                    break;
                case TimelineItem.Content.Reasoning reasoning:
                    text.Append("Reasoning:\n").Append(keepFull ? reasoning.Value.Text : Shorten(reasoning.Value.Text, options.RetainedCharacters,
                        "long recorded output: head and tail", item.Id, handoffFile)).Append('\n');
                    break;
                case TimelineItem.Content.Notice notice:
                    text.Append(notice.Value.NoticeLevel).Append(": ").Append(notice.Value.Message).Append('\n');
                    if (notice.Value.NoticeLevel == Notice.Level.Error) lastError = notice.Value.Message;
                    break;
                case TimelineItem.Content.TurnEnd end:
                    text.Append("Turn: ").Append(JsonSerializer.Serialize(end.Value)).Append('\n');
                    break;
            }
        }
        text.Append("\nLast error:\n").Append(lastError ?? "None recorded").Append('\n');
        text.Append("Queued user instructions:\n");
        foreach (var prompt in context.History.FollowUps)
        {
            text.Append(prompt.Text).Append('\n');
            foreach (var attachment in prompt.Attachments) text.Append(attachment.Name).Append(" · ").Append(attachment.Path).Append('\n');
        }
        var summary = text.ToString();
        return new(summary, full, handoffFile, new(Encoding.UTF8.GetByteCount(full), Encoding.UTF8.GetByteCount(summary)));
    }

    private static string Shorten(string value, int limit, string reason, string itemId, string path)
    {
        var characters = value.EnumerateRunes().ToArray();
        if (characters.Length <= limit) return value;
        var head = limit / 2;
        return string.Concat(characters.Take(head).Select(rune => rune.ToString())) + "\n[" + reason + "; full item " + itemId + " in " + path + "]\n"
            + string.Concat(characters.TakeLast(limit - head).Select(rune => rune.ToString()));
    }
}

internal sealed class HandoffContextConverter : JsonConverter<HandoffContext>
{
    public override HandoffContext Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new(root.GetProperty("History").Deserialize<ThreadDocument>(options)!, root.GetProperty("Task").GetString()!, root.GetProperty("CurrentDiff").GetString()!)
        {
            ChangedFiles = root.GetProperty("ChangedFiles").Deserialize<string[]>(options)!,
            Approvals = root.GetProperty("Approvals").Deserialize<HandoffApproval[]>(options)!,
            EventJournal = root.GetProperty("EventJournal").Deserialize<string[]>(options)!,
            IsQuiescent = root.TryGetProperty("IsQuiescent", out var quiescent) && quiescent.GetBoolean(),
        };
    }

    public override void Write(Utf8JsonWriter writer, HandoffContext value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new { value.History, value.Task, value.CurrentDiff, value.IsQuiescent, ChangedFiles = value.ChangedFiles.ToArray(),
            Approvals = value.Approvals.ToArray(), EventJournal = value.EventJournal.ToArray() }, options);
}
