using System.Globalization;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// Foundation unit F.ICUFormatters (IMPLEMENTING.md 4.3; R-FMT-2 restating ED-034; ED-073; ED-094; ED-096): the
// user-locale formatters Swift reaches through ICU, ported over Windows' System32\icu.dll, and ByteCountFormatter from
// its documented rules. Every non-ASCII character is an eight-digit escape with its name (R-STR-9).
//
// The surface under test is the one ED-096 accepted (port-only, src/Marrow.Core/Port/SwiftFormatters.cs, namespace
// Marrow.Core.Port):
//   SwiftFormatters.CurrentLocale      Ambient<string>, an ICU locale id ("en_US"): Locale.autoupdatingCurrent
//   SwiftFormatters.CurrentTimeZone    Ambient<TimeZoneInfo>: TimeZone.autoupdatingCurrent
//   SwiftFormatters.Formatted(DateTimeOffset, DateFormatStyle[, string locale, TimeZoneInfo zone])
//                                      Date.formatted(_:) and Date.formatted(date:time:)
//   SwiftFormatters.RelativeNamed(DateTimeOffset date[, DateTimeOffset now, string locale, TimeZoneInfo zone])
//                                      Date.formatted(.relative(presentation: .named)); now defaults to SwiftDate.Now
//   SwiftFormatters.SpellOut(long, string locale) -> string?     NumberFormatter .spellOut, string(from:)
//   SwiftFormatters.Formatted(long[, string locale])             Int.formatted()
//   SwiftFormatters.FractionLength(double, int, string locale)   .number.precision(.fractionLength(n)).locale(l)
//   SwiftFormatters.Percent(double, int, string locale)          .percent.precision(.fractionLength(n))
//   SwiftFormatters.Currency(double, string code, string locale) .currency(code:).locale(l)
//   SwiftFormatters.FileByteCount(long, string locale)           ByteCountFormatter, countStyle .file, other defaults
//   DateFormatStyle                    Date.FormatStyle: static DateTime, static Of(DateStyle, TimeStyle), Year(),
//                                      Month(Symbol.Month = Abbreviated), Day(), Weekday(Symbol.Weekday = Abbreviated),
//                                      Hour(), Minute(); Template (the ICU skeleton); nested DateStyle, TimeStyle,
//                                      Symbol.Month, Symbol.Weekday.
//
// Where the expectations come from (R-TEST-4), none of them from a C# implementation:
// - Swift call sites, B2 1fbf81e: SidebarView.swift:676-685, UsageViews.swift:180-191, Components.swift:31,
//   Hydra.swift:981, UsageLimitSignal.swift:42-52, AboutUpdateViews.swift:422, :784, :838, TourPages.swift:15-24,
//   CommandCodeSession.swift:406, ProviderCredits.swift:34-49, DownloadsPopover.swift:33-37 and :74,
//   AttachmentPreview.swift:96-106.
// - swift-foundation (github.com/swiftlang/swift-foundation, main, read 2026-09-23), Sources/FoundationInternationalization:
//   Formatting/Date/DateFormatStyle.swift (formatterTemplate :157-176 appends era, year, quarter, month, week, day,
//   dayOfYear, weekday, dayPeriod, hour, minute, second, fraction, zone in that order; collection(date:) :220-239;
//   collection(time:) :241-256; field defaults: month(_ = .abbreviated), weekday(_ = .abbreviated),
//   hour(_ = .defaultDigits(amPM: .abbreviated)), minute(_ = .defaultDigits));
//   Formatting/Date/DateFieldSymbol.swift (raw values y, M, MMM, MMMM, d, EEE, EEEE, j, m, mm, s, ss, z);
//   ICU/ICUPatternGenerator.swift (udatpg_getBestPatternWithOptions with UDATPG_MATCH_ALL_FIELDS_LENGTH);
//   Formatting/Date/Date+RelativeFormatStyle.swift (format uses Date.now :293-295; the calendar defaults to
//   Calendar.autoupdatingCurrent :260 and every field is allowed :256; _largestNonZeroComponent :431-460 rounds the
//   reference to whole seconds :437, rounds hour, minute and second to the next smaller unit and re-computes after a
//   carry :418-427, and aligns day, week, month and year to the reference's component boundary :341-362);
//   ICURelativeDateFormatter.swift (sortedAllowedComponents :28 includes weekOfMonth; ureldatefmt_format for .named).
// - CLDR (github.com/unicode-org/cldr, release-42 and release-46, common/main): de.xml and fr.xml percentFormat,
//   zh_Hant.xml gregorian availableFormats "hm"/"Bhm" and dayPeriods.
// - Rulings on the questions an earlier attempt reported: CurrentLocale's default is the bundle-matched en_<region>
//   id (ED-132, swift-foundation Locale_ICU.swift:229-279 and :1639-1743); the Windows short-time pattern stands for
//   the macOS 12/24-hour preference, as an hours keyword on that default only (ED-134); ByteCountFormatter words stay
//   English in every locale (ED-133); U+202F before the day period in the en family (ED-135). Their tests carry
//   Oracle O-37 (the default locale) and O-23 (ICU and CLDR text).
// - ByteCountFormatter: swift-corelibs-foundation Tests/Foundation/TestByteCountFormatter.swift test_DefaultValues,
//   test_zeroBytes, test_oneByte and test_isAdaptiveTrue, which pin the Darwin defaults and outputs.
// - ICU and CLDR output text (U+202F before AM/PM since CLDR 42, the medium date-time glue "{1}, {0}", fr grouping
//   U+202F, named relative units, spell-out rules, currency symbols and spacing).
//
// None of this has been checked against a running macOS 26 Foundation (OD-0001), and ED-086 leaves open whether
// swift-foundation answers transfer to it. So every expectation except the two the Swift source states itself
// (TourPages.swift:15-17 "Twenty-six", ProviderCredits.swift:34-36 "$0.43"), the ED-094 calendar and zone choices and
// the .NET negative control carries [Trait("Oracle", "O-23")] (R-TEST-6) until the oracle settles it.
//
// ED-094: Calendar.current is CultureInfo.CurrentCulture.Calendar with the local zone, so every test that formats a
// date pins a Gregorian culture for its own flow (CultureScope.Gregorian) unless it is showing the calendar follow it.
public sealed class SwiftFormattersTests
{
    private const string EnUS = "en_US";
    private const string NarrowNoBreakSpace = "\U0000202F"; // U+202F NARROW NO-BREAK SPACE
    private const string NoBreakSpace = "\U000000A0";       // U+00A0 NO-BREAK SPACE

    // 2021-01-17 16:03:12 in America/Chicago (UTC-6 in January), a Sunday: the instant of swift-foundation's own
    // doc examples in DateFormatStyle.swift.
    private static readonly DateTimeOffset Birthday = new(2021, 1, 17, 22, 3, 12, TimeSpan.Zero);

    // A Tuesday at noon UTC, the reference instant of the relative-date cases.
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static TimeZoneInfo Chicago => TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static TimeZoneInfo Tokyo => TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    // ---- Date.FormatStyle: the skeleton each call site builds ----------------------------------------------------

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-096")]
    [Trait("Oracle", "O-23")]
    public void Each_dateTime_call_site_builds_the_skeleton_swift_foundation_appends_in_field_order()
    {
        // SidebarView.swift:680
        Assert.Equal("EEEE", DateFormatStyle.DateTime.Weekday(DateFormatStyle.Symbol.Weekday.Wide).Template);
        // SidebarView.swift:682
        Assert.Equal("MMMMd", DateFormatStyle.DateTime.Month(DateFormatStyle.Symbol.Month.Wide).Day().Template);
        // SidebarView.swift:684: year is appended before month whatever the call order (formatterTemplate :160-162)
        Assert.Equal("yMMMMd", DateFormatStyle.DateTime.Month(DateFormatStyle.Symbol.Month.Wide).Day().Year().Template);
        // UsageViews.swift:188: weekday before hour; hour() is .defaultDigits(amPM: .abbreviated) "j", minute() "m"
        Assert.Equal("EEEjm", DateFormatStyle.DateTime.Weekday(DateFormatStyle.Symbol.Weekday.Abbreviated).Hour().Minute().Template);
        // UsageViews.swift:190, Components.swift:31
        Assert.Equal("MMMd", DateFormatStyle.DateTime.Month(DateFormatStyle.Symbol.Month.Abbreviated).Day().Template);
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-096")]
    [Trait("Oracle", "O-23")]
    public void Month_and_weekday_default_to_abbreviated_as_in_swift_foundation()
    {
        Assert.Equal("MMM", DateFormatStyle.DateTime.Month().Template);
        Assert.Equal("EEE", DateFormatStyle.DateTime.Weekday().Template);
        Assert.Equal("d", DateFormatStyle.DateTime.Day().Template);
        Assert.Equal("y", DateFormatStyle.DateTime.Year().Template);
        Assert.Equal("j", DateFormatStyle.DateTime.Hour().Template);
        Assert.Equal("m", DateFormatStyle.DateTime.Minute().Template);
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-096")]
    [Trait("Oracle", "O-23")]
    public void Date_and_time_styles_expand_to_the_fields_collection_date_and_time_add()
    {
        // AboutUpdateViews.swift:784: date .long adds day, year and month .wide (collection(date:) :226-233)
        Assert.Equal("yMMMMd", DateFormatStyle.Of(DateFormatStyle.DateStyle.Long, DateFormatStyle.TimeStyle.Omitted).Template);
        // Hydra.swift:981, UsageLimitSignal.swift:45: time .shortened adds hour "j" and minute .twoDigits "mm" (:247-248)
        Assert.Equal("jmm", DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened).Template);
        // UsageLimitSignal.swift:51
        Assert.Equal("yMMMdjmm", DateFormatStyle.Of(DateFormatStyle.DateStyle.Abbreviated, DateFormatStyle.TimeStyle.Shortened).Template);
        // the remaining branches of collection(date:) and collection(time:)
        Assert.Equal("yMd", DateFormatStyle.Of(DateFormatStyle.DateStyle.Numeric, DateFormatStyle.TimeStyle.Omitted).Template);
        Assert.Equal("yMMMMdEEEE", DateFormatStyle.Of(DateFormatStyle.DateStyle.Complete, DateFormatStyle.TimeStyle.Omitted).Template);
        Assert.Equal("jmmss", DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Standard).Template);
        Assert.Equal("jmmssz", DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Complete).Template);
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-096")]
    [Trait("Oracle", "O-23")]
    public void A_style_with_no_fields_falls_back_to_a_numeric_date_and_shortened_time()
    {
        // DateFormatStyle.swift:525-531 leaves _symbols nil when both styles are .omitted, and the symbols getter at
        // :452-457 then returns DateFieldCollection().collection(date: .numeric).collection(time: .shortened):
        // "yMd" + "jmm". Not an empty string.
        Assert.Equal("yMdjmm", DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Omitted).Template);
        // A bare .dateTime is .init() (:1238), so its _symbols are nil as well and it reads the same fallback; a field
        // modifier with a non-nil option starts from an empty collection instead (:691-692), which is why
        // .dateTime.weekday(.wide) is "EEEE" and not "yMdjmmEEEE".
        Assert.Equal("yMdjmm", DateFormatStyle.DateTime.Template);
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    public void Dotnet_long_date_pattern_is_not_the_Swift_long_style()
    {
        // Negative control (R-TEST-4): .NET's "D" for en-US carries the weekday; Swift's .long is yMMMMd, no weekday.
        var dotnet = new DateTime(2021, 1, 17).ToString("D", CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("Sunday, January 17, 2021", dotnet);
    }

    // ---- Date.FormatStyle: the text ICU gives for those skeletons ------------------------------------------------

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-073")]
    [Trait("Oracle", "O-23")]
    public void Sidebar_day_labels_are_the_localized_skeleton_patterns()
    {
        using var culture = CultureScope.Gregorian();
        // SidebarView.swift:680-684
        Assert.Equal("Sunday", SwiftFormatters.Formatted(Birthday, DateFormatStyle.DateTime.Weekday(DateFormatStyle.Symbol.Weekday.Wide), EnUS, Chicago));
        Assert.Equal("January 17", SwiftFormatters.Formatted(Birthday, DateFormatStyle.DateTime.Month(DateFormatStyle.Symbol.Month.Wide).Day(), EnUS, Chicago));
        Assert.Equal("January 17, 2021", SwiftFormatters.Formatted(Birthday, DateFormatStyle.DateTime.Month(DateFormatStyle.Symbol.Month.Wide).Day().Year(), EnUS, Chicago));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    public void Usage_and_component_labels_use_abbreviated_names()
    {
        using var culture = CultureScope.Gregorian();
        // UsageViews.swift:190, Components.swift:31
        Assert.Equal("Jan 17", SwiftFormatters.Formatted(Birthday, DateFormatStyle.DateTime.Month(DateFormatStyle.Symbol.Month.Abbreviated).Day(), EnUS, Chicago));
        // UsageViews.swift:188: en's "Ehm" pattern keeps "h:mm" for skeleton minute "m", because
        // UDATPG_MATCH_ALL_FIELDS_LENGTH leaves a field alone whose length equals the matched skeleton's.
        Assert.Equal("Sun 4:03" + NarrowNoBreakSpace + "PM", SwiftFormatters.Formatted(Birthday, DateFormatStyle.DateTime.Weekday(DateFormatStyle.Symbol.Weekday.Abbreviated).Hour().Minute(), EnUS, Chicago));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    public void Shortened_time_puts_a_narrow_no_break_space_before_the_day_period()
    {
        using var culture = CultureScope.Gregorian();
        // Hydra.swift:981, UsageLimitSignal.swift:45
        var text = SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened), EnUS, Chicago);
        Assert.Equal("4:03" + NarrowNoBreakSpace + "PM", text);
        Assert.Equal(new byte[] { 0x34, 0x3A, 0x30, 0x33, 0xE2, 0x80, 0xAF, 0x50, 0x4D }, Encoding.UTF8.GetBytes(text));
        Assert.DoesNotContain(" ", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    public void Date_and_time_styles_render_as_the_Foundation_presets()
    {
        using var culture = CultureScope.Gregorian();
        // AboutUpdateViews.swift:784 ("Released \(...)")
        Assert.Equal("January 17, 2021", SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Long, DateFormatStyle.TimeStyle.Omitted), EnUS, Chicago));
        // UsageLimitSignal.swift:51: the medium date-time glue "{1}, {0}"
        Assert.Equal("Jan 17, 2021, 4:03" + NarrowNoBreakSpace + "PM", SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Abbreviated, DateFormatStyle.TimeStyle.Shortened), EnUS, Chicago));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    public void The_time_zone_decides_the_day_and_the_hour()
    {
        using var culture = CultureScope.Gregorian();
        // The same instant is Monday 7:03 AM in Tokyo (UTC+9).
        Assert.Equal("Monday", SwiftFormatters.Formatted(Birthday, DateFormatStyle.DateTime.Weekday(DateFormatStyle.Symbol.Weekday.Wide), EnUS, Tokyo));
        Assert.Equal("7:03" + NarrowNoBreakSpace + "AM", SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened), EnUS, Tokyo));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    public void The_hour_skeleton_j_follows_the_locales_hour_cycle()
    {
        using var culture = CultureScope.Gregorian();
        // "j" is 12-hour with a day period in en_US and 24-hour in en_GB.
        Assert.Equal("22:03", SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened), "en_GB", TimeZoneInfo.Utc));
        Assert.Equal("10:03" + NarrowNoBreakSpace + "PM", SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened), EnUS, TimeZoneInfo.Utc));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    public void In_zh_TW_the_shortened_time_uses_the_conversational_hour()
    {
        // DateFormatStyle.swift:106-150 (preferredHour, called by formatterTemplate at :167-168): when the locale's
        // hour cycle shows a day period and locale.language.languageCode == .chinese && locale.region == .taiwan,
        // hour .defaultDigits(amPM: .abbreviated) "j" becomes .conversationalDefaultDigitsWithAbbreviatedAMPM "C"
        // (DateFieldSymbol.swift:707). So time .shortened is skeleton "Cmm" in zh_TW, and stays "jmm" in en_US (the
        // locale-free Template above). The ED-096 Template takes no locale, so the zh_TW skeleton is proven through its
        // text: CLDR zh_Hant maps "C" through TW's first allowed hour format "hB" to "Bh:mm", and "j" through the
        // preferred "h" to "ah:mm". At 22:03 the flexible day period B is evening1 U+665A U+4E0A, where the AM/PM
        // marker a would be U+4E0B U+5348; so a port that always sends "j" writes U+4E0B U+5348 "10:03".
        using var culture = CultureScope.Gregorian();
        var shortened = DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened);

        var text = SwiftFormatters.Formatted(Birthday, shortened, "zh_TW", TimeZoneInfo.Utc);

        // U+665A CJK UNIFIED IDEOGRAPH-665A, U+4E0A CJK UNIFIED IDEOGRAPH-4E0A (evening1), then 10:03
        Assert.Equal("\U0000665A\U00004E0A10:03", text);
        // U+4E0B CJK UNIFIED IDEOGRAPH-4E0B, U+5348 CJK UNIFIED IDEOGRAPH-5348 (pm): what "j" would give
        Assert.DoesNotContain("\U00004E0B\U00005348", text, StringComparison.Ordinal);
        // The same instant in en_US: "j" and "C" both give h:mm a there, and the hour cycle is unchanged.
        Assert.Equal("10:03" + NarrowNoBreakSpace + "PM", SwiftFormatters.Formatted(Birthday, shortened, EnUS, TimeZoneInfo.Utc));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-096")]
    [Trait("Oracle", "O-23")]
    public void Without_a_locale_or_zone_the_date_follows_the_current_ones()
    {
        // Date.FormatStyle defaults to Locale.autoupdatingCurrent and TimeZone.autoupdatingCurrent; the Swift call
        // sites (SidebarView.swift:680-684, AboutUpdateViews.swift:784, Hydra.swift:981) never pass either.
        using var culture = CultureScope.Gregorian();
        using (SwiftFormatters.CurrentLocale.Override(EnUS))
        using (SwiftFormatters.CurrentTimeZone.Override(Chicago))
        {
            Assert.Equal("January 17, 2021", SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Long, DateFormatStyle.TimeStyle.Omitted)));
        }

        using (SwiftFormatters.CurrentLocale.Override("de_DE"))
        using (SwiftFormatters.CurrentTimeZone.Override(Tokyo))
        {
            Assert.Equal("18. Januar 2021", SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Long, DateFormatStyle.TimeStyle.Omitted)));
        }
    }

    // ---- ED-094: Calendar.current and TimeZone.current ---------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-094")]
    public void The_date_calendar_is_the_current_cultures_calendar_even_with_an_explicit_locale()
    {
        // Date.FormatStyle's calendar defaults to Calendar.autoupdatingCurrent whatever locale it is given, and ED-094
        // makes that CultureInfo.CurrentCulture.Calendar. th-TH's calendar is the Thai Buddhist one: 2021 is 2564.
        using var culture = CultureScope.Of("th-TH");
        Assert.IsType<ThaiBuddhistCalendar>(CultureInfo.CurrentCulture.Calendar);

        var text = SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Long, DateFormatStyle.TimeStyle.Omitted), EnUS, Chicago);

        Assert.Contains("2564", text, StringComparison.Ordinal);
        Assert.DoesNotContain("2021", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-094")]
    public void The_same_date_in_a_Gregorian_culture_keeps_the_Gregorian_year()
    {
        // The control for the test above: only the culture changed.
        using var culture = CultureScope.Gregorian();

        var text = SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Long, DateFormatStyle.TimeStyle.Omitted), EnUS, Chicago);

        Assert.Contains("2021", text, StringComparison.Ordinal);
        Assert.DoesNotContain("2564", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-094")]
    public void The_current_time_zone_defaults_to_the_local_zone()
    {
        // TimeZone.autoupdatingCurrent; ED-094 names the local time zone.
        Assert.Equal(TimeZoneInfo.Local.Id, SwiftFormatters.CurrentTimeZone.Current.Id);
    }

    // ---- .relative(presentation: .named) -------------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData(0, "now")]               // no non-zero component: (second, 0), which ureldatefmt names "now"
    [InlineData(-30, "30 seconds ago")]
    [InlineData(-89, "1 minute ago")]    // minute -1, second -29: 29 * 2 < 60, no carry
    [InlineData(-90, "2 minutes ago")]   // minute -1, second -30: 30 * 2 >= 60 carries to -2
    [InlineData(-300, "5 minutes ago")]
    [InlineData(-3600, "1 hour ago")]
    [InlineData(-46800, "13 hours ago")] // 23:00 the day before: hour is the largest component, so it rounds, not aligns
    [InlineData(10800, "in 3 hours")]
    [InlineData(-86400, "yesterday")]    // day, measured from the end of today: -1 day
    [InlineData(-172800, "2 days ago")]
    [InlineData(86400, "tomorrow")]      // day, measured from the start of today: +1 day
    // weekOfMonth is an allowed component (ICURelativeDateFormatter.swift:28; all fields allowed by default,
    // Date+RelativeFormatStyle.swift:256) and is aligned like day (:341-362). en-US weeks start on Sunday, so the
    // week of Now ends Sat 2026-09-26 23:59:59.
    [InlineData(-604800, "last week")]   // Tue 09-15 12:00: 11 days 11:59:59 back from the week end, week -1
    [InlineData(-864000, "2 weeks ago")] // Sat 09-12 12:00: 14 days 11:59:59 back from the week end, week -2
                                         // (unaligned, 10 days would be week -1, "last week")
    [InlineData(-3888000, "last month")] // Sat 08-08 12:00: month -1, aligned to the end of September
    // The re-computation after a carry (:418-427): the rounded value is shifted off destDate and the components are
    // measured again; when a larger component comes out, that one is used.
    [InlineData(-85200, "yesterday")]    // 23h40m: hour -23, minute -40 carries to hour -24, which re-computes as day -1
                                         // (without :422-424, "24 hours ago")
    [InlineData(-3570, "1 hour ago")]    // 59m30s: minute -59, second -30 carries to minute -60, which re-computes as
                                         // hour -1 (without :422-424, "60 minutes ago")
    public void Named_relative_dates_pick_the_largest_component_as_swift_foundation_does(long secondsFromNow, string expected)
    {
        // AboutUpdateViews.swift:838 ("Checked \(...).")
        using var culture = CultureScope.Gregorian();
        Assert.Equal(expected, SwiftFormatters.RelativeNamed(Now.AddSeconds(secondsFromNow), Now, EnUS, TimeZoneInfo.Utc));
    }

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData(-29.5, "30 seconds ago")] // 29.5 rounds away from zero to 30
    [InlineData(-29.4, "29 seconds ago")]
    public void Sub_second_differences_round_to_the_nearest_second_away_from_zero(double secondsFromNow, string expected)
    {
        // Date+RelativeFormatStyle.swift:437: refDate = destDate.addingTimeInterval(refDate.timeIntervalSince(destDate)
        // .rounded(increment: 1.0, rule: .toNearestOrAwayFromZero)), before any component is measured. A port that
        // truncates ticks says "29 seconds ago" for both.
        using var culture = CultureScope.Gregorian();
        Assert.Equal(expected, SwiftFormatters.RelativeNamed(Now.AddSeconds(secondsFromNow), Now, EnUS, TimeZoneInfo.Utc));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-094")]
    [Trait("Oracle", "O-23")]
    public void The_zone_reaches_the_relative_day_alignment()
    {
        // RelativeFormatStyle has no time zone of its own: day alignment uses calendar.dateInterval(of:start:interval:
        // for:) (:349), and the calendar is Calendar.autoupdatingCurrent (:260), whose zone ED-094 makes the local one.
        // date is 2026-09-21 00:00Z.
        using var culture = CultureScope.Gregorian();
        var date = Now.AddHours(-36);

        // UTC: the day of Now ends 09-22 23:59:59, 1 day 23:59:59 after date: day -1.
        Assert.Equal("yesterday", SwiftFormatters.RelativeNamed(date, Now, EnUS, TimeZoneInfo.Utc));
        // America/Chicago (CDT, UTC-5): Now is 09-22 07:00 and date is 09-20 19:00; the day ends 09-22 23:59:59 CDT,
        // 2 days 04:59:59 after date: day -2.
        Assert.Equal("2 days ago", SwiftFormatters.RelativeNamed(date, Now, EnUS, Chicago));

        // The same pair with the zone omitted, through the CurrentTimeZone ambient (the default path).
        using (SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc))
        {
            Assert.Equal("yesterday", SwiftFormatters.RelativeNamed(date, Now, EnUS));
        }

        using (SwiftFormatters.CurrentTimeZone.Override(Chicago))
        {
            Assert.Equal("2 days ago", SwiftFormatters.RelativeNamed(date, Now, EnUS));
        }
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    public void A_year_back_is_named_last_year()
    {
        // year -1, aligned to the end of 2026: from 2026-12-31 23:59:59 back to 2025-09-22 is still year -1.
        using var culture = CultureScope.Gregorian();
        var aYearAgo = new DateTimeOffset(2025, 9, 22, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("last year", SwiftFormatters.RelativeNamed(aYearAgo, Now, EnUS, TimeZoneInfo.Utc));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Rule", "R-TIME-1")]
    [Trait("Oracle", "O-23")]
    public void Named_relative_dates_measure_from_SwiftDate_Now()
    {
        // Date.RelativeFormatStyle.format(_:) uses Date.now (Date+RelativeFormatStyle.swift:293-295).
        using var runtime = new TestRuntime();
        using var culture = CultureScope.Gregorian();
        using var locale = SwiftFormatters.CurrentLocale.Override(EnUS);
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var checkedAt = SwiftDate.AddingTimeInterval(SwiftDate.Now, -300);

        Assert.Equal("5 minutes ago", SwiftFormatters.RelativeNamed(checkedAt));
        runtime.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("10 minutes ago", SwiftFormatters.RelativeNamed(checkedAt));
    }

    // ---- NumberFormatter .spellOut ---------------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-073")]
    public void Spell_out_of_the_theme_count_capitalizes_to_the_word_the_tour_states()
    {
        // TourPages.swift:15-23: en_US spell-out of AppTheme.allCases.count, 26 cases (AppTheme.swift:15-40), then
        // spelled.prefix(1).uppercased() + spelled.dropFirst(); the comment at :15 states the result "Twenty-six".
        var spelled = SwiftFormatters.SpellOut(26, EnUS);
        Assert.NotNull(spelled);
        Assert.Equal("Twenty-six", SwiftString.Uppercased(SwiftString.Prefix(spelled, 1)) + SwiftString.DropFirst(spelled));
    }

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData(0, "zero")]
    [InlineData(21, "twenty-one")]
    [InlineData(25, "twenty-five")]
    [InlineData(26, "twenty-six")]
    [InlineData(100, "one hundred")]
    [InlineData(115, "one hundred fifteen")]
    [InlineData(-3, "minus three")]
    public void Spell_out_follows_the_ICU_en_rules(long value, string expected) =>
        Assert.Equal(expected, SwiftFormatters.SpellOut(value, EnUS));

    // ---- Int.formatted() -------------------------------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData(0L, "en_US", "0")]
    [InlineData(999L, "en_US", "999")]
    [InlineData(12345L, "en_US", "12,345")]
    [InlineData(-1234567L, "en_US", "-1,234,567")]
    [InlineData(12345L, "de_DE", "12.345")]
    [InlineData(12345L, "fr_FR", "12\U0000202F345")] // U+202F NARROW NO-BREAK SPACE, the fr grouping separator
    public void Int_formatted_groups_digits_by_the_locale(long value, string locale, string expected) =>
        // CommandCodeSession.swift:406 ("Compacted context, freeing \(saved.formatted()) tokens.")
        Assert.Equal(expected, SwiftFormatters.Formatted(value, locale));

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-096")]
    [Trait("Oracle", "O-23")]
    public void Int_formatted_without_a_locale_follows_the_current_one()
    {
        using (SwiftFormatters.CurrentLocale.Override(EnUS))
        {
            Assert.Equal("12,345", SwiftFormatters.Formatted(12345L));
        }

        using (SwiftFormatters.CurrentLocale.Override("de_DE"))
        {
            Assert.Equal("12.345", SwiftFormatters.Formatted(12345L));
        }
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-096")]
    public void With_no_override_the_formatters_read_the_ambients_own_default()
    {
        // Int.formatted() (CommandCodeSession.swift:406) uses Locale.autoupdatingCurrent. What that default is derived
        // from on Windows is ED-132 (the bundle-matched en_<region> id, pinned per culture by the O-37 tests below);
        // this test pins only that the omitted locale IS the ambient's current value, whatever the process culture.
        var current = SwiftFormatters.CurrentLocale.Current;
        Assert.False(string.IsNullOrEmpty(current));
        Assert.Equal(SwiftFormatters.Formatted(12345L, current), SwiftFormatters.Formatted(12345L));
    }

    // ---- Double.formatted(.number / .percent / .currency) -----------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData(3.0, "3.00")]
    [InlineData(1234.5, "1,234.50")]
    [InlineData(0.125, "0.12")] // FormatStyle rounds half to even
    [InlineData(0.375, "0.38")]
    public void Fraction_length_two_pads_groups_and_rounds_half_even(double value, string expected) =>
        // ProviderCredits.swift:48 plain(_:)
        Assert.Equal(expected, SwiftFormatters.FractionLength(value, 2, EnUS));

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData(0.0, "0%")]
    [InlineData(0.5, "50%")]
    [InlineData(0.425, "42%")] // 42.5 rounds half to even
    [InlineData(1.0, "100%")]
    public void Percent_with_no_fraction_digits(double value, string expected) =>
        // AboutUpdateViews.swift:422 (accessibility value of the download progress)
        Assert.Equal(expected, SwiftFormatters.Percent(value, 0, EnUS));

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData("de_DE")]
    [InlineData("fr_FR")]
    public void Percent_follows_the_locales_percent_pattern(string locale) =>
        // AboutUpdateViews.swift:422 sets no locale, so the user's applies. CLDR de and fr percentFormat is
        // "#,##0" U+00A0 "%" (common/main/de.xml and fr.xml, release-42 and release-46 alike): a port that appends
        // "%" to the number fails here.
        Assert.Equal("50" + NoBreakSpace + "%", SwiftFormatters.Percent(0.5, 0, locale));

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    public void Currency_in_en_US_writes_the_dollar_amount_the_source_names()
    {
        // ProviderCredits.swift:34-36: pinned to English formatting so it reads "$0.43"; :44 the call.
        Assert.Equal("$0.43", SwiftFormatters.Currency(0.43, "USD", EnUS));
    }

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData(1234.5, "USD", "$1,234.50")]
    [InlineData(-2.0, "USD", "-$2.00")]
    [InlineData(0.43, "EUR", "\U000020AC0.43")]    // U+20AC EURO SIGN
    [InlineData(1234.4, "JPY", "\U000000A51,234")] // U+00A5 YEN SIGN; the yen has no minor digits
    [InlineData(0.43, "XYZ", "XYZ" + NoBreakSpace + "0.43")] // unknown code: the code itself, U+00A0 before a digit
    public void Currency_uses_the_codes_symbol_and_minor_digits(double value, string code, string expected) =>
        // ProviderCredits.swift:44; the caller has already uppercased the code and checked three letters (:40-43).
        Assert.Equal(expected, SwiftFormatters.Currency(value, code, EnUS));

    // ---- ByteCountFormatter, countStyle .file ----------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    [InlineData(0L, "Zero KB")]          // allowsNonnumericFormatting defaults to true (test_zeroBytes)
    [InlineData(1L, "1 byte")]           // test_oneByte
    [InlineData(499L, "499 bytes")]      // test_isAdaptiveTrue from here on
    [InlineData(900L, "900 bytes")]
    [InlineData(999L, "999 bytes")]
    [InlineData(1000L, "1 KB")]          // .file counts in 1000s
    [InlineData(1023L, "1 KB")]
    [InlineData(1024L, "1 KB")]
    [InlineData(11999L, "12 KB")]        // adaptive: no fraction digits for KB
    [InlineData(900000L, "900 KB")]
    [InlineData(12345678L, "12.3 MB")]   // one fraction digit for MB
    [InlineData(123456789L, "123.5 MB")]
    [InlineData(999900000L, "999.9 MB")]
    [InlineData(1234567898L, "1.23 GB")] // two for GB and above
    [InlineData(12345678987L, "12.35 GB")]
    public void File_byte_counts_follow_the_documented_defaults(long bytes, string expected) =>
        // DownloadsPopover.swift:33-37 and :74, AttachmentPreview.swift:96-106 (AttachmentPreview does not skip 0)
        Assert.Equal(expected, SwiftFormatters.FileByteCount(bytes, EnUS));

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-23")]
    public void File_byte_counts_use_the_locales_decimal_separator()
    {
        // The separator follows the locale. The words ("Zero KB", "1 byte", "bytes", "KB", "MB", "GB") stay English
        // in every locale by ED-133 (the O-23 parked choice in IMPLEMENTING.md Appendix B); the tests after this one
        // pin that wording (DownloadsPopover.swift:33-37 and AttachmentPreview.swift:96-100 set no locale).
        Assert.Equal("12,3 MB", SwiftFormatters.FileByteCount(12345678L, "de_DE"));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-133")]
    [Trait("Oracle", "O-23")]
    public void Byte_count_words_stay_English_in_every_locale_and_only_the_number_follows_it()
    {
        // ED-133: the words are Foundation.framework strings in the app's only localization, en; the number is
        // NumberFormatter .decimal in the locale (swift-corelibs-foundation ByteCountFormatter.swift string(fromByteCount:)
        // and formatNumberFor(bytes:unit:), zeroPadsFractionDigits false, isAdaptive true).
        Assert.Equal("Zero KB", SwiftFormatters.FileByteCount(0L, "de_DE"));
        Assert.Equal("1 byte", SwiftFormatters.FileByteCount(1L, "de_DE"));
        Assert.Equal("12,3 MB", SwiftFormatters.FileByteCount(12345678L, "fr_FR"));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-132")]
    [Trait("Decision", "ED-133")]
    [Trait("Oracle", "O-23")]
    [Trait("Oracle", "O-37")]
    public void A_source_site_passes_the_current_locale_so_the_byte_count_number_follows_the_users_region()
    {
        // DownloadsPopover.swift:33-37 and AttachmentPreview.swift:96-100 set no locale, so the port's caller passes
        // SwiftFormatters.CurrentLocale.Current (ED-133), which under de-DE is en_DE (ED-132): decimal comma.
        using var culture = CultureScope.Of("de-DE");
        Assert.Equal("12,3 MB", SwiftFormatters.FileByteCount(12345678L, SwiftFormatters.CurrentLocale.Current));
    }

    // ---- ED-132: the default of CurrentLocale --------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-132")]
    [Trait("Oracle", "O-37")]
    [InlineData("de-DE", "en_DE")]
    [InlineData("fr-FR", "en_FR")]
    [InlineData("en-GB", "en_GB")]
    [InlineData("en-US", "en_US")]
    [InlineData("", "en_001")] // the invariant culture: uloc_forLanguageTag("") is empty, so the fallback en_001 (:274-279)
    public void The_default_locale_keeps_the_users_region_and_takes_the_apps_only_language_en(string culture, string expected)
    {
        // Locale_ICU.swift:229-279 with bundle matching on and Bundle.main.localizations == ["en"] (ED-132):
        // localeIdentifierForCanonicalizedLocalizations (:1712-1743) keeps an en preferred locale and otherwise replaces
        // its language and script (:1657-1708). Not CurrentCulture.Name with '-' replaced by '_' (that gives de_DE).
        using var scope = CultureScope.Of(culture);
        Assert.Equal(expected, SwiftFormatters.CurrentLocale.Current);
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-132")]
    [Trait("Oracle", "O-37")]
    public void Under_de_DE_the_default_locale_formats_German_region_numbers_with_English_words()
    {
        // ED-132 pinned results: en_DE groups with a period, and its long date is day-first in English.
        using var culture = CultureScope.Of("de-DE");
        Assert.Equal("12.345", SwiftFormatters.Formatted(12345L));
        Assert.Equal("17 January 2021", SwiftFormatters.Formatted(Birthday, DateFormatStyle.Of(DateFormatStyle.DateStyle.Long, DateFormatStyle.TimeStyle.Omitted), zone: TimeZoneInfo.Utc));
    }

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-132")]
    [Trait("Oracle", "O-37")]
    [InlineData("fr-FR")]
    [InlineData("")]
    public void Under_fr_FR_and_the_invariant_culture_the_default_locale_groups_with_a_comma(string culture)
    {
        // ED-132 pinned results: en_FR and en_001 both give "12,345".
        using var scope = CultureScope.Of(culture);
        Assert.Equal("12,345", SwiftFormatters.Formatted(12345L));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-094")]
    [Trait("Decision", "ED-132")]
    [Trait("Oracle", "O-23")]
    [Trait("Oracle", "O-37")]
    public void Relative_week_alignment_takes_the_first_weekday_from_the_current_locale_not_the_style_locale()
    {
        // Date+RelativeFormatStyle.swift:260: the style's calendar is Calendar.autoupdatingCurrent, whose locale is
        // Locale.current, so :349 aligns weeks with its firstWeekday; the style locale (en_US here) only words the
        // result. dest is Sun 2026-09-13 12:00Z, 9 days before Now (week -1 unaligned, so the week is aligned).
        // en_US weeks start on Sunday: the week of Now ends Sat 09-26 23:59:59, 13 days 11:59:59 after dest: week -1.
        // en_DE weeks start on Monday: the week of Now ends Sun 09-27 23:59:59, 14 days 11:59:59 after dest: week -2.
        var dest = Now.AddDays(-9);
        using (CultureScope.Gregorian())
        {
            Assert.Equal("last week", SwiftFormatters.RelativeNamed(dest, Now, EnUS, TimeZoneInfo.Utc));
        }

        using (CultureScope.Of("de-DE"))
        {
            Assert.Equal("2 weeks ago", SwiftFormatters.RelativeNamed(dest, Now, EnUS, TimeZoneInfo.Utc));
        }
    }

    // ---- ED-134: the Windows short-time pattern as the 12/24-hour preference --------------------------------------

    private static CultureInfo WithShortTime(string culture, string shortTimePattern)
    {
        var clone = (CultureInfo)CultureInfo.GetCultureInfo(culture).Clone();
        clone.DateTimeFormat.ShortTimePattern = shortTimePattern;
        return clone;
    }

    private static DateFormatStyle Shortened => DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened);

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-134")]
    [Trait("Oracle", "O-37")]
    public void A_24_hour_short_time_in_a_12_hour_region_gives_the_default_locale_24_hours()
    {
        // Locale_Preferences.swift:193-198 (AppleICUForce24HourTime -> h23) reaches the pattern generator through the
        // hours keyword of identifierCapturingPreferences (ICUPatternGenerator.swift:88-94); ED-134 reads it from the
        // user's short-time pattern.
        using var culture = CultureScope.Of(WithShortTime("en-US", "HH:mm"));
        Assert.Equal("22:03", SwiftFormatters.Formatted(Birthday, Shortened, zone: TimeZoneInfo.Utc));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-134")]
    [Trait("Oracle", "O-37")]
    public void An_explicit_locale_or_an_override_carries_no_hour_preference()
    {
        // ED-134: the keyword is appended to the ED-132 default id only; Locale(identifier:) has no preferences.
        using var culture = CultureScope.Of(WithShortTime("en-US", "HH:mm"));
        Assert.Equal("10:03" + NarrowNoBreakSpace + "PM", SwiftFormatters.Formatted(Birthday, Shortened, EnUS, TimeZoneInfo.Utc));
        using (SwiftFormatters.CurrentLocale.Override(EnUS))
        {
            Assert.Equal("10:03" + NarrowNoBreakSpace + "PM", SwiftFormatters.Formatted(Birthday, Shortened, zone: TimeZoneInfo.Utc));
        }
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-134")]
    [Trait("Oracle", "O-37")]
    public void An_unchanged_short_time_pattern_is_no_preference()
    {
        // ED-134: a preference exists only when the first unquoted H or h differs from the culture's own pattern.
        using (CultureScope.Of("en-US"))
        {
            Assert.Equal("10:03" + NarrowNoBreakSpace + "PM", SwiftFormatters.Formatted(Birthday, Shortened, zone: TimeZoneInfo.Utc));
        }

        using (CultureScope.Of("en-GB"))
        {
            Assert.Equal("22:03", SwiftFormatters.Formatted(Birthday, Shortened, zone: TimeZoneInfo.Utc));
        }
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-134")]
    [Trait("Decision", "ED-135")]
    [Trait("Decision", "ED-153")]
    [Trait("Oracle", "O-23")]
    [Trait("Oracle", "O-37")]
    public void A_12_hour_short_time_in_a_24_hour_region_gives_the_default_locale_a_day_period()
    {
        // AppleICUForce12HourTime -> h12 (Locale_Preferences.swift:193-198); ED-134 reads it from "h:mm tt". The day
        // period follows U+202F in the en family (ED-135). en_GB takes lowercase am/pm from en_001 (ED-153).
        using var culture = CultureScope.Of(WithShortTime("en-GB", "h:mm tt"));
        Assert.Equal("10:03" + NarrowNoBreakSpace + "pm", SwiftFormatters.Formatted(Birthday, Shortened, zone: TimeZoneInfo.Utc));
    }

    // ---- ED-135: U+202F before the day period in the en family ------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Decision", "ED-135")]
    [Trait("Oracle", "O-23")]
    public void A_pattern_that_already_has_the_narrow_no_break_space_keeps_exactly_one()
    {
        // ED-135: icu.dll already gives U+202F for en_TW, and the repair is idempotent.
        using var culture = CultureScope.Gregorian();
        var text = SwiftFormatters.Formatted(Birthday, Shortened, "en_TW", TimeZoneInfo.Utc);
        Assert.Equal("10:03" + NarrowNoBreakSpace + "PM", text);
    }
}
