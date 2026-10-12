using System.Text;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

// PORT-ONLY: OD-0114 / ED-407. Capture before ToolCall's existing 60 KB output cap.
// A separate append-only journal preserves the parity storage schema and survives runtime eviction/restart.
public static class HandoffJournal
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string PathIn(string threadFolder) => Path.Combine(threadFolder, "handoff-events.jsonl");

    public static void Record(string threadFolder, ProviderEvent value)
    {
        if (Encode(value) is { } line) AppendBatch(threadFolder, [line]);
    }

    public static string? Encode(ProviderEvent value) => value is
        ProviderEvent.ToolStarted or ProviderEvent.ToolOutput or ProviderEvent.ToolUpdated
        or ProviderEvent.Approval or ProviderEvent.Question or ProviderEvent.RequestResolved
        or ProviderEvent.MessageDelta or ProviderEvent.MessageCompleted or ProviderEvent.TurnCompleted or ProviderEvent.Exited
        or ProviderEvent.ReasoningDelta or ProviderEvent.ReasoningCompleted or ProviderEvent.PlanDelta or ProviderEvent.PlanCompleted
        or ProviderEvent.Todos or ProviderEvent.Diff or ProviderEvent.Notice
        ? JsonSerializer.Serialize(new { Event = value.GetType().Name, Payload = (object)value }, Options) : null; // ED-NEW-perf-ui-thread-5: one pass over the event (it was serialized to a tree and the tree again)

    // Original provider IDs are preserved in the archive, but only the display projection is replayed.
    public static string? EncodeTrace(ProviderEvent value) => Encode(value) is null ? null
        : JsonSerializer.Serialize(new { Event = value.GetType().Name, Payload = JsonSerializer.SerializeToElement(value, value.GetType(), Options), Replay = false }, Options);

    public static string EncodeResolution(string id, object result) => Encode("Resolution", new { Id = id, Result = result });
    public static void Resolution(string threadFolder, string id, object result) => AppendBatch(threadFolder, [EncodeResolution(id, result)]);
    private static string Encode(string kind, object payload) => JsonSerializer.Serialize(new { Event = kind, Payload = payload }, Options);

    private const long LiveJournalBytes = 4 * 1024 * 1024;
    private static readonly object FileGate = new();

    public static void AppendBatch(string threadFolder, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        lock (FileGate)
        {
            Directory.CreateDirectory(threadFolder);
            var path = PathIn(threadFolder);
            var bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
            var buffered = new StringBuilder();
            long bufferedBytes = 0;
            void Flush()
            {
                if (buffered.Length == 0) return;
                File.AppendAllText(path, buffered.ToString(), new UTF8Encoding(false, true));
                bytes += bufferedBytes;
                buffered.Clear();
                bufferedBytes = 0;
            }
            foreach (var line in lines)
            {
                var size = Encoding.UTF8.GetByteCount(line) + 1L;
                if (bytes + bufferedBytes + size > LiveJournalBytes)
                {
                    Flush();
                    if (bytes > 0) { Rotate(threadFolder); bytes = 0; }
                }
                if (size > LiveJournalBytes)
                {
                    // A single large output is stored whole in a compressed segment, never clipped to fit the live file.
                    var segment = NextSegment(threadFolder);
                    File.WriteAllText(segment, line + "\n", new UTF8Encoding(false, true));
                    Compress(segment);
                }
                else { buffered.Append(line).Append('\n'); bufferedBytes += size; }
            }
            Flush();
            if (!File.Exists(path)) using (File.Create(path)) { }
        }
    }

    private static string NextSegment(string folder)
    {
        var existing = Directory.EnumerateFiles(folder, "handoff-events-*.jsonl*").Select(Path.GetFileName)
            .Select(name => long.TryParse(name!.Split('-')[2].Split('.')[0], out var number) ? number : 0).DefaultIfEmpty().Max();
        return Path.Combine(folder, "handoff-events-" + (existing + 1).ToString("D12", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");
    }

    private static void Rotate(string folder)
    {
        var segment = NextSegment(folder);
        File.Move(PathIn(folder), segment, overwrite: false);
        Compress(segment);
    }

    private static void Compress(string segment)
    {
        var temporary = segment + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = File.OpenRead(segment))
            using (var output = File.Create(temporary))
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest)) input.CopyTo(gzip);
            File.Move(temporary, segment + ".gz", overwrite: false);
            File.Delete(segment);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string[] Read(string threadFolder)
    {
        HandoffJournalWriter.WaitForQueuedWrites(); // ED-NEW-perf-ui-thread-5: the lines handed to the writer are on disk before they are read
        lock (FileGate)
        {
            if (!Directory.Exists(threadFolder)) return HandoffJournalWriter.RefusedFor(threadFolder);
            var lines = new List<string>();
            var stems = Directory.EnumerateFiles(threadFolder, "handoff-events-*.jsonl*")
                .Where(path => path.EndsWith(".jsonl", StringComparison.Ordinal) || path.EndsWith(".jsonl.gz", StringComparison.Ordinal))
                .Select(path => path.EndsWith(".gz", StringComparison.Ordinal) ? path[..^3] : path).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            foreach (var stem in stems)
            {
                // A crash after publishing gzip but before removing its raw input must not replay the same events twice.
                if (File.Exists(stem + ".gz"))
                {
                    using var input = File.OpenRead(stem + ".gz");
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzip, new UTF8Encoding(false, true));
                    while (reader.ReadLine() is { } line) lines.Add(line);
                }
                else lines.AddRange(File.ReadAllLines(stem));
            }
            if (File.Exists(PathIn(threadFolder))) lines.AddRange(File.ReadAllLines(PathIn(threadFolder)));
            lines.AddRange(HandoffJournalWriter.RefusedFor(threadFolder)); // a batch the disk refused follows the lines on disk
            return [.. lines];
        }
    }

    public static ThreadDocument Restore(ThreadDocument history, IReadOnlyList<string> journal)
    {
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in journal)
        {
            using var entry = JsonDocument.Parse(line);
            if (entry.RootElement.TryGetProperty("Replay", out var replay) && !replay.GetBoolean()) continue;
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
                    if (payload.GetProperty("Text").GetString() is { Length: > 0 } completedText)
                        messages[payload.GetProperty("Id").GetString()!] = completedText;
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
