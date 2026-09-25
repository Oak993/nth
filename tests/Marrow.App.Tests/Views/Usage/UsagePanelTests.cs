using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Usage;

[Collection("Headless")]
public sealed class UsagePanelTests(HeadlessSession headless)
{
    private static string AllText(Control control)
    {
        var parts = new System.Collections.Generic.List<string>();
        void Walk(Control node)
        {
            if (node is TextBlock text) parts.Add(text.Text ?? "");
            if (node is ContentControl { Content: Control child }) Walk(child);
            if (node is ContentControl { Content: string label }) parts.Add(label);
            if (node is Panel panel) foreach (var kid in panel.Children) Walk(kid);
            if (node is Decorator { Child: Control decorated }) Walk(decorated);
        }
        Walk(control);
        return string.Join("\n", parts);
    }

    private static UsagePanel Show(AppModelHarness harness, UsagePanel panel)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, harness.MakeModel());
        host.Child = panel;
        var window = new AvaloniaWindow { Content = host };
        window.Show();
        return panel;
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(7L, "7")]
    [InlineData(999L, "999")]
    [InlineData(1_000L, "1k")]
    [InlineData(1_500L, "1.5k")]
    [InlineData(12_300L, "12.3k")]
    [InlineData(999_999L, "1000k")]
    [InlineData(1_000_000L, "1M")]
    [InlineData(1_200_000L, "1.2M")]
    [InlineData(999_999_999L, "1000M")]
    [InlineData(1_000_000_000L, "1B")]
    [InlineData(1_200_000_000L, "1.2B")]
    [InlineData(1_500_000_000L, "1.5B")]
    [InlineData(-1L, "-1")]
    [InlineData(-1_500L, "-1500")]
    [Trait("Req", "PR-USAGE-014")] // App/Views/Usage/UsagePanel.swift:110-119
    public void Tokens_uses_compact_suffixes_at_the_source_thresholds(long count, string expected)
    {
        Assert.Equal(expected, UsagePanel.Tokens(count));
    }

    [Theory]
    [InlineData(1_250L, "1.2k")]
    [InlineData(2_250L, "2.2k")]
    [Trait("Req", "PR-USAGE-014")] // App/Views/Usage/UsagePanel.swift:112-113
    [Trait("Rule", "R-FMT-3")]
    public void Tokens_formats_one_decimal_with_half_to_even_rounding(long count, string expected)
    {
        Assert.Equal(expected, UsagePanel.Tokens(count));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-011")] // App/Views/Usage/UsagePanel.swift:29-43
    public void The_context_row_shows_used_and_window_tokens_with_the_rounded_percentage()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var usage = new ContextUsage { UsedTokens = 1_500, WindowTokens = 4_000 };
            var panel = Show(harness, new UsagePanel(usage, ProviderKind.Cursor));

            var text = AllText(panel);
            Assert.Contains("Context window", text, System.StringComparison.Ordinal);
            Assert.Contains("1.5k / 4k (38%)", text, System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-011")] // App/Views/Usage/UsagePanel.swift:26,29
    public void No_context_row_is_shown_when_usage_has_no_window()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var usage = new ContextUsage { UsedTokens = 1_500, WindowTokens = null };
            var panel = Show(harness, new UsagePanel(usage, ProviderKind.Cursor));

            Assert.DoesNotContain("Context window", AllText(panel), System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-016")] // App/Views/Usage/UsagePanel.swift:88-98
    public void A_provider_with_neither_limits_nor_credits_renders_no_sections_beyond_context()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var panel = Show(harness, new UsagePanel(null, ProviderKind.Cursor));

            Assert.Equal("", AllText(panel).Trim());
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-016")] // App/Views/Usage/UsagePanel.swift:88-91
    public void A_provider_that_exposes_plan_limits_renders_the_plan_limits_section_without_throwing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var ex = Record.Exception(() => Show(harness, new UsagePanel(null, ProviderKind.Codex)));

            Assert.Null(ex);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-016")] // App/Views/Usage/UsagePanel.swift:94-97
    public void A_provider_that_exposes_credits_renders_the_credits_section_without_throwing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var ex = Record.Exception(() => Show(harness, new UsagePanel(null, ProviderKind.Deepseek)));

            Assert.Null(ex);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-017")] // App/Views/Usage/UsagePanel.swift:80-85
    public void Distinct_heads_and_lead_providers_each_showing_a_section_render_the_lead_and_heads_headers()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var panel = Show(harness, new UsagePanel(null, ProviderKind.Codex, headsProvider: ProviderKind.Deepseek));

            var text = AllText(panel);
            Assert.Contains("Lead · Codex", text, System.StringComparison.Ordinal);
            Assert.Contains("Heads · DeepSeek", text, System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-017")] // App/Views/Usage/UsagePanel.swift:80-81
    public void The_heads_provider_equal_to_the_lead_provider_is_not_treated_as_distinct()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var panel = Show(harness, new UsagePanel(null, ProviderKind.Codex, headsProvider: ProviderKind.Codex));

            Assert.DoesNotContain("Lead ·", AllText(panel), System.StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-USAGE-021")] // App/Views/Usage/UsagePanel.swift:106-107
    public void FillsWidth_switches_between_a_fixed_360_width_and_stretching_to_the_container(bool fillsWidth)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var panel = Show(harness, new UsagePanel(null, ProviderKind.Cursor, fillsWidth: fillsWidth));

            var border = Assert.IsType<Border>(panel.Child);
            if (fillsWidth)
            {
                Assert.True(double.IsNaN(border.Width));
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, border.HorizontalAlignment);
            }
            else
            {
                Assert.Equal(360, border.Width);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, border.HorizontalAlignment);
            }
        });
    }
}
