using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.MultiAccount.Usage;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Usage;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Usage;

// PORT-ONLY: MA-3b-9, locked Usage B :4328-4350, OD-0115.
[Collection("Headless")]
public sealed class MultiAccountUsageTests(HeadlessSession headless)
{
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Current_page_navigates_to_all_accounts_and_back_with_focus(bool reduce)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Connect(ProviderKind.Codex); rig.Usage[a.Id] = new(.91, null, .42, null, false, false);
        var current = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        using var model = new UsagePanelModel(current, [new FakeThreadAccountPort(rig, ProviderKind.Codex)]);
        MultiAccountUsageView? view = null;
        MultiAccountHarness.Run(headless, rig, () => view = new MultiAccountUsageView(model), h =>
        {
            Assert.Contains("Usage", h.Words()); Assert.Contains("this thread’s account", h.Words()); Assert.Contains("Claude Max 1", h.Words());
            Assert.Contains("5-hour window", h.Words()); Assert.Contains("91%", h.Words()); Assert.Contains("This week", h.Words());
            Assert.Contains("Next if it runs out: Claude Max 2", h.Words()); Assert.Contains("All accounts · 2 more", h.Words());
            Assert.False(view!.AllShown); Assert.True(h.Press("usage-all-accounts")); Assert.True(view.AllShown);
            Assert.Contains("Codex", h.Words()); Assert.True(h.Find<Button>("usage-back").IsFocused);
            Assert.True(h.Press("usage-back")); Assert.False(view.AllShown); Assert.True(h.Find<Button>("usage-all-accounts").IsFocused);
            h.Press("usage-all-accounts"); view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); h.Settle();
            Assert.False(view.AllShown);
        }, reduceMotion: reduce);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Existing_usage_panel_selects_multi_account_view_only_with_two_accounts()
    {
        var rig = new AccountsRig(); rig.Connect(ProviderKind.Codex); rig.ConnectAnother(ProviderKind.Codex);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Codex); using var model = new UsagePanelModel(port, []);
        using var seam = MultiAccountUsageSeams.Offer.Override(_ => model);
        MultiAccountHarness.Run(headless, rig, () => new UsagePanel(null, ProviderKind.Codex), h =>
        {
            Assert.Contains("All accounts · 1 more", h.Words()); Assert.Single(h.All().OfType<MultiAccountUsageView>());
        });
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Local_and_no_five_hour_states_show_the_correct_words(bool local)
    {
        var rig = new AccountsRig(); var service = local ? ProviderKind.Local : ProviderKind.Codex; var a = rig.Connect(service); rig.ConnectAnother(service);
        rig.Usage[a.Id] = new(null, null, .2, null, true, false);
        using var model = new UsagePanelModel(new FakeThreadAccountPort(rig, service), []);
        MultiAccountHarness.Run(headless, rig, () => new MultiAccountUsageView(model), h =>
        {
            Assert.Contains(local ? "No limit · this PC" : "No 5-hour limit", h.Words());
            Assert.DoesNotContain("0%", h.Words());
        });
    }
}
