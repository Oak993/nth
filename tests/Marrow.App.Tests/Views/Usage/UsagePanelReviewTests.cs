using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Usage;

[Collection("Headless")]
public sealed class UsagePanelReviewTests(HeadlessSession headless)
{
    private static StackPanel Body(UsagePanel panel) => Assert.IsType<StackPanel>(Assert.IsType<Border>(panel.Child).Child);

    [Fact]
    [Trait("Req", "PR-USAGE-010")] // App/Views/Usage/UsagePanel.swift:71-78
    public void Appearance_refreshes_lead_limits_and_heads_credits_through_inherited_model()
    {
        headless.OnUiThread(() => {
            using var harness = new AppModelHarness();
            harness.Main.OnMain(() => {
                var model = new AppModel(); var registry = model.Providers;
                typeof(ProviderRegistry).GetProperty("PlanLimits")!.SetValue(registry,
                    ValueMap<ProviderKind, PlanLimits>.Empty.Mutating(d => d[ProviderKind.Zai] = new PlanLimits { Windows = ValueList<PlanLimits.Window>.Empty }));
                typeof(ProviderRegistry).GetProperty("Credits")!.SetValue(registry,
                    ValueMap<ProviderKind, ProviderCredits>.Empty.Mutating(d => d[ProviderKind.Deepseek] = new ProviderCredits {Total=2, Currency="USD", IsUsable=true}));
                var panel = new UsagePanel(null, ProviderKind.Zai, ProviderKind.Deepseek);
                var host = new Border { Child = panel }; AppEnvironment.SetAppModel(host, model);
                var window = new Avalonia.Controls.Window {Content=host};
                try {
                    window.Show();
                    Assert.Null(panel.DataContext);
                    // Isolated credentials are empty: each real refresh synchronously removes its stale cache.
                    Assert.False(registry.PlanLimits.ContainsKey(ProviderKind.Zai));
                    Assert.False(registry.Credits.ContainsKey(ProviderKind.Deepseek));
                } finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-021")] // App/Views/Usage/UsagePanel.swift:98-106
    public void Two_provider_commandcode_section_has_exactly_one_inner_divider()
    {
        headless.OnUiThread(() => {
            var children = Body(new UsagePanel(null, ProviderKind.Commandcode, ProviderKind.Cursor)).Children;
            Assert.Collection(children, c=>Assert.IsType<Border>(c), c=>Assert.IsType<PlanLimitsView>(c), c=>Assert.IsType<ChromeDivider>(c), c=>Assert.IsType<CreditsView>(c));
        });
    }

    [Theory]
    [InlineData(false,0)] [InlineData(true,2)]
    [Trait("Req", "PR-USAGE-017")] // App/Views/Usage/UsagePanel.swift:52-66
    public void Single_provider_dividers_depend_only_on_context(bool context, int expected)
    {
        headless.OnUiThread(() => {
            var children = Body(new UsagePanel(context ? new ContextUsage {UsedTokens=2,WindowTokens=4} : null, ProviderKind.Commandcode)).Children;
            Assert.Equal(expected, children.OfType<ChromeDivider>().Count());
            var limits = children.OfType<PlanLimitsView>().Single(); var credits = children.OfType<CreditsView>().Single();
            Assert.Equal(children.IndexOf(limits)+(context ? 2:1),children.IndexOf(credits));
        });
    }

    [Theory]
    [InlineData("cursor","deepseek",false,0)] [InlineData("codex","deepseek",false,1)]
    [InlineData("cursor","deepseek",true,1)] [InlineData("codex","cursor",false,0)]
    [Trait("Req", "PR-USAGE-018")] // App/Views/Usage/UsagePanel.swift:48-51
    [Trait("Req", "PR-USAGE-019")] // App/Views/Usage/UsagePanel.swift:84-90
    public void Heads_section_obeys_divider_predicates_and_is_fully_omitted_without_capabilities(string leadRaw,string headsRaw,bool context,int dividers)
    {
        headless.OnUiThread(() => {
            var lead = SwiftEnum<ProviderKind>.FromRawValue(leadRaw)!.Value; var heads = SwiftEnum<ProviderKind>.FromRawValue(headsRaw)!.Value;
            var panel = new UsagePanel(context ? new ContextUsage {UsedTokens=2,WindowTokens=4} : null, lead, heads);
            Assert.Equal(dividers, Body(panel).Children.OfType<ChromeDivider>().Count());
            var headers = Body(panel).Children.OfType<Border>().Select(b=>b.Child).OfType<StackPanel>().SelectMany(p=>p.Children).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            if (headsRaw == "cursor") Assert.DoesNotContain(headers,t=>t!.StartsWith("Heads ·",StringComparison.Ordinal));
            else Assert.Contains("Heads · DeepSeek",headers);
        });
    }
}
