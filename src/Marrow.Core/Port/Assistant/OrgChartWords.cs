// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.1 "Sidebar", 9.4 "Org chart (Team tab)"; the canvas's AS table and renderOdet, work/design/assistant/index.html lines 1621-1627 and
// 2878-2886; unit AS-17; ED-NEW-dextra-as17-4). No Swift twin.
// The words of the org chart and of the sidebar's Astur rows, in one place so the two never read differently: the current work line under a name, the short label of
// a mission chip, the usage meter's sentence, an Astur's slice in words, and what a screen reader hears for a node. Pure: no I/O, no clock.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

public static class OrgChartWords
{
    // U+2026 HORIZONTAL ELLIPSIS, as the other words of the assistant use it.
    private const string Ellipsis = "\U00002026";

    /// <summary>The longest work line kept; a node shows two lines of it and the rest is for the hover.</summary>
    public const int WorkMax = 120;

    /// <summary>The longest mission chip label (the chip is a small capsule; the full title is its name and tip).</summary>
    public const int ChipMax = 14;

    /// <summary>
    /// The line under a name: what the assistant is doing now. <paramref name="live"/> is what its live tasks do (each one's activity or title, the one
    /// to show first first); <paramref name="decision"/> is the title of the first decision that waits on the owner, when one does.
    /// </summary>
    public static string WorkLine(AsturPresence presence, IReadOnlyList<string> live, string? decision = null)
    {
        ArgumentNullException.ThrowIfNull(live);
        var first = live.Count > 0 ? Clip(live[0], WorkMax) : null;
        var more = live.Count > 1 ? $" (+{live.Count - 1} more)" : "";
        return presence switch
        {
            AsturPresence.Waiting => string.IsNullOrWhiteSpace(decision) ? "Waiting on you" : "Waiting on you: " + Clip(decision, WorkMax),
            AsturPresence.Working or AsturPresence.Thinking => first is null ? AsturPresences.Label(presence) : first + more,
            AsturPresence.Blocked or AsturPresence.Paused or AsturPresence.Stopped => first is null ? AsturPresences.Label(presence) : AsturPresences.Label(presence) + ": " + first + more,
            AsturPresence.Idle => "Nothing at work",
            _ => throw new ArgumentOutOfRangeException(nameof(presence)),
        };
    }

    /// <summary>The label of a mission chip: the start of its title, cut at a word, never longer than <see cref="ChipMax"/>.</summary>
    public static string ChipLabel(string title)
    {
        var text = Collapse(title);
        if (text.Length == 0) return "Task";
        if (text.Length <= ChipMax) return text;
        var cut = text[..(ChipMax - 1)];
        var space = cut.LastIndexOf(' ');
        if (space >= 4) cut = cut[..space];
        return cut.TrimEnd(' ', ',', ':', ';', '.') + Ellipsis;
    }

    /// <summary>The chip that stands for the tasks that have no chip of their own ("+2").</summary>
    public static string MoreChip(int hidden) => "+" + hidden.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// An Astur's use of today against its slice, in words: how much of the slice is used (the larger of its percent and its dollars), or that it is at it.
    /// A fraction of null means it has no slice.
    /// </summary>
    public static string SliceUse(double? fraction)
    {
        if (fraction is not { } f) return "No daily slice of its own";
        if (f >= 1) return "At its daily slice";
        return Percent(f) + " of its daily slice";
    }

    /// <summary>Dextra's figure: the most any account window was used today by everyone together (9.3 "Totals").</summary>
    public static string TotalUse(double worstWindowPercent) =>
        worstWindowPercent < .5 ? "Nothing used today" : Percent(worstWindowPercent / 100) + " of the busiest window today, all together";

    /// <summary>An Astur's slice (9.3: "Each Astur's budget is a slice it cannot exceed"): "Up to 15% of any account a day", with its dollars when it has some.</summary>
    public static string SliceLine(AsturBudget? budget)
    {
        if (budget is null) return "No slice of its own: your reserves apply";
        var line = "Up to " + budget.DailyPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% of any account a day";
        if (budget.DailyDollars is { } dollars) line += " and $" + dollars.ToString("0.##", CultureInfo.InvariantCulture) + " a day on an API key";
        return line;
    }

    /// <summary>"1 mission" and "2 missions": a count and its noun (the plural given, so "duty" can be "duties").</summary>
    public static string Count(int n, string one, string many) => n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);

    /// <summary>"3 missions, middle dot, 2 duties": two counts side by side, for the detail's paired fields.</summary>
    public static string Pair(string first, string second) => first + " \U000000B7 " + second;

    /// <summary>What a screen reader hears for a node: the name, what it is doing in a word, and the line under it. The mark is not read.</summary>
    public static string NodeName(string name, AsturPresence presence, string work, int waiting)
    {
        var text = name + ", " + AsturPresences.Label(presence);
        if (waiting > 0) text += ", " + ControlWords.Plural(waiting, "decision") + " open";
        return work.Length == 0 ? text : text + ". " + work;
    }

    /// <summary>What Pause says it did to one assistant's tasks: how many pause (each at its next turn boundary, nothing cut), or why none did.</summary>
    public static string PauseLine(string name, int paused, string? refusal = null) =>
        paused > 0 ? $"Pausing {ControlWords.Plural(paused, "task")} of {name}: each stops at its next turn boundary and nothing is cut."
        : !string.IsNullOrWhiteSpace(refusal) ? refusal
        : $"{name} has nothing to pause.";

    /// <summary>What Resume says it did to one assistant's tasks.</summary>
    public static string ResumeLine(string name, int resumed, string? refusal = null) =>
        resumed > 0 ? $"Resumed {ControlWords.Plural(resumed, "task")} of {name}."
        : !string.IsNullOrWhiteSpace(refusal) ? refusal
        : $"{name} has nothing to resume.";

    /// <summary>Why "Add an Astur" waits: there are six, one for each look (2.5), so a seventh has none to take.</summary>
    public const string AllSixMade = "Six Asturs is the most: each has its own look.";

    /// <summary>The line under Dextra when it has no Asturs yet.</summary>
    public const string NoAsturs = "No Asturs yet. Ask Dextra to make one for an area of your life, such as a shop or a channel, or add one yourself.";

    private static string Percent(double fraction) =>
        Math.Round(Math.Max(0, fraction) * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Collapse(string? text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Clip(string text, int max)
    {
        var line = Collapse(text);
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + Ellipsis;
    }
}
