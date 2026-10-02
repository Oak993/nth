using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), :44-76 PlanLimitsView's foot: the provider's problem
// (an API-key note with a warning triangle, or the sign-in prompt with a Login button) and the empty sentence.
[Collection("Headless")]
public sealed class UsageViewsProblemNoteTests(HeadlessSession headless)
{
    private const string Problem = "Your session has expired. Sign in again to read plan limits.";

    private static Control Header(PlanLimitsView view) => Assert.IsType<StackPanel>(view.Child).Children[0];

    private static Control Foot(PlanLimitsView view) => Assert.IsType<StackPanel>(view.Child).Children[^1];

    private static SwiftImage Triangle(Control root) => All<SwiftImage>(root).Single(image => image.SymbolName == "exclamationmark.triangle.fill");

    // ------------------------------------------------------------------ PR-USAGE-051, UI-PROVREG-018, UI-USAGE-019 nothing reported

    [Theory]
    [InlineData(ProviderKind.Claude, "Claude did not report plan limits.")]
    [InlineData(ProviderKind.Zai, "Z.ai did not report plan limits.")]
    [InlineData(ProviderKind.Commandcode, "Command Code did not report plan limits.")]
    [Trait("Req", "PR-USAGE-051")] // :71-76 "\(provider.displayName) did not report plan limits."
    [Trait("Req", "UI-PROVREG-018")]
    [Trait("Req", "UI-USAGE-019")]
    public void With_nothing_stored_and_no_read_running_the_sentence_names_the_provider(ProviderKind provider, string sentence) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var view = rig.Show(new PlanLimitsView(provider));

        Assert.True(HasText(view, sentence));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-051")] // :71 } else if !isLoading { ... }
    [Trait("Req", "UI-PROVREG-018")] // "so it does not flash while a first read is in flight"
    [Trait("Req", "UI-USAGE-019")] // "hidden while loading, leaving header plus spinner alone"
    public void The_sentence_waits_for_loading_to_stop() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.SetLoadingLimits(ProviderKind.Claude, true);
        var view = rig.Show(new PlanLimitsView(ProviderKind.Claude));

        Assert.False(HasText(view, "Claude did not report plan limits."));
        Assert.Equal(["Plan usage limits"], Texts(view)); // the header alone
        Assert.Single(Spinners(view));

        rig.SetLoadingLimits(ProviderKind.Claude, false);
        Assert.True(HasText(view, "Claude did not report plan limits."));
        Assert.Empty(Spinners(view));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-051")] // :71 only when limits is nil: stored limits, even with no windows, are not "nothing"
    [Trait("Req", "UI-USAGE-019")] // "shown only when no limits are stored AND the provider is not loading"
    public void Stored_limits_replace_the_sentence() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var view = rig.Show(new PlanLimitsView(ProviderKind.Claude));
        Assert.True(HasText(view, "Claude did not report plan limits."));

        rig.PublishLimits(ProviderKind.Claude, Limits(windows: []));
        Assert.False(HasText(view, "Claude did not report plan limits."));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-051")] // :73-75 .font(.system(.callout)).foregroundStyle(Chrome.secondaryText).padding(.top, 8)
    [Trait("Req", "PR-USAGE-111")] // UsageViews.swift:73 .font(.system(.callout)): 12 points
    [Trait("Req", "UI-PROVREG-018")] // "Directly under the header with padding top 8, which is smaller than the 14 used for limit rows."
    [Trait("Req", "UI-USAGE-019")] // "Padded 8 at the top, directly under the header."; ED-059 .callout is 12
    public void The_sentence_is_callout_text_in_the_secondary_colour_8_points_under_the_header() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var view = rig.Show(new PlanLimitsView(ProviderKind.Claude));

        var sentence = Text(view, "Claude did not report plan limits.");
        AssertStyle(sentence, Callout);
        AssertColor(sentence, Chrome.SecondaryText);
        Assert.Equal(8, BoundsIn(sentence, view).Top - BoundsIn(Header(view), view).Bottom);
        Assert.Equal(0, BoundsIn(sentence, view).Left);
    });

    // ------------------------------------------------------------------ PR-USAGE-049, UI-PROVREG-019, UI-USAGE-016 API-key providers

    [Theory]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [Trait("Req", "PR-USAGE-049")] // :44-57 if provider.isAPIKeyBased: the warning triangle and the provider's own words
    [Trait("Req", "UI-PROVREG-019")]
    [Trait("Req", "UI-USAGE-016")]
    public void An_API_key_providers_problem_shows_the_triangle_and_its_own_words_with_no_action(ProviderKind provider) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(provider, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(provider));

        var note = Text(view, Problem); // printed verbatim, nothing added
        Assert.Equal(Problem, note.Text);
        Triangle(view);
        Assert.False(HasText(view, "Click here to login again"));
        Assert.Empty(Buttons(view)); // "none; there is no button on this variant"
    });

    [Fact]
    [Trait("Req", "PR-USAGE-049")] // :48-50 Image(systemName: "exclamationmark.triangle.fill").font(.system(.footnote, weight: .semibold)).foregroundStyle(Chrome.warning)
    [Trait("Req", "PR-USAGE-111")] // UsageViews.swift:48 .font(.system(.footnote, weight: .semibold)): 10 points
    [Trait("Req", "UI-PROVREG-019")]
    [Trait("Req", "UI-USAGE-016")] // ED-059 .footnote is 10
    public void The_triangle_is_a_semibold_footnote_glyph_in_the_warning_colour() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai));

        var glyph = Triangle(view);
        var font = SwiftImage.GetFont(glyph)!;
        Assert.Equal(SwiftFont.Footnote.FontWeight(SwiftFont.Weight.Semibold), font);
        Assert.Equal(10, font.PointSize);
        Assert.Equal(Resolved(Chrome.Warning), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-049")] // :51-54 Text(verbatim: problem).font(.system(.subheadline)).foregroundStyle(Chrome.secondaryText)
    [Trait("Req", "PR-USAGE-111")] // UsageViews.swift:51 .font(.system(.subheadline)): 11 points
    [Trait("Req", "UI-PROVREG-019")]
    [Trait("Req", "UI-USAGE-016")] // ED-059 .subheadline is 11
    public void The_problem_text_is_subheadline_in_the_secondary_colour() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai));

        var note = Text(view, Problem);
        AssertStyle(note, Subheadline);
        AssertColor(note, Chrome.SecondaryText);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-049")] // :44-57 .padding(.top, 14), HStack(alignment: .center, spacing: 6), Spacer(minLength: 8)
    [Trait("Req", "UI-PROVREG-019")] // "An HStack with alignment center and spacing 6, with padding top 14, holding the icon, the text, and a Spacer with minLength 8."
    [Trait("Req", "UI-USAGE-016")] // "Centre-aligned HStack with 6 points of spacing, padded 14 at the top; a warning triangle, the text, then Spacer(minLength: 8)"
    public void The_note_row_is_14_below_the_header_with_6_between_glyph_and_text_and_8_kept_clear_after_it() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai), width: 400);

        var glyph = BoundsIn(Triangle(view), view);
        var note = BoundsIn(Text(view, Problem), view);
        Assert.Equal(14, BoundsIn(Foot(view), view).Top - BoundsIn(Header(view), view).Bottom);
        Assert.Equal(0, glyph.Left);
        Assert.Equal(6, note.Left - glyph.Right);
        Assert.Equal(glyph.Center.Y, note.Center.Y, 0.5);

        // The minimum after the text: the 6 between it and the spacer plus the spacer's 8.
        var wide = Problem + " " + Problem + " " + Problem;
        rig.PublishLimits(ProviderKind.Zai, Limits(problem: wide));
        var longNote = BoundsIn(Text(view, wide), view);
        Assert.True(longNote.Right <= 400 - 14);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-049")] // :53-54 .lineLimit(2).fixedSize(horizontal: false, vertical: true): wraps to two lines and grows the row
    [Trait("Req", "UI-PROVREG-019")]
    [Trait("Req", "UI-USAGE-016")] // "text wraps to at most two lines and grows the row vertically"
    public void A_long_problem_wraps_to_at_most_two_lines_and_the_glyph_stays_centred_on_it() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(problem: "Short."));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai), width: 260);
        var oneLine = Text(view, "Short.").Bounds.Height;

        var twoLines = "Z.ai did not accept the key it was given for this account.";
        rig.PublishLimits(ProviderKind.Zai, Limits(problem: twoLines));
        var wrapped = Text(view, twoLines);
        Assert.InRange(wrapped.Bounds.Height, oneLine * 1.9, oneLine * 2.1);
        Assert.Equal(2, wrapped.MaxLines);
        Assert.True(Foot(view).Bounds.Height >= wrapped.Bounds.Height); // the row grew with it
        Assert.Equal(BoundsIn(wrapped, view).Center.Y, BoundsIn(Triangle(view), view).Center.Y, 0.5);

        var tooLong = string.Join(" ", Enumerable.Repeat("Z.ai refused the request for a reason it describes at length.", 6));
        rig.PublishLimits(ProviderKind.Zai, Limits(problem: tooLong));
        Assert.InRange(Text(view, tooLong).Bounds.Height, oneLine * 1.9, oneLine * 2.1); // never a third line
    });

    [Fact]
    [Trait("Req", "PR-USAGE-049")] // :44 the note sits after the windows (and any banked resets): "at the foot of the block"
    [Trait("Req", "UI-PROVREG-019")] // "The note sits at the FOOT of the block, after the windows and any banked resets."
    public void The_note_sits_at_the_foot_after_the_windows_and_the_banked_resets() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(
            windows: [Meter("w", "Daily", 12)],
            resets: Resets(1, Credit("c", title: "Reset card")),
            problem: Problem));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai));

        var note = BoundsIn(Text(view, Problem), view);
        Assert.True(note.Top > BoundsIn(All<LimitRow>(view).Single(), view).Bottom);
        Assert.True(note.Top > BoundsIn(All<BankedResetsView>(view).Single(), view).Bottom);
        Assert.Same(Foot(view), Text(view, Problem).FindLogicalAncestorOfType<Grid>());
        Assert.Equal(14, BoundsIn(Foot(view), view).Top - BoundsIn(All<BankedResetsView>(view).Single(), view).Bottom);
    });

    // ------------------------------------------------------------------ PR-USAGE-050, UI-PROVREG-020, UI-USAGE-017 the sign-in prompt

    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Commandcode)]
    [InlineData(ProviderKind.Pi)]
    [Trait("Req", "PR-USAGE-050")] // :58-69 the fixed sentence replaces the provider's own words
    [Trait("Req", "UI-PROVREG-020")] // "the provider's own problem text is NOT shown on this variant; only this fixed sentence is"
    [Trait("Req", "UI-USAGE-017")] // "The provider's own problem text is not shown on this branch."
    public void A_signed_in_providers_problem_is_replaced_by_the_fixed_sentence_and_a_Login_button(ProviderKind provider) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(provider, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(provider));

        Assert.True(HasText(view, "Click here to login again")); // no full stop
        Assert.False(HasText(view, Problem));
        Assert.Empty(All<SwiftImage>(view)); // and no warning triangle on this branch
        Assert.Single(Buttons(view));
        Assert.Equal("Login", Buttons(view)[0].Content);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-050")] // :59-63 .font(.system(.callout)).foregroundStyle(Chrome.secondaryText)
    [Trait("Req", "PR-USAGE-111")] // UsageViews.swift:61 .font(.system(.callout)): 12 points
    [Trait("Req", "UI-PROVREG-020")]
    [Trait("Req", "UI-USAGE-017")] // ED-059 .callout is 12
    public void The_prompt_is_callout_text_in_the_secondary_colour() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Claude, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Claude));

        var prompt = Text(view, "Click here to login again");
        AssertStyle(prompt, Callout);
        AssertColor(prompt, Chrome.SecondaryText);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-050")] // :58-68 HStack(spacing: 8) { text, Spacer(minLength: 8), Button }.padding(.top, 14)
    [Trait("Req", "UI-PROVREG-020")] // "An HStack with spacing 8 and padding top 14, holding the label, a Spacer with minLength 8, and the button."
    [Trait("Req", "UI-USAGE-017")] // "HStack with 8 points of spacing, padded 14 at the top; the text, Spacer(minLength: 8), then the Login button."
    public void The_prompt_row_is_14_below_the_header_with_the_button_at_the_trailing_edge() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Claude, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Claude), width: 400);

        var prompt = Text(view, "Click here to login again");
        var login = Buttons(view).Single();
        var host = Assert.IsAssignableFrom<Control>(login.Parent);
        Assert.Equal(14, BoundsIn(Foot(view), view).Top - BoundsIn(Header(view), view).Bottom);
        Assert.Equal(0, BoundsIn(prompt, view).Left);
        Assert.Equal(400, BoundsIn(host, view).Right); // trailing edge
        Assert.True(BoundsIn(host, view).Left >= BoundsIn(prompt, view).Right + 24); // beside the label, not over it
        Assert.Equal(BoundsIn(prompt, view).Center.Y, BoundsIn(host, view).Center.Y, 1); // the HStack is centre aligned

        // The least room: text, 8, Spacer(minLength: 8), 8, button.
        var row = Foot(view);
        row.Measure(Size.Infinity);
        Assert.Equal(NaturalWidth("Click here to login again", SwiftFont.Callout) + 24 + host.DesiredSize.Width, row.DesiredSize.Width, 1);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-050")] // "The row is a label plus a button, not a clickable row, despite the wording Click here: only the button acts."
    [Trait("Req", "UI-USAGE-017")] // "Despite the wording, the label is not clickable; only the adjacent button acts."
    public void The_wording_is_not_a_link_only_the_button_acts() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Claude, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Claude));

        var prompt = Text(view, "Click here to login again");
        Assert.Null(prompt.FindLogicalAncestorOfType<Button>());
        Assert.Empty(rig.Launches);
    });

    // ------------------------------------------------------------------ UI-PROVREG-021, UI-USAGE-018 the Login button

    [Theory]
    [InlineData(ProviderKind.Codex, "codex login")]
    [InlineData(ProviderKind.Claude, "claude auth login")]
    [InlineData(ProviderKind.Antigravity, "agy")]
    [InlineData(ProviderKind.Copilot, "copilot login")]
    [InlineData(ProviderKind.Commandcode, "cmd login")]
    [InlineData(ProviderKind.Pi, "pi")]
    [Trait("Req", "PR-USAGE-050")] // :64 Button("Login") { Workspace.openTerminal(running: provider.loginCommand) }
    [Trait("Req", "UI-PROVREG-021")] // "click: Workspace.openTerminal running the provider's loginCommand"
    [Trait("Req", "UI-USAGE-018")] // "The command strings are exact and must not be adapted for Windows"
    public void Login_opens_a_terminal_running_the_providers_own_login_command(ProviderKind provider, string command) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(provider, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(provider));
        Assert.Empty(rig.Launches);

        Click(Buttons(view).Single());

        var launch = Assert.Single(rig.Launches);
        Assert.Equal(@"C:\fixture\wt.exe", launch.Executable); // Windows Terminal, as Workspace.openTerminal prefers
        Assert.Equal(command, launch.Arguments[^1]); // the command, as given
        Assert.Equal("-Command", launch.Arguments[^2]);
        Assert.Equal("-NoExit", launch.Arguments[^3]);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-021")] // "buttonStyle .glass, controlSize .small."; "always enabled when shown"
    [Trait("Req", "UI-USAGE-018")] // "Glass button style"; "no disabled state"
    public void The_Login_button_is_an_always_enabled_glass_capsule() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Claude, Limits(problem: Problem));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Claude));

        var login = ButtonTitled(view, "Login");
        Assert.True(login.IsEnabled);
        var glass = GlassBehind(login)!;
        Assert.Equal(GlassShapeKind.Capsule, glass.Shape.Kind);
        Assert.Equal(RoundedCornerStyle.Continuous, glass.Shape.Style);

        rig.SetLoadingLimits(ProviderKind.Claude, true); // nothing about a running read disables it either
        Assert.True(ButtonTitled(view, "Login").IsEnabled);
    });

    [Fact]
    [Trait("Req", "UI-USAGE-018")] // "present only on the signed-in problem branch"
    [Trait("Req", "UI-PROVREG-021")]
    public void There_is_no_Login_button_without_a_problem() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Claude, Limits(windows: [Meter("w", "Daily", 12)]));
        var view = rig.Show(new PlanLimitsView(ProviderKind.Claude));

        Assert.Empty(Buttons(view));
        Assert.False(HasText(view, "Click here to login again"));

        rig.PublishLimits(ProviderKind.Claude, Limits(windows: [Meter("w", "Daily", 12)], problem: Problem));
        Assert.True(HasButton(view, "Login"));
        rig.PublishLimits(ProviderKind.Claude, Limits(windows: [Meter("w", "Daily", 12)]));
        Assert.False(HasButton(view, "Login")); // the prompt goes when the problem does
    });
}
