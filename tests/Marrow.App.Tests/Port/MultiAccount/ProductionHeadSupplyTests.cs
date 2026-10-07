using Marrow.App.Port.MultiAccount.Thread;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class ProductionHeadSupplyTests
{
    private static bool Verified(ProviderKind service) => service is ProviderKind.Claude or ProviderKind.Pi
        or ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local;

    [Fact]
    public void Production_supply_omits_every_unverified_head_without_removing_primary_account_rows()
    {
        var rig = new ProductionHeadSupplyRig();
        using var ask = rig.Ports.CreateOpinionsModel(rig.Thread, ProviderKind.Claude, rig.Consent)!;
        using var link = rig.Ports.CreateLinkModel(rig.Thread, ProviderKind.Claude, rig.Consent)!;
        var primary = rig.Raw[ProviderKind.Claude].ActiveAccountId;
        var expected = rig.Rows.Where(row => row.Id != primary && Verified(row.Service)).Select(row => row.Id).ToArray();
        Assert.Equal(Enum.GetValues<ProviderKind>().Length * 2, rig.Rows.Count);
        Assert.Equal(expected, ask.Rows.Select(row => row.Account.Id));
        Assert.Equal(expected, link.Accounts.Where(row => row.Id != primary).Select(row => row.Id));
        foreach (var row in rig.Rows.Where(row => !Verified(row.Service)))
        {
            Assert.False(link.CanLink(row.Id));
            Assert.False(link.RequestToggle(row.Id));
            link.Complete(row.Id);
            ask.Toggle(row.Id);
        }
        Assert.Empty(ask.Picked);
        Assert.Empty(rig.Links.Calls);
        Assert.Empty(rig.Opinions.Asked);
    }

    [Fact]
    public async Task Production_supply_and_generic_model_send_only_verified_opinion_ids()
    {
        var rig = new ProductionHeadSupplyRig();
        using var model = rig.Ports.CreateOpinionsModel(rig.Thread, ProviderKind.Claude, rig.Consent)!;
        model.ToggleAll();
        var expected = rig.Rows.Where(row => row.Id != rig.Raw[ProviderKind.Claude].ActiveAccountId && Verified(row.Service)).Select(row => row.Id).ToArray();
        Assert.Equal(expected, model.Picked);
        Assert.Equal(AskSendResult.NeedsConfirmation, await model.SendAsync("Offline request."));
        Assert.Empty(rig.Opinions.Asked);
        Assert.Equal(AskSendResult.Sent, await model.ConfirmAsync(true));
        Assert.Equal(expected, Assert.Single(rig.Opinions.Asked).Accounts);
    }

    [Fact]
    public async Task An_unverified_primary_can_supply_a_completed_answer_but_cannot_launch_private_opinions()
    {
        var rig = new ProductionHeadSupplyRig();
        using var ask = rig.Ports.CreateOpinionsModel(rig.Thread, ProviderKind.Codex, rig.Consent)!;
        using var link = rig.Ports.CreateLinkModel(rig.Thread, ProviderKind.Codex, rig.Consent)!;
        var primary = rig.Raw[ProviderKind.Codex].ActiveAccountId;
        Assert.Empty(ask.Rows);
        ask.ToggleAll();
        Assert.Equal(AskSendResult.NoSelection, await ask.SendAsync("Offline request."));
        Assert.Empty(rig.Opinions.Asked);
        Assert.Equal(primary, link.Active!.Id);
        Assert.All(link.Accounts.Where(row => row.Id != primary), row => Assert.True(Verified(row.Service)));
        Assert.False(link.CanLink(primary));
        var target = link.Accounts.First(row => row.Service == ProviderKind.Pi);
        Assert.False(link.RequestToggle(target.Id));
        Assert.Equal(target.Id, link.Confirm(true));
        link.Complete(target.Id);
        Assert.Equal(target.Id, Assert.Single(rig.Links.Calls).Account);
    }

    [Fact]
    public async Task Production_supply_rechecks_source_identity_and_clears_stale_opinion_selection()
    {
        var rig = new ProductionHeadSupplyRig();
        using var model = rig.Ports.CreateOpinionsModel(rig.Thread, ProviderKind.Claude, rig.Consent)!;
        model.ToggleAll();
        Assert.NotEmpty(model.Picked);
        rig.Raw[ProviderKind.Claude].Replace([]);
        Assert.Empty(model.Rows);
        Assert.Empty(model.Picked);
        Assert.Equal(AskSendResult.NoSelection, await model.SendAsync("Offline request."));
        Assert.Empty(rig.Opinions.Asked);
    }
}
