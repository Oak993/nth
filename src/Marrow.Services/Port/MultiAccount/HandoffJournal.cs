using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

// PORT-ONLY: OD-0114 / ED-NEW-ma-4. Capture before ToolCall's existing 60 KB output cap.
// A separate append-only journal preserves the parity storage schema and survives runtime eviction/restart.
public static class HandoffJournal
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string PathIn(string threadFolder) => Path.Combine(threadFolder, "handoff-events.jsonl");

    public static void Record(string threadFolder, ProviderEvent value)
    {
        // Stream deltas and all final/replacement outputs remain recoverable, including failed commands.
        if (value is ProviderEvent.ToolStarted or ProviderEvent.ToolOutput or ProviderEvent.ToolUpdated
            or ProviderEvent.Approval or ProviderEvent.Question or ProviderEvent.RequestResolved
            or ProviderEvent.MessageDelta or ProviderEvent.MessageCompleted or ProviderEvent.TurnCompleted or ProviderEvent.Exited
            or ProviderEvent.ReasoningDelta or ProviderEvent.ReasoningCompleted or ProviderEvent.PlanDelta or ProviderEvent.PlanCompleted
            or ProviderEvent.Todos or ProviderEvent.Diff or ProviderEvent.Notice)
            Append(threadFolder, value.GetType().Name, JsonSerializer.SerializeToElement(value, value.GetType(), Options));
    }

    public static void Resolution(string threadFolder, string id, object result) => Append(threadFolder, "Resolution", new { Id = id, Result = result });

    private static void Append(string threadFolder, string kind, object payload)
    {
        Directory.CreateDirectory(threadFolder);
        File.AppendAllText(PathIn(threadFolder), JsonSerializer.Serialize(new { Event = kind, Payload = payload }, Options) + "\n", new UTF8Encoding(false, true));
    }

    public static string[] Read(string threadFolder) => File.Exists(PathIn(threadFolder)) ? File.ReadAllLines(PathIn(threadFolder)) : [];

    public static ThreadDocument Restore(ThreadDocument history, IReadOnlyList<string> journal)
    {
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in journal)
        {
            using var entry = JsonDocument.Parse(line);
            var payload = entry.RootElement.GetProperty("Payload");
            switch (entry.RootElement.GetProperty("Event").GetString())
            {
                case "ToolStarted":
                    outputs[payload.GetProperty("Id").GetString()!] = payload.GetProperty("Call").GetProperty("output").GetString()!;
                    break;
                case "ToolOutput":
                    var id = payload.GetProperty("Id").GetString()!;
                    outputs[id] = outputs.GetValueOrDefault(id, "") + payload.GetProperty("Text").GetString();
                    break;
                case "ToolUpdated":
                    if (payload.GetProperty("Update").TryGetProperty("Output", out var output) && output.ValueKind == JsonValueKind.String)
                        outputs[payload.GetProperty("Id").GetString()!] = output.GetString()!;
                    break;
                case "MessageCompleted":
                    messages[payload.GetProperty("Id").GetString()!] = payload.GetProperty("Text").GetString()!;
                    break;
                case "MessageDelta":
                    var messageId = payload.GetProperty("Id").GetString()!;
                    messages[messageId] = messages.GetValueOrDefault(messageId, "") + payload.GetProperty("Text").GetString();
                    break;
            }
        }
        return history with
        {
            Items = new ValueList<TimelineItem>(history.Items.Select(item => item.TimelineItemContent switch
            {
                TimelineItem.Content.Tool tool when outputs.TryGetValue(item.Id, out var output) =>
                    item with { TimelineItemContent = new TimelineItem.Content.Tool(tool.Value with { Output = output }) },
                TimelineItem.Content.Assistant assistant when messages.TryGetValue(item.Id, out var message) =>
                    item with { TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { Text = message }) },
                _ => item,
            })),
        };
    }
}
