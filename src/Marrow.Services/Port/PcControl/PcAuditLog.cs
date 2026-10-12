// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1, ED-NEW-pcctl-7; DESIGN.md 4.6). The audit log: one
// JSON line per tool call in <data>\pc-control\<thread>\<yyyy-mm-dd>.jsonl, with the time of each step, so what Tactus did
// and how long each part took is on record. Typed text is logged as a length unless the user turned on "Log typed text".
// Writing never throws into a tool call; a failed write is dropped. Step timing (PcTimings) is measured on the engine's
// clock.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Support;

namespace Marrow.Services.Port.PcControl;

public sealed class PcAuditLog : IPcAudit
{
    private readonly Func<string> root;
    private readonly string thread;
    private readonly Lock gate = new();

    /// <summary>A log for one chat thread under <paramref name="root"/> (default: the app's pc-control data folder).</summary>
    public PcAuditLog(string thread, Func<string>? root = null)
    {
        this.thread = Safe(thread);
        this.root = root ?? (() => StoragePaths.Directory("pc-control"));
    }

    /// <summary>The file a line written at <paramref name="time"/> goes to.</summary>
    public string PathFor(DateTimeOffset time) =>
        Path.Combine(root(), thread, time.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");

    public void Write(PcAuditEntry entry)
    {
        try
        {
            var line = Line(entry);
            var path = PathFor(entry.Time);
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + "\n");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The JSON line for one entry.</summary>
    public static string Line(PcAuditEntry entry)
    {
        var json = new JsonObject
        {
            ["time"] = entry.Time.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["tool"] = entry.Tool,
            ["app"] = entry.App,
            ["window"] = entry.Window,
            ["element"] = entry.Element,
            ["role"] = entry.Role,
            ["result"] = entry.Result,
            ["ms"] = entry.DurationMs,
        };
        if (entry.Rung is not null) json["rung"] = entry.Rung;
        if (entry.TextLength is { } length) json["text_length"] = length;
        if (entry.Text is not null) json["text"] = entry.Text;
        if (entry.Steps.Count > 0)
        {
            var steps = new JsonObject();
            foreach (var (name, ms) in entry.Steps) steps[name] = (steps[name]?.GetValue<long>() ?? 0) + ms;
            json["steps"] = steps;
        }
        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    // A thread id becomes a folder name: letters, digits, '-' and '_' only.
    private static string Safe(string text)
    {
        var clean = new string(text.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return clean.Length == 0 ? "thread" : clean[..Math.Min(clean.Length, 80)];
    }
}

/// <summary>Step times for one call, on the engine's clock: Mark ends the running step and starts the next.</summary>
public sealed class PcTimings(IPcClock clock)
{
    private readonly List<KeyValuePair<string, long>> steps = [];
    private readonly long start = clock.NowMs;
    private long last = clock.NowMs;

    public IReadOnlyList<KeyValuePair<string, long>> Steps => steps;

    public long Total => clock.NowMs - start;

    /// <summary>Records the time since the last mark under <paramref name="step"/>.</summary>
    public void Mark(string step)
    {
        var now = clock.NowMs;
        steps.Add(new(step, now - last));
        last = now;
    }

    /// <summary>The summed time of one step name.</summary>
    public long Of(string step) => steps.Where(s => s.Key == step).Sum(s => s.Value);
}
