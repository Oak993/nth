// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Caps", 11.7 "Rules can be per assistant"; the approved canvas "Standing rules"; unit AS-15;
// ED-NEW-dextra-as15-2). No Swift twin. Reads a cap from the owner's words: "Channel A: at most 15% of any account a day", "at most $20
// a day on API keys", "at most 5% of any account for this task". A cap that cannot be read says why. Pure.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>An assistant the owner may name in a cap: its id and the name he knows it by.</summary>
public sealed record CapAssistant(string Id, string Name);

/// <summary>What the owner's words came to: a cap, or why they could not be read.</summary>
public sealed record CostCapReading(CostCap? Cap, string? Problem)
{
    public static CostCapReading Made(CostCap cap) => new(cap, null);

    public static CostCapReading Unreadable(string problem) => new(null, problem);
}

public static partial class CostCapText
{
    [GeneratedRegex(@"(?<![\d.])(\d{1,3}(?:\.\d+)?)\s*(?:%|percent\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Percent();

    [GeneratedRegex(@"\$\s*(\d{1,6}(?:\.\d{1,2})?)", RegexOptions.CultureInvariant)]
    private static partial Regex Dollars();

    [GeneratedRegex(@"\bat\s+most\b|\bno\s+more\s+than\b|\bcap\b|\bup\s+to\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ACap();

    [GeneratedRegex(@"\b(?:keep|reserve|hold)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Keeps();

    [GeneratedRegex(@"\b(?:a|per|each|every)\s+day\b|\bdaily\b|\btoday\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerDay();

    [GeneratedRegex(@"\b(?:this|per|each|every)\s+(?:task|mission)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerTask();

    /// <summary>The words speak of a cap ("at most", "no more than", "cap") and not of a reserve to keep: the Reserves line sends them here.</summary>
    public static bool LooksLikeCap(string? words) => !string.IsNullOrWhiteSpace(words) && ACap().IsMatch(words) && !Keeps().IsMatch(words);

    public static string Example => "Say a cap like: at most 15% of any account a day.";

    /// <summary>
    /// Reads <paramref name="text"/> as a cap. A standing cap (Settings > Reserves) is of a day; a task's cap (set where the task
    /// starts, <paramref name="forTask"/>) is of the task. With dollars alone and no assistant named the cap is the global API
    /// one (13.2: "Globally: dollars per day for API keys"); with a percent and no assistant named it limits each assistant on its
    /// own. <paramref name="apiKeyNames"/> are the names the owner gave his API keys.
    /// </summary>
    public static CostCapReading Read(string? text, IReadOnlyList<CapAssistant> assistants, IReadOnlyList<string> apiKeyNames, bool forTask = false)
    {
        var words = (text ?? "").Trim();
        if (words.Length == 0) return CostCapReading.Unreadable(Example);
        double? percent = null;
        decimal? dollars = null;
        if (Percent().Match(words) is { Success: true } p)
        {
            if (!double.TryParse(p.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0 || value > 100)
                return CostCapReading.Unreadable("A cap is more than 0% and at most 100%.");
            percent = value;
        }
        if (Dollars().Match(words) is { Success: true } d)
        {
            if (!decimal.TryParse(d.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var money) || money <= 0)
                return CostCapReading.Unreadable("Say how many dollars the cap is, for example: at most $20 a day on API keys.");
            dollars = money;
        }
        if (percent is null && dollars is null) return CostCapReading.Unreadable("Say how much, for example: at most 15% of any account a day.");

        if (forTask)
            return CostCapReading.Made(new CostCap("", CostCapScope.Task) { Percent = percent, Dollars = dollars });

        var day = PerDay().IsMatch(words);
        if (PerTask().IsMatch(words) && !day)
            return CostCapReading.Unreadable("A cap for one task is set where the task starts: Adjust on its plan.");
        if (!day) return CostCapReading.Unreadable("Say when the cap applies, for example: at most 15% of any account a day.");

        var assistant = Named(words, assistants.Select(a => a.Name)) is { } name
            ? assistants.First(a => string.Equals(a.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            : null;
        var keys = NamedAll(words, apiKeyNames);

        if (assistant is null && percent is null)
        {
            // Dollars alone: the global one, of API keys (13.2).
            return CostCapReading.Made(new CostCap("", CostCapScope.ApiDay) { Dollars = dollars, AccountNames = new ValueList<string>(keys) });
        }
        return CostCapReading.Made(new CostCap("", CostCapScope.AssistantDay) { Percent = percent, Dollars = dollars, AssistantId = assistant?.Id });
    }

    // The longest name first, so "Channel A" is not read as "Channel" and a name inside a longer word is not the name.
    private static string? Named(string words, IEnumerable<string> names) => NamedAll(words, names).FirstOrDefault();

    private static List<string> NamedAll(string words, IEnumerable<string> names)
    {
        var found = new List<string>();
        var rest = words;
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length))
        {
            var at = rest.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            var before = at == 0 || !char.IsLetterOrDigit(rest[at - 1]);
            var end = at + name.Length;
            var after = end >= rest.Length || !char.IsLetterOrDigit(rest[end]);
            if (!before || !after) continue;
            found.Add(name);
            rest = rest.Remove(at, name.Length).Insert(at, new string(' ', name.Length));
        }
        return found;
    }
}
