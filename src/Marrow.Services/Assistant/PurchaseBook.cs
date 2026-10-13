// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "The switch", "Limits", "History"; ED-NEW-dextra-as20-9). No Swift twin.
// What purchases keep in the Assistant's store (purchases.json, beside reserves.json): the switch, the card's currency, the
// limits, and the history. A history row is the shop, the amount, the date and the mission, with what the limits need to
// count it (its id, currency, the assistant that asked, and whether it went through); it never holds card data. Before nth
// places an order it writes the row as "paying" (the intent); after, the outcome. A row still "paying" when the book loads
// is a crash between the two: it becomes "unknown", counts against the limits, and is never placed again. The file carries a
// version; fields it does not know survive a load and save; a file from a newer nth is left untouched and keeps purchases off.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public enum PurchaseRowState { Paying, Paid, NotPaid, Unknown }

/// <summary>One purchase in the history. <see cref="Shop"/> is the shop's host ("shop.example").</summary>
public sealed record PurchaseRow(Guid Id, string Shop, decimal Amount, string Currency, DateTimeOffset At, Guid? Mission, string Assistant, PurchaseRowState State)
{
    /// <summary>Whether it counts against the limits: everything that may have been paid.</summary>
    public bool Counts => State != PurchaseRowState.NotPaid;
}

/// <summary>The owner's purchase settings: the switch (off by default), the card's currency and the limits.</summary>
public sealed record PurchasePolicy(bool On, string Currency, PurchaseLimits Limits)
{
    public static PurchasePolicy Default { get; } = new(false, "USD", PurchaseLimits.Default);
}

/// <summary>The newer-nth file cannot be changed here.</summary>
public sealed class PurchaseBookReadOnlyException() : InvalidOperationException(PurchaseWords.NewerFile);

public sealed class PurchaseBook
{
    public const int Version = 1;
    public const int HistoryKept = 1_000;

    private readonly Action<JsonObject> save;
    private readonly object gate = new();
    private readonly JsonObject root;
    private readonly List<(PurchaseRow Row, JsonObject Node)> rows = [];
    private PurchasePolicy policy;

    /// <summary>The book over the store's purchases.json.</summary>
    public static PurchaseBook Over(AssistantStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var document = store.Purchases(out var unreadable);
        return new PurchaseBook(() => document, store.SavePurchases, unreadable);
    }

    /// <summary>
    /// The book over a document. <paramref name="unreadable"/>: a file is there that does not parse; like a newer nth's
    /// file it keeps purchases off and is never written over (its history may hold what the limits must count).
    /// </summary>
    public PurchaseBook(Func<JsonObject?> load, Action<JsonObject> save, bool unreadable = false)
    {
        ArgumentNullException.ThrowIfNull(load);
        this.save = save ?? throw new ArgumentNullException(nameof(save));
        root = load()?.DeepClone() as JsonObject ?? new JsonObject();
        var version = Whole(root["version"]) ?? (root.Count == 0 ? Version : 0);
        ReadOnly = unreadable || version > Version;
        Unreadable = unreadable;
        policy = ReadPolicy(root);
        if (ReadOnly) policy = policy with { On = false };
        var interrupted = false;
        if (root["history"] is JsonArray history)
            foreach (var node in history.OfType<JsonObject>())
            {
                if (ReadRow(node) is not { } row) continue;
                if (row.State == PurchaseRowState.Paying && !ReadOnly)
                {
                    row = row with { State = PurchaseRowState.Unknown };
                    node["state"] = Name(row.State);
                    interrupted = true;
                }
                rows.Add((row, node));
            }
        if (!interrupted) return;
        // The rows a crash left "paying" are stored as "unknown" now; when that fails they still read "unknown" here, and again next time.
        try { Store(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException) { Fault = error.GetType().Name; }
    }

    /// <summary>Why the last write the owner did not ask for failed (the load's "unknown" rows, a turn-off), or null.</summary>
    public string? Fault { get; private set; }

    /// <summary>A file from a newer nth, or one that does not parse: purchases stay off here and nothing is written.</summary>
    public bool ReadOnly { get; }

    /// <summary>The file is there but does not parse.</summary>
    public bool Unreadable { get; }

    public PurchasePolicy Policy { get { lock (gate) return policy; } }

    /// <summary>The history, newest first.</summary>
    public IReadOnlyList<PurchaseRow> History { get { lock (gate) return [.. rows.Select(r => r.Row).OrderByDescending(r => r.At)]; } }

    /// <summary>Raised after any change was stored.</summary>
    public event Action? Changed;

    /// <summary>Stores the settings. Throws <see cref="PurchaseBookReadOnlyException"/> on a newer nth's file, and what the store throws.</summary>
    public void SetPolicy(PurchasePolicy next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.Limits.Problem() is { } problem) throw new ArgumentException(problem, nameof(next));
        lock (gate)
        {
            if (ReadOnly) throw new PurchaseBookReadOnlyException();
            var before = policy;
            policy = next;
            try { Store(); }
            catch { policy = before; throw; }
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Turns purchases off. It never fails the owner: when the store cannot be written the switch is still off for this
    /// run (and a newer nth's file has purchases off already). True when it was stored.
    /// </summary>
    public bool TurnOff()
    {
        bool stored;
        lock (gate)
        {
            policy = policy with { On = false };
            if (ReadOnly) return false;
            try { Store(); stored = true; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                stored = false;
                Fault = error.GetType().Name;
            }
        }
        Changed?.Invoke();
        return stored;
    }

    /// <summary>Writes the intent before an order is placed: a "paying" row. Throws when it cannot be stored (then no order is placed).</summary>
    public PurchaseRow Begin(PurchaseRequest request, Guid? mission, string assistant, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = new PurchaseRow(Guid.NewGuid(), CheckoutOrigins.Host(request.Origin), request.Total, request.Currency, at, mission, assistant, PurchaseRowState.Paying);
        lock (gate)
        {
            if (ReadOnly) throw new PurchaseBookReadOnlyException();
            rows.Add((row, new JsonObject()));
            try { Store(); }
            catch
            {
                rows.RemoveAt(rows.Count - 1);
                throw;
            }
        }
        Changed?.Invoke();
        return row;
    }

    /// <summary>Records how an order ended. A row that cannot be stored stays "paying" on disk, and reads "unknown" next time.</summary>
    public void Settle(Guid id, PurchaseRowState state)
    {
        lock (gate)
        {
            var at = rows.FindIndex(r => r.Row.Id == id);
            if (at < 0) return;
            rows[at] = (rows[at].Row with { State = state }, rows[at].Node);
            Store();
        }
        Changed?.Invoke();
    }

    /// <summary>What was spent today and this month in <paramref name="zone"/>, in the policy's currency, all and by assistant.</summary>
    public PurchaseSpend Spend(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        lock (gate)
        {
            var month = rows.Select(r => r.Row).Where(r => r.Counts && r.Currency == policy.Currency)
                .Select(r => (Row: r, At: TimeZoneInfo.ConvertTime(r.At, zone)))
                .Where(r => r.At.Year == local.Year && r.At.Month == local.Month).ToList();
            var today = month.Where(r => r.At.Day == local.Day).Sum(r => r.Row.Amount);
            var by = month.GroupBy(r => r.Row.Assistant, StringComparer.Ordinal)
                .Select(g => new KeyValuePair<string, decimal>(g.Key, g.Sum(r => r.Row.Amount)));
            return new PurchaseSpend(today, month.Sum(r => r.Row.Amount)) { ThisMonthBy = new ValueMap<string, decimal>(by) };
        }
    }

    // ---- the document ----

    private void Store()
    {
        root["version"] = Version;
        root["on"] = policy.On;
        root["currency"] = policy.Currency;
        if (root["limits"] is not JsonObject limits)
        {
            limits = new JsonObject();
            root["limits"] = limits;
        }
        limits["purchase"] = policy.Limits.PerPurchase;
        limits["day"] = policy.Limits.PerDay;
        limits["month"] = policy.Limits.PerMonth;
        var asturs = new JsonObject();
        foreach (var (id, limit) in policy.Limits.PerAstur) asturs[id] = limit;
        limits["asturs"] = asturs;
        // the newest rows are kept; the oldest beyond the cap leave the file
        foreach (var old in rows.OrderBy(r => r.Row.At).Take(Math.Max(0, rows.Count - HistoryKept)).ToList()) rows.Remove(old);
        var history = new JsonArray();
        foreach (var (row, node) in rows.OrderBy(r => r.Row.At))
        {
            var copy = node.DeepClone().AsObject();
            copy["id"] = row.Id.ToString("D");
            copy["shop"] = row.Shop;
            copy["amount"] = row.Amount;
            copy["currency"] = row.Currency;
            copy["at"] = row.At.ToString("O", CultureInfo.InvariantCulture);
            copy["mission"] = row.Mission?.ToString("D");
            copy["assistant"] = row.Assistant;
            copy["state"] = Name(row.State);
            history.Add(copy);
        }
        root["history"] = history;
        save((JsonObject)root.DeepClone());
    }

    private static PurchasePolicy ReadPolicy(JsonObject root)
    {
        var limits = root["limits"] as JsonObject;
        decimal Limit(string name, decimal fallback) => Amount(limits?[name]) is { } v && PurchaseLimits.Invalid(v) is null ? v : fallback;
        var asturs = (limits?["asturs"] as JsonObject)?
            .Select(p => (p.Key, Value: Amount(p.Value)))
            .Where(p => p.Value is { } v && PurchaseLimits.Invalid(v) is null)
            .Select(p => new KeyValuePair<string, decimal>(p.Key, p.Value!.Value))
            .OrderBy(p => p.Key, StringComparer.Ordinal) ?? Enumerable.Empty<KeyValuePair<string, decimal>>();
        var currency = Text(root["currency"]) is { Length: 3 } c && c.All(char.IsAsciiLetterUpper) ? c : "USD";
        return new PurchasePolicy(Flag(root["on"]) ?? false, currency, new PurchaseLimits
        {
            PerPurchase = Limit("purchase", PurchaseLimits.DefaultPerPurchase),
            PerDay = Limit("day", PurchaseLimits.DefaultPerDay),
            PerMonth = Limit("month", PurchaseLimits.DefaultPerMonth),
            PerAstur = new ValueMap<string, decimal>(asturs),
        });
    }

    private static PurchaseRow? ReadRow(JsonObject node)
    {
        if (!Guid.TryParse(Text(node["id"]), out var id) || Text(node["shop"]) is not { Length: > 0 } shop || Amount(node["amount"]) is not { } amount
            || Text(node["currency"]) is not { Length: 3 } currency
            || !DateTimeOffset.TryParse(Text(node["at"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            || Text(node["assistant"]) is not { } assistant || State(Text(node["state"])) is not { } state) return null;
        Guid? mission = Guid.TryParse(Text(node["mission"]), out var m) ? m : null;
        return new PurchaseRow(id, shop, amount, currency, at, mission, assistant, state);
    }

    private static string Name(PurchaseRowState state) => state switch
    {
        PurchaseRowState.Paying => "paying",
        PurchaseRowState.Paid => "paid",
        PurchaseRowState.NotPaid => "not_paid",
        _ => "unknown",
    };

    private static PurchaseRowState? State(string? name) => name switch
    {
        "paying" => PurchaseRowState.Paying,
        "paid" => PurchaseRowState.Paid,
        "not_paid" => PurchaseRowState.NotPaid,
        "unknown" => PurchaseRowState.Unknown,
        _ => null,
    };

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool? Flag(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static long? Whole(JsonNode? node) => node is JsonValue v && v.TryGetValue<long>(out var n) ? n : null;

    private static decimal? Amount(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<decimal>(out var d)) return d;
        return v.TryGetValue<double>(out var x) && !double.IsNaN(x) && !double.IsInfinity(x) && Math.Abs(x) < 1e12 ? decimal.Round((decimal)x, 2) : null;
    }
}
