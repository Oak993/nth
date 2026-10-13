// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "The brain is cheap at rest", 5.4, 6; unit AS-15; ED-NEW-dextra-as15-6). No Swift twin.
// Feed triage and the digest use the cheapest capable model; planning and merging use the strong model, the one the owner chose for
// Dextra's brain. This is the pick, pure: which model reads the feed, given the models each usable account offers and what they cost.
// A job that reads text from a private or local-model chat may only go to a model that runs on this PC (5.4).
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>What a job needs of the brain's model: the cheap one that reads the feed (and composes the digest), or the strong one that plans and merges.</summary>
public enum BrainJob { Triage, Planning }

/// <summary>A model the brain could run a job on, on one account, with what it costs.</summary>
public sealed record BrainModel(string ProviderId, string Model, string AccountKey, string AccountName)
{
    /// <summary>US dollars per million input and output tokens; null: no price is known (such a model is never picked for being cheap).</summary>
    public decimal? InputPerMillion { get; init; }

    public decimal? OutputPerMillion { get; init; }

    public long? ContextWindow { get; init; }

    /// <summary>It runs on this PC (a local model server): it costs no money and no text leaves the PC.</summary>
    public bool RunsLocally { get; init; }

    /// <summary>Points under the account's floor; null: not known. An account at or past its floor has no room.</summary>
    public double? Room { get; init; }

    /// <summary>The account can be used now (signed in, not stopped, not at a usage limit).</summary>
    public bool Usable { get; init; } = true;

    /// <summary>The price of a typical read-and-label job, three parts input to one part output; null when unknown.</summary>
    public decimal? BlendedPerMillion => RunsLocally ? 0m
        : InputPerMillion is { } input ? input * 0.75m + (OutputPerMillion ?? input * 4m) * 0.25m
        : null;
}

/// <summary>The model a job goes to, or why there is none.</summary>
public sealed record BrainPick(BrainModel? Model, string Reason, bool IsBrain);

public static class BrainPicker
{
    /// <summary>A model with a smaller context window cannot read a feed batch whole, so it is not capable of triage.</summary>
    public const long MinTriageContext = 16_000;

    /// <summary>
    /// The model for a job. Planning (and merging) is the brain itself. Triage is the cheapest usable model that has room, a known price and the
    /// context to read a batch; a tie goes to the account with more room, then by provider and model name, so the same inputs
    /// always pick the same model. It is never dearer than the brain (then the brain itself is used, which also keeps its cache
    /// warm). A model on this PC costs nothing but is not picked for being cheap: a small local model is no judge of the owner's day. It
    /// is eligible only when <paramref name="requireLocal"/> says the text may not leave the PC, and then only a model on it is; with none
    /// the pick is empty and the caller summarises nothing (5.4).
    /// </summary>
    public static BrainPick Pick(BrainJob job, IReadOnlyList<BrainModel> options, BrainModel? brain, bool requireLocal = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (job == BrainJob.Planning)
        {
            if (brain is null) return new BrainPick(null, "No model is chosen for the brain.", false);
            return requireLocal && !brain.RunsLocally
                ? new BrainPick(null, "The brain's model is not on this PC, and this text may not leave it.", false)
                : new BrainPick(brain, "Planning and merging use the strong model.", true);
        }

        var best = options
            .Where(o => o.Usable && o.RunsLocally == requireLocal && o.BlendedPerMillion is not null
                && (o.Room is null || o.Room > 0) && (o.ContextWindow is null || o.ContextWindow >= MinTriageContext))
            .OrderBy(o => o.BlendedPerMillion)
            .ThenByDescending(o => o.Room ?? double.NegativeInfinity)
            .ThenBy(o => o.ProviderId, StringComparer.Ordinal)
            .ThenBy(o => o.Model, StringComparer.Ordinal)
            .FirstOrDefault();

        // ED-NEW-dextra-open-13: an unavailable text route is never a triage fallback; planning keeps its strong brain.
        var brainAllowed = brain is { Usable: true } && (!requireLocal || brain.RunsLocally);
        if (best is null || (brainAllowed && brain!.BlendedPerMillion is { } brainPrice && best.BlendedPerMillion >= brainPrice))
        {
            return brainAllowed
                ? new BrainPick(brain, best is null ? "No cheaper model is available, so the brain's own model reads it." : "The brain's model is already the cheapest that can read it.", true)
                : new BrainPick(null, requireLocal ? "No model on this PC can read it, and this text may not leave it." : "No model is available to read it.", false);
        }
        return new BrainPick(best, string.Create(CultureInfo.InvariantCulture, $"The cheapest capable model: {best.Model} on {best.AccountName}."), false);
    }
}
