using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Port.MultiAccount.Usage;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Usage;
using Marrow.Core.Port.MultiAccount.Shared;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Views.MultiAccount.Audit;
// PORT-ONLY: MA-5-1 (brief alias MA-3b-5-1), visible census across actual views. Spoken Fast mode is allowed; written Fast labels are not.
[Collection("Headless")]
public sealed class FastAndLimitsAuditTests(HeadlessSession headless)
{
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Fast_is_a_yellow_bolt_in_popover_defaults_and_pill_steps_and_never_written(bool fast)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var models = new ModelOption[] { new("opus", "Opus 5.5", efforts: new ValueList<string>(["low", "medium", "high", "max"]), defaultEffort: "medium", fastTier: "fast") };
        foreach (var account in new[] { a, b }) rig.Accounts.SetDefaults(account.Id, new() { Model = "opus", Effort = "high", FastMode = fast });
        var pill = rig.MakePill("Deep work", a, b); var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        using var popModel = new AccountPopoverModel(port, rig.Accounts, _ => models); using var usageModel = new UsagePanelModel(port, [port]);
        var capsuleModel = new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, id => rig.Usage.GetValueOrDefault(id));
        Control? pop = null, defaults = null, editor = null; AccountCapsule? capsule = null;
        MultiAccountHarness.Run(headless, rig, () =>
        {
            var context = new MultiAccountContext(rig.Model, rig.PillsModel, rig.Editor, _ => models, () => { });
            pop = new AccountPopover(popModel); defaults = new AccountDefaultsPanel(rig.Accounts, a.Id, rig.Accounts.DefaultName(a.Id), models);
            editor = new PillEditorView(context, rig.Editor(pill.Id), () => { });
            capsule = new AccountCapsule("plain"); capsule.Apply(capsuleModel.For(new CapsuleSubject(ProviderKind.Claude, a.Id, null, false, null))!, false);
            return new StackPanel { Children = { pop, defaults, editor, new ProvidersAccountsPreview(rig.Model, ProviderKind.Claude), new MultiAccountUsageView(usageModel), capsule } /* OD-0149: the accounts are on Settings > Providers */ };
        }, h =>
        {
            Assert.DoesNotContain(h.Words(), word => word.Contains("Fast", StringComparison.OrdinalIgnoreCase));
            foreach (var surface in new[] { pop!, defaults!, editor! })
            {
                var bolts = surface.GetVisualDescendants().OfType<SwiftImage>().Where(i => i.IsEffectivelyVisible && i.SymbolName == "bolt.fill").ToArray();
                Assert.Equal(fast, bolts.Length > 0);
                foreach (var bolt in bolts) Assert.Equal(SwiftColor.Yellow.Resolve(bolt.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(bolt.Foreground).Color);
            }
            var face = Assert.Single(capsule!.GetVisualDescendants().OfType<Button>());
            Assert.DoesNotContain("Fast", AutomationProperties.GetName(face) ?? "", StringComparison.OrdinalIgnoreCase);
        }, models: _ => models);
    }
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(ProviderKind.Claude, false)] [InlineData(ProviderKind.Claude, true)]
    [InlineData(ProviderKind.Codex, false)] [InlineData(ProviderKind.Codex, true)]
    [InlineData(ProviderKind.OpenRouter, false)] [InlineData(ProviderKind.OpenRouter, true)]
    [InlineData(ProviderKind.Local, false)] [InlineData(ProviderKind.Local, true)]
    public void No_five_hour_claim_uses_only_the_report_flag_in_every_app_surface(ProviderKind service, bool noFive)
    {
        var rig = new AccountsRig(); var a = rig.Connect(service, service == ProviderKind.Claude ? "Max" : null); rig.ConnectAnother(service, service == ProviderKind.Claude ? "Max" : null);
        rig.Usage[a.Id] = new(.12, null, .4, rig.Clock.GetUtcNow().AddDays(1), noFive, service == ProviderKind.Local);
        var port = new FakeThreadAccountPort(rig, service); using var pop = new AccountPopoverModel(port, rig.Accounts); using var usage = new UsagePanelModel(port, [port]);
        var capsule = new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, id => rig.Usage.GetValueOrDefault(id));
        MultiAccountHarness.Run(headless, rig, () => new StackPanel { Children = { new AccountPopover(pop), new MultiAccountUsageView(usage), new ProvidersAccountsPreview(rig.Model, service) } }, h =>
        {
            Assert.Equal(noFive, port.Accounts[0].NoFiveHour);
            Assert.Equal(noFive, (service == ProviderKind.Local ? rig.Model.LocalBlock : rig.Model.Blocks.Single(s => s.Service == service)).Rows[0].NoFiveHour);
            var words = h.Words();
            if (!noFive || service == ProviderKind.Local) Assert.DoesNotContain("No 5-hour limit", words);
            else Assert.Contains("No 5-hour limit", words); // current popover shows the reported fact even for a credit service.
            var aria = capsule.For(new CapsuleSubject(service, a.Id, null, false, null))!.Aria;
            Assert.Contains(noFive ? "weekly" : "5-hour", aria, StringComparison.Ordinal);
            Assert.DoesNotContain("Fast", aria, StringComparison.OrdinalIgnoreCase);
            rig.Accounts.SetPlan(a.Id, "Pro"); port.Refresh(); rig.Model.Refresh(); h.Settle();
            Assert.Equal(noFive, port.Accounts[0].NoFiveHour); // a plan change cannot create or remove a reported flag.
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Unknown_usage_never_claims_no_five_hour_and_a_report_changes_both_views_live()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); rig.ConnectAnother(ProviderKind.Claude, "Max");
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude); using var pop = new AccountPopoverModel(port, rig.Accounts); using var usage = new UsagePanelModel(port, [port]);
        MultiAccountHarness.Run(headless, rig, () => new StackPanel { Children = { new AccountPopover(pop), new MultiAccountUsageView(usage) } }, h =>
        {
            Assert.Null(NoFiveHourText.For(null)); Assert.DoesNotContain("No 5-hour limit", h.Words());
            rig.Usage[a.Id] = new(null, null, .23, null, true, false); port.Refresh(); h.Settle();
            Assert.Equal(2, h.Words().Count(w => w == "No 5-hour limit"));
            rig.Usage[a.Id] = new(.2, null, .23, null, false, false); port.Refresh(); h.Settle();
            Assert.DoesNotContain("No 5-hour limit", h.Words());
        });
    }
}
