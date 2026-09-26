using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.Core.Port;

// PORT-ONLY: the user-locale formatters Swift reaches through Foundation and ICU (R-FMT-2 restating ED-034; ED-073;
// ED-094; ED-096 the accepted surface; ED-132 to ED-135 the rulings), ported over Windows' own
// %SystemRoot%\System32\icu.dll, which .NET's globalization already loads, so no package and no app-local ICU.
// Ported code is swift-foundation (github.com/swiftlang/swift-foundation, main, read 2026-09-23),
// Sources/FoundationInternationalization, and swift-corelibs-foundation Sources/Foundation/ByteCountFormatter.swift;
// their lines are cited as "swift-foundation <File>.swift:<a>-<b>" and "corelibs <File>.swift:<a>-<b>".
// Swift call sites (B2 1fbf81e): SidebarView.swift:676-685, UsageViews.swift:180-191, Components.swift:31,
// Hydra.swift:981, UsageLimitSignal.swift:42-52, AboutUpdateViews.swift:422, :784, :838, TourPages.swift:15-24,
// CommandCodeSession.swift:406, ProviderCredits.swift:34-49, DownloadsPopover.swift:33-37, AttachmentPreview.swift:96-106.
// Out of scope here (App layer, owner question 8): the Windows custom date formats, a forced first weekday and
// live updates on WM_SETTINGCHANGE (CultureInfo.ClearCachedData).

/// <summary>
/// An ambient whose default is re-read every time (Swift's <c>autoupdatingCurrent</c>); a test overrides it for its own
/// async flow through <see cref="Ambient{T}"/>. ED-132: CurrentLocale's default is computed each time it is read.
/// </summary>
public sealed class Autoupdating<T> where T : class
{
    private readonly Ambient<Func<T>> inner;

    public Autoupdating(Func<T> read) => inner = new Ambient<Func<T>>(read);

    public T Current => inner.Current();

    /// <summary>Replaces the value for the calling async flow until the result is disposed.</summary>
    public IDisposable Override(T value) => inner.Override(() => value);

    /// <summary>App start-up, and headless UI sessions: a fixed process default in place of the computed one.</summary>
    public void SetProcessDefault(T value) => inner.SetProcessDefault(() => value);
}

/// <summary>
/// Date.FormatStyle as far as the Swift call sites build it (ED-096): the field collection and its ICU skeleton.
/// swift-foundation DateFormatStyle.swift:94-256 (DateFieldCollection), :448-534 (_symbols, init(date:time:)),
/// :684-700 and the field modifiers after it; raw values from DateFieldSymbol.swift.
/// </summary>
public sealed class DateFormatStyle
{
    public enum DateStyle { Omitted, Numeric, Abbreviated, Long, Complete }

    public enum TimeStyle { Omitted, Shortened, Standard, Complete }

    public static class Symbol
    {
        // DateFieldSymbol.swift: Month .abbreviated "MMM", .wide "MMMM"; Weekday .abbreviated "EEE", .wide "EEEE".
        // Only the options the Swift call sites use are ported.
        public enum Month { Abbreviated, Wide }

        public enum Weekday { Abbreviated, Wide }
    }

    // The raw values the collection holds (DateFieldSymbol.swift).
    private const string YearDefaultDigits = "y";
    private const string MonthDefaultDigits = "M";
    private const string MonthAbbreviated = "MMM";
    private const string MonthWide = "MMMM";
    private const string DayDefaultDigits = "d";
    private const string WeekdayAbbreviated = "EEE";
    private const string WeekdayWide = "EEEE";
    private const string HourDefaultDigitsWithAbbreviatedAMPM = "j";
    private const string HourConversationalDefaultDigitsWithAbbreviatedAMPM = "C";
    private const string MinuteDefaultDigits = "m";
    private const string MinuteTwoDigits = "mm";
    private const string SecondTwoDigits = "ss";
    private const string TimeZoneShortSpecificName = "z";

    // DateFormatStyle.swift:450 var _symbols: DateFieldCollection?
    private readonly Fields? symbols;

    private DateFormatStyle(Fields? symbols) => this.symbols = symbols;

    /// <summary>.dateTime, which is .init() (DateFormatStyle.swift:1238): no symbols yet.</summary>
    public static DateFormatStyle DateTime { get; } = new(null);

    /// <summary>Date.FormatStyle(date:time:) (DateFormatStyle.swift:520-531).</summary>
    public static DateFormatStyle Of(DateStyle date, TimeStyle time)
    {
        Fields? symbols = null;
        if (date != DateStyle.Omitted) symbols = (symbols ?? new Fields()).Collection(date);
        if (time != TimeStyle.Omitted) symbols = (symbols ?? new Fields()).Collection(time);
        return new DateFormatStyle(symbols);
    }

    // Each modifier with a non-nil option starts from an empty collection when _symbols is nil (DateFormatStyle.swift:691-692).
    public DateFormatStyle Year() => With(f => f with { Year = YearDefaultDigits });

    public DateFormatStyle Month(Symbol.Month format = Symbol.Month.Abbreviated) =>
        With(f => f with { Month = format == Symbol.Month.Wide ? MonthWide : MonthAbbreviated });

    public DateFormatStyle Day() => With(f => f with { Day = DayDefaultDigits });

    public DateFormatStyle Weekday(Symbol.Weekday format = Symbol.Weekday.Abbreviated) =>
        With(f => f with { Weekday = format == Symbol.Weekday.Wide ? WeekdayWide : WeekdayAbbreviated });

    /// <summary>hour(_ = .defaultDigits(amPM: .abbreviated)): "j".</summary>
    public DateFormatStyle Hour() => With(f => f with { Hour = HourDefaultDigitsWithAbbreviatedAMPM });

    /// <summary>minute(_ = .defaultDigits): "m".</summary>
    public DateFormatStyle Minute() => With(f => f with { Minute = MinuteDefaultDigits });

    /// <summary>The ICU skeleton, locale-free: formatterTemplate with the hour as the collection holds it.</summary>
    public string Template => Symbols.Template(Symbols.Hour);

    private DateFormatStyle With(Func<Fields, Fields> change) => new(change(symbols ?? new Fields()));

    // DateFormatStyle.swift:451-457: nil symbols read as a numeric date and a shortened time.
    private Fields Symbols => symbols ?? new Fields().Collection(DateStyle.Numeric).Collection(TimeStyle.Shortened);

    /// <summary>formatterTemplate(overridingDayPeriodWithLocale:) (DateFormatStyle.swift:157-176) for a locale.</summary>
    internal string TemplateFor(string localeIdentifier, string calendarIdentifier) =>
        Symbols.Template(PreferredHour(Symbols.Hour, localeIdentifier, calendarIdentifier));

    // DateFormatStyle.swift:106-150 preferredHour(withLocale:): in zh with region TW, when the locale's hour cycle
    // shows a day period, the default-digits hour becomes the conversational one. The other five hour options it maps
    // are not reachable through this surface (Hour() is always .defaultDigits(amPM: .abbreviated)).
    private static string? PreferredHour(string? hour, string localeIdentifier, string calendarIdentifier)
    {
        if (hour is null) return null;
        var showingDayPeriod = SwiftFormatters.HourCycle(localeIdentifier, calendarIdentifier) is "h11" or "h12";
        if (!showingDayPeriod) return hour;
        if (string.Equals(IcuFormatting.Language(localeIdentifier), "zh", StringComparison.Ordinal)
            && string.Equals(IcuFormatting.Region(localeIdentifier), "TW", StringComparison.Ordinal)
            && string.Equals(hour, HourDefaultDigitsWithAbbreviatedAMPM, StringComparison.Ordinal))
        {
            return HourConversationalDefaultDigitsWithAbbreviatedAMPM;
        }
        return hour;
    }

    // DateFieldCollection, the fields a call site can reach (DateFormatStyle.swift:94-103).
    private sealed record Fields(
        string? Year = null, string? Month = null, string? Day = null, string? Weekday = null,
        string? Hour = null, string? Minute = null, string? Second = null, string? TimeZoneSymbol = null)
    {
        // DateFormatStyle.swift:157-176: era, year, quarter, month, week, day, dayOfYear, weekday, dayPeriod, hour,
        // minute, second, fraction, zone.
        public string Template(string? hour) =>
            (Year ?? "") + (Month ?? "") + (Day ?? "") + (Weekday ?? "") + (hour ?? "") + (Minute ?? "") + (Second ?? "")
            + (TimeZoneSymbol ?? "");

        // DateFormatStyle.swift:220-239 collection(date:)
        public Fields Collection(DateStyle len) => len switch
        {
            DateStyle.Omitted => this,
            DateStyle.Numeric => this with { Day = DayDefaultDigits, Year = YearDefaultDigits, Month = MonthDefaultDigits },
            DateStyle.Abbreviated => this with { Day = DayDefaultDigits, Year = YearDefaultDigits, Month = MonthAbbreviated },
            DateStyle.Long => this with { Day = DayDefaultDigits, Year = YearDefaultDigits, Month = MonthWide },
            DateStyle.Complete => this with { Day = DayDefaultDigits, Year = YearDefaultDigits, Month = MonthWide, Weekday = WeekdayWide },
        };

        // DateFormatStyle.swift:241-256 collection(time:)
        public Fields Collection(TimeStyle len) => len switch
        {
            TimeStyle.Omitted => this,
            TimeStyle.Shortened => this with { Hour = HourDefaultDigitsWithAbbreviatedAMPM, Minute = MinuteTwoDigits },
            TimeStyle.Standard => this with { Hour = HourDefaultDigitsWithAbbreviatedAMPM, Minute = MinuteTwoDigits, Second = SecondTwoDigits },
            TimeStyle.Complete => this with
            {
                Hour = HourDefaultDigitsWithAbbreviatedAMPM, Minute = MinuteTwoDigits, Second = SecondTwoDigits,
                TimeZoneSymbol = TimeZoneShortSpecificName,
            },
        };
    }
}

/// <summary>The formatters of R-FMT-2, over icu.dll (ED-096 surface).</summary>
public static class SwiftFormatters
{
    /// <summary>
    /// Locale.autoupdatingCurrent as an ICU id. Default: ED-132's bundle-matched en_&lt;region&gt; id plus ED-134's
    /// hours keyword, computed on every read.
    /// </summary>
    public static Autoupdating<string> CurrentLocale { get; } = new(DefaultLocaleIdentifier);

    /// <summary>TimeZone.autoupdatingCurrent: the local zone (ED-094).</summary>
    public static Autoupdating<TimeZoneInfo> CurrentTimeZone { get; } = new(() => TimeZoneInfo.Local);

    // ---- Date.formatted(_:) and Date.formatted(date:time:) -----------------------------------------------------

    /// <summary>
    /// Date.FormatStyle.format(_:): the skeleton through udatpg with UDATPG_MATCH_ALL_FIELDS_LENGTH
    /// (swift-foundation ICUPatternGenerator.swift:47-58, :86-92), then udat_open with that pattern
    /// (ICUDateFormatter.swift:38-60, :305-345). Locale and zone default to the current ones; the calendar is
    /// Calendar.autoupdatingCurrent, which ED-094 makes CultureInfo.CurrentCulture.Calendar.
    /// </summary>
    public static string Formatted(DateTimeOffset date, DateFormatStyle style, string? locale = null, TimeZoneInfo? zone = null)
    {
        var localeIdentifier = locale ?? CurrentLocale.Current;
        var timeZone = zone ?? CurrentTimeZone.Current;
        var calendarIdentifier = CurrentCalendarIdentifier();
        var withCalendar = IcuFormatting.WithKeyword(localeIdentifier, "calendar", calendarIdentifier);
        var skeleton = style.TemplateFor(localeIdentifier, calendarIdentifier);
        // ICUPatternGenerator.swift:86-92: without a pattern the skeleton itself is used.
        var pattern = IcuFormatting.BestPattern(withCalendar, skeleton) ?? skeleton;
        pattern = RepairDayPeriodSpace(pattern, localeIdentifier, skeleton);
        return IcuFormatting.FormatDate(withCalendar, IcuFormatting.ZoneIdentifier(timeZone), pattern, date) ?? "";
    }

    // ---- Date.formatted(.relative(presentation: .named)) ---------------------------------------------------------

    /// <summary>
    /// Date.RelativeFormatStyle, presentation .named, unitsStyle .wide, all fields allowed
    /// (swift-foundation Date+RelativeFormatStyle.swift:250-257, :293-295, :333-339). The style locale words the
    /// result; the calendar is Calendar.autoupdatingCurrent (:260): its locale is CurrentLocale (so its first weekday
    /// aligns weeks), its calendar ED-094's, and its zone <paramref name="zone"/> or the current zone.
    /// </summary>
    public static string RelativeNamed(DateTimeOffset date, DateTimeOffset? now = null, string? locale = null, TimeZoneInfo? zone = null)
    {
        // :293-295 format(_:) measures from Date.now.
        var refDate = now ?? SwiftDate.Now;
        var styleLocale = locale ?? CurrentLocale.Current;
        var timeZone = zone ?? CurrentTimeZone.Current;
        var calendarLocale = IcuFormatting.WithKeyword(CurrentLocale.Current, "calendar", CurrentCalendarIdentifier());
        using var calendar = IcuCalendar.Open(IcuFormatting.ZoneIdentifier(timeZone), calendarLocale);
        // :333-339 _format
        var largest = LargestNonZeroComponent(date, refDate, calendar);
        if (largest is null) return "";
        return IcuFormatting.FormatRelative(styleLocale, largest.Value.Value, largest.Value.Component) ?? "";
    }

    // ---- NumberFormatter .spellOut -----------------------------------------------------------------------------

    /// <summary>NumberFormatter with numberStyle .spellOut, string(from:) (TourPages.swift:19-23): ICU UNUM_SPELLOUT.</summary>
    public static string? SpellOut(long value, string locale) => IcuFormatting.FormatWithStyle(IcuFormatting.UnumSpellout, locale, value);

    // ---- Int.formatted(), .number, .percent, .currency -----------------------------------------------------------

    /// <summary>Int.formatted() (CommandCodeSession.swift:406): the integer style, no options, in the locale.</summary>
    public static string Formatted(long value, string? locale = null) =>
        IcuFormatting.FormatSkeleton("", locale ?? CurrentLocale.Current, value) ?? "";

    /// <summary>.number.precision(.fractionLength(n)).locale(l) (ProviderCredits.swift:48).</summary>
    public static string FractionLength(double value, int fractionLength, string locale) =>
        IcuFormatting.FormatSkeleton(FractionPrecision(fractionLength), locale, value) ?? "";

    /// <summary>.percent.precision(.fractionLength(n)) (AboutUpdateViews.swift:422).</summary>
    public static string Percent(double value, int fractionLength, string locale) =>
        IcuFormatting.FormatSkeleton("percent scale/100 " + FractionPrecision(fractionLength), locale, value) ?? "";

    /// <summary>.currency(code:).locale(l) (ProviderCredits.swift:44): the code's symbol and minor digits.</summary>
    public static string Currency(double value, string code, string locale) =>
        IcuFormatting.FormatSkeleton("currency/" + code, locale, value) ?? "";

    private static string FractionPrecision(int length) =>
        length == 0 ? "precision-integer" : "." + new string('0', length);

    // ---- ByteCountFormatter, countStyle .file --------------------------------------------------------------------

    /// <summary>
    /// ByteCountFormatter with its defaults and countStyle .file (DownloadsPopover.swift:33-37, AttachmentPreview.swift:96-100):
    /// corelibs ByteCountFormatter.swift:162-172 string(fromByteCount:), :186-210 convertValue, :297-303 divide,
    /// :306-383 formatNumberFor (zeroPadsFractionDigits false, isAdaptive true), :401-423 partsToIncludeFor.
    /// ED-133: the words are English in every locale; the locale formats the number only. The Swift sites set no
    /// locale, so their callers pass <see cref="CurrentLocale"/>.
    /// </summary>
    public static string FileByteCount(long bytes, string locale)
    {
        // :186-210 with the decimal byte sizes (:436) for .file (:167-168)
        var value = (double)bytes;
        if (value == 0) return "Zero" + " " + "KB"; // :188-189 partsToIncludeFor(value: "Zero", unit: .KB)
        if (value == 1 || value == -1) return ByteCountNumber(value, "byte", locale); // :191-192
        if (value < 1000d && value > -1000d) return ByteCountNumber(value, "bytes", locale); // :193-194
        if (value < 1e6 && value > -1e6) return ByteCountNumber(value / 1e3, "KB", locale);
        if (value < 1e9 && value > -1e9) return ByteCountNumber(value / 1e6, "MB", locale);
        if (value < 1e12 && value > -1e12) return ByteCountNumber(value / 1e9, "GB", locale);
        if (value < 1e15 && value > -1e15) return ByteCountNumber(value / 1e12, "TB", locale);
        if (value < 1e18 && value > -1e18) return ByteCountNumber(value / 1e15, "PB", locale);
        return ByteCountNumber(value / 1e18, "EB", locale);
    }

    // corelibs ByteCountFormatter.swift:337-363, case (false, true); NumberFormatter .decimal in the locale.
    private static string ByteCountNumber(double bytes, string unit, string locale)
    {
        string? number;
        switch (unit)
        {
            case "bytes" or "byte" or "KB":
                number = IcuFormatting.FormatDecimal(locale, bytes, minimumFractionDigits: 0, maximumFractionDigits: 0);
                break;
            case "MB":
                number = IcuFormatting.FormatDecimal(locale, bytes, minimumFractionDigits: 0, maximumFractionDigits: 1);
                break;
            default:
                // :354-360: a negative value is rounded to hundredths first (Foundation round(), half away from zero).
                var value = bytes < 0 ? Math.Round(bytes * 100, MidpointRounding.AwayFromZero) / 100 : bytes;
                number = IcuFormatting.FormatDecimal(locale, value, minimumFractionDigits: 0, maximumFractionDigits: 2);
                break;
        }
        // :408-409 "\(value) \(unit)"
        return (number ?? "") + " " + unit;
    }

    // ---- Duration.UnitsFormatStyle, narrow width, maximumUnitCount 2, fractionalPart .hide(rounded: .down) --------
    //
    // decisions_draft.md E1 (evidence_6_1/sf-swift-6.2-RELEASE-Duration+UnitsFormatStyle.swift,
    // sf-swift-6.2-RELEASE-DurUnitsTests.swift, dur.js/dur.out.txt). UNVERIFIED under ED-086: not run against
    // macOS Foundation.framework. Oracle O-65.

    /// <summary>
    /// The three duration units the B3 call sites allow (swift-foundation Duration.UnitsFormatStyle.Unit,
    /// Duration+UnitsFormatStyle.swift :73-138: only the seconds/minutes/hours cases are ever requested).
    /// Hydra.swift:1420-1423 (<c>duration(_:)</c>) and HydraPanel.swift:545-551 (<c>elapsed</c>) allow
    /// {Seconds, Minutes}; Components.swift:37-40 (<c>RelativeTime.duration</c>, called from
    /// HydraHeadPopovers.swift:125) allows {Seconds, Minutes, Hours}.
    /// </summary>
    public enum DurationUnit { Hours, Minutes, Seconds }

    // Largest to smallest, with each unit's length in seconds and its ICU duration subtype name
    // (Unit.icuSkeleton, Duration+UnitsFormatStyle.swift :92-113: "measure-unit/duration-<subtype>").
    private static readonly (DurationUnit Unit, long Seconds, string IcuName)[] DurationUnitsBySize =
    [
        (DurationUnit.Hours, 3600L, "hour"),
        (DurationUnit.Minutes, 60L, "minute"),
        (DurationUnit.Seconds, 1L, "second"),
    ];

    /// <summary>
    /// <c>Duration.seconds(totalSeconds).formatted(.units(allowed: allowedUnits, width: .narrow, maximumUnitCount: 2,
    /// fractionalPart: .hide(rounded: .down)))</c>, en: the exact configuration of Hydra.swift:1420-1423,
    /// HydraPanel.swift:545-551 ({.seconds, .minutes}) and Components.swift:37-40 ({.seconds, .minutes, .hours}).
    /// Ported from swift-foundation Duration+UnitsFormatStyle.swift:
    /// <list type="bullet">
    /// <item><c>unitsToUse</c> (:434-469): when the allowed count already fits <c>maximumUnitCount</c>, zero-valued
    /// units are dropped (:440-442); otherwise, if dropping zero units still does not fit, the first
    /// <c>maximumUnitCount</c> units starting at the first non-zero one are kept and their values recomputed fresh
    /// from the total (:459-469).</item>
    /// <item><c>_getSkeletons</c>/<c>_formatFields</c> (:307-367): a narrow ICU measure-unit skeleton per field, 0
    /// fraction digits (<c>fractionalPart</c> is hidden), a sign shown only on the first (most significant) field of
    /// a negative value (:337-358).</item>
    /// <item>The <c>units.count == 0</c> fallback to the smallest allowed unit at zero (:323-328): "0s" for both B3
    /// configurations, since seconds is always allowed.</item>
    /// <item><c>_getFullListPattern</c>/<c>format</c> (:274-282, :392-419): here ICU's own list formatter over CLDR's
    /// <c>listPattern/unit-narrow</c> (<c>ulistfmt_openForType</c>, ULISTFMT_TYPE_UNITS/ULISTFMT_WIDTH_NARROW) stands
    /// in for Apple's <c>uatmufmt_getListPattern</c>, which Windows ICU does not export.</item>
    /// </list>
    /// Width is always narrow, <c>maximumUnitCount</c> is always 2 and <c>fractionalPart</c> is always
    /// <c>hide(rounded: .down)</c>: every integer-second input the three sites format has no leftover to round, so
    /// that rounding rule is never actually exercised, and this helper does not generalize beyond these
    /// configurations (decisions_draft.md E1).
    /// </summary>
    public static string DurationUnits(long totalSeconds, IReadOnlyCollection<DurationUnit> allowedUnits, string locale)
    {
        const int maximumUnitCount = 2;

        var units = new List<DurationUnit>(3);
        foreach (var (unit, _, _) in DurationUnitsBySize)
        {
            if (Contains(allowedUnits, unit)) units.Add(unit);
        }

        var values = ValuesForDurationUnits(units, totalSeconds);

        List<DurationUnit> useUnits;
        List<long> useValues;
        if (allowedUnits.Count <= maximumUnitCount) // :440-442
        {
            (useUnits, useValues) = RemovingZeroDurationUnits(units, values);
        }
        else
        {
            var (nonZeroUnits, nonZeroValues) = RemovingZeroDurationUnits(units, values);
            if (nonZeroUnits.Count <= maximumUnitCount)
            {
                useUnits = nonZeroUnits;
                useValues = nonZeroValues;
            }
            else
            {
                var idx = values.FindIndex(v => v != 0);
                var count = Math.Min(units.Count, idx + maximumUnitCount) - idx;
                useUnits = units.GetRange(idx, count);
                useValues = ValuesForDurationUnits(useUnits, totalSeconds); // :467, recomputed fresh from the total
            }
        }

        if (useUnits.Count == 0)
        {
            // :323-328: the smallest allowed unit, shown at zero. Both B3 configurations always allow seconds.
            var smallest = DurationUnit.Seconds;
            foreach (var (unit, _, _) in DurationUnitsBySize)
            {
                if (Contains(allowedUnits, unit)) smallest = unit; // walks largest to smallest; the last hit is smallest
            }
            return FormatDurationField(0, smallest, signAlways: false, locale);
        }

        var mostSignificant = useUnits[0]; // DurationUnitsBySize order keeps useUnits sorted largest to smallest
        var isNegative = false;
        foreach (var v in useValues) { if (v < 0) { isNegative = true; break; } }

        var fields = new string[useUnits.Count];
        for (var i = 0; i < useUnits.Count; i++)
        {
            var signAlways = isNegative && useUnits[i] == mostSignificant;
            fields[i] = FormatDurationField(useValues[i], useUnits[i], signAlways, locale);
        }

        return fields.Length == 1 ? fields[0] : (IcuFormatting.FormatUnitList(locale, fields) ?? string.Join(" ", fields));
    }

    private static bool Contains(IReadOnlyCollection<DurationUnit> allowed, DurationUnit unit)
    {
        foreach (var u in allowed) { if (u == unit) return true; }
        return false;
    }

    // swift-foundation duration.valuesForUnits(_:trailingFractionalLength:smallestUnitRounding:roundingIncrement:)
    // (Duration+FieldsFormatStyle.swift, not saved as evidence): each unit's whole count, largest to smallest, with
    // the remainder carried to the next unit. The B3 sites always pass a whole number of seconds (Duration.seconds(Int)),
    // so the trailing "rounded: .down" fractional step never has anything left to round, and truncating integer
    // division (which C#'s "/" already does toward zero, matching Duration's own division) is exact.
    private static List<long> ValuesForDurationUnits(IReadOnlyList<DurationUnit> units, long totalSeconds)
    {
        var values = new List<long>(units.Count);
        var remainder = totalSeconds;
        for (var i = 0; i < units.Count; i++)
        {
            var unitSeconds = SecondsPerDurationUnit(units[i]);
            if (i == units.Count - 1)
            {
                values.Add(remainder / unitSeconds);
            }
            else
            {
                var value = remainder / unitSeconds;
                values.Add(value);
                remainder -= value * unitSeconds;
            }
        }
        return values;
    }

    // Duration+UnitsFormatStyle.swift :421-431 removingZeroUnits
    private static (List<DurationUnit> Units, List<long> Values) RemovingZeroDurationUnits(List<DurationUnit> units, List<long> values)
    {
        var keptUnits = new List<DurationUnit>(units.Count);
        var keptValues = new List<long>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] != 0) { keptUnits.Add(units[i]); keptValues.Add(values[i]); }
        }
        return (keptUnits, keptValues);
    }

    private static long SecondsPerDurationUnit(DurationUnit unit)
    {
        foreach (var (u, seconds, _) in DurationUnitsBySize) { if (u == unit) return seconds; }
        throw new ArgumentOutOfRangeException(nameof(unit));
    }

    private static string DurationIcuName(DurationUnit unit)
    {
        foreach (var (u, _, name) in DurationUnitsBySize) { if (u == unit) return name; }
        throw new ArgumentOutOfRangeException(nameof(unit));
    }

    // Duration+UnitsFormatStyle.swift :316-367 _getSkeletons/_formatFields: a narrow ICU measure-unit skeleton, 0
    // fraction digits (fractionalPart is hidden), sign shown only when signAlways is set (the most significant field
    // of a negative value, :348-358; the .always(includingZero: true) epsilon case at :350-355 is unreachable for
    // these two configurations, since zero-dropping never leaves a zero-valued field as the most significant one
    // when the total is a nonzero integer number of seconds).
    private static string FormatDurationField(long value, DurationUnit unit, bool signAlways, string locale)
    {
        var icuName = DurationIcuName(unit);
        var skeleton = "measure-unit/duration-" + icuName + " unit-width-narrow precision-integer "
            + (signAlways ? "sign-always" : "sign-never");
        // :312 fallback if ICU can't format it: "\(value) \(unit.icuSkeleton)"
        return IcuFormatting.FormatSkeleton(skeleton, locale, value)
            ?? (value.ToString(CultureInfo.InvariantCulture) + " measure-unit/duration-" + icuName);
    }

    // ---- ByteCountFormatStyle(style: .file) -------------------------------------------------------------------
    //
    // decisions_draft.md E2 (evidence_6_1/sf-swift-6.2-RELEASE-ByteCountFormatStyle.swift,
    // sf-swift-6.2-RELEASE-ByteTests.swift). Not the same wording as SwiftFormatters.FileByteCount (ByteCountFormatter,
    // ED-133): "kB" not "KB", "Zero kB" not "Zero KB", and the unit switches to kB at 999 bytes exactly (:194-207,
    // strict "&lt;"). UNVERIFIED under ED-086: not run against macOS Foundation.framework. Oracle O-66.

    // ByteCountFormatStyle.Unit (:28-54): only byte through petabyte are named; exabyte and above are declared but
    // never reached (maxDecimalSizes has 6 entries, and Int64.max as the last one means petabyte is the practical
    // ceiling, confirmed by ByteTests.swift testEveryAllowedUnit :190-208, where allowing eb/zb/ybOrHigher alone
    // still formats in PB).
    private static readonly string[] DigitalUnitNames = ["byte", "kilobyte", "megabyte", "gigabyte", "terabyte", "petabyte"];

    // :123 maxDecimalSizes. The comparison below is a strict "&lt;" (:201), so each threshold value itself already
    // belongs to the NEXT unit, not the one it is the "max" of: 999 is the first kB value (not the last byte value),
    // 999_499 is the first MB value (not the last kB value), and 999_949_999 is the first GB value (not the last MB
    // value). decisions_draft.md's own table gets the byte/kB transition right but mislabels the kB/MB and MB/GB
    // transition examples as staying in the lower unit; the source (this file) wins over the draft (see the unit's
    // report to the orchestrator).
    private static readonly long[] MaxDecimalSizes = [999L, 999_499L, 999_949_999L, 999_994_999_999L, 999_994_999_999_999L, long.MaxValue];

    // :53 decimalByteSizes
    private static readonly long[] DecimalByteSizes = [1L, 1_000L, 1_000_000L, 1_000_000_000L, 1_000_000_000_000L, 1_000_000_000_000_000L];

    /// <summary>
    /// <c>ByteCountFormatStyle(style: .file).format(_:)</c> at its defaults (<c>allowedUnits</c> .all,
    /// <c>spellsOutZero</c> true, <c>includesActualByteCount</c> false): the three B3 sites (AttachmentPreview.swift:95-98,
    /// DownloadsPopover.swift:68, AboutUpdateViews.swift:787) all use <c>.byteCount(style: .file)</c> or
    /// <c>ByteCountFormatStyle(style: .file)</c> with no other parameter, so none overrides a default.
    /// Ported from swift-foundation ByteCountFormatStyle.swift <c>Attributed._format</c> (:151-242): the best unit is
    /// chosen by <c>maxDecimalSizes</c> with a strict "&lt;" comparison (:194-207); the number carries 0 fraction
    /// digits for byte and kilobyte, up to 1 for megabyte, and up to 2 from gigabyte up, ICU's own optional "#"
    /// digits, trimmed and half-even rounded (:212-220, ICU's default rounding mode); and only the byte unit is
    /// <c>unit-width-full-name</c> ("1 byte", "998 bytes") — every other unit takes ICU's default (short) measure
    /// width, which is where "kB" (not <see cref="FileByteCount"/>'s "KB") comes from (:222).
    /// </summary>
    public static string ByteCountStyleFile(long bytes, string locale)
    {
        double doubleValue = bytes;
        if (doubleValue == 0) return ZeroByteCount(locale); // :153-180, style-independent

        var absValue = Math.Abs(doubleValue);
        var bestUnit = 0;
        for (var idx = 0; idx < MaxDecimalSizes.Length; idx++)
        {
            bestUnit = idx; // :196-200: bestUnit is set before the break check, every iteration
            if (absValue < MaxDecimalSizes[idx]) break;
        }

        var unitValue = doubleValue / DecimalByteSizes[bestUnit];
        var precision = bestUnit switch // :212-220
        {
            0 or 1 => ".",   // byte, kilobyte: 0 fraction digits
            2 => ".#",       // megabyte: up to 1
            _ => ".##",      // gigabyte and above: up to 2
        };
        var widthToken = bestUnit == 0 ? " unit-width-full-name" : ""; // :222
        var skeleton = precision + " measure-unit/digital-" + DigitalUnitNames[bestUnit] + widthToken;
        return IcuFormatting.FormatSkeleton(skeleton, locale, unitValue)
            ?? (unitValue.ToString(CultureInfo.InvariantCulture) + " " + DigitalUnitNames[bestUnit]);
    }

    // ByteCountFormatStyle.swift :153-179: spellsOutZero (default true) with unit .kilobyte, since allowedUnits .all
    // contains .kb (:152). useSpelloutZero(forLocale:unit:) returns true for language "en" (:131-133), so the numeric
    // run becomes NumberFormatter .spellOut of 0, capitalized to the beginning of a sentence
    // (DescriptiveNumberFormatConfiguration .beginningOfSentence, :164; SwiftFormatters.SpellOut already ports the
    // same RBNF call), and only the unit text keeps the plain "0 kB" formatting's CLDR word (:175-177 replaces just
    // the numeric run of the attributed string, not the unit run; this port has no attributed-string field ranges, so
    // it locates the unit word by splitting on the first space instead, which is exact for the en family this
    // configuration ever reaches, ED-132).
    private static string ZeroByteCount(string locale)
    {
        var zeroKilobytes = IcuFormatting.FormatSkeleton("measure-unit/digital-kilobyte", locale, 0L) ?? "0 kilobyte";
        var spaceIndex = zeroKilobytes.IndexOf(' ', StringComparison.Ordinal);
        var unitWord = spaceIndex >= 0 ? zeroKilobytes[(spaceIndex + 1)..] : zeroKilobytes;
        var spelled = SpellOut(0, locale) ?? "zero";
        var capitalized = SwiftString.Uppercased(SwiftString.Prefix(spelled, 1)) + SwiftString.DropFirst(spelled);
        return capitalized + " " + unitWord;
    }

    // ---- ED-132: the default of CurrentLocale --------------------------------------------------------------------

    // swift-foundation Locale_ICU.swift:204-281 for an app whose bundle localizations are only ["en"] (ED-132), plus
    // ED-134's hours keyword.
    private static string DefaultLocaleIdentifier()
    {
        // AppleLocale: uloc_forLanguageTag of the Windows regional format, used as it is, possibly empty.
        var preferredLocale = IcuFormatting.ForLanguageTag(CultureInfo.CurrentCulture.Name);
        // :228-234 bundle matching; the matched localization is always "en".
        var ident = LocaleIdentifierForCanonicalizedLocalizations(preferredLocale);
        // :236-239 no match: the canonical preferred locale.
        ident ??= CanonicalLocaleIdentifier(preferredLocale);
        // :274-281
        var fixedIdent = ident.Length != 0 ? ident : "en_001";
        var identifier = CanonicalLocaleIdentifier(fixedIdent);
        // ED-134: the user's 12/24-hour preference, on this default id only.
        var hours = HourCyclePreference();
        return hours is null ? identifier : IcuFormatting.WithKeyword(identifier, "hours", hours);
    }

    // swift-foundation Locale_ICU.swift:1712-1737 localeIdentifierForCanonicalizedLocalizations, with
    // canonicalizedLocalizations ["en"], so preferredLocalizations[0] is "en".
    private static string? LocaleIdentifierForCanonicalizedLocalizations(string preferredLocaleID)
    {
        const string preferredLocalization = "en";
        if (preferredLocaleID.Length == 0) return null;
        var preferredLocaleLanguageID = LanguageIdentifierWithScriptCode(preferredLocaleID);
        var preferredLocalizationLanguageID = LanguageIdentifierWithScriptCode(preferredLocalization);
        if (preferredLocaleLanguageID is not null && preferredLocalizationLanguageID is not null)
        {
            return string.Equals(preferredLocaleLanguageID, preferredLocalizationLanguageID, StringComparison.Ordinal)
                ? preferredLocaleID
                : LocaleIdentifierByReplacingLanguageCodeAndScriptCode(preferredLocalization, preferredLocaleID);
        }
        return null;
    }

    // swift-foundation Locale_ICU.swift:1639-1655 languageIdentifierWithScriptCodeForLocaleIdentifier
    private static string? LanguageIdentifierWithScriptCode(string localeID)
    {
        var maximized = IcuFormatting.AddLikelySubtags(localeID);
        if (maximized is null) return null;
        var language = IcuFormatting.Language(maximized);
        var script = IcuFormatting.Script(maximized);
        if (language.Length == 0 || script.Length == 0) return null;
        return language + "-" + script;
    }

    // swift-foundation Locale_ICU.swift:1657-1708 localeIdentifierByReplacingLanguageCodeAndScriptCode
    private static string? LocaleIdentifierByReplacingLanguageCodeAndScriptCode(string localeIDWithDesiredLangCode, string localeIDWithDesiredComponents)
    {
        var langIDToUse = LanguageIdentifierWithScriptCode(localeIDWithDesiredLangCode);
        if (langIDToUse is null) return null;
        var maximizedLocaleID = IcuFormatting.AddLikelySubtags(localeIDWithDesiredComponents);
        if (maximizedLocaleID is null) return null;
        var languageCode = IcuFormatting.Language(langIDToUse);
        var scriptCode = IcuFormatting.Script(langIDToUse);
        if (languageCode.Length == 0 || scriptCode.Length == 0) return null;

        // 1. Language & Script (:1682-1685)
        var id = IcuFormatting.Compose(languageCode, scriptCode, IcuFormatting.Region(maximizedLocaleID),
            IcuFormatting.Variant(maximizedLocaleID), IcuFormatting.Keywords(maximizedLocaleID));

        // 2. Numbering System (:1687-1702)
        var numberingSystem = IcuFormatting.NumberingSystemForLocaleIdentifier(localeIDWithDesiredComponents);
        var validNumberingSystems = IcuFormatting.ValidNumberingSystems(localeIDWithDesiredLangCode);
        var whichNumberingSystem = validNumberingSystems.IndexOf(numberingSystem);
        id = whichNumberingSystem > 0
            ? IcuFormatting.WithKeyword(id, "numbers", validNumberingSystems[whichNumberingSystem])
            : IcuFormatting.WithKeyword(id, "numbers", "");

        // 3. Construct & Canonicalize (:1704-1707)
        return CanonicalLocaleIdentifier(id);
    }

    // Locale._canonicalLocaleIdentifier (CoreFoundation, outside the open source). ED-132 reads it as the step-3
    // comment at swift-foundation Locale_ICU.swift:1704-1705 states it: the script is dropped only where it is implicit,
    // that is where uloc_addLikelySubtags(language_region) yields the same script; never uloc_minimizeSubtags.
    private static string CanonicalLocaleIdentifier(string identifier)
    {
        if (identifier.Length == 0) return identifier;
        var script = IcuFormatting.Script(identifier);
        if (script.Length == 0) return identifier;
        var language = IcuFormatting.Language(identifier);
        var region = IcuFormatting.Region(identifier);
        var likely = IcuFormatting.AddLikelySubtags(region.Length == 0 ? language : language + "_" + region);
        if (likely is null || !string.Equals(IcuFormatting.Script(likely), script, StringComparison.Ordinal)) return identifier;
        return IcuFormatting.Compose(language, "", region, IcuFormatting.Variant(identifier), IcuFormatting.Keywords(identifier));
    }

    // ---- ED-134: the hour cycle ------------------------------------------------------------------------------------

    // ED-134: a preference exists only when the first unquoted H or h of the user's short-time pattern differs from
    // that of the culture's own. H is h23 and h is h12 (Locale_Preferences.swift:193-198, :327-335: 24 wins, but a
    // pattern has one first hour letter). In .NET, K is the zone, not an hour.
    private static string? HourCyclePreference()
    {
        var current = CultureInfo.CurrentCulture;
        var user = FirstHourLetter(current.DateTimeFormat.ShortTimePattern);
        var plain = FirstHourLetter(CultureInfo.GetCultureInfo(current.Name).DateTimeFormat.ShortTimePattern);
        if (user is null || user == plain) return null;
        return user == 'H' ? "h23" : "h12";
    }

    private static char? FirstHourLetter(string pattern)
    {
        char? quote = null;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (quote is not null)
            {
                if (c == quote) quote = null;
                continue;
            }
            if (c == '\\') { i++; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c is 'H' or 'h') return c;
        }
        return null;
    }

    // swift-foundation Locale_ICU.swift:1134-1170 Locale.hourCycle: the hours keyword of the identifier first, else
    // the pattern generator's default hour cycle for the locale and calendar, else h23.
    internal static string HourCycle(string localeIdentifier, string calendarIdentifier)
    {
        var keyword = IcuFormatting.KeywordValue(localeIdentifier, "hours");
        if (keyword is "h11" or "h12" or "h23" or "h24") return keyword;
        return IcuFormatting.DefaultHourCycle(IcuFormatting.WithKeyword(localeIdentifier, "calendar", calendarIdentifier)) ?? "h23";
    }

    // ---- ED-135: U+202F before the day period ----------------------------------------------------------------------

    // UNVERIFIED O-23: icu.dll 72.1 carries CLDR's alt="ascii" en time patterns (U+0020 before a); macOS 26 and CLDR
    // 42+ use U+202F. Checked against every CLDR release-46 en*.xml: the one en pattern that keeps U+0020 before a is
    // region ID's skeleton hmsv ("h.mm.ss. a v"). ED-135.
    private static readonly DayPeriodSpaceRule EnDayPeriodSpace = new(
        Language: "en",
        Space: "\U0000202F", // U+202F NARROW NO-BREAK SPACE
        KeepRegion: "ID",
        KeepSkeleton: "hmsv");

    private sealed record DayPeriodSpaceRule(string Language, string Space, string KeepRegion, string KeepSkeleton);

    // ED-135: before udat_open, for a locale whose language is en, an unquoted U+0020 directly before an unquoted
    // pattern letter a becomes U+202F. Quoted text, b and B fields and an existing U+202F are left alone.
    private static string RepairDayPeriodSpace(string pattern, string localeIdentifier, string skeleton)
    {
        var rule = EnDayPeriodSpace;
        if (!string.Equals(IcuFormatting.Language(localeIdentifier), rule.Language, StringComparison.Ordinal)) return pattern;
        if (string.Equals(IcuFormatting.Region(localeIdentifier), rule.KeepRegion, StringComparison.Ordinal)
            && string.Equals(skeleton, rule.KeepSkeleton, StringComparison.Ordinal))
        {
            return pattern;
        }
        var result = new StringBuilder(pattern.Length);
        var quoted = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\'')
            {
                // '' is a literal apostrophe inside or outside quotes; a single ' toggles quoting.
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    result.Append("''");
                    i++;
                    continue;
                }
                quoted = !quoted;
                result.Append(c);
                continue;
            }
            if (!quoted && c == ' ' && i + 1 < pattern.Length && pattern[i + 1] == 'a')
            {
                result.Append(rule.Space);
                continue;
            }
            result.Append(c);
        }
        return result.ToString();
    }

    // ---- Calendar.autoupdatingCurrent (ED-094) ---------------------------------------------------------------------

    // ED-094: the user's calendar is CultureInfo.CurrentCulture.Calendar, named here by its ICU calendar keyword
    // (Calendar.localeIdentifierWithCalendar, swift-foundation Calendar_ICU.swift:2323-2327). A calendar without an
    // ICU twin (.NET's KoreanCalendar, a Gregorian year count with the Dangi era) falls to gregorian.
    private static string CurrentCalendarIdentifier() => CultureInfo.CurrentCulture.Calendar switch
    {
        ThaiBuddhistCalendar => "buddhist",
        JapaneseCalendar => "japanese",
        TaiwanCalendar => "roc",
        UmAlQuraCalendar => "islamic-umalqura",
        HijriCalendar => "islamic-civil",
        HebrewCalendar => "hebrew",
        PersianCalendar => "persian",
        _ => "gregorian",
    };

    // ---- Date.RelativeFormatStyle internals (swift-foundation Date+RelativeFormatStyle.swift) ----------------------

    internal enum Component { Year, Month, WeekOfMonth, Day, Hour, Minute, Second, Nanosecond }

    // ICURelativeDateFormatter.swift:28 sortedAllowedComponents; all of them are allowed by default (:256).
    private static readonly Component[] SortedAllowedComponents =
        [Component.Year, Component.Month, Component.WeekOfMonth, Component.Day, Component.Hour, Component.Minute, Component.Second];

    // Date.ComponentsFormatStyle.Field.Option order: a larger unit compares greater.
    private static int Option(Component component) => component switch
    {
        Component.Year => 7,
        Component.Month => 6,
        Component.WeekOfMonth => 5,
        Component.Day => 4,
        Component.Hour => 3,
        Component.Minute => 2,
        Component.Second => 1,
        Component.Nanosecond => 0,
    };

    private readonly record struct ComponentAndValue(Component Component, long Value);

    // :468-475 nonZeroComponentsAndValue, first
    private static ComponentAndValue? FirstNonZero(Dictionary<Component, long> components)
    {
        foreach (var component in SortedAllowedComponents)
        {
            if (components.TryGetValue(component, out var value) && value != 0) return new ComponentAndValue(component, value);
        }
        return null;
    }

    // :431-460 _largestNonZeroComponent
    private static ComponentAndValue? LargestNonZeroComponent(DateTimeOffset destDate, DateTimeOffset reference, IcuCalendar calendar)
    {
        var smallest = SortedAllowedComponents[^1];
        // :437 round the reference to whole seconds away from zero, measured from destDate
        var interval = SwiftDate.ReferenceSeconds(reference) - SwiftDate.ReferenceSeconds(destDate);
        var refDate = SwiftDate.AddingTimeInterval(destDate, Math.Round(interval, MidpointRounding.AwayFromZero));
        var allowedComponents = SortedAllowedComponents;
        var dateComponents = calendar.DateComponents(allowedComponents, refDate, destDate);
        var largest = FirstNonZero(dateComponents) ?? new ComponentAndValue(smallest, 0);
        var comp = largest.Component;
        if (comp is Component.Hour or Component.Minute or Component.Second)
        {
            return RoundedLargestComponentValue(refDate, destDate, calendar, allowedComponents, comp) ?? largest;
        }
        return AlignedComponentValue(largest.Component, destDate, refDate, calendar, allowedComponents) ?? largest;
    }

    // :341-362 _alignedComponentValue
    private static ComponentAndValue? AlignedComponentValue(Component component, DateTimeOffset destDate, DateTimeOffset refDate, IcuCalendar calendar, Component[] allowedComponents)
    {
        var dateInterval = calendar.DateInterval(component, refDate);
        if (dateInterval is null) return null;
        var (refDateStart, length) = dateInterval.Value;
        var refDateEnd = SwiftDate.AddingTimeInterval(refDateStart, length - 1);
        var dateComponents = refDate < destDate
            ? calendar.DateComponents(allowedComponents, refDateStart, destDate)
            : calendar.DateComponents(allowedComponents, refDateEnd, destDate);
        return FirstNonZero(dateComponents);
    }

    // :364-428 _roundedLargestComponentValue
    private static ComponentAndValue? RoundedLargestComponentValue(DateTimeOffset refDate, DateTimeOffset destDate, IcuCalendar calendar, Component[] allowedComponents, Component largestAllowedComponent)
    {
        var requested = SortedAllowedComponents.Where(c => Option(c) <= Option(largestAllowedComponent)).Append(Component.Nanosecond).ToArray();
        var components = calendar.DateComponents(requested, refDate, destDate);
        var nanosecond = components[Component.Nanosecond];
        if (components.TryGetValue(Component.Second, out var seconds) && Math.Abs(nanosecond) >= 500_000_000)
        {
            components[Component.Second] = seconds + Math.Sign(nanosecond);
        }

        var largestNonZeroComponent = FirstNonZero(components)?.Component;
        Component? largest = null;
        foreach (var component in SortedAllowedComponents)
        {
            if (!allowedComponents.Contains(component)) continue;
            if (largestNonZeroComponent is null || Option(component) >= Option(largestNonZeroComponent.Value)) largest = component;
        }
        if (largest is null) return null;

        ComponentAndValue secondLargest = new(Component.Nanosecond, nanosecond);
        foreach (var component in SortedAllowedComponents)
        {
            if (Option(component) < Option(largest.Value))
            {
                secondLargest = new ComponentAndValue(component, components.TryGetValue(component, out var v) ? v : 0);
                break;
            }
        }

        var roundedValue = components.TryGetValue(largest.Value, out var largestValue) ? largestValue : 0;
        var rangeCount = RangeCount(secondLargest.Component, largest.Value);
        if (rangeCount is not null)
        {
            var v = secondLargest.Value;
            if (Math.Abs(v) * 2 >= rangeCount.Value) roundedValue += v > 0 ? 1 : -1;
        }

        var shiftedDate = calendar.DateByAdding(largest.Value, checked(-roundedValue), destDate);
        if (shiftedDate is null) return null;

        // :422-424 re-calculate in case rounding made the next larger component non-zero
        var newRoundedLargest = FirstNonZero(calendar.DateComponents(allowedComponents, shiftedDate.Value, destDate));
        if (newRoundedLargest is not null && newRoundedLargest.Value.Component != largest.Value) return newRoundedLargest;
        return new ComponentAndValue(largest.Value, roundedValue);
    }

    // swift-foundation Calendar_ICU.swift:464-510 range(of:in:for:) for the pairs the rounding reaches, through
    // easyMinMaxRange (:270-282): hour 0..<24, minute 0..<60, second 0..<60, nanosecond 0..<1_000_000_000.
    private static long? RangeCount(Component smaller, Component larger)
    {
        if (larger == Component.Nanosecond) return null;
        return smaller switch
        {
            Component.Hour => larger is Component.Second or Component.Minute or Component.Hour ? null : 24,
            Component.Minute => larger is Component.Second or Component.Minute ? null : 60,
            Component.Second => larger is Component.Second ? null : 60,
            Component.Nanosecond => 1_000_000_000,
            _ => null, // not reachable: the second-largest component is always smaller than an hour
        };
    }

    // An ICU UCalendar standing for Calendar.autoupdatingCurrent (swift-foundation Calendar_ICU.swift).
    internal sealed class IcuCalendar : IDisposable
    {
        private IntPtr handle;

        private IcuCalendar(IntPtr handle) => this.handle = handle;

        public static IcuCalendar Open(string zoneIdentifier, string localeIdentifier)
        {
            var status = 0;
            var handle = IcuFormatting.Native.ucal_open(zoneIdentifier, zoneIdentifier.Length, localeIdentifier, 0, ref status);
            if (status > 0 || handle == IntPtr.Zero) throw new InvalidOperationException($"ucal_open failed with ICU error {status.ToString(CultureInfo.InvariantCulture)}.");
            return new IcuCalendar(handle);
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) IcuFormatting.Native.ucal_close(handle);
            handle = IntPtr.Zero;
        }

        private static int Field(Component component) => component switch
        {
            Component.Year => IcuFormatting.UcalYear,
            Component.Month => IcuFormatting.UcalMonth,
            Component.WeekOfMonth => IcuFormatting.UcalWeekOfMonth,
            Component.Day => IcuFormatting.UcalDate,
            Component.Hour => IcuFormatting.UcalHourOfDay,
            Component.Minute => IcuFormatting.UcalMinute,
            Component.Second => IcuFormatting.UcalSecond,
            Component.Nanosecond => throw new ArgumentOutOfRangeException(nameof(component)),
        };

        // swift-foundation Calendar_ICU.swift:1330-1390 dateComponents(_:from:to:)
        public Dictionary<Component, long> DateComponents(IEnumerable<Component> requested, DateTimeOffset start, DateTimeOffset end)
        {
            var set = requested.ToHashSet();
            var status = 0;
            IcuFormatting.Native.ucal_clear(handle);
            var curr = IcuFormatting.UDate(start);
            var currX = Math.Floor(curr);
            var diff = curr - currX;
            curr = currX;
            var goal = IcuFormatting.UDate(end) - diff;
            IcuFormatting.Native.ucal_setMillis(handle, curr, ref status);
            var result = new Dictionary<Component, long>();
            foreach (var component in SortedAllowedComponents)
            {
                if (!set.Contains(component)) continue;
                result[component] = IcuFormatting.Native.ucal_getFieldDifference(handle, goal, Field(component), ref status);
            }
            if (set.Contains(Component.Nanosecond))
            {
                var curr0 = IcuFormatting.Native.ucal_getMillis(handle, ref status);
                var tmp = Math.Floor((goal - curr0) * 1.0e+6);
                result[Component.Nanosecond] = tmp >= int.MaxValue ? int.MaxValue : tmp <= int.MinValue ? int.MinValue : (long)tmp;
            }
            IcuFormatting.Check(status, "ucal_getFieldDifference");
            return result;
        }

        // Calendar.dateInterval(of:start:interval:for:) for day, weekOfMonth, month and year: the start of the unit
        // that holds the date (the week starts on the calendar's firstWeekday, from its locale) and the length to the
        // start of the next one (swift-foundation Calendar_ICU.swift:1522-1700).
        public (DateTimeOffset Start, double Length)? DateInterval(Component component, DateTimeOffset date)
        {
            var status = 0;
            IcuFormatting.Native.ucal_setMillis(handle, Math.Floor(IcuFormatting.UDate(date)), ref status);
            switch (component)
            {
                case Component.Year:
                    IcuFormatting.Native.ucal_set(handle, IcuFormatting.UcalDayOfYear, 1);
                    break;
                case Component.Month:
                    IcuFormatting.Native.ucal_set(handle, IcuFormatting.UcalDate, 1);
                    break;
                case Component.WeekOfMonth:
                case Component.Day:
                    break;
                default:
                    return null;
            }
            IcuFormatting.Native.ucal_set(handle, IcuFormatting.UcalHourOfDay, 0);
            IcuFormatting.Native.ucal_set(handle, IcuFormatting.UcalMinute, 0);
            IcuFormatting.Native.ucal_set(handle, IcuFormatting.UcalSecond, 0);
            IcuFormatting.Native.ucal_set(handle, IcuFormatting.UcalMillisecond, 0);
            if (component == Component.WeekOfMonth)
            {
                var dayOfWeek = IcuFormatting.Native.ucal_get(handle, IcuFormatting.UcalDayOfWeek, ref status);
                var firstDay = IcuFormatting.Native.ucal_getAttribute(handle, IcuFormatting.UcalFirstDayOfWeek);
                IcuFormatting.Native.ucal_add(handle, IcuFormatting.UcalDate, -((dayOfWeek - firstDay + 7) % 7), ref status);
            }
            var start = IcuFormatting.Native.ucal_getMillis(handle, ref status);
            var (field, amount) = component switch
            {
                Component.Year => (IcuFormatting.UcalYear, 1),
                Component.Month => (IcuFormatting.UcalMonth, 1),
                Component.WeekOfMonth => (IcuFormatting.UcalDate, 7),
                _ => (IcuFormatting.UcalDate, 1),
            };
            IcuFormatting.Native.ucal_add(handle, field, amount, ref status);
            var next = IcuFormatting.Native.ucal_getMillis(handle, ref status);
            if (status > 0) return null;
            return (IcuFormatting.FromUDate(start), (next - start) / 1000.0);
        }

        // Calendar.date(byAdding:value:to:) with wrappingComponents false: ucal_add, the sub-millisecond part kept.
        public DateTimeOffset? DateByAdding(Component component, long value, DateTimeOffset date)
        {
            var status = 0;
            var udate = IcuFormatting.UDate(date);
            var whole = Math.Floor(udate);
            IcuFormatting.Native.ucal_setMillis(handle, whole, ref status);
            IcuFormatting.Native.ucal_add(handle, Field(component), checked((int)value), ref status);
            var result = IcuFormatting.Native.ucal_getMillis(handle, ref status);
            if (status > 0) return null;
            return IcuFormatting.FromUDate(result + (udate - whole));
        }
    }
}

/// <summary>The icu.dll calls behind <see cref="SwiftFormatters"/> (unversioned C exports, Windows 10 1903 and later).</summary>
internal static class IcuFormatting
{
    private const int BufferOverflow = 15; // U_BUFFER_OVERFLOW_ERROR

    internal const int UnumDecimal = 1;          // UNUM_DECIMAL
    internal const int UnumSpellout = 5;         // UNUM_SPELLOUT
    private const int UnumMaxFractionDigits = 6; // UNUM_MAX_FRACTION_DIGITS
    private const int UnumMinFractionDigits = 7; // UNUM_MIN_FRACTION_DIGITS
    private const int UnumRoundingMode = 11;     // UNUM_ROUNDING_MODE
    private const int UnumRoundHalfEven = 4;     // UNUM_ROUND_HALFEVEN

    internal const int UcalYear = 1, UcalMonth = 2, UcalWeekOfMonth = 4, UcalDate = 5, UcalDayOfYear = 6, UcalDayOfWeek = 7,
        UcalHourOfDay = 11, UcalMinute = 12, UcalSecond = 13, UcalMillisecond = 14;
    internal const int UcalFirstDayOfWeek = 1; // UCAL_FIRST_DAY_OF_WEEK

    private const int UdatPattern = -2;                          // UDAT_PATTERN
    private const int UdatpgMatchAllFieldsLength = (1 << 16) - 1; // UDATPG_MATCH_ALL_FIELDS_LENGTH
    private const int UdispctxCapitalizationNone = 1 << 8;       // UDISPCTX_CAPITALIZATION_NONE, FormatStyleCapitalizationContext .unknown
    private const int UdatStyleLong = 0;                         // UDAT_STYLE_LONG, unitsStyle .wide
    private const int UlistfmtTypeUnits = 2;                     // ULISTFMT_TYPE_UNITS (AND=0, OR=1, UNITS=2)
    private const int UlistfmtWidthNarrow = 2;                   // ULISTFMT_WIDTH_NARROW (WIDE=0, SHORT=1, NARROW=2)


    internal static void Check(int status, string call)
    {
        if (status > 0) throw new InvalidOperationException($"{call} failed with ICU error {status.ToString(CultureInfo.InvariantCulture)}.");
    }

    internal static double UDate(DateTimeOffset date) => SwiftDate.Seconds1970(date) * 1000.0;

    internal static DateTimeOffset FromUDate(double udate) => SwiftDate.From1970Seconds(udate / 1000.0);

    // TimeZone.identifier is an IANA id; a Windows zone id converts to its IANA twin.
    internal static string ZoneIdentifier(TimeZoneInfo zone)
    {
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
    }

    // ---- Locale ids ------------------------------------------------------------------------------------------

    private delegate int CharWriter(byte[] buffer, int capacity, ref int status);

    private delegate int UCharWriter(char[] buffer, int capacity, ref int status);

    private static string? ReadChars(CharWriter write)
    {
        var buffer = new byte[157]; // ULOC_FULLNAME_CAPACITY
        var status = 0;
        var length = write(buffer, buffer.Length, ref status);
        if (status == BufferOverflow)
        {
            buffer = new byte[length + 1];
            status = 0;
            length = write(buffer, buffer.Length, ref status);
        }
        if (status > 0) return null;
        return Encoding.ASCII.GetString(buffer, 0, length);
    }

    private static string? ReadUChars(UCharWriter write)
    {
        var buffer = new char[128];
        var status = 0;
        var length = write(buffer, buffer.Length, ref status);
        if (status == BufferOverflow)
        {
            buffer = new char[length + 1];
            status = 0;
            length = write(buffer, buffer.Length, ref status);
        }
        if (status > 0) return null;
        return new string(buffer, 0, length);
    }

    internal static string ForLanguageTag(string tag) =>
        ReadChars((byte[] b, int c, ref int s) => { var parsed = 0; return Native.uloc_forLanguageTag(tag, b, c, ref parsed, ref s); }) ?? "";

    internal static string? AddLikelySubtags(string id) => ReadChars((byte[] b, int c, ref int s) => Native.uloc_addLikelySubtags(id, b, c, ref s));

    internal static string Language(string id) => ReadChars((byte[] b, int c, ref int s) => Native.uloc_getLanguage(id, b, c, ref s)) ?? "";

    internal static string Script(string id) => ReadChars((byte[] b, int c, ref int s) => Native.uloc_getScript(id, b, c, ref s)) ?? "";

    internal static string Region(string id) => ReadChars((byte[] b, int c, ref int s) => Native.uloc_getCountry(id, b, c, ref s)) ?? "";

    internal static string Variant(string id) => ReadChars((byte[] b, int c, ref int s) => Native.uloc_getVariant(id, b, c, ref s)) ?? "";

    /// <summary>The "@key=value;..." part of an ICU id, or "".</summary>
    internal static string Keywords(string id)
    {
        var at = id.IndexOf('@', StringComparison.Ordinal);
        return at < 0 ? "" : id[at..];
    }

    internal static string? KeywordValue(string id, string key)
    {
        var value = ReadChars((byte[] b, int c, ref int s) => Native.uloc_getKeywordValue(id, key, b, c, ref s));
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>uloc_setKeywordValue; an empty value removes the keyword.</summary>
    internal static string WithKeyword(string id, string key, string value)
    {
        var buffer = new byte[157 + 64];
        var source = Encoding.ASCII.GetBytes(id);
        if (source.Length >= buffer.Length) return id;
        Array.Copy(source, buffer, source.Length);
        var status = 0;
        var length = Native.uloc_setKeywordValue(key, value, buffer, buffer.Length, ref status);
        if (status > 0) return id;
        return Encoding.ASCII.GetString(buffer, 0, length);
    }

    /// <summary>Locale.Components.icuIdentifier from its parts: language_Script_REGION_VARIANT@keywords.</summary>
    internal static string Compose(string language, string script, string region, string variant, string keywords)
    {
        var id = new StringBuilder(language);
        if (script.Length != 0) id.Append('_').Append(script);
        if (region.Length != 0 || variant.Length != 0) id.Append('_').Append(region);
        if (variant.Length != 0) id.Append('_').Append(variant);
        id.Append(keywords);
        return id.ToString();
    }

    // ---- Numbering systems (swift-foundation Locale+Components_ICU.swift:565-660) -------------------------------

    private static string? NumberingSystemName(string localeIdentifier)
    {
        var status = 0;
        var system = Native.unumsys_open(localeIdentifier, ref status);
        try
        {
            if (status > 0 || system == IntPtr.Zero) return null;
            return Marshal.PtrToStringAnsi(Native.unumsys_getName(system));
        }
        finally
        {
            if (system != IntPtr.Zero) Native.unumsys_close(system);
        }
    }

    // Locale+Components_ICU.swift:651-660 NumberingSystem(localeIdentifier:), else :569-578 defaultNumberingSystem, else latn
    internal static string NumberingSystemForLocaleIdentifier(string localeID)
    {
        if (KeywordValue(localeID, "numbers") is not null)
        {
            return NumberingSystemName(localeID) ?? "latn";
        }
        return NumberingSystemName(WithKeyword(localeID, "numbers", "default")) ?? "latn";
    }

    // Locale+Components_ICU.swift:580-632 validNumberingSystems(for:)
    internal static List<string> ValidNumberingSystems(string localeId)
    {
        var result = new List<string>();
        var numbers = KeywordValue(localeId, "numbers");
        if (numbers is not null) result.Add(numbers);
        var language = Language(localeId);
        string[] queryList = language is "th" or "zh" or "wuu" or "yue"
            ? ["default"]
            : ["default", "native", "traditional", "finance"];
        foreach (var q in queryList)
        {
            var status = 0;
            var system = Native.unumsys_open(WithKeyword(localeId, "numbers", q), ref status);
            try
            {
                if (status > 0 || system == IntPtr.Zero) continue;
                if (Native.unumsys_isAlgorithmic(system) != 0 || Native.unumsys_getRadix(system) != 10) continue;
                var name = Marshal.PtrToStringAnsi(Native.unumsys_getName(system));
                if (name is null) continue;
                if (!result.Contains(name)) result.Add(name);
            }
            finally
            {
                if (system != IntPtr.Zero) Native.unumsys_close(system);
            }
        }
        if (!result.Contains("latn")) result.Add("latn");
        return result;
    }

    // ---- Dates -------------------------------------------------------------------------------------------------

    private static readonly ConcurrentDictionary<(string Locale, string Skeleton), string?> Patterns = new();

    // swift-foundation ICUPatternGenerator.swift:47-58 _patternForSkeleton
    internal static string? BestPattern(string localeIdentifier, string skeleton) =>
        Patterns.GetOrAdd((localeIdentifier, skeleton), static key =>
        {
            var status = 0;
            var generator = Native.udatpg_open(key.Locale, ref status);
            try
            {
                if (status > 0 || generator == IntPtr.Zero) return null;
                return ReadUChars((char[] b, int c, ref int s) =>
                    Native.udatpg_getBestPatternWithOptions(generator, key.Skeleton, key.Skeleton.Length, UdatpgMatchAllFieldsLength, b, c, ref s));
            }
            finally
            {
                if (generator != IntPtr.Zero) Native.udatpg_close(generator);
            }
        });

    // swift-foundation ICUPatternGenerator.swift:60-75 defaultHourCycle
    internal static string? DefaultHourCycle(string localeIdentifier) =>
        HourCycles.GetOrAdd(localeIdentifier, OpenDefaultHourCycle);

    private static readonly ConcurrentDictionary<string, string?> HourCycles = new(StringComparer.Ordinal);

    private static string? OpenDefaultHourCycle(string localeIdentifier)
    {
        var status = 0;
        var generator = Native.udatpg_open(localeIdentifier, ref status);
        try
        {
            if (status > 0 || generator == IntPtr.Zero) return null;
            var cycle = Native.udatpg_getDefaultHourCycle(generator, ref status);
            if (status > 0) return "h23";
            return cycle switch { 0 => "h11", 1 => "h12", 2 => "h23", 3 => "h24", _ => "h23" };
        }
        finally
        {
            if (generator != IntPtr.Zero) Native.udatpg_close(generator);
        }
    }

    // swift-foundation ICUDateFormatter.swift:38-60 (udat_open with UDAT_PATTERN, udat_setContext), :105-112 format
    internal static string? FormatDate(string localeIdentifier, string zoneIdentifier, string pattern, DateTimeOffset date)
    {
        var status = 0;
        var format = Native.udat_open(UdatPattern, UdatPattern, localeIdentifier, zoneIdentifier, zoneIdentifier.Length, pattern, pattern.Length, ref status);
        try
        {
            if (status > 0 || format == IntPtr.Zero) return null;
            Native.udat_setContext(format, UdispctxCapitalizationNone, ref status);
            var udate = UDate(date);
            return ReadUChars((char[] b, int c, ref int s) => Native.udat_format(format, udate, b, c, IntPtr.Zero, ref s));
        }
        finally
        {
            if (format != IntPtr.Zero) Native.udat_close(format);
        }
    }

    // swift-foundation ICURelativeDateFormatter.swift:40-80: ureldatefmt_open with no number format, UDAT_STYLE_LONG,
    // capitalization none; .named is ureldatefmt_format.
    internal static string? FormatRelative(string localeIdentifier, long value, SwiftFormatters.Component component)
    {
        var unit = component switch
        {
            SwiftFormatters.Component.Year => 0,        // UDAT_REL_UNIT_YEAR
            SwiftFormatters.Component.Month => 2,       // UDAT_REL_UNIT_MONTH
            SwiftFormatters.Component.WeekOfMonth => 3, // UDAT_REL_UNIT_WEEK
            SwiftFormatters.Component.Day => 4,         // UDAT_REL_UNIT_DAY
            SwiftFormatters.Component.Hour => 5,        // UDAT_REL_UNIT_HOUR
            SwiftFormatters.Component.Minute => 6,      // UDAT_REL_UNIT_MINUTE
            SwiftFormatters.Component.Second => 7,      // UDAT_REL_UNIT_SECOND
            SwiftFormatters.Component.Nanosecond => -1,
        };
        if (unit < 0) return null;
        var status = 0;
        var formatter = Native.ureldatefmt_open(localeIdentifier, IntPtr.Zero, UdatStyleLong, UdispctxCapitalizationNone, ref status);
        try
        {
            if (status > 0 || formatter == IntPtr.Zero) return null;
            return ReadUChars((char[] b, int c, ref int s) => Native.ureldatefmt_format(formatter, value, unit, b, c, ref s));
        }
        finally
        {
            if (formatter != IntPtr.Zero) Native.ureldatefmt_close(formatter);
        }
    }

    // ---- Lists (Duration.UnitsFormatStyle's list join) ----------------------------------------------------------

    // swift-foundation Duration+UnitsFormatStyle.swift :369-419 _getFullListPattern/_getListPattern: the CLDR
    // listPattern/unit-narrow entries Apple reads through uatmufmt_getListPattern, which Windows ICU does not export.
    // ulistfmt_openForType(locale, ULISTFMT_TYPE_UNITS, ULISTFMT_WIDTH_NARROW) reads the same CLDR list-pattern data
    // directly, and ICU itself picks the right pattern for the item count (there are never more than 2 items here,
    // since SwiftFormatters.DurationUnits' maximumUnitCount is always 2).
    internal static string? FormatUnitList(string localeIdentifier, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return "";
        if (items.Count == 1) return items[0];

        var handles = new IntPtr[items.Count];
        var lengths = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            handles[i] = Marshal.StringToHGlobalUni(items[i]);
            lengths[i] = items[i].Length;
        }
        try
        {
            var status = 0;
            var formatter = Native.ulistfmt_openForType(localeIdentifier, UlistfmtTypeUnits, UlistfmtWidthNarrow, ref status);
            try
            {
                if (status > 0 || formatter == IntPtr.Zero) return null;
                return ReadUChars((char[] b, int c, ref int s) => Native.ulistfmt_format(formatter, handles, lengths, items.Count, b, c, ref s));
            }
            finally
            {
                if (formatter != IntPtr.Zero) Native.ulistfmt_close(formatter);
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero) Marshal.FreeHGlobal(handle);
            }
        }
    }

    // ---- Numbers -----------------------------------------------------------------------------------------------

    // The number skeleton API swift-foundation's ICUNumberFormatter uses (unumf_openForSkeletonAndLocale).
    private static string? FormatSkeletonCore(string skeleton, string locale, Func<IntPtr, IntPtr, int> format)
    {
        var status = 0;
        var formatter = Native.unumf_openForSkeletonAndLocale(skeleton, skeleton.Length, locale, ref status);
        var result = IntPtr.Zero;
        try
        {
            if (status > 0 || formatter == IntPtr.Zero) return null;
            result = Native.unumf_openResult(ref status);
            if (status > 0 || result == IntPtr.Zero) return null;
            status = format(formatter, result);
            if (status > 0) return null;
            var formatted = result;
            return ReadUChars((char[] b, int c, ref int s) => Native.unumf_resultToString(formatted, b, c, ref s));
        }
        finally
        {
            if (result != IntPtr.Zero) Native.unumf_closeResult(result);
            if (formatter != IntPtr.Zero) Native.unumf_close(formatter);
        }
    }

    internal static string? FormatSkeleton(string skeleton, string locale, long value) =>
        FormatSkeletonCore(skeleton, locale, (f, r) => { var s = 0; Native.unumf_formatInt(f, value, r, ref s); return s; });

    internal static string? FormatSkeleton(string skeleton, string locale, double value) =>
        FormatSkeletonCore(skeleton, locale, (f, r) => { var s = 0; Native.unumf_formatDouble(f, value, r, ref s); return s; });

    // NumberFormatter (CFNumberFormatter over unum): a style in the locale.
    internal static string? FormatWithStyle(int style, string locale, long value)
    {
        var status = 0;
        var format = Native.unum_open(style, null, 0, locale, IntPtr.Zero, ref status);
        try
        {
            if (status > 0 || format == IntPtr.Zero) return null;
            return ReadUChars((char[] b, int c, ref int s) => Native.unum_formatInt64(format, value, b, c, IntPtr.Zero, ref s));
        }
        finally
        {
            if (format != IntPtr.Zero) Native.unum_close(format);
        }
    }

    // NumberFormatter numberStyle .decimal with fraction digits, rounding half even (the NumberFormatter default).
    internal static string? FormatDecimal(string locale, double value, int minimumFractionDigits, int maximumFractionDigits)
    {
        var status = 0;
        var format = Native.unum_open(UnumDecimal, null, 0, locale, IntPtr.Zero, ref status);
        try
        {
            if (status > 0 || format == IntPtr.Zero) return null;
            Native.unum_setAttribute(format, UnumRoundingMode, UnumRoundHalfEven);
            Native.unum_setAttribute(format, UnumMaxFractionDigits, maximumFractionDigits);
            Native.unum_setAttribute(format, UnumMinFractionDigits, minimumFractionDigits);
            return ReadUChars((char[] b, int c, ref int s) => Native.unum_formatDouble(format, value, b, c, IntPtr.Zero, ref s));
        }
        finally
        {
            if (format != IntPtr.Zero) Native.unum_close(format);
        }
    }

    // ---- P/Invoke ----------------------------------------------------------------------------------------------

#pragma warning disable IDE1006 // ICU's own names
    internal static class Native
    {
        private const string Library = "icu.dll";

        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int uloc_forLanguageTag([MarshalAs(UnmanagedType.LPStr)] string tag, [Out] byte[] buffer, int capacity, ref int parsed, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int uloc_addLikelySubtags([MarshalAs(UnmanagedType.LPStr)] string id, [Out] byte[] buffer, int capacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int uloc_getLanguage([MarshalAs(UnmanagedType.LPStr)] string id, [Out] byte[] buffer, int capacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int uloc_getScript([MarshalAs(UnmanagedType.LPStr)] string id, [Out] byte[] buffer, int capacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int uloc_getCountry([MarshalAs(UnmanagedType.LPStr)] string id, [Out] byte[] buffer, int capacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int uloc_getVariant([MarshalAs(UnmanagedType.LPStr)] string id, [Out] byte[] buffer, int capacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int uloc_getKeywordValue([MarshalAs(UnmanagedType.LPStr)] string id, [MarshalAs(UnmanagedType.LPStr)] string key, [Out] byte[] buffer, int capacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int uloc_setKeywordValue([MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value, [In, Out] byte[] buffer, int capacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr unumsys_open([MarshalAs(UnmanagedType.LPStr)] string locale, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void unumsys_close(IntPtr system);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr unumsys_getName(IntPtr system);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern sbyte unumsys_isAlgorithmic(IntPtr system);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int unumsys_getRadix(IntPtr system);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr udatpg_open([MarshalAs(UnmanagedType.LPStr)] string locale, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void udatpg_close(IntPtr generator);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int udatpg_getBestPatternWithOptions(IntPtr generator, string skeleton, int length, int options, [Out] char[] buffer, int capacity, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int udatpg_getDefaultHourCycle(IntPtr generator, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr udat_open(int timeStyle, int dateStyle, [MarshalAs(UnmanagedType.LPStr)] string locale, string zone, int zoneLength, string pattern, int patternLength, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void udat_close(IntPtr format);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void udat_setContext(IntPtr format, int value, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int udat_format(IntPtr format, double date, [Out] char[] buffer, int capacity, IntPtr position, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr ucal_open(string zone, int zoneLength, [MarshalAs(UnmanagedType.LPStr)] string locale, int type, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void ucal_close(IntPtr calendar);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void ucal_clear(IntPtr calendar);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void ucal_setMillis(IntPtr calendar, double date, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern double ucal_getMillis(IntPtr calendar, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void ucal_set(IntPtr calendar, int field, int value);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int ucal_get(IntPtr calendar, int field, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void ucal_add(IntPtr calendar, int field, int amount, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int ucal_getAttribute(IntPtr calendar, int attribute);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int ucal_getFieldDifference(IntPtr calendar, double target, int field, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr ureldatefmt_open([MarshalAs(UnmanagedType.LPStr)] string locale, IntPtr numberFormat, int width, int context, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void ureldatefmt_close(IntPtr formatter);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int ureldatefmt_format(IntPtr formatter, double offset, int unit, [Out] char[] buffer, int capacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr ulistfmt_openForType([MarshalAs(UnmanagedType.LPStr)] string locale, int type, int width, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void ulistfmt_close(IntPtr listfmt);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int ulistfmt_format(IntPtr listfmt, [In] IntPtr[] strings, [In] int[] stringLengths, int stringCount, [Out] char[] result, int resultCapacity, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr unumf_openForSkeletonAndLocale(string skeleton, int length, [MarshalAs(UnmanagedType.LPStr)] string locale, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr unumf_openResult(ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void unumf_formatInt(IntPtr formatter, long value, IntPtr result, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void unumf_formatDouble(IntPtr formatter, double value, IntPtr result, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int unumf_resultToString(IntPtr result, [Out] char[] buffer, int capacity, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void unumf_close(IntPtr formatter);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void unumf_closeResult(IntPtr result);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr unum_open(int style, string? pattern, int patternLength, [MarshalAs(UnmanagedType.LPStr)] string locale, IntPtr parseError, ref int status);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void unum_close(IntPtr format);
        [DllImport(Library)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void unum_setAttribute(IntPtr format, int attribute, int value);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int unum_formatInt64(IntPtr format, long value, [Out] char[] buffer, int capacity, IntPtr position, ref int status);
        [DllImport(Library, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int unum_formatDouble(IntPtr format, double value, [Out] char[] buffer, int capacity, IntPtr position, ref int status);
    }
#pragma warning restore IDE1006
}
