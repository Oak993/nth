using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsagePanel.swift (B3 52bc792), :32-46 the context window row and its bar.
[Collection("Headless")]
public sealed class UsagePanelContextRowTests(HeadlessSession headless)
{
    private static ContextUsage Context(long used, long window) => new() { UsedTokens = used, WindowTokens = window };

    // The row's two texts: the literal label, and the value, the only text with the slash in it.
    private static (TextBlock Label, TextBlock Value) Row(UsagePanel panel) =>
        (Text(panel, "Context window"), All<TextBlock>(panel).Single(text => text.Text!.Contains(" / ", StringComparison.Ordinal)));

    private static UsageBar Bar(UsagePanel panel) => All<UsageBar>(panel).Single();

    // :35-43 HStack { Text("Context window").foregroundStyle(Chrome.secondaryText); Spacer(minLength: 12);
    // Text(verbatim: ...).foregroundStyle(Chrome.secondaryText).monospacedDigit() }.font(.system(.body))
    [Fact]
    [Trait("Req", "PR-USAGE-012")] // UsagePanel.swift:35-43 the .body row in secondaryText, digits monospaced on the value only
    [Trait("Req", "UI-USAGE-005")] // UsagePanel.swift:35-43 the label: .body (13 at ED-059), Chrome.secondaryText
    [Trait("Req", "UI-USAGE-006")] // UsagePanel.swift:39-43 the value: .body inherited from the row, monospaced digits, Chrome.secondaryText
    [Trait("Req", "PR-USAGE-111")] // UsagePanel.swift:43 .font(.system(.body)): the 13-point style on the context row
    public void The_row_is_body_text_in_secondary_colour_with_monospaced_digits_on_the_value_only() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(Context(1_500, 4_000), ProviderKind.Cursor));
        var (label, value) = Row(panel);

        Assert.Equal("Context window", label.Text);
        AssertStyle(label, Body);
        AssertStyle(value, Body, monospacedDigit: true);
        AssertColor(label, Chrome.SecondaryText);
        AssertColor(value, Chrome.SecondaryText);
        Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(Avalonia.Styling.ThemeVariant.Light), Resolved(Chrome.SecondaryText)); // ED-062
    });

    // :35-43 HStack, label first, Spacer(minLength: 12), value last; the row is the first child of the VStack, which sits in
    // .padding(16) and a 360-point frame (:68-69).
    [Fact]
    [Trait("Req", "PR-USAGE-012")] // UsagePanel.swift:35-43 label on the left, value right-aligned
    [Trait("Req", "UI-USAGE-005")] // UsagePanel.swift:35-43 "Left item of an HStack"; first child, so at the 16 point content inset
    [Trait("Req", "UI-USAGE-006")] // UsagePanel.swift:35-43 "Trailing item of the context row"
    public void The_label_sits_at_the_content_inset_and_the_value_is_right_aligned_to_the_far_inset() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(Context(1_500, 4_000), ProviderKind.Cursor));
        var (label, value) = Row(panel);

        Assert.Equal(16, BoundsIn(label, panel).Left); // .padding(16)
        Assert.Equal(16, BoundsIn(label, panel).Top); // the first child of the VStack
        Assert.Equal(360 - 16, BoundsIn(value, panel).Right, 0.01); // trailing item of a 360-point frame
        Assert.Equal(BoundsIn(label, panel).Top, BoundsIn(value, panel).Top, 0.01); // one row
    });

    // :38 Spacer(minLength: 12): however tight the row, the label and the value are at least 12 points apart.
    [Theory]
    [InlineData(4)] // squeezed: the texts alone fill all but 4 points of the content width
    [InlineData(12)] // exactly the minimum
    [InlineData(60)] // roomy: the spacer takes the slack
    [Trait("Req", "PR-USAGE-012")] // UsagePanel.swift:38 Spacer(minLength: 12)
    public void The_label_and_value_are_never_closer_than_the_spacers_12_points(double slack) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var labelWidth = NaturalWidth("Context window", SwiftFont.Body);
        var valueWidth = NaturalWidth("1.5k / 4k (38%)", SwiftFont.Body.MonospacedDigit());
        // fillsWidth: the panel takes the window's width (:69-70), so the content is the window less the 16-point insets.
        var panel = rig.Show(new UsagePanel(Context(1_500, 4_000), ProviderKind.Cursor, fillsWidth: true), width: Math.Ceiling(32 + labelWidth + valueWidth + slack));
        var (label, value) = Row(panel);

        var gap = BoundsIn(value, panel).Left - BoundsIn(label, panel).Right;
        if (slack >= 12) Assert.Equal(slack, gap, 1.0); // the spacer is all the room that is left
        else Assert.Equal(12, gap, 0.01); // Spacer(minLength: 12) holds its minimum
    });

    // :39 Text(verbatim: "\(Self.tokens(usage.usedTokens)) / \(Self.tokens(window)) (\(Int((fraction * 100).rounded()))%)")
    [Theory]
    [InlineData(0L, 200_000L, "0 / 200k (0%)")]
    [InlineData(950L, 1_000L, "950 / 1k (95%)")]
    [InlineData(123_456L, 200_000L, "123.5k / 200k (62%)")]
    [InlineData(1_250_000L, 2_000_000L, "1.2M / 2M (63%)")] // :112 "%.1f" of 1.25 is 1.2 (R-FMT-3); 62.5 rounds away from zero to 63
    [InlineData(1_500_000_000L, 2_000_000_000L, "1.5B / 2B (75%)")]
    [InlineData(300_000L, 200_000L, "300k / 200k (100%)")] // fraction is clamped to 1 (Timeline.swift:340) while the used count is not
    [Trait("Req", "PR-USAGE-012")] // UsagePanel.swift:39 "<used> / <window> (<n>%)"
    [Trait("Req", "UI-USAGE-006")] // UsagePanel.swift:39 "one space either side of the slash and one before the opening parenthesis, none inside it"
    public void The_value_reads_used_slash_window_then_the_percentage_in_parentheses(long used, long window, string expected) => headless.OnUiThread(() =>
    {
        var (_, value) = Row(new UsagePanel(Context(used, window), ProviderKind.Cursor));

        Assert.Equal(expected, value.Text);
    });

    // :39 Int((fraction * 100).rounded()): .rounded() is toNearestOrAwayFromZero, not truncation and not banker's rounding.
    [Theory]
    [InlineData(4L, "0%")] // 0.004: below 0.005 still reads 0%
    [InlineData(5L, "1%")] // 0.005 * 100 = 0.5 rounds away from zero to 1 (truncation would say 0)
    [InlineData(125L, "13%")] // 12.5 rounds away from zero to 13 (banker's rounding would say 12)
    [InlineData(25L, "3%")] // 2.5 rounds to 3 (banker's rounding would say 2)
    [InlineData(500L, "50%")]
    [InlineData(994L, "99%")] // 99.4 does not round up
    [InlineData(995L, "100%")] // 99.5 rounds up to 100
    [InlineData(1_000L, "100%")]
    [InlineData(1_500L, "100%")] // already clamped to at most 1: never above 100%
    [Trait("Req", "PR-USAGE-013")] // UsagePanel.swift:39 Int((fraction * 100).rounded())
    public void The_percentage_is_the_fraction_times_100_rounded_half_away_from_zero(long used, string expected) => headless.OnUiThread(() =>
    {
        var (_, value) = Row(new UsagePanel(Context(used, 1_000), ProviderKind.Cursor));

        Assert.EndsWith(" (" + expected + ")", value.Text, StringComparison.Ordinal);
    });

    // :34 `if let usage, let fraction = usage.fraction, let window = usage.windowTokens`; Timeline.swift:339 a window of 0 has no fraction.
    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [Trait("Req", "UI-USAGE-005")] // "absent when the usage has no fraction or no windowTokens"
    [Trait("Req", "UI-USAGE-006")] // "always present when the row is present"
    [Trait("Req", "PR-USAGE-015")] // the bar belongs to the same block: it is absent with the row
    public void Without_a_fraction_neither_the_row_nor_its_bar_is_drawn(long? window) => headless.OnUiThread(() =>
    {
        var panel = new UsagePanel(new ContextUsage { UsedTokens = 1_500, WindowTokens = window }, ProviderKind.Cursor);

        Assert.DoesNotContain(All<TextBlock>(panel), text => text.Text == "Context window");
        Assert.Empty(All<UsageBar>(panel));
    });

    // :44-45 UsageBar(fraction: fraction, tint: Chrome.accent, label: "Context window").padding(.top, 10)
    [Theory]
    [InlineData(0.25)]
    [InlineData(0.8)] // a plan-limit bar turns warning from 75 (UsageViews.swift:175) ...
    [InlineData(0.95)] // ... and danger from 90 (:174): the context bar never does
    [InlineData(1.0)]
    [Trait("Req", "PR-USAGE-015")] // UsagePanel.swift:44 tint: Chrome.accent unconditionally, filled to the raw fraction
    public void The_bar_is_filled_to_the_raw_fraction_in_the_accent_colour_at_any_level(double fraction) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(Context((long)(fraction * 4_000), 4_000), ProviderKind.Cursor));
        var bar = Bar(panel);
        var track = All<Border>(bar)[0];
        var fill = All<Border>(bar)[1];

        Assert.Equal(360 - 32, track.Bounds.Width, 0.01);
        Assert.Equal((360 - 32) * fraction, fill.Bounds.Width, 1.0); // the raw fraction, not a rounded percentage (layout snaps to whole pixels)
        Assert.Equal(Resolved(Chrome.Accent), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(fill.Background).Color);
    });

    // :44-45 .padding(.top, 10): the bar sits 10 points under the row; full content width.
    [Fact]
    [Trait("Req", "PR-USAGE-015")] // UsagePanel.swift:45 "placed 10 points below the row"
    public void The_bar_sits_10_points_under_the_row_across_the_content_width() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(Context(1_500, 4_000), ProviderKind.Cursor));
        var (label, value) = Row(panel);
        var bar = Bar(panel);

        var rowBottom = Math.Max(BoundsIn(label, panel).Bottom, BoundsIn(value, panel).Bottom);
        Assert.Equal(10, BoundsIn(bar, panel).Top - rowBottom, 0.01);
        Assert.Equal(16, BoundsIn(bar, panel).Left);
        Assert.Equal(360 - 32, bar.Bounds.Width, 0.01);
        Assert.Equal(5, bar.Bounds.Height);
    });

    // :44 label: "Context window": what a screen reader says for the bar (UsageViews.swift:393).
    [Fact]
    [Trait("Req", "PR-USAGE-015")] // UsagePanel.swift:44 "the accessibility label exactly \"Context window\""
    public void The_bar_is_labelled_Context_window_for_accessibility() => headless.OnUiThread(() =>
    {
        var panel = new UsagePanel(Context(1_500, 4_000), ProviderKind.Cursor);

        Assert.Equal("Context window", ControlAutomationPeer.CreatePeerForElement(Bar(panel)).GetName());
    });
}
