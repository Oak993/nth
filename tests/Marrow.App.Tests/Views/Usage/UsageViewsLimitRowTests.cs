using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), :141-192 LimitRow: the title row (title, reset time,
// percentage), the optional detail line and the bar, in a VStack(alignment: .leading, spacing: 8).
[Collection("Headless")]
public sealed class UsageViewsLimitRowTests(HeadlessSession headless)
{
    private static LimitRow ShowRow(UsageViewsRig rig, PlanLimits.Window window, double width = 400) =>
        rig.Show(new LimitRow(window), width);

    private static StackPanel Column(LimitRow row) => Assert.IsType<StackPanel>(row.Child);

    private static Grid Heading(LimitRow row) => Assert.IsType<Grid>(Column(row).Children[0]);

    private static TextBlock Title(LimitRow row, string title) => Text(Heading(row), title);

    private static TextBlock Percentage(LimitRow row) => All<TextBlock>(Heading(row)).Last();

    private static TextBlock? Reset(LimitRow row) => All<TextBlock>(Heading(row)).FirstOrDefault(block => block.Text!.StartsWith("Resets ", StringComparison.Ordinal));

    private static string Plain(string? text) => (text ?? "").Replace(' ', ' ').Replace(' ', ' ');

    // ------------------------------------------------------------------ UI-PROVREG-013, UI-USAGE-011 the title

    [Fact]
    [Trait("Req", "UI-PROVREG-013")] // :145-151 Text(verbatim: window.title).font(.system(.body)).foregroundStyle(Chrome.primaryText).lineLimit(1)
    [Trait("Req", "UI-USAGE-011")] // ED-059 .body is 13, Chrome.primaryText
    public void The_title_is_the_readers_text_in_body_type_and_the_primary_colour_at_the_leading_edge() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Weekly · Fable", 40));

        var title = Title(row, "Weekly · Fable");
        AssertStyle(title, Body);
        AssertColor(title, Chrome.PrimaryText);
        Assert.Equal(0, BoundsIn(title, row).X); // the leading item of the row
        Assert.Equal(0, BoundsIn(title, row).Y); // the first line of the VStack
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-013")] // :150 .lineLimit(1)
    [Trait("Req", "UI-USAGE-011")] // "one line only; a long title truncates with an ellipsis"
    public void A_long_title_stays_on_one_line_and_truncates_with_an_ellipsis() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var shortRow = ShowRow(rig, Meter("a", "Daily", 40, resetsAt: SwiftDate.Now.AddHours(1)), width: 220);
        var oneLine = Title(shortRow, "Daily").Bounds.Height;

        var longTitle = "An extraordinarily long weekly limit title that cannot possibly fit on a single line of this row";
        var longRow = ShowRow(rig, Meter("b", longTitle, 40, resetsAt: SwiftDate.Now.AddHours(1)), width: 220);
        var title = Title(longRow, longTitle);

        Assert.Equal(oneLine, title.Bounds.Height); // not wrapped onto a second line
        Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
        Assert.Equal(1, title.MaxLines);
        Assert.True(BoundsIn(title, longRow).Right <= BoundsIn(Reset(longRow)!, longRow).Left); // squeezed to make room, not grown over the reset text
    });

    // ------------------------------------------------------------------ UI-PROVREG-014, UI-USAGE-012 the reset text

    [Fact]
    [Trait("Req", "UI-PROVREG-014")] // :152-157 "Resets \(Self.resetText(resetsAt))", .font(.system(.callout)), secondaryText, lineLimit(1)
    [Trait("Req", "UI-USAGE-012")] // ED-059 .callout is 12
    public void The_reset_text_is_callout_type_in_the_secondary_colour_between_title_and_percentage() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "5-hour limit", 40, resetsAt: SwiftDate.Now.AddSeconds(3 * 3600 + 34 * 60 + 10)));

        var reset = Reset(row)!;
        Assert.Equal("Resets in 3 hr 34 min", reset.Text);
        AssertStyle(reset, Callout);
        AssertColor(reset, Chrome.SecondaryText);
        Assert.Equal(1, reset.MaxLines);
        var title = BoundsIn(Title(row, "5-hour limit"), row);
        var percentage = BoundsIn(Percentage(row), row);
        Assert.True(title.Right < BoundsIn(reset, row).Left);
        Assert.True(BoundsIn(reset, row).Right < percentage.Left);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-014")] // :152 if let resetsAt = window.resetsAt
    [Trait("Req", "UI-USAGE-012")] // "absent when the window carries no reset date"
    public void No_reset_date_means_no_reset_text() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Extra usage", 40));

        Assert.Null(Reset(row));
        Assert.Equal(["Extra usage", "40%"], Texts(Heading(row)));
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-014")] // :180-191 the four relative forms; the row adds "Resets " and one space
    [Trait("Req", "UI-USAGE-012")] // "A past date reads Resets in 1 min, never in 0 min."
    public void The_reset_text_is_relative_within_a_day_a_weekday_within_six_and_a_date_beyond() => headless.OnUiThread(() =>
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        using var rig = new UsageViewsRig();
        var now = SwiftDate.Now;

        var us = CultureInfo.GetCultureInfo("en-US");
        string ResetFor(DateTimeOffset date) => Reset(ShowRow(rig, Meter("w", "T", 1, resetsAt: date)))!.Text!;
        string Weekday(DateTimeOffset date) => us.DateTimeFormat.GetAbbreviatedDayName(date.UtcDateTime.DayOfWeek) + " " + date.UtcDateTime.ToString("h:mm tt", us);

        Assert.Equal("Resets in 1 min", ResetFor(now.AddSeconds(-5))); // a past date: never "in 0 min"
        Assert.Equal("Resets in 1 min", ResetFor(now.AddSeconds(20))); // under a minute
        Assert.Equal("Resets in 5 min", ResetFor(now.AddMinutes(5).AddSeconds(59)));
        Assert.Equal("Resets in 1 hr 0 min", ResetFor(now.AddHours(1)));
        Assert.Equal("Resets in 3 hr 34 min", ResetFor(now.AddSeconds(3 * 3600 + 34 * 60 + 59)));
        Assert.Equal("Resets in 23 hr 59 min", ResetFor(now.AddSeconds(86_399)));

        var tomorrow = now.AddSeconds(86_400); // exactly a day: no longer "in ..."
        Assert.Equal("Resets " + Weekday(tomorrow), Plain(ResetFor(tomorrow)));
        var later = now.AddDays(2).AddHours(3).AddMinutes(45);
        Assert.Equal("Resets " + Weekday(later), Plain(ResetFor(later)));
        var lastWeekday = now.AddSeconds(6 * 86_400 - 1);
        Assert.Equal("Resets " + Weekday(lastWeekday), Plain(ResetFor(lastWeekday)));

        var sixDays = now.AddSeconds(6 * 86_400); // six days and beyond: abbreviated month and day
        Assert.Equal("Resets " + sixDays.UtcDateTime.ToString("MMM d", us), ResetFor(sixDays));
        var tenDays = now.AddDays(10);
        Assert.Equal("Resets " + tenDays.UtcDateTime.ToString("MMM d", us), ResetFor(tenDays));
    });

    // ------------------------------------------------------------------ UI-PROVREG-015, UI-USAGE-013 the percentage

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(0.4, "0%")]
    [InlineData(0.5, "1%")]
    [InlineData(2.5, "3%")] // rounded half away from zero, as Swift's .rounded() does
    [InlineData(99.4, "99%")]
    [InlineData(99.6, "100%")]
    [InlineData(140, "140%")] // unclamped
    [InlineData(-3.6, "-4%")] // a negative reported percent prints as a negative number
    [Trait("Req", "UI-PROVREG-015")] // :158-161 "\(Int(window.percent.rounded()))%"
    [Trait("Req", "UI-USAGE-013")]
    public void The_percentage_is_the_rounded_value_with_no_space_before_the_sign(double percent, string expected) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Daily", percent));

        Assert.Equal(expected, Percentage(row).Text);
    });

    [Theory]
    [InlineData(10)]
    [InlineData(80)] // the bar turns warning from 75
    [InlineData(95)] // and danger from 90: the number never does
    [Trait("Req", "UI-PROVREG-015")] // :158-161 .font(.system(.callout)).foregroundStyle(Chrome.secondaryText).monospacedDigit()
    [Trait("Req", "UI-USAGE-013")] // "the percentage never takes the warning or danger colour"
    public void The_percentage_is_callout_type_with_monospaced_digits_and_always_the_secondary_colour(double percent) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Daily", percent));

        var text = Percentage(row);
        AssertStyle(text, Callout, monospacedDigit: true);
        AssertColor(text, Chrome.SecondaryText);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-015")] // :146-162 the trailing element of the title row
    [Trait("Req", "UI-USAGE-013")] // "Trailing item of the row."
    public void The_percentage_ends_the_title_row() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var withReset = ShowRow(rig, Meter("w", "Daily", 12, resetsAt: SwiftDate.Now.AddHours(2)), width: 300);
        Assert.Equal(300, BoundsIn(Percentage(withReset), withReset).Right);

        var without = ShowRow(rig, Meter("w", "Daily", 12), width: 300);
        Assert.Equal(300, BoundsIn(Percentage(without), without).Right);
    });

    // ------------------------------------------------------------------ the title row's spacing (HStack(spacing: 12), Spacer(minLength: 8))

    [Fact]
    [Trait("Req", "UI-PROVREG-013")] // :146 HStack(spacing: 12) { title, Spacer(minLength: 8), reset, percentage }
    [Trait("Req", "UI-PROVREG-014")] // each neighbour 12 away: title, 12, Spacer(8), 12, reset, 12, percentage
    [Trait("Req", "UI-USAGE-012")] // "Between the title's Spacer(minLength: 8) and the percentage."
    public void The_title_row_keeps_12_point_steps_around_a_spacer_of_at_least_8() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Weekly · all models", 64, resetsAt: SwiftDate.Now.AddHours(5)));

        var heading = Heading(row);
        var title = Title(row, "Weekly · all models");
        var reset = Reset(row)!;
        var percentage = Percentage(row);
        heading.Measure(Size.Infinity);

        // Content-sized: title, 12 + 8 + 12 to the reset text, 12 to the percentage.
        var titles = NaturalWidth(title.Text!, SwiftFont.Body);
        var resets = NaturalWidth(reset.Text!, SwiftFont.Callout);
        var percents = NaturalWidth(percentage.Text!, SwiftFont.Callout.MonospacedDigit());
        Assert.Equal(titles + 12 + 8 + 12 + resets + 12 + percents, heading.DesiredSize.Width, 1);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-014")] // :153-156 .lineLimit(1) on the reset text; it is the title that gives way
    [Trait("Req", "UI-USAGE-012")] // "one line, never truncated away before the title is"
    public void When_the_row_is_too_narrow_the_title_gives_way_and_the_reset_text_stays_whole() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var title = "A weekly limit with a rather long name";
        var row = ShowRow(rig, Meter("w", title, 64, resetsAt: SwiftDate.Now.AddSeconds(5 * 3600 + 5)), width: 260);

        var titleBlock = Title(row, title);
        var reset = Reset(row)!;
        Assert.Equal("Resets in 5 hr 0 min", reset.Text);
        Assert.Equal(NaturalWidth(reset.Text!, SwiftFont.Callout), reset.Bounds.Width, 1); // the reset text has all it needs
        Assert.True(titleBlock.Bounds.Width < titleBlock.DesiredSize.Width || BoundsIn(titleBlock, row).Right < BoundsIn(reset, row).Left);
        Assert.True(BoundsIn(reset, row).Left - BoundsIn(titleBlock, row).Right >= 32); // the 12 + 8 + 12 the title keeps clear
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-013")] // :146-161 HStack alignment is the SwiftUI default, .center
    [Trait("Req", "UI-PROVREG-014")]
    [Trait("Req", "UI-PROVREG-015")]
    public void The_texts_of_the_title_row_share_one_centre_line() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Daily", 12, resetsAt: SwiftDate.Now.AddHours(2)));

        var title = BoundsIn(Title(row, "Daily"), row).Center.Y;
        Assert.Equal(title, BoundsIn(Reset(row)!, row).Center.Y, 0.25);
        Assert.Equal(title, BoundsIn(Percentage(row), row).Center.Y, 0.25);
    });

    // ------------------------------------------------------------------ PR-USAGE-030, UI-PROVREG-016, UI-USAGE-014 the detail line

    [Fact]
    [Trait("Req", "PR-USAGE-030")] // :163-168 Text(verbatim: detail).font(.system(.subheadline)).foregroundStyle(Chrome.secondaryText).lineLimit(1)
    [Trait("Req", "UI-PROVREG-016")]
    [Trait("Req", "UI-USAGE-014")] // ED-059 .subheadline is 11
    public void A_detail_is_printed_verbatim_in_subheadline_type_and_the_secondary_colour() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Usage limit", 30, detail: "1,234 of 5,000 credits"));

        var detail = Text(row, "1,234 of 5,000 credits");
        AssertStyle(detail, Subheadline);
        AssertColor(detail, Chrome.SecondaryText);
        Assert.Equal(1, detail.MaxLines);
        Assert.Equal(TextTrimming.CharacterEllipsis, detail.TextTrimming);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-030")] // "placed 8 points below the title row (the VStack spacing) and 8 points above the bar"
    [Trait("Req", "UI-PROVREG-016")] // "Between the header HStack and the bar, inside the row VStack with spacing 8."
    [Trait("Req", "UI-USAGE-014")] // "Second child of the row's VStack, 8 points below the title row and 8 above the bar."
    public void The_detail_sits_8_points_under_the_title_row_and_8_above_the_bar() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Usage limit", 30, detail: "12 of 40 calls"));

        var heading = BoundsIn(Heading(row), row);
        var detail = BoundsIn(Text(row, "12 of 40 calls"), row);
        var bar = BoundsIn(Assert.Single(All<UsageBar>(row)), row);
        Assert.Equal(8, detail.Top - heading.Bottom);
        Assert.Equal(8, bar.Top - detail.Bottom);
        Assert.Equal(0, detail.Left);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-030")] // "When detail is nil the line is absent and the bar sits 8 points under the title row."
    [Trait("Req", "UI-PROVREG-016")] // "shown only when the window carries a detail string"
    [Trait("Req", "UI-USAGE-014")] // "absent when the window carries no detail"
    public void Without_a_detail_the_bar_sits_8_points_under_the_title_row() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Usage limit", 30));

        Assert.Equal(["Usage limit", "30%"], Texts(row));
        var heading = BoundsIn(Heading(row), row);
        var bar = BoundsIn(Assert.Single(All<UsageBar>(row)), row);
        Assert.Equal(8, bar.Top - heading.Bottom);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-030")] // :167 .lineLimit(1): a long detail truncates rather than wrapping
    [Trait("Req", "UI-PROVREG-016")]
    [Trait("Req", "UI-USAGE-014")] // "one line, truncating"
    public void A_long_detail_truncates_on_one_line() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var shortRow = ShowRow(rig, Meter("a", "Usage limit", 30, detail: "1 of 2 calls"), width: 200);
        var oneLine = Text(shortRow, "1 of 2 calls").Bounds.Height;

        var longDetail = "1,234,567 of 9,876,543 calls used so far in the current monthly window";
        var longRow = ShowRow(rig, Meter("b", "Usage limit", 30, detail: longDetail), width: 200);

        Assert.Equal(oneLine, Text(longRow, longDetail).Bounds.Height);
        Assert.True(BoundsIn(Text(longRow, longDetail), longRow).Right <= 200);
    });

    // ------------------------------------------------------------------ the row as a whole

    [Fact]
    [Trait("Req", "UI-USAGE-011")] // :144 VStack(alignment: .leading, spacing: 8): title row, optional detail, bar
    [Trait("Req", "UI-PROVREG-013")]
    public void The_row_is_title_row_then_bar_filling_the_width() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = ShowRow(rig, Meter("w", "Daily", 30), width: 320);

        var bar = Assert.Single(All<UsageBar>(row));
        Assert.Equal(2, Column(row).Children.Count);
        Assert.Same(bar, Column(row).Children[1]); // the last child
        Assert.Equal(320, bar.Bounds.Width); // full content width
        Assert.Equal(0, BoundsIn(bar, row).Left);
    });
}
