// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1). The savings ledger (ISavingsLedger): what Ignis saved,
// lost and spent, in buckets by local day, chat, kind and source, plus its events, kept at Storage.Root/ignis/ledger-v1.json.
//
// File shape (compact JSON, fixed member order, buckets sorted): {"version":1,"buckets":[{"day":"2026-10-10",
// "thread":"<guid>","kind":"saved|lost|spent|missed","source":"reuse|keptWarm|tidyUp|smartHelpers|longerMemory"?,"amount":0.42,"count":3}],
// "events":[{"id","thread","kind","at","amount"?,"amountKind","agent"?,"head"?,"model"?,"atTokens"?,"reason"?}]}.
// Every change is written at once (atomic replace). A file that cannot be read is moved aside (ledger-v1.corrupt-<stamp>.json,
// never deleted) and the ledger starts empty; an entry with an unknown value is skipped and the rest kept. Events older than
// EventDays are dropped (the buckets keep their money for ever).
using System.Globalization;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.Services.Port.Ignis;

public sealed class IgnisSavingsLedger : ISavingsLedger
{
    /// <summary>ENGINEERING DECISION: how many days of events are kept (History shows today; Settings shows totals only).</summary>
    public const int EventDays = 60;

    private readonly Lock gate = new();
    private readonly TimeZoneInfo zone;
    private readonly Dictionary<BucketKey, (decimal Amount, int Count)> buckets = [];
    private readonly Dictionary<string, IgnisEvent> events = new(StringComparer.Ordinal);

    /// <param name="dataRoot">The app's data root (Storage.Root); the file is dataRoot/ignis/ledger-v1.json.</param>
    /// <param name="zone">The time zone that decides an entry's local day; null: the PC's.</param>
    public IgnisSavingsLedger(string dataRoot, TimeZoneInfo? zone = null)
    {
        this.zone = zone ?? TimeZoneInfo.Local;
        FilePath = Path.Combine(dataRoot, "ignis", "ledger-v1.json");
        Load();
    }

    public event Action? Changed;

    public string FilePath { get; }

    /// <summary>The path a corrupt file was moved to on load; null when the file read cleanly or was missing.</summary>
    public string? RecoveredFrom { get; private set; }

    public DateOnly LocalDay(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    public void Record(IgnisLedgerEntry entry)
    {
        if (entry.Amount == 0m && entry.Count == 0) return;
        lock (gate)
        {
            var key = new BucketKey(LocalDay(entry.At), entry.ThreadID, entry.Kind, entry.Kind is IgnisLedgerKind.Lost or IgnisLedgerKind.Missed ? null : entry.Source);
            var (amount, count) = buckets.GetValueOrDefault(key);
            buckets[key] = (amount + entry.Amount, count + entry.Count);
            Save();
        }
        Changed?.Invoke();
    }

    public void Record(IgnisEvent @event)
    {
        lock (gate)
        {
            events[@event.Id] = @event;
            Prune(SwiftDate.Now);
            Save();
        }
        Changed?.Invoke();
    }

    public IgnisDayTotals Day(DateOnly day)
    {
        lock (gate) return Totals(day, buckets.Where(pair => pair.Key.Day == day));
    }

    public IgnisDayTotals ChatDay(Guid thread, DateOnly day)
    {
        lock (gate) return Totals(day, buckets.Where(pair => pair.Key.Day == day && pair.Key.Thread == thread));
    }

    public IReadOnlyList<IgnisDayTotals> Days(DateOnly from, DateOnly to)
    {
        var list = new List<IgnisDayTotals>();
        for (var day = from; day <= to; day = day.AddDays(1)) list.Add(Day(day));
        return list;
    }

    public decimal AllTimeSaved
    {
        get { lock (gate) return buckets.Where(pair => pair.Key.Kind == IgnisLedgerKind.Saved).Sum(pair => pair.Value.Amount); }
    }

    public DateOnly? Since
    {
        get { lock (gate) return buckets.Count == 0 ? null : buckets.Keys.Min(key => key.Day); }
    }

    public IReadOnlyList<IgnisEvent> Events(Guid thread, DateOnly day)
    {
        lock (gate)
            return [.. events.Values.Where(e => e.ThreadID == thread && LocalDay(e.At) == day).OrderByDescending(e => e.At).ThenByDescending(e => e.Id, StringComparer.Ordinal)];
    }

    public IgnisEvent? Event(string id)
    {
        lock (gate) return events.GetValueOrDefault(id);
    }

    private static IgnisDayTotals Totals(DateOnly day, IEnumerable<KeyValuePair<BucketKey, (decimal Amount, int Count)>> rows)
    {
        var t = IgnisDayTotals.Empty(day);
        foreach (var (key, (amount, count)) in rows)
        {
            t = (key.Kind, key.Source) switch
            {
                (IgnisLedgerKind.Saved, IgnisSource.Reuse) => t with { Reuse = t.Reuse + amount, Reuses = t.Reuses + count },
                (IgnisLedgerKind.Saved, IgnisSource.KeptWarm) => t with { KeptWarm = t.KeptWarm + amount },
                (IgnisLedgerKind.Saved, IgnisSource.TidyUp) => t with { TidyUp = t.TidyUp + amount, TidyUps = t.TidyUps + count },
                (IgnisLedgerKind.Saved, IgnisSource.SmartHelpers) => t with { SmartHelpers = t.SmartHelpers + amount, HelperJobs = t.HelperJobs + count },
                (IgnisLedgerKind.Saved, IgnisSource.LongerMemory) => t with { LongerMemory = t.LongerMemory + amount },
                (IgnisLedgerKind.Lost, _) => t with { Lost = t.Lost + amount },
                (IgnisLedgerKind.Missed, _) => t with { Missed = t.Missed + amount },
                (IgnisLedgerKind.Spent, IgnisSource.KeptWarm) => t with { Spent = t.Spent + amount, Pings = t.Pings + count },
                (IgnisLedgerKind.Spent, IgnisSource.LongerMemory) => t with { Spent = t.Spent + amount, LongerMemorySpent = t.LongerMemorySpent + amount },
                (IgnisLedgerKind.Spent, _) => t with { Spent = t.Spent + amount },
                _ => t,
            };
        }
        return t;
    }

    private void Prune(DateTimeOffset now)
    {
        var oldest = LocalDay(now).AddDays(-EventDays);
        foreach (var id in events.Values.Where(e => LocalDay(e.At) < oldest).Select(e => e.Id).ToList()) events.Remove(id);
    }

    // ---- file ----

    /// <summary>An event's time in the file: UTC to the tick, "2026-10-10T09:41:00.0000000Z".</summary>
    private const string AtFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private sealed record BucketKey(DateOnly Day, Guid Thread, IgnisLedgerKind Kind, IgnisSource? Source);

    private static readonly (IgnisLedgerKind Kind, string Raw)[] KindNames = [(IgnisLedgerKind.Saved, "saved"), (IgnisLedgerKind.Lost, "lost"), (IgnisLedgerKind.Spent, "spent"), (IgnisLedgerKind.Missed, "missed")];
    private static readonly (IgnisSource Source, string Raw)[] SourceNames =
        [(IgnisSource.Reuse, "reuse"), (IgnisSource.KeptWarm, "keptWarm"), (IgnisSource.TidyUp, "tidyUp"), (IgnisSource.SmartHelpers, "smartHelpers"), (IgnisSource.LongerMemory, "longerMemory")];
    private static readonly (IgnisEventKind Kind, string Raw)[] EventNames =
    [
        (IgnisEventKind.KeptWarm, "keptWarm"), (IgnisEventKind.WarmedAgain, "warmedAgain"), (IgnisEventKind.TidiedUp, "tidiedUp"),
        (IgnisEventKind.WentCold, "wentCold"), (IgnisEventKind.HelperRan, "helperRan"), (IgnisEventKind.HeadTidiedAfterSwitch, "headTidiedAfterSwitch"),
        // ED-NEW-ignis-guards
        (IgnisEventKind.KeepWarmHeld, "keepWarmHeld"), (IgnisEventKind.LetCool, "letCool"), (IgnisEventKind.KeepWarmOn, "keepWarmOn"), (IgnisEventKind.SwitchCost, "switchCost"), (IgnisEventKind.CacheNotReused, "cacheNotReused"),
    ];
    private static readonly (IgnisAmountKind Kind, string Raw)[] AmountNames = [(IgnisAmountKind.Cost, "cost"), (IgnisAmountKind.Saved, "saved"), (IgnisAmountKind.Lost, "lost")];

    private static string Raw<T>((T Value, string Raw)[] names, T value) where T : struct, Enum => names.First(pair => pair.Value.Equals(value)).Raw;

    private static T? From<T>((T Value, string Raw)[] names, string? raw) where T : struct, Enum
    {
        foreach (var (value, name) in names) if (string.Equals(name, raw, StringComparison.Ordinal)) return value;
        return null;
    }

    private void Load()
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(FilePath)) return;
            bytes = File.ReadAllBytes(FilePath);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                throw new JsonException("not a version 1 ledger");
            if (root.TryGetProperty("buckets", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var row in list.EnumerateArray()) ReadBucket(row);
            if (root.TryGetProperty("events", out var eventList) && eventList.ValueKind == JsonValueKind.Array)
                foreach (var row in eventList.EnumerateArray()) if (ReadEvent(row) is { } e) events[e.Id] = e;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            buckets.Clear();
            events.Clear();
            MoveAside();
        }
    }

    private void MoveAside()
    {
        var stamp = SwiftDate.Now.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        var aside = Path.Combine(Path.GetDirectoryName(FilePath)!, "ledger-v1.corrupt-" + stamp + ".json");
        for (var n = 2; File.Exists(aside); n++) aside = Path.Combine(Path.GetDirectoryName(FilePath)!, "ledger-v1.corrupt-" + stamp + "-" + n.ToString(CultureInfo.InvariantCulture) + ".json");
        try
        {
            File.Move(FilePath, aside);
            RecoveredFrom = aside;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ReadBucket(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return;
        if (!DateOnly.TryParseExact(Text(row, "day"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return;
        if (!Guid.TryParseExact(Text(row, "thread"), "D", out var thread)) return;
        if (From(KindNames, Text(row, "kind")) is not { } kind) return;
        IgnisSource? source = null;
        if (Text(row, "source") is { } rawSource && (source = From(SourceNames, rawSource)) is null) return;
        if (!row.TryGetProperty("amount", out var amountElement) || amountElement.ValueKind != JsonValueKind.Number || !amountElement.TryGetDecimal(out var amount)) return;
        var count = row.TryGetProperty("count", out var countElement) && countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt32(out var c) ? c : 0;
        var key = new BucketKey(day, thread, kind, source);
        var (old, oldCount) = buckets.GetValueOrDefault(key);
        buckets[key] = (old + amount, oldCount + count);
    }

    private static IgnisEvent? ReadEvent(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        if (Text(row, "id") is not { Length: > 0 } id) return null;
        if (!Guid.TryParseExact(Text(row, "thread"), "D", out var thread)) return null;
        if (From(EventNames, Text(row, "kind")) is not { } kind) return null;
        if (!DateTimeOffset.TryParseExact(Text(row, "at"), AtFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)) return null;
        decimal? amount = row.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out var value) ? value : null;
        var amountKind = From(AmountNames, Text(row, "amountKind")) ?? IgnisAmountKind.Cost;
        long? atTokens = row.TryGetProperty("atTokens", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out var tokens) ? tokens : null;
        return new IgnisEvent(id, thread, kind, at, amount, amountKind, Text(row, "agent"), Text(row, "head"), Text(row, "model"), atTokens, Text(row, "reason"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The file's bytes for the current state (also what a test compares).</summary>
    public byte[] Encode()
    {
        lock (gate) return EncodeLocked();
    }

    private byte[] EncodeLocked()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("buckets");
            foreach (var (key, (amount, count)) in buckets
                         .OrderBy(pair => pair.Key.Day)
                         .ThenBy(pair => pair.Key.Thread.ToString("D"), StringComparer.Ordinal)
                         .ThenBy(pair => pair.Key.Kind)
                         .ThenBy(pair => pair.Key.Source.HasValue ? (int)pair.Key.Source.Value : -1))
            {
                writer.WriteStartObject();
                writer.WriteString("day", key.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteString("thread", key.Thread.ToString("D"));
                writer.WriteString("kind", Raw(KindNames, key.Kind));
                if (key.Source is { } source) writer.WriteString("source", Raw(SourceNames, source));
                writer.WriteNumber("amount", amount);
                writer.WriteNumber("count", count);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("events");
            foreach (var e in events.Values.OrderBy(e => e.At).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", e.Id);
                writer.WriteString("thread", e.ThreadID.ToString("D"));
                writer.WriteString("kind", Raw(EventNames, e.Kind));
                writer.WriteString("at", e.At.ToUniversalTime().ToString(AtFormat, CultureInfo.InvariantCulture));
                if (e.Amount is { } amount) writer.WriteNumber("amount", amount);
                writer.WriteString("amountKind", Raw(AmountNames, e.AmountKind));
                if (e.AgentID is { } agent) writer.WriteString("agent", agent);
                if (e.HeadLabel is { } head) writer.WriteString("head", head);
                if (e.Model is { } model) writer.WriteString("model", model);
                if (e.AtTokens is { } tokens) writer.WriteNumber("atTokens", tokens);
                if (e.Reason is { } reason) writer.WriteString("reason", reason);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        FileOps.WriteAtomic(FilePath, EncodeLocked());
    }
}
