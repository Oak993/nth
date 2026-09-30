using System;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Xunit;

namespace Marrow.App.Tests.Views.Usage;

[Collection("Headless")]
public sealed class UsageBarAccessibilityTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(-0.2, "0% used")]
    [InlineData(0, "0% used")]
    [InlineData(0.005, "1% used")]
    [InlineData(0.5, "50% used")]
    [InlineData(1.4, "100% used")]
    [Trait("Req", "PR-USAGE-033")] // UsageViews.swift:392-394
    [Trait("Req", "UI-USAGE-007")]
    public void Rendered_bar_exposes_one_read_only_percentage_value(double fraction, string expected) =>
        headless.OnUiThread(() =>
        {
            var bar = new UsageBar(fraction, Chrome.Accent, "Context window");
            var window = new Avalonia.Controls.Window { Width = 360, Height = 100, Content = bar };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Assert.True(bar.Bounds.Width > 0);

                var peer = ControlAutomationPeer.CreatePeerForElement(bar);
                Assert.Equal("Context window", peer.GetName());
                Assert.Equal(AutomationControlType.ProgressBar, peer.GetAutomationControlType());
                Assert.Empty(peer.GetChildren() ?? []);
                var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetProvider<IValueProvider>());
                Assert.True(value.IsReadOnly);
                Assert.Equal(expected, value.Value);
                Assert.Throws<InvalidOperationException>(() => value.SetValue("75% used"));
                Assert.Equal(expected, value.Value);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });

    [Fact]
    [Trait("Req", "PR-USAGE-033")] // UsageViews.swift:377-379,392-394
    public void Bar_without_an_explicit_label_exposes_Used() => headless.OnUiThread(() =>
    {
        var peer = ControlAutomationPeer.CreatePeerForElement(new UsageBar(0.5, Chrome.Accent));
        Assert.Equal("Used", peer.GetName());
        Assert.Equal("50% used", Assert.IsAssignableFrom<IValueProvider>(peer.GetProvider<IValueProvider>()).Value);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-011")] // UsagePanel.swift:29-43
    [Trait("Req", "PR-USAGE-033")] // UsageViews.swift:392-394
    public void Context_panel_supplies_its_context_label_and_rounded_value() => headless.OnUiThread(() =>
    {
        // Do not attach UsagePanel: attachment refreshes provider data. Its context subtree
        // is constructed synchronously, so this exercises the real caller without I/O.
        var panel = new UsagePanel(
            new ContextUsage { UsedTokens = 500, WindowTokens = 4000 },
            ProviderKind.Cursor);
        var bar = Assert.Single(LogicalQuery.SelfAndDescendants<UsageBar>(panel));
        var peer = ControlAutomationPeer.CreatePeerForElement(bar);
        Assert.Equal("Context window", peer.GetName());
        Assert.Equal("13% used", Assert.IsAssignableFrom<IValueProvider>(peer.GetProvider<IValueProvider>()).Value);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-027")] // UsageViews.swift:158
    [Trait("Req", "PR-USAGE-031")] // UsageViews.swift:169
    [Trait("Req", "PR-USAGE-033")] // UsageViews.swift:392-394
    public void Limit_row_keeps_reported_text_but_clamps_its_accessible_bar_value() =>
        headless.OnUiThread(() =>
        {
            var row = new LimitRow(new PlanLimits.Window
            {
                Id = "five-hour",
                Title = "5-hour limit",
                Percent = 140,
            });
            Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(row),
                text => text.Text == "140%");
            var bar = Assert.Single(LogicalQuery.SelfAndDescendants<UsageBar>(row));
            var peer = ControlAutomationPeer.CreatePeerForElement(bar);
            Assert.Equal("5-hour limit", peer.GetName());
            Assert.Equal("100% used", Assert.IsAssignableFrom<IValueProvider>(peer.GetProvider<IValueProvider>()).Value);
            Assert.Empty(peer.GetChildren() ?? []);
        });
}
