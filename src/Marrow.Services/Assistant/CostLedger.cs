// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Caps", 15.3; unit AS-15; ED-NEW-dextra-as15-3). No Swift twin.
// What each assistant used today, per account and window, and the money spent on API keys: the figures a day's cap is checked
// against, kept on disk (costs.json) so a restart does not give the day back. The day is the local calendar day. The format
// carries a version; a file with a newer version is read for nothing and never written over (what is spent this run still counts, in memory, so a cap
// never reads a day as empty); fields this build does not know are
// written back; a line that is not a row is skipped, never repaired; only the last three days are kept and a day holds a bounded
// number of rows, so the file never grows without bound.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed class CostLedger
{
    public const int FormatVersion = 1;

    /// <summary>Days kept: today, and two days back so a clock change or a late reading never loses a day that still counts.</summary>
    public const int DaysKept = 3;

    /// <summary>The most rows one day keeps (an assistant, an account and a window each), so a runaway list cannot grow the file.</summary>
    public const int MaxRowsPerDay = 2048;

    private const string DollarsWindow = "Dollars";

    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly Dictionary<(DateOnly Day, string Assistant, string Account, string Window), Row> rows = [];
    private JsonObject? root;
    private DateOnly prunedFor;

    private sealed class Row(string name, bool api)
    {
        public string Name { get; set; } = name;
        public bool Api { get; set; } = api;
        public double Amount { get; set; }
        public JsonObject? Unknown { get; set; }
    }

    public CostLedger(TimeProvider? time = null) => clock = time ?? TimeProvider.System;

    /// <summary>The file's version was newer than this build knows: nothing was read and nothing is written over it; this run's spend is still counted, in memory.</summary>
    public bool ReadOnly { get; private set; }

    /// <summary>Something was added (the store's writer listens; it writes at most every half minute).</summary>
    public event Action? Changed;

    /// <summary>The local calendar day now.</summary>
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>Points of one account's window used today by this assistant's work.</summary>
    public void AddPoints(string assistantId, string accountKey, string accountName, bool isApiKey, ReserveWindowKind window, double points)
    {
        if (!double.IsFinite(points) || points <= 0) return;
        Add(assistantId, accountKey, accountName, isApiKey, window.ToString(), points);
    }

    /// <summary>Money spent today on an API key by this assistant's work.</summary>
    public void AddDollars(string assistantId, string accountKey, string accountName, decimal dollars)
    {
        if (dollars <= 0) return;
        Add(assistantId, accountKey, accountName, true, DollarsWindow, (double)dollars);
    }

    private void Add(string assistantId, string accountKey, string accountName, bool api, string window, double amount)
    {
        lock (gate)
        {
            // A newer version's file is never written over (ToJson says null), but what is spent this run is still counted in memory: a day's
            // cap that read nothing would never hold (ED-NEW-dextra-as15-10).
            var today = Today;
            var key = (today, assistantId, accountKey, window);
            if (!rows.TryGetValue(key, out var row))
            {
                if (rows.Count(r => r.Key.Day == today) >= MaxRowsPerDay) return;
                rows[key] = row = new Row(accountName, api);
            }
            row.Name = accountName;
            row.Api = api;
            row.Amount += amount;
            if (prunedFor != today)
            {
                Prune(today);
                prunedFor = today;
            }
        }
        Changed?.Invoke();
    }

    /// <summary>What a day's cap counts today: the rows it covers, summed per account window, and the dollars.</summary>
    public CapUse UseOf(CostCap cap) => UseOf(cap, Today);

    /// <summary>
    /// What a cap counts on a day. <paramref name="onlyAssistant"/> limits it to one assistant's rows: a cap that names no assistant
    /// limits each assistant on its own, so the caller asks once for each.
    /// </summary>
    public CapUse UseOf(CostCap cap, DateOnly day, string? onlyAssistant = null)
    {
        ArgumentNullException.ThrowIfNull(cap);
        var use = new CapUse();
        lock (gate)
        {
            foreach (var (key, row) in rows)
            {
                if (key.Day != day || (onlyAssistant is not null && key.Assistant != onlyAssistant)) continue;
                if (!cap.Covers(new CapSubject(key.Assistant, key.Account, row.Name, row.Api))) continue;
                if (key.Window == DollarsWindow) use.AddDollars((decimal)Math.Round(row.Amount, 6));
                else if (Enum.TryParse<ReserveWindowKind>(key.Window, out var kind)) use.Add(key.Account, kind, row.Amount, row.Name);
            }
        }
        return use;
    }

    // Under the lock.
    private void Prune(DateOnly today)
    {
        var oldest = today.AddDays(-(DaysKept - 1));
        foreach (var key in rows.Keys.Where(k => k.Day < oldest).ToList()) rows.Remove(key);
    }

    // ---- the stored document ----

    /// <summary>The document to store, or null when the file read was newer than this build knows (it is left as it was).</summary>
    public JsonObject? ToJson()
    {
        lock (gate)
        {
            if (ReadOnly) return null;
            var today = Today;
            Prune(today);
            var document = root is null ? new JsonObject() : (JsonObject)root.DeepClone();
            document["version"] = FormatVersion;
            var days = new JsonArray();
            foreach (var group in rows.GroupBy(r => r.Key.Day).OrderBy(g => g.Key))
            {
                days.Add(new JsonObject
                {
                    ["day"] = group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["rows"] = new JsonArray([.. group.OrderBy(r => r.Key.Assistant, StringComparer.Ordinal).ThenBy(r => r.Key.Account, StringComparer.Ordinal)
                        .ThenBy(r => r.Key.Window, StringComparer.Ordinal).Select(r => Write(r.Key.Assistant, r.Key.Account, r.Key.Window, r.Value))]),
                });
            }
            document["days"] = days;
            return document;
        }
    }

    private static JsonNode Write(string assistant, string account, string window, Row row)
    {
        var node = row.Unknown is null ? new JsonObject() : (JsonObject)row.Unknown.DeepClone();
        node["assistant"] = assistant;
        node["account"] = account;
        node["name"] = row.Name;
        node["api"] = row.Api;
        node["window"] = window;
        node["amount"] = row.Amount;
        return node;
    }

    /// <summary>Loads a stored document; a newer version makes the ledger read-only and loads nothing.</summary>
    public void Load(JsonObject? document)
    {
        lock (gate)
        {
            rows.Clear();
            root = null;
            ReadOnly = false;
            if (document is null) return;
            if (AssistantJson.Number(document["version"]) is { } number && number > FormatVersion)
            {
                ReadOnly = true;
                return;
            }
            root = (JsonObject)document.DeepClone();
            root.Remove("version");
            root.Remove("days");
            var today = Today;
            var oldest = today.AddDays(-(DaysKept - 1));
            if (document["days"] is not JsonArray days) return;
            foreach (var dayNode in days.OfType<JsonObject>())
            {
                if (dayNode["day"] is not JsonValue dayValue || !dayValue.TryGetValue<string>(out var dayText)
                    || !DateOnly.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    || day < oldest || day > today.AddDays(1) || dayNode["rows"] is not JsonArray lines) continue;
                var count = 0;
                foreach (var line in lines.OfType<JsonObject>())
                {
                    if (count >= MaxRowsPerDay) break;
                    if (ReadRow(line) is not { } read) continue;
                    var key = (day, read.Assistant, read.Account, read.Window);
                    if (rows.ContainsKey(key)) continue;
                    rows[key] = read.Row;
                    count++;
                }
            }
        }
    }

    private static (string Assistant, string Account, string Window, Row Row)? ReadRow(JsonObject line)
    {
        try
        {
            if (line["assistant"] is not JsonValue a || !a.TryGetValue<string>(out var assistant) || assistant.Length == 0 || assistant.Length > 64) return null;
            if (line["account"] is not JsonValue k || !k.TryGetValue<string>(out var account) || account.Length == 0 || account.Length > 256) return null;
            if (line["window"] is not JsonValue w || !w.TryGetValue<string>(out var window)
                || (window != DollarsWindow && !Enum.TryParse<ReserveWindowKind>(window, out _))) return null;
            if (AssistantJson.Number(line["amount"]) is not { } amount || !double.IsFinite(amount) || amount <= 0) return null;
            var name = line["name"] is JsonValue n && n.TryGetValue<string>(out var text) && text.Length > 0 && text.Length <= 256 ? text : account;
            var api = line["api"] is JsonValue p && p.TryGetValue<bool>(out var flag) && flag;
            var unknown = new JsonObject();
            foreach (var (field, value) in line)
                if (field is not ("assistant" or "account" or "name" or "api" or "window" or "amount")) unknown[field] = value?.DeepClone();
            return (assistant, account, window, new Row(name, api) { Amount = amount, Unknown = unknown.Count > 0 ? unknown : null });
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
