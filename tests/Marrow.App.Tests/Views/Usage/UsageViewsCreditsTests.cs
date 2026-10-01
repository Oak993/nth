using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), :86-139 CreditsView: "Remaining credits" with its note on
// the left; the spinner, the amount and the Top up link on the right.
[Collection("Headless")]
public sealed class UsageViewsCreditsTests(HeadlessSession headless)
{
    private const ProviderKind Provider = ProviderKind.Deepseek;

    private static StackPanel Left(CreditsView view) => Assert.IsType<StackPanel>(Title(view).Parent);

    private static TextBlock Title(CreditsView view) => Text(view, "Remaining credits");

    private static TextBlock Amount(CreditsView view, string text) => Text(view, text);

    /// <summary>The right-hand group: the panel that holds the Top up link (or the amount).</summary>
    private static StackPanel Right(CreditsView view) => Assert.IsType<StackPanel>(
        Assert.IsType<DockPanel>(view.Child).Children.OfType<StackPanel>().Single(panel => !ReferenceEquals(panel, Left(view))));

    // ------------------------------------------------------------------ PR-PROVREG-212, UI-PROVREG-034 the note

    [Fact]
    [Trait("Req", "PR-PROVREG-212")] // :128-138 no balance, not loading: "<name> did not report a balance."
    [Trait("Req", "UI-PROVREG-034")]
    public void With_no_balance_and_no_read_running_the_note_says_the_provider_did_not_report_one() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var deepseek = rig.Show(new CreditsView(ProviderKind.Deepseek));
        Assert.Equal(["Remaining credits", "DeepSeek did not report a balance."], Texts(Left(deepseek)));

        var commandCode = rig.Show(new CreditsView(ProviderKind.Commandcode));
        Assert.Equal(["Remaining credits", "Command Code did not report a balance."], Texts(Left(commandCode)));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-212")] // :129-130 "While loading with no balance, no note is drawn at all."
    [Trait("Req", "UI-PROVREG-034")] // "no balance and loading: no note at all"
    public void While_the_first_read_is_running_there_is_no_note() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.SetLoadingCredits(Provider, true);
        var view = rig.Show(new CreditsView(Provider));

        Assert.Equal(["Remaining credits"], Texts(Left(view)));

        rig.SetLoadingCredits(Provider, false); // the read ended with nothing
        Assert.Equal(["Remaining credits", "DeepSeek did not report a balance."], Texts(Left(view)));
    });

    [Theory]
    [InlineData(0, true)] // nothing left
    [InlineData(5, false)] // credit that cannot be used is as good as none
    [Trait("Req", "PR-PROVREG-212")] // :132-134 "<name> declines turns until the balance is topped up."
    [Trait("Req", "UI-PROVREG-034")] // "depleted: the declines-turns sentence"
    public void A_depleted_balance_says_the_provider_declines_turns_until_it_is_topped_up(double total, bool usable) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(total, usable, monthly: 3, toppedUp: 2)); // the splits do not matter once depleted
        var view = rig.Show(new CreditsView(Provider));

        Assert.Equal(["Remaining credits", "DeepSeek declines turns until the balance is topped up."], Texts(Left(view)));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-212")] // :135 otherwise the breakdown text, which may itself be nil
    [Trait("Req", "UI-PROVREG-034")] // "otherwise: the breakdown, which itself may be absent, in which case there is no note"
    public void A_balance_with_credit_shows_the_breakdown_or_no_note_when_there_is_none() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(15, monthly: 12, toppedUp: 2.5, granted: 0.5));
        var view = rig.Show(new CreditsView(Provider));
        Assert.Equal(["Remaining credits", "Monthly $12.00 · Topped up $2.50 · Granted $0.50"], Texts(Left(view)));

        rig.PublishCredits(Provider, Balance(15, toppedUp: 15));
        Assert.Equal(["Remaining credits", "Topped up $15.00"], Texts(Left(view)));

        rig.PublishCredits(Provider, Balance(15)); // a balance with no positive splits: no note line
        Assert.Equal(["Remaining credits"], Texts(Left(view)));
        Assert.Single(Left(view).Children);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-034")] // :99-104 .font(.system(.subheadline)).foregroundStyle(Chrome.secondaryText).fixedSize(horizontal: false, vertical: true)
    [Trait("Req", "UI-USAGE-030")] // the column keeps 2 points between its lines
    public void The_note_is_subheadline_text_in_the_secondary_colour_2_points_under_the_title_even_when_depleted() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(0));
        var view = rig.Show(new CreditsView(Provider));

        var note = Text(view, "DeepSeek declines turns until the balance is topped up.");
        AssertStyle(note, Subheadline);
        AssertColor(note, Chrome.SecondaryText); // "only the amount turns red, not the note"
        Assert.Equal(2, BoundsIn(note, view).Top - BoundsIn(Title(view), view).Bottom);
        Assert.Equal(0, BoundsIn(note, view).Left);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-034")] // :103 .fixedSize(horizontal: false, vertical: true): "wraps rather than truncating"
    public void A_long_note_wraps_onto_more_lines_instead_of_truncating() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(15, monthly: 12, toppedUp: 2.5, granted: 0.5));
        var wide = rig.Show(new CreditsView(Provider), width: 600);
        var note = Text(wide, "Monthly $12.00 · Topped up $2.50 · Granted $0.50");
        var oneLine = note.Bounds.Height;

        var narrow = rig.Show(new CreditsView(Provider), width: 200);
        var wrapped = Text(narrow, "Monthly $12.00 · Topped up $2.50 · Granted $0.50");
        Assert.True(wrapped.Bounds.Height >= oneLine * 1.9); // two lines or more, none cut off
        Assert.Equal(TextWrapping.Wrap, wrapped.TextWrapping);
    });

    // ------------------------------------------------------------------ PR-USAGE-052, UI-PROVREG-033, UI-USAGE-030 the row

    [Fact]
    [Trait("Req", "PR-USAGE-052")] // :94-98 Text(verbatim: "Remaining credits").font(.system(.body)).foregroundStyle(Chrome.primaryText)
    [Trait("Req", "UI-PROVREG-033")]
    [Trait("Req", "UI-USAGE-030")] // ED-059 .body is 13
    public void The_title_is_body_text_in_the_primary_colour_at_the_leading_edge() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(12.34, monthly: 12.34));
        var view = rig.Show(new CreditsView(Provider));

        var title = Title(view);
        AssertStyle(title, Body);
        AssertColor(title, Chrome.PrimaryText);
        Assert.Equal(0, BoundsIn(title, view).Left);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-052")] // :94 HStack(alignment: .center, spacing: 16), :105 Spacer(minLength: 12), :107 HStack(spacing: 10)
    [Trait("Req", "UI-PROVREG-033")] // "an HStack with alignment center and spacing 16, holding a VStack ..., a Spacer with minLength 12, and a trailing HStack with spacing 10"
    [Trait("Req", "UI-USAGE-030")] // "a centre-aligned HStack with 16 points of spacing"
    public void The_left_column_and_the_right_group_are_16_12_16_apart_at_the_least_and_the_group_ends_the_row() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(12.34, monthly: 12.34, toppedUp: 0));
        rig.SetLoadingCredits(Provider, true);
        var view = rig.Show(new CreditsView(Provider), width: 420);

        var topUp = Assert.IsAssignableFrom<Control>(ButtonTitled(view, "Top up").Parent);
        Assert.Equal(420, BoundsIn(topUp, view).Right); // the trailing item of the row

        view.Measure(Size.Infinity);
        var left = Math.Max(NaturalWidth("Remaining credits", SwiftFont.Body), NaturalWidth("Monthly $12.34", SwiftFont.Subheadline));
        var right = Spinners(view).Single().DesiredSize.Width + 10
            + NaturalWidth("$12.34", SwiftFont.Body.FontWeight(SwiftFont.Weight.Semibold).MonospacedDigit()) + 10
            + topUp.DesiredSize.Width;
        Assert.Equal(left + 16 + 12 + 16 + right, view.DesiredSize.Width, 1);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-052")] // :94 alignment: .center; :107-123 the right group is centred on the row as well
    [Trait("Req", "UI-PROVREG-033")]
    [Trait("Req", "UI-USAGE-030")]
    public void Both_sides_share_the_rows_centre_line() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(12.34)); // no note: the title column is shorter than the Top up button
        var view = rig.Show(new CreditsView(Provider), width: 420);

        var title = BoundsIn(Title(view), view);
        var amount = BoundsIn(Amount(view, "$12.34"), view);
        var topUp = BoundsIn(Assert.IsAssignableFrom<Control>(ButtonTitled(view, "Top up").Parent), view);
        Assert.Equal(topUp.Center.Y, amount.Center.Y, 0.5);
        Assert.Equal(topUp.Center.Y, title.Center.Y, 0.5);

        // With a note the left column is the tall side: the right group is centred against it.
        rig.PublishCredits(Provider, Balance(12.34, monthly: 12.34));
        var row = view.Bounds.Height;
        Assert.True(row > topUp.Height * 1.5);
        Assert.Equal(row / 2, BoundsIn(Amount(view, "$12.34"), view).Center.Y, 0.5);
        Assert.Equal(row / 2, BoundsIn(Assert.IsAssignableFrom<Control>(ButtonTitled(view, "Top up").Parent), view).Center.Y, 0.5);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-052")] // "In order: a mini spinner while loading, the amount when a balance is known, and a Top up link-button"; "All three right-hand items can be present at once."
    [Trait("Req", "UI-PROVREG-033")]
    [Trait("Req", "UI-PROVREG-035")] // "Inside the trailing HStack with spacing 10, after the optional spinner and before the Top up link."
    [Trait("Req", "UI-USAGE-033")] // "First item of the right-hand group, 10 points before the amount."
    public void The_right_group_runs_spinner_amount_top_up_with_10_points_between() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(12.34, monthly: 12.34));
        rig.SetLoadingCredits(Provider, true);
        var view = rig.Show(new CreditsView(Provider), width: 420);

        var spinner = BoundsIn(Spinners(view).Single(), view);
        var amount = BoundsIn(Amount(view, "$12.34"), view);
        var topUp = BoundsIn(Assert.IsAssignableFrom<Control>(ButtonTitled(view, "Top up").Parent), view);
        Assert.Equal(10, amount.Left - spinner.Right);
        Assert.Equal(10, topUp.Left - amount.Right);
        Assert.Equal(amount.Center.Y, spinner.Center.Y, 0.5);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-052")] // "With no balance and no loading, the right-hand group holds only the Top up button."
    [Trait("Req", "UI-PROVREG-035")] // "hidden entirely when there is no balance"
    public void With_no_balance_and_no_read_the_right_group_holds_only_Top_up() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var view = rig.Show(new CreditsView(Provider));

        Assert.Empty(Spinners(view));
        Assert.Equal(["Remaining credits", "DeepSeek did not report a balance."], Texts(view).Where(text => text != "Top up")); // no amount anywhere
        Assert.Equal("Top up", Assert.Single(Buttons(view)).Content);
    });

    // ------------------------------------------------------------------ UI-PROVREG-035 the amount

    [Fact]
    [Trait("Req", "UI-PROVREG-035")] // :111-116 Text(verbatim: credits.amountText).font(.system(.body, weight: .semibold)).monospacedDigit().foregroundStyle(...).lineLimit(1)
    public void The_amount_is_semibold_body_text_with_monospaced_digits_in_the_primary_colour() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(12.34, monthly: 12.34));
        var view = rig.Show(new CreditsView(Provider));

        var amount = Amount(view, "$12.34"); // en_US with the provider's currency
        AssertStyle(amount, Body, FontWeight.SemiBold, monospacedDigit: true);
        AssertColor(amount, Chrome.PrimaryText);
        Assert.Equal(1, amount.MaxLines);
    });

    [Theory]
    [InlineData(0, true, "$0.00")] // nothing left
    [InlineData(7.5, false, "$7.50")] // credit that cannot be used
    [Trait("Req", "UI-PROVREG-035")] // :113 foregroundStyle(credits.isDepleted ? Chrome.danger : Chrome.primaryText)
    public void A_depleted_amount_reads_in_the_danger_colour(double total, bool usable, string text) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(total, usable));
        var view = rig.Show(new CreditsView(Provider));

        AssertColor(Amount(view, text), Chrome.Danger);
    });

    // ------------------------------------------------------------------ PR-USAGE-059, UI-USAGE-033 the spinner

    [Fact]
    [Trait("Req", "PR-USAGE-059")] // :93, :108-110 if isLoading { ProgressView().controlSize(.mini) }
    [Trait("Req", "UI-USAGE-033")] // "visible while the provider is in loadingCredits"
    public void A_spinner_runs_while_the_provider_is_loading_credits_and_the_amount_stays_beside_it() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(12.34));
        var view = rig.Show(new CreditsView(Provider));
        Assert.Empty(Spinners(view));

        rig.SetLoadingCredits(Provider, true);
        var spinner = Assert.Single(Spinners(view));
        Assert.True(spinner.IsIndeterminate);
        Assert.True(HasText(view, "$12.34")); // "A previously read amount stays visible beside it."

        rig.SetLoadingCredits(ProviderKind.Commandcode, true); // another provider's read is not this one's
        rig.SetLoadingCredits(Provider, false);
        Assert.Empty(Spinners(view));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-059")] // "refreshCredits has no watchdog, so a read that never returns leaves this spinner turning for the rest of the run"
    [Trait("Req", "UI-USAGE-033")] // "can remain visible for the rest of the run ...; do not add the plan-limits watchdog here"
    public void The_credits_spinner_has_no_watchdog_of_its_own() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.SetLoadingCredits(Provider, true);
        var view = rig.Show(new CreditsView(Provider));

        rig.Advance(TimeSpan.FromMinutes(10)); // far past the 25 seconds the limits read is held to
        Assert.Single(Spinners(view));
    });

    // ------------------------------------------------------------------ UI-PROVREG-036 the Top up link

    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Commandcode)]
    [Trait("Req", "UI-PROVREG-036")] // :118-122 if let topUpURL = CreditsReader.topUpURL(provider) { Link("Top up", ...).buttonStyle(.glass) }
    [Trait("Req", "PR-USAGE-052")]
    public void Top_up_shows_for_a_provider_with_a_top_up_page_whatever_the_balance(ProviderKind provider) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var none = rig.Show(new CreditsView(provider));
        Assert.True(HasButton(none, "Top up"));

        rig.PublishCredits(provider, Balance(0)); // depleted
        Assert.True(HasButton(none, "Top up"));
        rig.PublishCredits(provider, Balance(50, monthly: 50)); // plenty
        var button = ButtonTitled(none, "Top up");
        Assert.True(button.IsEnabled);
        var glass = GlassBehind(button)!; // buttonStyle(.glass)
        Assert.Equal(GlassShapeKind.Capsule, glass.Shape.Kind);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-036")] // "shown only when the provider has a top-up URL, which is DeepSeek and Command Code"
    public void A_provider_without_a_top_up_page_has_no_link() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(ProviderKind.Claude, Balance(5));
        var view = rig.Show(new CreditsView(ProviderKind.Claude));

        Assert.Empty(Buttons(view));
        Assert.True(HasText(view, "$5.00"));
    });

    // ------------------------------------------------------------------ PR-USAGE-058 the animation

    [Fact]
    [Trait("Req", "PR-USAGE-058")] // :125 .animation(.easeOut(duration: 0.15), value: credits)
    [Trait("Req", "UI-PROVREG-033")] // "animates with easeOut over 0.15 seconds when the balance changes"
    public void A_new_balance_is_applied_with_a_0_15_second_ease_out_and_an_identical_one_with_none() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(12.34, monthly: 12.34));
        var view = rig.Show(new CreditsView(Provider));
        var seen = new List<Animation?>();
        view.PropertyChanged += (_, e) => { if (e.Property == Decorator.ChildProperty) seen.Add(Transaction.Current); };

        rig.PublishCredits(Provider, Balance(9.99, monthly: 9.99)); // a different balance
        Assert.Equal([Animation.EaseOut(duration: 0.15)], seen);
        Assert.True(HasText(view, "$9.99"));

        seen.Clear();
        rig.PublishCredits(Provider, Balance(9.99, monthly: 9.99)); // an identical re-read
        Assert.All(seen, animation => Assert.Null(animation));
        seen.Clear();
        rig.SetLoadingCredits(Provider, true); // a read starting is not a new balance
        Assert.All(seen, animation => Assert.Null(animation));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-058")] // "it has no reduce-motion branch" (unlike Chrome.panelSlide)
    public void The_credits_ease_out_stays_0_15_seconds_with_reduce_motion_on() => headless.OnUiThread(() =>
    {
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        using var rig = new UsageViewsRig();
        rig.PublishCredits(Provider, Balance(12.34, monthly: 12.34));
        var view = rig.Show(new CreditsView(Provider));
        var seen = new List<Animation?>();
        view.PropertyChanged += (_, e) => { if (e.Property == Decorator.ChildProperty) seen.Add(Transaction.Current); };

        rig.PublishCredits(Provider, Balance(9.99, monthly: 9.99));

        Assert.Equal([Animation.EaseOut(duration: 0.15)], seen);
        Assert.NotEqual(Chrome.PanelSlide, seen[0]);
    });
}
