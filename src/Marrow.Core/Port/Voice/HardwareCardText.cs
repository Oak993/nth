// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-14): the words of the Voice typing page's hardware card and when each shows
// (voice-SPEC V-16.3.2; Q-V-18 "Checked on {date}", ED-NEW-ledger-5 item 7).
using System.Globalization;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Voice;

/// <summary>Where the hardware check stands (V-16.3.2): not running, running, or finished with its recommendation shown.</summary>
public enum HardwareCardPhase { Idle, Running, Done }

/// <summary>The hardware card's words (V-16.3.2, DESIGN voice.js:163-185).</summary>
public static partial class HardwareCardText
{
    /// <summary>A manual check this recent reads `Checked just now`; an older one reads `Checked on {date}`. ENGINEERING DECISION (Q-V-18): the spec fixes neither the wording's reach nor a limit.</summary>
    public static readonly TimeSpan JustNowWithin = TimeSpan.FromMinutes(5);

    public const string Title = "Recommended for this PC";
    public const string UseRecommended = "Use recommended";
    public const string InUse = "In use";
    public const string DownloadingThenInUse = "Downloading, then in use";
    public const string Note = "The first time voice typing is set up, this check runs by itself and installs the model it recommends. Run it again any time.";

    /// <summary>The probe rows' names, the key column (V-10.1).</summary>
    public static IReadOnlyList<string> ProbeKeys { get; } = ["Processor", "Memory", "Graphics", "GPU backend"];

    public static string Keep(string currentModel) => "Keep " + currentModel;

    public static string Kept(string currentModel) => "You kept " + currentModel;

    /// <summary>
    /// The title row's detail. Running: `Checking this computer`. A stored first-run result: `Checked by itself when voice typing was set up`. A result of the user's
    /// own check: `Checked just now` within <see cref="JustNowWithin"/>, afterwards `Checked on {date}` (Q-V-18: a later visit must not say "just now"). Nothing stored:
    /// `Not checked on this computer yet`. The date is the long date of <paramref name="culture"/> without the weekday, in <paramref name="zone"/> (local time by default).
    /// </summary>
    public static string Detail(HardwareCardPhase phase, VoiceHardwareRecord? stored, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        if (phase == HardwareCardPhase.Running) return "Checking this computer";
        if (stored is null) return "Not checked on this computer yet";
        if (stored.Origin == HardwareOrigin.Auto) return "Checked by itself when voice typing was set up";
        if (now - stored.At < JustNowWithin) return "Checked just now";
        return "Checked on " + DateText(stored.At, culture ?? CultureInfo.CurrentCulture, zone ?? TimeZoneInfo.Local);
    }

    /// <summary>The capsule's label: `Check my hardware` before any check, `Checking` while one runs, `Check again` after.</summary>
    public static string ButtonLabel(HardwareCardPhase phase, bool checkedBefore) =>
        phase == HardwareCardPhase.Running ? "Checking" : phase == HardwareCardPhase.Done || checkedBefore ? "Check again" : "Check my hardware";

    private static string DateText(DateTimeOffset at, CultureInfo culture, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var pattern = Weekday().Replace(culture.DateTimeFormat.LongDatePattern, "").Trim(' ', ',', '.');
        return local.ToString(pattern.Length == 0 ? "d" : pattern, culture);
    }

    // The long date's weekday and the punctuation after it ("dddd, MMMM d, yyyy" -> "MMMM d, yyyy").
    [GeneratedRegex(@"d{3,4}\s*[,.]?\s*|\s*[,.]?\s*d{3,4}")]
    private static partial Regex Weekday();
}
