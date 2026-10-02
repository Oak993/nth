using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsagePanel.swift (B3 52bc792), :48-66 the dividers and :81-108 the Lead and Heads sections.
[Collection("Headless")]
public sealed class UsagePanelSectionTests(HeadlessSession headless)
{
    private const string Dot = "·"; // U+00B7 MIDDLE DOT (UI-USAGE-008 notes)

    private static ContextUsage Context() => new() { UsedTokens = 1_000, WindowTokens = 4_000 };

    // The panel's VStack: the Border the panel wraps its content in, then the stack inside it.
    private static IReadOnlyList<Control> Stack(UsagePanel panel) =>
        [.. Assert.IsType<StackPanel>(Assert.IsType<Border>(panel.Child).Child).Children];

    // SwiftUI's Divider(): one point high, across the width.
    private static bool IsRule(Control control) => control is Border { Height: 1 };

    private static Control[] Rules(UsagePanel panel) => [.. Stack(panel).Where(IsRule)];

    private static void AssertInset14(UsagePanel panel, Control rule)
    {
        var stack = Stack(panel);
        var index = stack.ToList().IndexOf(rule);
        Assert.Equal(14, BoundsIn(rule, panel).Top - BoundsIn(stack[index - 1], panel).Bottom, 0.01); // .padding(.vertical, 14)
        Assert.Equal(14, BoundsIn(stack[index + 1], panel).Top - BoundsIn(rule, panel).Bottom, 0.01);
    }

    // :55, :62 Divider().padding(.vertical, 14), only `if showsContext`; :57 PlanLimitsView, :64 CreditsView.
    [Fact]
    [Trait("Req", "UI-USAGE-035")] // UsagePanel.swift:53-58 the rule before the limits, only when the context block was drawn: full width, 14 above and below
    public void The_rule_before_the_limits_is_a_full_width_hairline_14_points_clear_above_and_below() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(Context(), ProviderKind.Zai));

        var rule = Assert.Single(Rules(panel));
        Assert.IsType<PlanLimitsView>(Stack(panel)[Stack(panel).ToList().IndexOf(rule) + 1]);
        Assert.Equal(1, rule.Bounds.Height);
        Assert.Equal(360 - 32, rule.Bounds.Width, 0.01); // the whole content width
        Assert.Equal(16, BoundsIn(rule, panel).Left);
        AssertInset14(panel, rule);
        // "Platform separator colour": the port draws SwiftUI's Divider() as Chrome.primaryText at 0.22 (PlainControls.Divider).
        Assert.Equal(Resolved(Chrome.PrimaryText.Opacity(0.22)), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(((Border)rule).Background).Color);
    });

    [Fact]
    [Trait("Req", "UI-USAGE-035")] // UsagePanel.swift:53-58 "only when the context block was drawn": none without it
    public void Without_the_context_block_there_is_no_rule_before_the_limits() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(null, ProviderKind.Zai));

        Assert.Empty(Rules(panel));
        Assert.IsType<PlanLimitsView>(Assert.Single(Stack(panel)));
    });

    [Fact]
    [Trait("Req", "UI-USAGE-035")] // UsagePanel.swift:60-65 the rule before the credits, only when the context block was drawn
    public void The_rule_before_the_credits_is_drawn_only_after_a_context_block() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var withContext = rig.Show(new UsagePanel(Context(), ProviderKind.Deepseek));
        var rule = Assert.Single(Rules(withContext));
        Assert.IsType<CreditsView>(Stack(withContext)[Stack(withContext).ToList().IndexOf(rule) + 1]);
        Assert.Equal(360 - 32, rule.Bounds.Width, 0.01);
        AssertInset14(withContext, rule);

        var without = rig.Show(new UsagePanel(null, ProviderKind.Deepseek));
        Assert.Empty(Rules(without));
    });

    // :48-51 providerSection(provider, role: "Lead", needsDivider: showsContext); providerSection(heads, role: "Heads",
    // needsDivider: showsContext || showsLimits || showsCredits); :88-90 Divider().padding(.vertical, 14).
    [Fact]
    [Trait("Req", "UI-USAGE-035")] // UsagePanel.swift:49, :51, :88-90 the rule before Lead (only after context) and before Heads (after context or the lead's sections)
    public void The_rules_before_Lead_and_Heads_are_each_14_points_clear_and_full_width() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(Context(), ProviderKind.Zai, ProviderKind.Deepseek));

        var stack = Stack(panel);
        var rules = Rules(panel);
        Assert.Equal(2, rules.Length);
        Assert.IsType<Border>(stack[stack.ToList().IndexOf(rules[0]) + 1]); // the rule comes right before the Lead header, a Border around the icon and title
        foreach (var rule in rules)
        {
            Assert.Equal(360 - 32, rule.Bounds.Width, 0.01);
            AssertInset14(panel, rule);
        }
        Assert.IsType<PlanLimitsView>(stack[stack.ToList().IndexOf(rules[1]) - 1]); // the lead's limits end, then the rule, then Heads
    });

    [Theory]
    [InlineData(false, ProviderKind.Zai, ProviderKind.Deepseek, 1)] // no context: no rule before Lead; the lead's limits earn the rule before Heads
    [InlineData(true, ProviderKind.Cursor, ProviderKind.Zai, 1)] // context but a lead with nothing: no Lead section, so only the rule before Heads
    [InlineData(false, ProviderKind.Cursor, ProviderKind.Zai, 0)] // nothing before the Heads section: no rule at all
    [InlineData(false, ProviderKind.Cursor, ProviderKind.Deepseek, 0)]
    [InlineData(true, ProviderKind.Zai, ProviderKind.Deepseek, 2)]
    [Trait("Req", "UI-USAGE-035")] // UsagePanel.swift:48-51, :87-90 the predicates of the Lead and Heads rules
    public void The_Lead_and_Heads_rules_follow_their_own_predicates(bool context, ProviderKind lead, ProviderKind heads, int rules) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(context ? Context() : null, lead, heads));

        Assert.Equal(rules, Rules(panel).Length);
    });

    // :98-103 `if showsLimits && showsCredits { Divider().padding(.vertical, 14) }`, always when both show. The one provider that
    // exposes both is Command Code, whose refresh reads a stored login from the home directory, so the panel is not attached here.
    [Fact]
    [Trait("Req", "UI-USAGE-035")] // UsagePanel.swift:101-103 the rule between limits and credits: always when both show, 14 above and below
    public void The_rule_between_limits_and_credits_is_always_drawn_and_padded_14_above_and_below() => headless.OnUiThread(() =>
    {
        foreach (var context in new ContextUsage?[] { null, Context() })
        {
            var panel = new UsagePanel(context, ProviderKind.Commandcode, ProviderKind.Cursor);
            var stack = Stack(panel);
            var limits = stack.OfType<PlanLimitsView>().Single();
            var credits = stack.OfType<CreditsView>().Single();
            var between = stack.Skip(stack.ToList().IndexOf(limits) + 1).Take(stack.ToList().IndexOf(credits) - stack.ToList().IndexOf(limits) - 1).ToArray();
            var rule = Assert.Single(between);
            Assert.True(IsRule(rule));
            Assert.Equal(new Avalonia.Thickness(0, 14, 0, 14), rule.Margin);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, rule.HorizontalAlignment);
        }
    });

    // :91-96 HStack(spacing: 6) { ProviderIcon(provider: p, size: 13); Text(verbatim: "\(role) · \(p.displayName)")
    // .font(.system(.callout, weight: .semibold)).foregroundStyle(Chrome.primaryText) }.padding(.bottom, 8)
    [Fact]
    [Trait("Req", "UI-USAGE-008")] // UsagePanel.swift:91-97 the header: a 13 x 13 icon, 6 points, the text in callout semibold in primaryText, 8 below
    [Trait("Req", "PR-USAGE-111")] // UsagePanel.swift:94 .font(.system(.callout, weight: .semibold)): 12 points, weight 600
    public void The_Lead_and_Heads_headers_are_a_13_point_icon_and_a_callout_semibold_title() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(null, ProviderKind.Zai, ProviderKind.Deepseek));

        foreach (var (text, provider, next) in new (string, ProviderKind, Type)[]
                 {
                     ("Lead " + Dot + " Z.ai", ProviderKind.Zai, typeof(PlanLimitsView)),
                     ("Heads " + Dot + " DeepSeek", ProviderKind.Deepseek, typeof(CreditsView)),
                 })
        {
            var title = Text(panel, text);
            AssertStyle(title, Callout, Avalonia.Media.FontWeight.SemiBold);
            AssertColor(title, Chrome.PrimaryText);
            var header = Assert.IsType<StackPanel>(title.Parent);
            var icon = Assert.IsType<ProviderIcon>(header.Children[0]);
            Assert.Equal(provider, icon.Provider);
            Assert.Equal(13, icon.Size);
            var glyph = Assert.IsAssignableFrom<Control>(icon.Child); // the drawn 13 x 13 image, centred in the row beside the title
            Assert.Equal(13, glyph.Bounds.Width, 0.01);
            Assert.Equal(13, glyph.Bounds.Height, 0.01);
            Assert.Equal(BoundsIn(title, panel).Top + title.Bounds.Height / 2, BoundsIn(glyph, panel).Top + 13.0 / 2, 0.51); // HStack centres its items
            Assert.Equal(6, BoundsIn(title, panel).Left - BoundsIn(glyph, panel).Right, 0.01); // HStack(spacing: 6)
            Assert.Equal(16, BoundsIn(glyph, panel).Left);
            var stack = Stack(panel);
            var section = stack.First(c => c.GetType() == next);
            Assert.Equal(8, BoundsIn(section, panel).Top - BoundsIn(header, panel).Bottom, 0.01); // .padding(.bottom, 8)
        }
    });

    [Fact]
    [Trait("Req", "UI-USAGE-008")] // UsagePanel.swift:29, :87 "absent unless a distinct heads provider is set"; a provider with neither limits nor credits gets no section
    public void No_Lead_or_Heads_header_appears_without_a_distinct_heads_provider_or_without_sections() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        foreach (var heads in new ProviderKind?[] { null, ProviderKind.Zai })
        {
            var panel = rig.Show(new UsagePanel(null, ProviderKind.Zai, heads));
            Assert.DoesNotContain(All<TextBlock>(panel), text => text.Text!.StartsWith("Lead ", StringComparison.Ordinal) || text.Text.StartsWith("Heads ", StringComparison.Ordinal));
        }

        var leadless = rig.Show(new UsagePanel(null, ProviderKind.Cursor, ProviderKind.Zai));
        Assert.DoesNotContain(All<TextBlock>(leadless), text => text.Text!.StartsWith("Lead ", StringComparison.Ordinal));
        Assert.Contains("Heads " + Dot + " Z.ai", Texts(leadless));

        var headless2 = rig.Show(new UsagePanel(null, ProviderKind.Zai, ProviderKind.Cursor));
        Assert.Contains("Lead " + Dot + " Z.ai", Texts(headless2));
        Assert.DoesNotContain(All<TextBlock>(headless2), text => text.Text!.StartsWith("Heads ", StringComparison.Ordinal));
    });

    // PR-USAGE-111: every usage label that was a fixed size at B1 is a named text style at B2, in its own weight. The UsagePanel's
    // two sites (:43 .body, :94 .callout semibold) and the sites of UsageViews.swift the panel hosts: :18, :97, :148, :230, :326
    // .body; :113 .body semibold; :154, :159, :233 .callout; :165, :101, :330 .subheadline.
    [Fact]
    [Trait("Req", "PR-USAGE-111")] // UsagePanel.swift:43, :94; UsageViews.swift:18, :97, :113, :148, :154, :159, :165, :230, :233, :326, :330, :101
    public void Every_usage_label_in_the_panel_is_set_in_its_named_text_style_at_the_ED_059_size() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var panel = rig.Show(new UsagePanel(Context(), ProviderKind.Zai, ProviderKind.Deepseek));
        rig.PublishLimits(ProviderKind.Zai, Limits(
            windows: [Meter("w", "Daily", 40, resetsAt: SwiftDate.Now.AddHours(3), detail: "A detail line")],
            planName: "Pro",
            resets: Resets(2, Credit("a", title: "5-hour reset", detail: "Expires in 3 days"), Credit("b", title: "Weekly reset", detail: "Never expires"))));
        rig.PublishCredits(ProviderKind.Deepseek, Balance(12.34, usable: true, toppedUp: 10, granted: 2.34));
        rig.Settle();

        var credits = Balance(12.34, usable: true, toppedUp: 10, granted: 2.34);
        // .body (13)
        foreach (var text in new[] { "Context window", "Plan usage limits " + Dot + " Pro", "Daily", "Banked resets", "5-hour reset", "Weekly reset", "Remaining credits" })
            AssertStyle(Text(panel, text), Body);
        // .body semibold with monospaced digits (:113-114) and the monospaced context value (:41)
        AssertStyle(Text(panel, credits.AmountText), Body, Avalonia.Media.FontWeight.SemiBold, monospacedDigit: true);
        AssertStyle(Text(panel, "1k / 4k (25%)"), Body, monospacedDigit: true);
        // .callout (12)
        Assert.Contains(All<TextBlock>(panel), text => text.Text!.StartsWith("Resets ", StringComparison.Ordinal));
        foreach (var text in All<TextBlock>(panel).Where(t => t.Text!.StartsWith("Resets ", StringComparison.Ordinal)))
            AssertStyle(text, Callout);
        AssertStyle(Text(panel, "40%"), Callout, monospacedDigit: true);
        AssertStyle(Text(panel, Dot + " 2 available"), Callout);
        // .subheadline (11)
        foreach (var text in new[] { "A detail line", "Expires in 3 days", "Never expires", credits.BreakdownText! })
            AssertStyle(Text(panel, text), Subheadline);
    });

    // The four text styles the record names, at the sizes of upstream's own table (ChatZoom.swift:51-54).
    [Theory]
    [InlineData(SwiftFont.TextStyle.Body, 13.0)]
    [InlineData(SwiftFont.TextStyle.Callout, 12.0)]
    [InlineData(SwiftFont.TextStyle.Subheadline, 11.0)]
    [InlineData(SwiftFont.TextStyle.Footnote, 10.0)]
    [Trait("Req", "PR-USAGE-111")] // ED-059: body 13, callout 12, subheadline 11, footnote 10 at 100% zoom
    public void The_four_usage_text_styles_resolve_to_13_12_11_and_10_pixels(SwiftFont.TextStyle style, double size)
    {
        Assert.Equal(size, SwiftFont.System(style).Resolve().FontSize);
        Assert.Equal(size, SwiftFont.System(style, weight: SwiftFont.Weight.Semibold).Resolve().FontSize); // a weight keeps the size
    }
}
