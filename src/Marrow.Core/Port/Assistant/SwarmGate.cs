// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Swarm gate", 11.4; unit AS-15; ED-NEW-dextra-as15-1). No Swift twin.
// The swarm gate: before more than 8 agents, or any cloud or API fan-out, the plan card shows the agents, the estimated time, the
// estimated tokens or dollars and which accounts pay, and the owner must tap Start. Pure data and words; ReservePlan carries the
// lanes (where each group of agents runs) and ReservePlanWords adds the reasons; PlanCard draws the facts.
using System.Globalization;
using Marrow.Core.Models;

namespace Marrow.Core.Port.Assistant;

/// <summary>Where an agent runs (13.2): a CLI process on this PC, a model behind an API key, or the provider's own cloud.</summary>
public enum SwarmPlace { Local, Api, Cloud }

/// <summary>A group of agents of a plan that run on one account, and where.</summary>
public sealed record SwarmLane(string AccountKey, string AccountName, SwarmPlace Place, int Agents);

/// <summary>Which providers count as API or cloud (ED-NEW-dextra-as15-1 point 2).</summary>
public static class SwarmPlaces
{
    /// <summary>
    /// An API-key provider (DeepSeek, Meta, Z.ai, OpenRouter) and OpenCode Zen, the pay-per-token route, are API; Devin runs its
    /// agents in its own cloud; everything else is a CLI process on this PC (a local model server costs no money either).
    /// </summary>
    public static SwarmPlace Of(ProviderKind provider) =>
        provider.IsAPIKeyBased || provider == ProviderKind.Opencode ? SwarmPlace.Api
        : provider == ProviderKind.Devin ? SwarmPlace.Cloud
        : SwarmPlace.Local;
}

/// <summary>The four facts the plan card shows when the swarm gate applies (13.2).</summary>
public sealed record SwarmFacts(string Agents, string Time, string Cost, string PaidBy)
{
    public const string AgentsLabel = "Agents";
    public const string TimeLabel = "Time";
    public const string CostLabel = "Estimated cost";
    public const string PaidByLabel = "Paid by";

    /// <summary>The facts as label and value, in the order the card shows them.</summary>
    public IReadOnlyList<(string Label, string Value)> Rows => [(AgentsLabel, Agents), (TimeLabel, Time), (CostLabel, Cost), (PaidByLabel, PaidBy)];
}

/// <summary>The swarm gate's rules and words.</summary>
public static class SwarmGate
{
    /// <summary>More agents than this need the owner's Start (11.4, 13.2).</summary>
    public const int AgentsWithoutTap = ReserveMath.StartTapAgents;

    /// <summary>
    /// Two cloud or API agents at once are a fan-out; one is not (it is one more agent on a paid account, and a plan expected to
    /// spend over $2 of API money needs the owner's Start anyway, 11.4).
    /// </summary>
    public const int FanOutAgents = 2;

    private const string Dot = " \U000000B7 ";

    /// <summary>The agents that run on a paid account: API or cloud.</summary>
    public static int PaidAgents(IEnumerable<SwarmLane> lanes) => lanes.Where(l => l.Place != SwarmPlace.Local).Sum(l => Math.Max(0, l.Agents));

    public static bool IsFanOut(IEnumerable<SwarmLane> lanes) => PaidAgents(lanes) >= FanOutAgents;

    /// <summary>"3 cloud or API agents at once", or null when the plan has no fan-out.</summary>
    public static string? FanOutReason(IReadOnlyList<SwarmLane> lanes)
    {
        var paid = PaidAgents(lanes);
        return paid >= FanOutAgents ? string.Create(CultureInfo.InvariantCulture, $"{paid} cloud or API agents at once") : null;
    }

    /// <summary>Whether the gate applies to the plan: more than 8 agents, or a cloud or API fan-out.</summary>
    public static bool Applies(ReservePlan plan) => plan.Agents > AgentsWithoutTap || IsFanOut(plan.Lanes);

    /// <summary>The facts the card shows, or null when the gate does not apply to the plan.</summary>
    public static SwarmFacts? Facts(ReservePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Applies(plan) ? new SwarmFacts(AgentsLine(plan), TimeLine(plan), CostLine(plan), PaidByLine(plan)) : null;
    }

    private static string AgentsLine(ReservePlan plan)
    {
        var total = Math.Max(1, Math.Max(plan.Agents, plan.Lanes.Sum(l => Math.Max(0, l.Agents))));
        var text = string.Create(CultureInfo.InvariantCulture, $"{total} {(total == 1 ? "agent" : "agents")}");
        if (plan.Lanes.Count == 0) return text;
        var paid = PaidAgents(plan.Lanes);
        var local = plan.Lanes.Where(l => l.Place == SwarmPlace.Local).Sum(l => Math.Max(0, l.Agents));
        var parts = new List<string>();
        if (local > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{local} on this PC"));
        if (paid > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{paid} on cloud or API accounts"));
        return parts.Count == 0 ? text : text + ": " + string.Join(", ", parts);
    }

    private static string TimeLine(ReservePlan plan) =>
        plan.Duration is { } time ? "about " + ReservePlanWords.Minutes(time) : "not estimated";

    private static string CostLine(ReservePlan plan)
    {
        var parts = new List<string>();
        if (plan.TokensP50 > 0)
        {
            var upTo = plan.TokensP90 > plan.TokensP50 ? $" (up to {Tokens(plan.TokensP90)})" : "";
            parts.Add($"about {Tokens(plan.TokensP50)} tokens{upTo}");
        }
        if (plan.ApiDollars > 0) parts.Add($"about ${plan.ApiDollars.ToString("0.##", CultureInfo.InvariantCulture)} of API spend");
        return parts.Count == 0 ? "not estimated" : string.Join(Dot, parts);
    }

    private static string PaidByLine(ReservePlan plan)
    {
        var names = plan.Accounts.Select(a => a.Name).Concat(plan.Lanes.Select(l => l.AccountName))
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count switch
        {
            0 => "no account named yet",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }

    /// <summary>1,200,000 reads "1.2M"; 48,000 reads "48k"; under a thousand reads as it is.</summary>
    public static string Tokens(long tokens)
    {
        if (tokens >= 1_000_000) return (tokens / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (tokens >= 1_000)
        {
            var thousands = Math.Round(tokens / 1_000.0);
            return thousands >= 1_000 ? "1M" : thousands.ToString("0", CultureInfo.InvariantCulture) + "k";
        }
        return Math.Max(0, tokens).ToString(CultureInfo.InvariantCulture);
    }
}
