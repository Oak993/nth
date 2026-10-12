using System.Globalization;

// PORT-ONLY: web surfaces W5, the words and numbers of a Background tasks card (E:/Droppy/work/design/web-surfaces/index.html,
// the canvas's fmtT and the sample cards: "12m 05s", "165.4k tokens", "132 tool uses", "62 %", "Done 4 min ago", "3.1 of 5.0 GB";
// DESIGN.md 3.10; ED-NEW-web-w5-1). No Swift counterpart. Pure functions of numbers, so the pane and the assistant's board
// say the same thing and a tick never builds a string a test cannot name.

namespace Marrow.Core.Models;

public static class BackgroundJobText
{
    /// <summary>How long a finished job stays in the list before its card fades away (3.10 "fades after 10 minutes").</summary>
    public static readonly TimeSpan DoneLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The live values refresh this often (3.10 "Live values tick at most 4 times a second").</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private static string Two(long value) => value.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>The canvas's elapsed time: "0m 44s", "12m 05s", and from an hour on "1h 04m".</summary>
    public static string Elapsed(TimeSpan span)
    {
        var seconds = (long)Math.Floor(Math.Max(0, span.TotalSeconds));
        var minutes = seconds / 60;
        return minutes >= 60
            ? $"{(minutes / 60).ToString(CultureInfo.InvariantCulture)}h {Two(minutes % 60)}m"
            : $"{minutes.ToString(CultureInfo.InvariantCulture)}m {Two(seconds % 60)}s";
    }

    /// <summary>The widest elapsed text a card draws without growing: "99h 59m".</summary>
    public const string ElapsedReserve = "99h 59m";

    /// <summary>"812 tokens", "165.4k tokens", "1.2M tokens". Whole-number arithmetic, so a figure rounds half up the same way everywhere.</summary>
    public static string Tokens(long count)
    {
        count = Math.Max(0, count);
        if (count < 1000) return count.ToString(CultureInfo.InvariantCulture) + " tokens";
        var tenths = (count + 50) / 100; // tenths of a thousand
        if (tenths < 10_000) return (tenths / 10).ToString(CultureInfo.InvariantCulture) + "." + (tenths % 10).ToString(CultureInfo.InvariantCulture) + "k tokens";
        var millions = (count + 50_000) / 100_000; // tenths of a million
        return (millions / 10).ToString(CultureInfo.InvariantCulture) + "." + (millions % 10).ToString(CultureInfo.InvariantCulture) + "M tokens";
    }

    public const string TokensReserve = "999.9k tokens";

    /// <summary>"1 tool use", "132 tool uses".</summary>
    public static string ToolUses(long count)
    {
        count = Math.Max(0, count);
        return count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " tool use" : " tool uses");
    }

    public const string ToolUsesReserve = "99999 tool uses";

    /// <summary>"62 %", the canvas's download figure (a space before the sign).</summary>
    public static string Percent(double progress)
    {
        var value = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0, 1);
        return Math.Floor(value * 100 + 1e-9).ToString("0", CultureInfo.InvariantCulture) + " %";
    }

    public const string PercentReserve = "100 %";

    /// <summary>"Done 4 min ago", "Failed just now", "Stopped 9 min ago"; null for a job that has not ended.</summary>
    public static string? EndedAgo(BackgroundJobState state, TimeSpan age)
    {
        var word = state switch
        {
            BackgroundJobState.Done => "Done",
            BackgroundJobState.Failed => "Failed",
            BackgroundJobState.Stopped => "Stopped",
            _ => null,
        };
        if (word is null) return null;
        var minutes = (long)Math.Floor(Math.Max(0, age.TotalMinutes));
        if (minutes < 1) return word + " just now";
        if (minutes < 60) return word + " " + minutes.ToString(CultureInfo.InvariantCulture) + " min ago";
        var hours = minutes / 60;
        return word + " " + hours.ToString(CultureInfo.InvariantCulture) + " h ago";
    }

    /// <summary>
    /// "3.1 of 5.0 GB": both figures in the unit of the total (decimal units, as model sizes are announced), so the two always share a unit.
    /// </summary>
    public static string BytesOf(long done, long total)
    {
        done = Math.Max(0, done);
        total = Math.Max(0, total);
        var (divisor, unit, format) = total switch
        {
            >= 1_000_000_000 => (1_000_000_000.0, "GB", "0.0"),
            >= 10_000_000 => (1_000_000.0, "MB", "0"),
            >= 1_000_000 => (1_000_000.0, "MB", "0.0"),
            >= 1000 => (1000.0, "KB", "0"),
            _ => (1.0, "B", "0"),
        };
        string Figure(long bytes) => (bytes / divisor).ToString(format, CultureInfo.InvariantCulture);
        return Figure(Math.Min(done, total)) + " of " + Figure(total) + " " + unit;
    }

    /// <summary>The action's accessible name for a card: "Stop Port settings tests".</summary>
    public static string ActionName(string verb, string title) => verb + " " + title;

    /// <summary>One line of a title: line breaks and runs of white space become single spaces, cut at <paramref name="limit"/> with an ellipsis.</summary>
    public static string OneLine(string? text, int limit = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new System.Text.StringBuilder(Math.Min(text.Length, limit + 1));
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(c);
            if (builder.Length > limit) break;
        }
        if (builder.Length <= limit) return builder.ToString();
        var cut = builder.ToString(0, Math.Max(0, limit - 1));
        return cut.TrimEnd() + "\u2026";
    }
}
