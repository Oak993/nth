using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), :204-308 BankedResetsView (header, status line, rows) and
// :313-371 BankedResetRow. Spending a reset end to end is in UsageViewsBankedResetSpendTests.
[Collection("Headless")]
public sealed class UsageViewsBankedResetsTests(HeadlessSession headless)
{
    // The Codex spend path fails closed offline (no executable), so a click on "Reset now" in these tests starts nothing real.
    private const ProviderKind Provider = ProviderKind.Codex;

    private static BankedResetsView Block(UsageViewsRig rig, PlanLimits.ResetCredits credits, double width = 400) =>
        rig.Show(new BankedResetsView(Provider, credits), width);

    private static StackPanel Content(BankedResetsView block) => Assert.IsType<StackPanel>(block.Child);

    private static StackPanel Header(BankedResetsView block) => Assert.IsType<StackPanel>(Content(block).Children[0]);

    private static BankedResetRow[] Rows(BankedResetsView block) => All<BankedResetRow>(block);

    private static TextBlock RowTitle(BankedResetRow row) => All<TextBlock>(row)[0];

    private static TextBlock RowDetail(BankedResetRow row) => All<TextBlock>(row)[1];

    private static string Plain(string? text) => (text ?? "").Replace(' ', ' ').Replace(' ', ' ');

    private static PlanLimits.ResetCredits Two() =>
        Resets(2, Credit("a", title: "5-hour reset", detail: "Expires in 3 days"), Credit("b", title: "Weekly reset", detail: "Never expires"));

    // ------------------------------------------------------------------ UI-PROVREG-023/024, UI-USAGE-020/021 the header

    [Fact]
    [Trait("Req", "UI-PROVREG-023")] // :228-231 Text(verbatim: "Banked resets").font(.system(.body)).foregroundStyle(Chrome.primaryText)
    [Trait("Req", "UI-USAGE-020")] // ED-059 .body is 13; "Banked resets", lowercase r
    public void The_title_is_body_text_in_the_primary_colour_at_the_top_left_of_the_block() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        var title = Text(Header(block), "Banked resets");
        AssertStyle(title, Body);
        AssertColor(title, Chrome.PrimaryText);
        Assert.Equal(0, BoundsIn(title, block).X);
        Assert.Equal(0, BoundsIn(Header(block), block).Y); // the first row of the VStack(spacing: 0)
    });

    [Theory]
    [InlineData(0, "· None available yet", "Banked resets, None available yet")]
    [InlineData(1, "· 1 available", "Banked resets, 1 available")]
    [InlineData(2, "· 2 available", "Banked resets, 2 available")]
    [InlineData(12, "· 12 available", "Banked resets, 12 available")]
    [Trait("Req", "UI-PROVREG-024")] // :232-235, :285-291 countText: "None available yet" / "1 available" / "\(n) available"
    [Trait("Req", "UI-USAGE-021")] // "The leading U+00B7 and its trailing space belong to this label"
    [Trait("Req", "UI-PROVREG-023")] // :236-237 .accessibilityElement(children: .combine): one line to a screen reader
    [Trait("Req", "UI-USAGE-020")] // "read as for example Banked resets, 2 available"
    public void The_count_follows_the_available_number_and_the_header_reads_as_one_element(long available, string count, string spoken) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Resets(available));

        Assert.Equal(["Banked resets", count], Texts(Header(block)));
        Assert.Equal(spoken, AutomationProperties.GetName(Header(block)));
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-024")] // :232-235 .font(.system(.callout)).foregroundStyle(Chrome.secondaryText)
    [Trait("Req", "UI-USAGE-021")] // ED-059 .callout is 12; "Immediately after the header, 6 points apart."
    [Trait("Req", "UI-PROVREG-023")] // :228 HStack(spacing: 6)
    [Trait("Req", "UI-USAGE-020")] // "HStack with 6 points of spacing"
    public void The_count_is_callout_text_in_the_secondary_colour_6_points_after_the_title() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        var title = BoundsIn(Text(Header(block), "Banked resets"), block);
        var count = Text(Header(block), "· 2 available");
        AssertStyle(count, Callout);
        AssertColor(count, Chrome.SecondaryText);
        Assert.Equal(6, BoundsIn(count, block).Left - title.Right);
    });

    // ------------------------------------------------------------------ PR-USAGE-036, UI-PROVREG-027, UI-USAGE-024 which rows there are

    [Fact]
    [Trait("Req", "PR-USAGE-036")] // :220-224 listed credits first, then placeholders for the unlisted balance
    [Trait("Req", "UI-PROVREG-027")] // "Placeholder rows are synthesised for the difference between availableCount and the listed credits ... appended after the listed ones"
    [Trait("Req", "UI-USAGE-024")] // "The index counts all rows, listed and placeholder together, and is one-based."
    public void Listed_credits_come_first_then_one_placeholder_per_unlisted_reset() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Resets(4, Credit("a", title: "5-hour reset"), Credit("b")));

        var rows = Rows(block);
        Assert.Equal(["5-hour reset", "Reset 2", "Reset 3", "Reset 4"], rows.Select(row => RowTitle(row).Text)); // a titled credit, an untitled one, two placeholders
        Assert.Equal(["No expiry reported", "No expiry reported", "No expiry reported", "No expiry reported"], rows.Select(row => RowDetail(row).Text));
    });

    [Theory]
    [InlineData(1, 3)] // listed more than available: the difference is floored at zero, no listed row is dropped
    [InlineData(3, 3)]
    [InlineData(0, 3)]
    [Trait("Req", "PR-USAGE-036")] // :222 max(0, credits.availableCount - listed.count)
    [Trait("Req", "UI-PROVREG-027")]
    public void A_smaller_available_count_never_removes_listed_rows(long available, int listed) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var credits = Resets(available, [.. Enumerable.Range(1, listed).Select(index => Credit("c" + index, title: "Card " + index))]);
        var block = Block(rig, credits);

        Assert.Equal(["Card 1", "Card 2", "Card 3"], Rows(block).Select(row => RowTitle(row).Text));
        Assert.Equal(available == 0 ? "· None available yet" : available == 1 ? "· 1 available" : "· " + available + " available", Texts(Header(block))[1]);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-036")] // :223 "unlisted-\($0)" keys, zero based, each with a nil credit
    public void Placeholder_rows_are_keyed_unlisted_0_1_and_so_on() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Resets(3, Credit("only")));
        var rows = Rows(block);
        Assert.Equal(3, rows.Length);

        Click(ButtonTitled(rows[1], "Use reset"));
        Assert.Equal(new BankedResetsView.Phase.Confirming("unlisted-0"), PhaseOf(block));
        Click(ButtonTitled(Rows(block)[2], "Use reset"));
        Assert.Equal(new BankedResetsView.Phase.Confirming("unlisted-1"), PhaseOf(block));
        Click(ButtonTitled(Rows(block)[0], "Use reset"));
        Assert.Equal(new BankedResetsView.Phase.Confirming("only"), PhaseOf(block)); // a listed credit is keyed by its own id
    });

    [Theory]
    [InlineData(1, "Reset")]
    [InlineData(2, "Reset 1")]
    [Trait("Req", "UI-PROVREG-027")] // :326 several ? "Reset \(index + 1)" : "Reset"
    [Trait("Req", "UI-USAGE-024")] // "numbered fallback only when there is more than one row"
    public void An_untitled_row_is_Reset_alone_and_Reset_n_among_several(long available, string firstTitle) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Resets(available));

        Assert.Equal(firstTitle, RowTitle(Rows(block)[0]).Text);
        Assert.Equal(available, Rows(block).Length);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-036")] // "availableCount zero with a non-empty credits array still renders the listed rows, and the header still reads None available yet"
    public void Zero_available_with_listed_credits_still_shows_them() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Resets(0, Credit("a", title: "Card A"), Credit("b", title: "Card B")));

        Assert.Equal(["Card A", "Card B"], Rows(block).Select(row => RowTitle(row).Text));
        Assert.Equal("· None available yet", Texts(Header(block))[1]);
    });

    // ------------------------------------------------------------------ UI-PROVREG-028, UI-USAGE-025 the detail / expiry line

    [Fact]
    [Trait("Req", "UI-PROVREG-028")] // :329-343 row.credit?.detail ?? expiryText: the provider's own detail wins, verbatim
    [Trait("Req", "UI-USAGE-025")] // <credit detail>
    public void The_credits_own_detail_is_printed_verbatim_and_wins_over_the_expiry() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Resets(1, Credit("a", title: "Card", detail: "Valid until the end of the month", expiresAt: SwiftDate.Now.AddHours(1))));

        Assert.Equal("Valid until the end of the month", RowDetail(Rows(block)[0]).Text);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-028")] // :339-343 expiryText
    [Trait("Req", "UI-USAGE-025")] // "No expiry reported", "Expired", "Expires <reset text>"; "Expired wins at exactly the current instant."
    public void Without_a_detail_the_line_reports_no_expiry_expired_or_when_it_expires() => headless.OnUiThread(() =>
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        using var rig = new UsageViewsRig();
        var now = SwiftDate.Now;
        var us = CultureInfo.GetCultureInfo("en-US");
        string DetailFor(DateTimeOffset? expiresAt) =>
            RowDetail(Rows(Block(rig, Resets(1, Credit("a", title: "Card", expiresAt: expiresAt))))[0]).Text!;

        Assert.Equal("No expiry reported", DetailFor(null));
        Assert.Equal("Expired", DetailFor(now)); // at exactly the current instant
        Assert.Equal("Expired", DetailFor(now.AddSeconds(-1)));
        Assert.Equal("Expired", DetailFor(now.AddDays(-30)));
        Assert.Equal("Expires in 2 hr 30 min", DetailFor(now.AddSeconds(2 * 3600 + 30 * 60 + 30)));
        Assert.Equal("Expires in 1 min", DetailFor(now.AddSeconds(5))); // the same relative text the limit rows use
        var inThreeDays = now.AddDays(3).AddHours(3).AddMinutes(45);
        Assert.Equal("Expires " + us.DateTimeFormat.GetAbbreviatedDayName(inThreeDays.UtcDateTime.DayOfWeek) + " " + inThreeDays.UtcDateTime.ToString("h:mm tt", us), Plain(DetailFor(inThreeDays)));
        var inTenDays = now.AddDays(10);
        Assert.Equal("Expires " + inTenDays.UtcDateTime.ToString("MMM d", us), DetailFor(inTenDays));
    });

    [Fact]
    [Trait("Req", "UI-USAGE-025")] // "A placeholder row always reads No expiry reported, because its credit is nil."
    [Trait("Req", "UI-PROVREG-028")]
    public void A_placeholder_row_always_reads_no_expiry_reported() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Resets(2));

        Assert.Equal(["No expiry reported", "No expiry reported"], Rows(block).Select(row => RowDetail(row).Text));
    });

    // ------------------------------------------------------------------ the row (UI-PROVREG-027/028, UI-USAGE-024/025, PR-USAGE-045)

    [Fact]
    [Trait("Req", "UI-PROVREG-027")] // :323-328 .font(.system(.body)).foregroundStyle(Chrome.primaryText).lineLimit(1)
    [Trait("Req", "UI-USAGE-024")] // ED-059 .body is 13
    public void The_row_title_is_body_text_in_the_primary_colour_on_one_line() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        var title = RowTitle(Rows(block)[0]);
        Assert.Equal("5-hour reset", title.Text);
        AssertStyle(title, Body);
        AssertColor(title, Chrome.PrimaryText);
        Assert.Equal(1, title.MaxLines);
        Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
        Assert.Equal(0, BoundsIn(title, block).Left);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-028")] // :329-332 .font(.system(.subheadline)).foregroundStyle(Chrome.secondaryText).lineLimit(1)
    [Trait("Req", "UI-USAGE-025")] // ED-059 .subheadline is 11; "Second line of the row's left column, 2 points under the title."
    [Trait("Req", "PR-USAGE-045")] // :322-336 VStack(alignment: .leading, spacing: 2)
    public void The_row_detail_is_subheadline_text_in_the_secondary_colour_2_points_under_the_title() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        var row = Rows(block)[0];
        var detail = RowDetail(row);
        AssertStyle(detail, Subheadline);
        AssertColor(detail, Chrome.SecondaryText);
        Assert.Equal(1, detail.MaxLines);
        Assert.Equal(TextTrimming.CharacterEllipsis, detail.TextTrimming);
        Assert.Equal(2, BoundsIn(detail, block).Top - BoundsIn(RowTitle(row), block).Bottom);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-027")] // :328, :332 lineLimit(1): a long title and a long detail truncate, they do not wrap
    [Trait("Req", "UI-USAGE-024")] // "one line, truncating"
    [Trait("Req", "UI-USAGE-025")] // "one line, truncating"
    public void A_long_title_and_detail_stay_on_one_line_beside_the_button() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var short1 = Rows(Block(rig, Resets(1, Credit("a", title: "Card", detail: "Short"))))[0];
        var oneTitle = RowTitle(short1).Bounds.Height;
        var oneDetail = RowDetail(short1).Bounds.Height;

        var longTitle = "A banked reset with an extraordinarily long name that cannot fit";
        var longDetail = "Expires after a very long and wordy description of exactly when";
        var block = Block(rig, Resets(1, Credit("a", title: longTitle, detail: longDetail)), width: 260);
        var row = Rows(block)[0];

        Assert.Equal(oneTitle, RowTitle(row).Bounds.Height);
        Assert.Equal(oneDetail, RowDetail(row).Bounds.Height);
        var button = Assert.IsAssignableFrom<Control>(ButtonTitled(row, "Use reset").Parent);
        Assert.True(BoundsIn(RowTitle(row), block).Right <= BoundsIn(button, block).Left);
        Assert.Equal(260, BoundsIn(button, block).Right);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-045")] // :240-252, :322-336 rows padded 12 at the top; HStack(alignment: .center, spacing: 12) ... Spacer(minLength: 12) ... control
    [Trait("Req", "UI-PROVREG-027")] // "An HStack with alignment center and spacing 12, carrying padding top 12, holding a VStack ..., a Spacer with minLength 12, and the control."
    public void Rows_are_12_points_apart_and_keep_36_points_between_the_labels_and_the_control() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        var rows = Rows(block);
        Assert.Equal(12, BoundsIn(rows[0], block).Top - BoundsIn(Header(block), block).Bottom);
        Assert.Equal(12, BoundsIn(rows[1], block).Top - BoundsIn(rows[0], block).Bottom);

        var row = rows[0];
        var button = Assert.IsAssignableFrom<Control>(ButtonTitled(row, "Use reset").Parent);
        row.Measure(Size.Infinity);
        var labels = Math.Max(NaturalWidth("5-hour reset", SwiftFont.Body), NaturalWidth("Expires in 3 days", SwiftFont.Subheadline));
        Assert.Equal(labels + 12 + 12 + 12 + button.DesiredSize.Width, row.DesiredSize.Width, 1); // spacing, Spacer(minLength: 12), spacing
        Assert.Equal(BoundsIn(row, block).Center.Y, BoundsIn(button, block).Center.Y, 0.5); // centre aligned
    });

    // ------------------------------------------------------------------ PR-USAGE-047 no status line while at rest

    [Fact]
    [Trait("Req", "PR-USAGE-047")] // :280-282 default: EmptyView() for idle, confirming and applying
    public void Idle_confirming_and_applying_draw_no_status_line_and_the_first_row_is_12_under_the_header() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        foreach (var phase in new BankedResetsView.Phase[]
                 {
                     new BankedResetsView.Phase.Idle(),
                     new BankedResetsView.Phase.Confirming("a"),
                     new BankedResetsView.Phase.Applying("a"),
                 })
        {
            SetPhase(block, phase);
            rig.Settle();
            Assert.Equal(3, Content(block).Children.Count); // the header and the two rows, nothing between
            Assert.Equal(12, BoundsIn(Rows(block)[0], block).Top - BoundsIn(Header(block), block).Bottom);
        }
    });

    // ------------------------------------------------------------------ UI-PROVREG-025, PR-USAGE-047 the outcome line

    [Theory]
    [InlineData(PlanLimits.ResetOutcome.Reset, "Usage was reset.", "checkmark.circle.fill", true)]
    [InlineData(PlanLimits.ResetOutcome.NothingToReset, "There was no active usage to reset.", "info.circle", false)]
    [InlineData(PlanLimits.ResetOutcome.NoCredit, "No banked resets are left.", "info.circle", false)]
    [InlineData(PlanLimits.ResetOutcome.AlreadyRedeemed, "That reset was already used.", "info.circle", false)]
    [Trait("Req", "UI-PROVREG-025")] // :260-273 the outcome line: glyph and message in success, or secondary text
    public void The_outcome_line_is_a_subheadline_glyph_and_message_in_the_success_or_the_secondary_colour(PlanLimits.ResetOutcome outcome, string message, string symbol, bool success) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());
        SetPhase(block, new BankedResetsView.Phase.Done(outcome));
        rig.Settle();

        var status = Assert.IsType<StackPanel>(Content(block).Children[1]); // directly under the header and above the rows
        var glyph = Assert.Single(All<SwiftImage>(status));
        var text = Text(status, message);
        var colour = success ? Chrome.Success : Chrome.SecondaryText;
        Assert.Equal(symbol, glyph.SymbolName);
        Assert.Equal(SwiftFont.Subheadline.FontWeight(SwiftFont.Weight.Semibold), SwiftImage.GetFont(glyph)); // :263 .font(.system(.subheadline, weight: .semibold))
        Assert.Equal(Resolved(colour), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
        AssertStyle(text, Subheadline);
        AssertColor(text, colour);
        Assert.Equal(SymbolEffectKind.Bounce, glyph.DeferredSymbolEffect); // :265 .symbolEffect(.bounce, value: phase)
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-025")] // :260-262 HStack(spacing: 5) ... .padding(.top, 4): directly under the header and above the rows
    public void The_outcome_line_is_4_points_under_the_header_with_5_between_glyph_and_text_and_the_rows_12_below() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());
        SetPhase(block, new BankedResetsView.Phase.Done(PlanLimits.ResetOutcome.Reset));
        rig.Settle();

        var status = BoundsIn(Content(block).Children[1], block);
        Assert.Equal(4, status.Top - BoundsIn(Header(block), block).Bottom);
        var glyph = BoundsIn(Assert.Single(All<SwiftImage>(Content(block).Children[1])), block);
        var text = BoundsIn(Text(block, "Usage was reset."), block);
        Assert.Equal(5, text.Left - glyph.Right);
        Assert.Equal(12, BoundsIn(Rows(block)[0], block).Top - status.Bottom);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-026")] // :274-279 case .failed(let message): Text(verbatim:).font(.system(.subheadline)).foregroundStyle(Chrome.danger).padding(.top, 4)
    [Trait("Req", "PR-USAGE-046")] // "One line of text in the .subheadline text style in Chrome.danger, 4 points under the header ... no prefix, no icon and no full stop added"
    public void A_failure_is_the_error_text_alone_in_subheadline_danger_4_points_under_the_header() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());
        SetPhase(block, new BankedResetsView.Phase.Failed("Z.ai declined the reset"));
        rig.Settle();

        var text = Assert.IsType<TextBlock>(Content(block).Children[1]); // a single Text, no stack and no icon
        Assert.Equal("Z.ai declined the reset", text.Text); // verbatim, no full stop added
        AssertStyle(text, Subheadline);
        AssertColor(text, Chrome.Danger);
        Assert.Equal(4, BoundsIn(text, block).Top - BoundsIn(Header(block), block).Bottom);
        Assert.Equal(12, BoundsIn(Rows(block)[0], block).Top - BoundsIn(text, block).Bottom);
        Assert.Empty(All<SwiftImage>(block));
    });

    // ------------------------------------------------------------------ PR-USAGE-039, UI-PROVREG-029, UI-USAGE-026 Use reset

    [Fact]
    [Trait("Req", "PR-USAGE-039")] // :361-365 Button("Use reset", action: onConfirm).buttonStyle(.glass).controlSize(.small)
    [Trait("Req", "UI-PROVREG-029")] // "The label is exactly Use reset with a lowercase r"; "default state of every row"
    [Trait("Req", "UI-USAGE-026")] // "rest: enabled"; Glass button style
    public void At_rest_every_row_offers_an_enabled_glass_Use_reset_button() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        foreach (var row in Rows(block))
        {
            var button = ButtonTitled(row, "Use reset");
            Assert.True(button.IsEnabled);
            Assert.Equal(GlassShapeKind.Capsule, GlassBehind(button)!.Shape.Kind);
            Assert.Single(Buttons(row));
        }
    });

    [Fact]
    [Trait("Req", "PR-USAGE-039")] // :246 onConfirm: { phase = .confirming(row.id) }: only this row's control swaps
    [Trait("Req", "UI-PROVREG-029")] // "click: moves this row into the confirming state; nothing is sent yet"
    [Trait("Req", "UI-USAGE-026")] // "Nothing is sent to the provider on this click; the confirmation step follows."
    public void Use_reset_moves_the_block_to_confirming_for_that_row_and_sends_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        Click(ButtonTitled(Rows(block)[1], "Use reset"));

        Assert.Equal(new BankedResetsView.Phase.Confirming("b"), PhaseOf(block));
        Assert.Equal(["Use reset"], Buttons(Rows(block)[0]).Select(button => (string?)button.Content));
        Assert.Equal(["Cancel", "Reset now"], Buttons(Rows(block)[1]).Select(b => (string?)b.Content));
        Assert.Empty(rig.Launches);
        Assert.Null(rig.Registry.RedeemingReset);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-039")] // :363 .disabled(isApplying): "not disabled during a confirming phase on another row"
    [Trait("Req", "UI-USAGE-026")]
    public void A_confirming_row_leaves_the_others_Use_reset_enabled() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());

        Click(ButtonTitled(Rows(block)[0], "Use reset"));

        Assert.True(ButtonTitled(Rows(block)[1], "Use reset").IsEnabled);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-039")] // :368-370 isApplying matches the case without comparing ids: every row is disabled
    [Trait("Req", "UI-PROVREG-029")] // "disabled while ANY row in the block is applying, not just this one"
    [Trait("Req", "UI-USAGE-026")] // "disabled while ANY row of the block is in the applying phase, not only this one"
    public void While_any_row_is_applying_every_other_Use_reset_is_disabled() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Resets(3, Credit("a", title: "A"), Credit("b", title: "B"), Credit("c", title: "C")));

        SetPhase(block, new BankedResetsView.Phase.Applying("b"));
        rig.Settle();

        var rows = Rows(block);
        Assert.False(ButtonTitled(rows[0], "Use reset").IsEnabled);
        Assert.False(ButtonTitled(rows[2], "Use reset").IsEnabled);
        Assert.Empty(Buttons(rows[1])); // the row being spent has no button at all
    });

    // ------------------------------------------------------------------ PR-USAGE-040, UI-PROVREG-030/031, UI-USAGE-027/028 Cancel and Reset now

    [Fact]
    [Trait("Req", "PR-USAGE-040")] // :346-352 case .confirming(let id) where id == row.id: HStack(spacing: 6) { Cancel, Reset now }
    [Trait("Req", "UI-PROVREG-030")] // "First element of an HStack with spacing 6."
    [Trait("Req", "UI-PROVREG-031")] // "Second element of the confirm HStack with spacing 6."
    [Trait("Req", "UI-USAGE-027")] // "first of a pair with 6 points of spacing"
    [Trait("Req", "UI-USAGE-028")] // "second of the confirmation pair"
    public void The_confirming_row_shows_Cancel_then_Reset_now_6_points_apart_and_the_others_keep_Use_reset() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());
        Click(ButtonTitled(Rows(block)[0], "Use reset"));

        var confirming = Rows(block)[0];
        Assert.Equal(["Cancel", "Reset now"], Buttons(confirming).Select(button => (string?)button.Content));
        var cancel = BoundsIn(Assert.IsAssignableFrom<Control>(ButtonTitled(confirming, "Cancel").Parent), block);
        var resetNow = BoundsIn(Assert.IsAssignableFrom<Control>(ButtonTitled(confirming, "Reset now").Parent), block);
        Assert.Equal(6, resetNow.Left - cancel.Right);
        Assert.Equal(400, resetNow.Right); // the pair ends the row
        Assert.False(HasButton(confirming, "Use reset"));

        Assert.Equal(["Use reset"], Buttons(Rows(block)[1]).Select(button => (string?)button.Content));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-040")] // :348 Button("Cancel").buttonStyle(.glass), :350 Button("Reset now").buttonStyle(.glassProminent).tint(Chrome.accent)
    [Trait("Req", "UI-PROVREG-030")] // "buttonStyle .glass"
    [Trait("Req", "UI-PROVREG-031")] // "buttonStyle .glassProminent ... tinted with Chrome.accent"
    [Trait("Req", "UI-USAGE-027")] // "Glass button style, untinted."
    [Trait("Req", "UI-USAGE-028")] // "Prominent glass button style tinted Chrome.accent."
    public void Cancel_is_plain_glass_and_Reset_now_is_prominent_glass_tinted_with_the_accent() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());
        Click(ButtonTitled(Rows(block)[0], "Use reset"));

        var cancel = GlassBehind(ButtonTitled(Rows(block)[0], "Cancel"))!;
        var resetNow = GlassBehind(ButtonTitled(Rows(block)[0], "Reset now"))!;
        Assert.Equal(GlassShapeKind.Capsule, cancel.Shape.Kind);
        Assert.Equal(GlassShapeKind.Capsule, resetNow.Shape.Kind);
        Assert.NotEqual(Chrome.Accent, cancel.TintColor); // "untinted": no accent
        Assert.Equal(Chrome.Accent, resetNow.TintColor); // .tint(Chrome.accent)
        Assert.True(ButtonTitled(Rows(block)[0], "Cancel").IsEnabled);
        Assert.True(ButtonTitled(Rows(block)[0], "Reset now").IsEnabled);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-040")] // :247 onCancel: { phase = .idle }: returns the whole block to idle without sending anything
    [Trait("Req", "UI-PROVREG-030")] // "click: returns the whole block to idle, so the confirm pair disappears"
    [Trait("Req", "UI-USAGE-027")] // "click: returns the whole block to idle without sending anything"
    public void Cancel_returns_the_block_to_idle_and_sends_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());
        Click(ButtonTitled(Rows(block)[0], "Use reset"));

        Click(ButtonTitled(Rows(block)[0], "Cancel"));

        Assert.IsType<BankedResetsView.Phase.Idle>(PhaseOf(block));
        Assert.All(Rows(block), row => Assert.Equal(["Use reset"], Buttons(row).Select(button => (string?)button.Content)));
        Assert.Null(rig.Registry.RedeemingReset);
        Assert.Empty(All<ProgressBar>(block));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-040")] // "Confirming a second row while one is already confirming moves the phase to that row, so the first row silently returns to Use reset."
    [Trait("Req", "UI-USAGE-027")] // "Confirming a different row also abandons this one, because the phase carries a single row id."
    [Trait("Req", "UI-PROVREG-030")] // "Because the phase is held for the whole block, cancelling one row cancels the only confirmation that can exist at a time."
    public void Confirming_a_second_row_returns_the_first_to_Use_reset() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());
        Click(ButtonTitled(Rows(block)[0], "Use reset"));

        Click(ButtonTitled(Rows(block)[1], "Use reset"));

        Assert.Equal(new BankedResetsView.Phase.Confirming("b"), PhaseOf(block));
        Assert.Equal(["Use reset"], Buttons(Rows(block)[0]).Select(button => (string?)button.Content));
        Assert.Equal(["Cancel", "Reset now"], Buttons(Rows(block)[1]).Select(button => (string?)button.Content));
    });

    // ------------------------------------------------------------------ UI-PROVREG-032, UI-USAGE-029 Resetting...

    [Fact]
    [Trait("Req", "UI-PROVREG-032")] // :353-360 HStack(spacing: 6) { ProgressView().controlSize(.mini), Text("Resetting…").font(.system(.callout)).foregroundStyle(Chrome.secondaryText) }
    [Trait("Req", "UI-USAGE-029")] // "a mini spinner then the label"; "The ellipsis is the single character U+2026"
    public void The_row_being_spent_shows_a_spinner_and_Resetting_6_points_apart_in_place_of_its_button() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, Two());
        SetPhase(block, new BankedResetsView.Phase.Applying("a"));
        rig.Settle();

        var spent = Rows(block)[0];
        var label = Text(spent, "Resetting…");
        Assert.EndsWith("…", label.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("...", label.Text, StringComparison.Ordinal);
        AssertStyle(label, Callout);
        AssertColor(label, Chrome.SecondaryText);
        var spinner = Assert.Single(Spinners(spent));
        Assert.True(spinner.IsIndeterminate);
        Assert.Equal(6, BoundsIn(label, block).Left - BoundsIn(spinner, block).Right);
        Assert.Equal(400, BoundsIn(label, block).Right); // trailing end of the row
        Assert.Empty(Buttons(spent));

        Assert.Empty(Spinners(Rows(block)[1])); // present only on the row being spent
        Assert.DoesNotContain("Resetting…", Texts(Rows(block)[1]));
    });

    // ------------------------------------------------------------------ PR-USAGE-034, UI-PROVREG-022 the block inside the limits

    private static Control[] Stack(PlanLimitsView view) => [.. Assert.IsType<StackPanel>(view.Child).Children];

    private static bool IsRule(Control control) => control is Border { Height: 1 };

    [Fact]
    [Trait("Req", "PR-USAGE-034")] // :30-39 Divider().padding(.top, 14) then BankedResetsView(...).padding(.top, 14), the divider only when there are windows
    [Trait("Req", "UI-PROVREG-022")] // "A plain Divider with padding top 14, drawn only when banked resets are present AND the windows list is non-empty."
    [Trait("Req", "UI-USAGE-020")] // "the block is padded 14 at the top and preceded by a divider when there are windows"
    public void With_windows_the_banked_resets_follow_a_rule_14_below_the_last_row_and_14_above_the_block() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(windows: [Meter("w", "Daily", 12), Meter("v", "Weekly", 40)], resets: Two()));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai));

        var stack = Stack(view);
        var rule = stack.Single(IsRule);
        var rows = All<LimitRow>(view);
        var block = All<BankedResetsView>(view).Single();
        Control[] expected = [.. rows, rule, block];
        Assert.Equal(expected, stack.Skip(1)); // header, windows, rule, block: nothing between
        Assert.Equal(14, BoundsIn(rule, view).Top - BoundsIn(rows[^1], view).Bottom);
        Assert.Equal(1, rule.Bounds.Height);
        Assert.Equal(14, BoundsIn(block, view).Top - BoundsIn(rule, view).Bottom); // no bottom padding of its own: the block's 14
        Assert.Equal(0, BoundsIn(rule, view).Left); // no leading inset
        Assert.Equal(400, rule.Bounds.Width);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-034")] // "A provider reporting reset credits but zero windows shows the block with no rule above it."
    [Trait("Req", "UI-PROVREG-022")] // "hidden when there are banked resets but no windows"
    public void Without_windows_the_block_has_no_rule_above_it_and_sits_14_under_the_header() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(windows: [], resets: Two()));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai));

        Assert.DoesNotContain(Stack(view), IsRule);
        var block = All<BankedResetsView>(view).Single();
        Assert.Equal(14, BoundsIn(block, view).Top - BoundsIn(Stack(view)[0], view).Bottom);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-034")] // "When resetCredits is nil neither the rule nor the block appears."
    [Trait("Req", "UI-PROVREG-022")] // "hidden when there are no banked resets"
    [Trait("Req", "UI-USAGE-020")] // "present only when the provider reported reset credits"
    public void Without_reset_credits_there_is_neither_a_rule_nor_a_block() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(windows: [Meter("w", "Daily", 12)], resets: null));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai));

        Assert.DoesNotContain(Stack(view), IsRule);
        Assert.Empty(All<BankedResetsView>(view));
        Assert.False(HasText(view, "Banked resets"));

        rig.PublishLimits(ProviderKind.Zai, Limits(windows: [Meter("w", "Daily", 12)], resets: Resets(0)));
        Assert.Single(All<BankedResetsView>(view)); // an empty bank is still a bank: the block, with its none-yet count
        Assert.Contains("· None available yet", Texts(view));
        Assert.Single(Stack(view), IsRule);

        rig.PublishLimits(ProviderKind.Zai, Limits(windows: [Meter("w", "Daily", 12)], resets: null));
        Assert.Empty(All<BankedResetsView>(view));
        Assert.DoesNotContain(Stack(view), IsRule);
    });
}
