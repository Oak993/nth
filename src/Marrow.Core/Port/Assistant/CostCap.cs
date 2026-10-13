// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Caps"; the approved canvas "Standing rules"; unit AS-15; ED-NEW-dextra-as15-2). No Swift twin.
// A cap is the other side of a reserve: a reserve keeps part of an account for the owner, a cap stops one piece of work from
// spending more than it was given. 13.2 names three: per mission (percent of each window and/or dollars), per assistant per day,
// and globally dollars per day for API keys. A cap pauses the work at a turn boundary, like a reserve does; it never cuts a turn.
// Pure data and the arithmetic of one check; CostGuard (Marrow.App) follows the spend and acts.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>What a cap limits (13.2): one mission, one assistant for a day, or every API key together for a day.</summary>
public enum CostCapScope { Task, AssistantDay, ApiDay }

/// <summary>The account a spend was made on, as a cap sees it.</summary>
public sealed record CapSubject(string AssistantId, string AccountKey, string AccountName, bool IsApiKey);

/// <summary>
/// One cap. <see cref="Percent"/> is the most of any account's window the work may use (every window the account reports; the
/// tighter one binds, as with a reserve, 11.3); <see cref="Dollars"/> is the most money it may spend. Either or both.
/// </summary>
public sealed record CostCap(string Id, CostCapScope Scope)
{
    public double? Percent { get; init; }

    public decimal? Dollars { get; init; }

    /// <summary>AssistantDay: whose day it limits; null: each assistant on its own.</summary>
    public string? AssistantId { get; init; }

    /// <summary>ApiDay: the keys it limits together, by the name the owner gave them (without case); empty: every API key.</summary>
    public ValueList<string> AccountNames { get; init; } = ValueList<string>.Empty;

    /// <summary>
    /// The cap as the engine reads it (ED-NEW-dextra-as15-2): a percent is held to 0..100 and a number that is not one is no
    /// percent; dollars are not negative; an API-key cap is of dollars alone (a key has money, not windows, 11.2).
    /// </summary>
    public CostCap Normalized() => this with
    {
        Percent = Scope == CostCapScope.ApiDay || Percent is not { } percent || !double.IsFinite(percent) ? null : Math.Clamp(percent, 0, 100),
        Dollars = Dollars is { } dollars ? Math.Max(0, dollars) : null,
    };

    /// <summary>True when the cap limits something: at least one amount, and an API-key cap has dollars.</summary>
    public bool Limits
    {
        get
        {
            var cap = Normalized();
            return cap.Scope == CostCapScope.ApiDay ? cap.Dollars is not null : cap.Percent is not null || cap.Dollars is not null;
        }
    }

    /// <summary>Whether the cap limits a spend on this account by this assistant's work.</summary>
    public bool Covers(CapSubject subject) => Scope switch
    {
        CostCapScope.Task => true,
        CostCapScope.AssistantDay => AssistantId is null || string.Equals(AssistantId, subject.AssistantId, StringComparison.Ordinal),
        CostCapScope.ApiDay => subject.IsApiKey && (AccountNames.Count == 0 || AccountNames.Any(n =>
            string.Equals(n.Trim(), subject.AccountName.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(n.Trim(), subject.AccountKey, StringComparison.Ordinal))),
        _ => false,
    };

    /// <summary>The cap's windows are of a day (the local calendar day), not of one piece of work.</summary>
    public bool IsDaily => Scope != CostCapScope.Task;
}

/// <summary>What a piece of work has used so far, as a cap counts it: points of each account's windows, and dollars.</summary>
public sealed class CapUse
{
    private readonly Dictionary<(string Account, ReserveWindowKind Kind), double> points = [];
    private readonly Dictionary<string, string> names = new(StringComparer.Ordinal);

    public decimal Dollars { get; private set; }

    public IReadOnlyDictionary<(string Account, ReserveWindowKind Kind), double> Points => points;

    /// <summary>Adds points of one account's window. <paramref name="accountName"/> is the name the owner gave the account, for the words.</summary>
    public void Add(string accountKey, ReserveWindowKind kind, double amount, string? accountName = null)
    {
        if (!double.IsFinite(amount) || amount <= 0) return;
        points[(accountKey, kind)] = points.GetValueOrDefault((accountKey, kind)) + amount;
        if (!string.IsNullOrWhiteSpace(accountName)) names[accountKey] = accountName;
    }

    /// <summary>The name the owner gave the account; its key when it was added without one.</summary>
    public string NameOf(string accountKey) => names.GetValueOrDefault(accountKey) ?? accountKey;

    public void AddDollars(decimal amount)
    {
        if (amount > 0) Dollars += amount;
    }
}

/// <summary>What the next turn is expected to use (its p90): points of each window of the account it runs on, and dollars.</summary>
public sealed record CapTurn(string AccountKey, string AccountName, IReadOnlyDictionary<ReserveWindowKind, double> Points, decimal Dollars);

/// <summary>What the owner agreed to over a cap this once (the card's first option): points per account window, and dollars.</summary>
public sealed record CapAllowance(IReadOnlyDictionary<(string Account, ReserveWindowKind Kind), double> Points, decimal Dollars)
{
    public static CapAllowance None { get; } = new(new Dictionary<(string, ReserveWindowKind), double>(), 0m);
}

/// <summary>
/// The window or the money that holds the work: the account and window (null window: dollars), what is used, the limit in force,
/// and what the next turn needs.
/// </summary>
public sealed record CapReach(string AccountKey, string AccountName, ReserveWindowKind? Window, double Used, double Limit, double Needs)
{
    public bool IsDollars => Window is null;

    /// <summary>Points or dollars still free under the limit; negative when the next turn would pass it.</summary>
    public double Room(bool beforeTurn) => Limit - Used - (beforeTurn ? Needs : 0);
}

/// <summary>The outcome of one check of one cap.</summary>
public sealed record CapVerdict(bool Crossed, CapReach? Reach)
{
    public static CapVerdict Go { get; } = new(false, null);
}

/// <summary>The arithmetic of a cap and its words. Functions of their inputs only: no clock, no store.</summary>
public static class CostCapMath
{
    /// <summary>
    /// Whether the work crosses the cap. With a <paramref name="turn"/>: the use plus the turn's p90 is over the limit (a limit
    /// the work would pass). Without one (between turns): the use has reached the limit, so no further turn may go. The tightest
    /// window (the least room) is the one reported.
    /// </summary>
    public static CapVerdict Check(CostCap cap, CapUse use, CapTurn? turn, CapAllowance? allowance = null)
    {
        ArgumentNullException.ThrowIfNull(cap);
        ArgumentNullException.ThrowIfNull(use);
        var normal = cap.Normalized();
        allowance ??= CapAllowance.None;
        CapReach? best = null;
        double bestRoom = double.PositiveInfinity;
        void Consider(CapReach reach, bool crossed)
        {
            if (!crossed) return;
            var room = reach.Room(beforeTurn: turn is not null);
            if (best is null || room < bestRoom) { best = reach; bestRoom = room; }
        }

        if (normal.Percent is { } percent)
        {
            // A turn can only add to the account it runs on, so the other accounts' windows are not its to pass; between turns every
            // window the work has used counts (any account reaching the cap holds it).
            var keys = turn is null
                ? new HashSet<(string Account, ReserveWindowKind Kind)>(use.Points.Keys)
                : new HashSet<(string Account, ReserveWindowKind Kind)>(turn.Points.Keys.Select(kind => (turn.AccountKey, kind)));
            foreach (var key in keys.OrderBy(k => k.Account, StringComparer.Ordinal).ThenBy(k => k.Kind))
            {
                var used = use.Points.GetValueOrDefault(key);
                var needs = turn is not null && key.Account == turn.AccountKey ? Math.Max(0, turn.Points.GetValueOrDefault(key.Kind)) : 0;
                var limit = percent + allowance.Points.GetValueOrDefault(key);
                var crossed = turn is null ? used >= limit : used + needs > limit;
                var name = turn is not null && key.Account == turn.AccountKey ? turn.AccountName : use.NameOf(key.Account);
                Consider(new CapReach(key.Account, name, key.Kind, used, limit, needs), crossed);
            }
        }
        // A turn that spends no money (a subscription account's) cannot pass a cap in dollars.
        if (normal.Dollars is { } dollars && (turn is null || turn.Dollars > 0))
        {
            var used = (double)use.Dollars;
            var needs = turn is null ? 0 : (double)Math.Max(0m, turn.Dollars);
            var limit = (double)(dollars + allowance.Dollars);
            var crossed = turn is null ? used >= limit : used + needs > limit;
            Consider(new CapReach(turn?.AccountKey ?? "", turn?.AccountName ?? "", null, used, limit, needs), crossed);
        }
        return best is null ? CapVerdict.Go : new CapVerdict(true, best);
    }

    /// <summary>
    /// How much the owner is asked to let the work go over this once: what the next turn needs beyond the limit, rounded up to a
    /// whole point (a whole dollar when it is money), at least 1. With no turn (between turns) it is the next whole unit.
    /// </summary>
    public static double AllowanceNeeded(CapReach reach) => Math.Max(1, Math.Ceiling(reach.Used + reach.Needs - reach.Limit - 1e-9));
}

/// <summary>The words of caps: one place, so the Reserves view, the card and the tests read the same.</summary>
public static class CostCapWords
{
    private const string Dot = " \U000000B7 ";

    /// <summary>
    /// "At most 5% of any account for this task", "Channel A: at most 15% of any account a day", "At most $20 a day on API keys".
    /// <paramref name="assistantName"/> turns an assistant's id into the name the owner sees.
    /// </summary>
    public static string Title(CostCap cap, Func<string, string?>? assistantName = null)
    {
        var c = cap.Normalized();
        var body = Body(c);
        return c.Scope switch
        {
            CostCapScope.Task => Capital(body + " for this task"),
            CostCapScope.ApiDay => Capital(body + " a day on " + Keys(c)),
            _ => c.AssistantId is { } id ? $"{assistantName?.Invoke(id) ?? id}: {body} a day" : Capital(body + " a day"),
        };
    }

    /// <summary>"at most 5% of any account", "at most $20", "at most 5% of any account and $2": the amount a cap names, without when it counts.</summary>
    public static string Body(CostCap cap)
    {
        var parts = new List<string>();
        if (cap.Percent is { } percent && cap.Scope != CostCapScope.ApiDay) parts.Add(Percent(percent) + " of any account");
        if (cap.Dollars is { } dollars) parts.Add("$" + dollars.ToString("0.##", CultureInfo.InvariantCulture));
        return "at most " + string.Join(" and ", parts);
    }

    /// <summary>The card's "why": which cap sent it. "Your cap for this task: at most 5% of any account".</summary>
    public static string Why(CostCap cap, Func<string, string?>? assistantName = null)
    {
        var c = cap.Normalized();
        var body = Body(c);
        return c.Scope switch
        {
            CostCapScope.Task => $"Your cap for this task: {body}",
            CostCapScope.ApiDay => $"Standing cap: {body} a day on {Keys(c)}",
            _ => c.AssistantId is { } id ? $"Standing cap for {assistantName?.Invoke(id) ?? id}: {body} a day" : $"Standing cap: {body} a day for each assistant",
        };
    }

    /// <summary>"Standing · Channel A only", "Standing · each assistant on its own", "This task".</summary>
    public static string Detail(CostCap cap, Func<string, string?>? assistantName = null)
    {
        var c = cap.Normalized();
        return c.Scope switch
        {
            CostCapScope.Task => "This task",
            CostCapScope.ApiDay => "Standing" + Dot + "API keys, dollars",
            _ => "Standing" + Dot + (c.AssistantId is { } id ? (assistantName?.Invoke(id) ?? id) + " only" : "each assistant on its own"),
        };
    }

    /// <summary>"5%", "$20", "5% and $2": the amounts a cap names.</summary>
    public static string Amount(CostCap cap)
    {
        var parts = new List<string>();
        if (cap.Percent is { } percent) parts.Add(Percent(percent));
        if (cap.Dollars is { } dollars) parts.Add("$" + dollars.ToString("0.##", CultureInfo.InvariantCulture));
        return string.Join(" and ", parts);
    }

    private static string Percent(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Keys(CostCap cap) => cap.AccountNames.Count == 0
        ? "API keys"
        : "the " + string.Join(" and ", cap.AccountNames) + (cap.AccountNames.Count == 1 ? " key" : " keys");

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>The binding window in words: "Claude 20x's 5-hour window", "the API keys' spend today".</summary>
    public static string Where(CapReach reach, bool daily)
    {
        if (reach.IsDollars) return daily ? "today's API spend" : "this task's spend";
        return $"{reach.AccountName}'s {ReserveWindowKinds.Words(reach.Window!.Value)}";
    }

    /// <summary>
    /// "Claude 20x's 5-hour window: 4.2% used by this task; your cap is 5%." or "Today's API spend is $1.80; your cap is $2.00."
    /// The toast's second sentence.
    /// </summary>
    public static string Standing(CostCap cap, CapReach reach)
    {
        if (reach.IsDollars)
        {
            var spent = Money(reach.Used);
            var allowed = Money(reach.Limit);
            return cap.IsDaily ? $"Today's API spend is {spent}; your cap is {allowed}." : $"This task has spent {spent}; your cap is {allowed}.";
        }
        var usedBy = cap.Scope == CostCapScope.Task ? "used by this task" : "used today";
        return $"{Where(reach, cap.IsDaily)}: {ReserveMath.Percent(reach.Used)} {usedBy}; your cap is {ReserveMath.Percent(reach.Limit)}.";
    }

    /// <summary>"... The next step needs about 1.3% more." The card's "where it stands".</summary>
    public static string Facts(CostCap cap, CapReach reach)
    {
        var text = Standing(cap, reach);
        if (reach.Needs <= 0) return text;
        var needs = reach.IsDollars ? Money(reach.Needs) : ReserveMath.Percent(reach.Needs);
        return text + $" The next step needs about {needs} more.";
    }

    public static string Money(double dollars) => "$" + dollars.ToString(dollars < 10 ? "0.00" : "0.##", CultureInfo.InvariantCulture);
}
