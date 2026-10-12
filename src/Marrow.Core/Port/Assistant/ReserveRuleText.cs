// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.3, 11.7; unit AS-13; ED-NEW-dextra-as13-1). No Swift twin.
// Reads a reserve rule from the owner's words ("always keep 25% on Codex A", "keep $5 a day on the OpenRouter key", "keep 30% of
// what's left"). A rule that names an amount and, when more than one account exists, the accounts it covers is made; one that
// does not name the accounts gets exactly one short question ("Keep 30% on every account, or only on Claude 20x?", 11.3); one
// that cannot be read says why. Accounts are matched by the names the owner gave them, the longest name first. Pure.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>What the owner's words came to: a rule, the one question to ask, or why they could not be read.</summary>
public sealed record ReserveRuleReading(ReserveRule? Rule, ReserveRuleQuestion? Question, string? Problem)
{
    public static ReserveRuleReading Made(ReserveRule rule) => new(rule, null, null);

    public static ReserveRuleReading Ask(ReserveRuleQuestion question) => new(null, question, null);

    public static ReserveRuleReading Unreadable(string problem) => new(null, null, problem);
}

/// <summary>The one question for a rule that did not say which accounts: every one, or only <see cref="Only"/>.</summary>
public sealed record ReserveRuleQuestion(string Text, ReserveRule Everywhere, ReserveRule OnlyOne, string Only);

public static partial class ReserveRuleText
{
    [GeneratedRegex(@"(?<![\d.])(\d{1,3}(?:\.\d+)?)\s*(?:%|percent\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Percent();

    [GeneratedRegex(@"\$\s*(\d{1,6}(?:\.\d{1,2})?)", RegexOptions.CultureInvariant)]
    private static partial Regex Dollars();

    [GeneratedRegex(@"\b(?:a|per|each|every)\s+month\b|\bmonthly\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerMonth();

    [GeneratedRegex(@"\bat\s+most\b|\bno\s+more\s+than\b|\bcap\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ACap();

    [GeneratedRegex(@"\b(?:every|each|all(?:\s+of\s+my)?|any)\s+accounts?\b|\beverywhere\b|\bon\s+all\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EveryAccount();

    [GeneratedRegex(@"\b(?:keep|reserve|hold)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Keeps();

    /// <summary>
    /// Reads <paramref name="text"/> as a rule of the given lifetime. A standing rule is of the whole window whatever the words
    /// say about "what is left" (11.3); a task's keeps what is left unless it says "of the whole window". With no account named and
    /// more than one known, the question is asked about <paramref name="focus"/> (the account the owner is looking at, or the one
    /// with a standing rule), else the first account.
    /// </summary>
    public static ReserveRuleReading Read(string? text, ReserveLifetime lifetime, IReadOnlyList<string> accountNames, string? focus = null)
    {
        var words = (text ?? "").Trim();
        if (words.Length == 0) return ReserveRuleReading.Unreadable(Example);
        if (ACap().IsMatch(words) && !Keeps().IsMatch(words)) return ReserveRuleReading.Unreadable("That is a cap on spending, not a reserve to keep. " + Example);
        var named = Named(words, accountNames);
        var scope = named.Count > 0 ? ReserveScope.Named([.. named]) : ReserveScope.Every;

        if (Dollars().Match(words) is { Success: true } money)
        {
            if (!decimal.TryParse(money.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars) || dollars <= 0)
                return ReserveRuleReading.Unreadable("Say how many dollars to keep, for example: keep $5 a day on the OpenRouter key.");
            if (named.Count == 0) return ReserveRuleReading.Unreadable("Say which key it is for, for example: keep $5 a day on the OpenRouter key.");
            var month = PerMonth().IsMatch(words);
            return ReserveRuleReading.Made(new ReserveRule("", scope, ReserveLifetime.Standing)
            {
                KeepDollars = dollars,
                Windows = month ? ReserveWindows.Month : ReserveWindows.Day,
            });
        }

        var percent = Percent().Match(words);
        if (!percent.Success || !double.TryParse(percent.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var keep))
            return ReserveRuleReading.Unreadable("Say how much to keep, for example: always keep 25% on Codex A.");
        if (keep is < 0 or > 100) return ReserveRuleReading.Unreadable("A reserve is between 0% and 100%.");

        // A standing rule is of the whole window; a task keeps what is left unless it says otherwise (11.3).
        var basis = lifetime == ReserveLifetime.Standing || words.Contains("whole window", StringComparison.OrdinalIgnoreCase)
            ? ReserveBasis.WholeWindow
            : ReserveBasis.WhatIsLeft;
        var rule = new ReserveRule("", scope, lifetime) { KeepPercent = keep, Basis = basis };
        if (named.Count > 0 || accountNames.Count <= 1 || EveryAccount().IsMatch(words)) return ReserveRuleReading.Made(rule);

        var only = accountNames.FirstOrDefault(n => string.Equals(n, focus, StringComparison.OrdinalIgnoreCase)) ?? accountNames[0];
        var amount = ReserveWords.Amount(rule);
        return ReserveRuleReading.Ask(new ReserveRuleQuestion($"Keep {amount} on every account, or only on {only}?",
            rule, rule with { Scope = ReserveScope.Named(only) }, only));
    }

    private static string Example => "Say a rule like: always keep 25% on Codex A.";

    // The accounts the words name, the longest name first so "Claude 20x" is not read as "Claude".
    private static List<string> Named(string words, IReadOnlyList<string> accountNames)
    {
        var found = new List<string>();
        var rest = words;
        foreach (var name in accountNames.Where(n => !string.IsNullOrWhiteSpace(n)).OrderByDescending(n => n.Length))
        {
            var at = rest.IndexOf(name.Trim(), StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            var before = at == 0 || !char.IsLetterOrDigit(rest[at - 1]);
            var end = at + name.Trim().Length;
            var after = end >= rest.Length || !char.IsLetterOrDigit(rest[end]);
            if (!before || !after) continue;
            found.Add(name.Trim());
            rest = rest.Remove(at, name.Trim().Length).Insert(at, new string(' ', name.Trim().Length));
        }
        return found;
    }
}
