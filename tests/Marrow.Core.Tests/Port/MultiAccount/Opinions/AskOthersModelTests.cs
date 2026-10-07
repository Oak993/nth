using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing.MultiAccountSettings;
using Xunit;
namespace Marrow.Core.Tests.Port.MultiAccount.Opinions;
// PORT-ONLY: opinions B :4804-4811, :4855; OD-0120 confirmation before sending.
public sealed class AskOthersModelTests
{
    [Fact, Trait("Rule","R-TEST-4")]
    public void Button_list_order_and_chips_use_the_page_words_and_only_other_enabled_accounts()
    {
        var rig=new OpinionsRig();using var model=rig.Model();Assert.True(model.Shown);
        Assert.Equal("Ask others for this message",model.ButtonAria);Assert.Equal("Also ask, for this message only",model.ListHeading);
        Assert.Equal("@all",model.AllLabel);Assert.Equal("Everyone with room",model.AllDetail);
        Assert.Equal("Claude Max 2 / Opus 5.5",model.Rows[0].Text);Assert.Equal("Codex 1 / GPT-5.5",model.Rows[1].Text);
        Assert.DoesNotContain(model.Rows,r=>r.Account.Id==rig.Lead);model.Toggle(rig.M2);model.Toggle(rig.C1);model.Toggle(rig.L1);
        Assert.Equal("Ask also: M2·C1·L1",model.ChipsText);Assert.Equal(new[]{rig.M2,rig.C1,rig.L1},model.Picked);
        Assert.Empty(rig.Port.Asked);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public void At_all_toggles_everyone_with_room_and_never_picks_off_or_exhausted()
    {
        var rig=new OpinionsRig();rig.Accounts.Accounts.SetEnabled(rig.C1,false);
        rig.Accounts.Usage[rig.M2]=new(1,null,.5,null,false,false);rig.Claude.Refresh();rig.Codex.Refresh();
        using var model=rig.Model();Assert.DoesNotContain(model.Rows,r=>r.Account.Id==rig.C1);Assert.False(model.Rows[0].CanAsk);
        model.Toggle(rig.M2);Assert.Empty(model.Picked);model.ToggleAll();Assert.True(model.AllSelected);
        Assert.Equal(3,model.Picked.Count);Assert.DoesNotContain(rig.M2,model.Picked);model.ToggleAll();Assert.Empty(model.Picked);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public void A_single_account_hides_the_offer_but_an_enabled_exhausted_other_still_exists()
    {
        var accounts=new AccountsRig();accounts.Connect(ProviderKind.Claude);var port=new FakeThreadAccountPort(accounts,ProviderKind.Claude);
        using var model=new AskOthersModel(Guid.NewGuid(),port,[port],new FakeOpinionsPort(),new(accounts.Settings));Assert.False(model.Shown);
        var other=accounts.ConnectAnother(ProviderKind.Claude);accounts.Usage[other.Id]=new(1,null,1,null,false,false);port.Refresh();Assert.True(model.Shown);Assert.False(model.Rows.Single().CanAsk);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public async Task Same_service_send_uses_only_selected_ids_and_clears_the_message_only_chips()
    {
        var rig=new OpinionsRig();using var model=rig.Model();model.Toggle(rig.M2);
        Assert.Equal(AskSendResult.Sent,await model.SendAsync("Check the retry policy."));
        var ask=Assert.Single(rig.Port.Asked);Assert.Equal(rig.Thread,ask.Thread);Assert.Equal("Check the retry policy.",ask.Message);Assert.Equal(new[]{rig.M2},ask.Accounts);
        Assert.Empty(model.Picked);Assert.Equal("",model.ChipsText);Assert.Equal("Also asked: M2",model.LastAskedNote);Assert.NotNull(model.LastRound);
        Assert.Equal(AskSendResult.NoSelection,await model.SendAsync("Next message."));Assert.Single(rig.Port.Asked);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public async Task Cross_service_send_waits_for_explicit_confirmation_with_an_unchecked_checkbox()
    {
        var rig=new OpinionsRig();using var model=rig.Model();model.Toggle(rig.M2);model.Toggle(rig.C1);model.Toggle(rig.L1);
        Assert.Equal(AskSendResult.NeedsConfirmation,await model.SendAsync("Is this safe?"));Assert.Empty(rig.Port.Asked);
        Assert.NotNull(model.Confirmation);Assert.False(model.Confirmation.DontAskAgain);Assert.Equal("Don't ask again",model.DontAskAgainLabel);
        Assert.Equal("Codex 1 is another service: it will see this conversation.\nLocal 1 is another service: it will see this conversation.",model.Confirmation.Text);
        Assert.Equal(AskSendResult.Sent,await model.ConfirmAsync(true));Assert.Equal(new[]{rig.M2,rig.C1,rig.L1},rig.Port.Asked.Single().Accounts);
        Assert.Equal("Also asked: M2 C1 L1",model.LastAskedNote);Assert.Empty(model.Picked);
        model.Toggle(rig.C1);Assert.Equal(AskSendResult.NeedsConfirmation,await model.SendAsync("Again?"));Assert.False(model.Confirmation!.DontAskAgain);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public async Task Canceling_confirmation_sends_nothing_and_cannot_remember_the_checked_box()
    {
        var rig=new OpinionsRig();using var model=rig.Model();model.Toggle(rig.C1);await model.SendAsync("secret");model.SetDontAskAgain(true);
        Assert.Equal(AskSendResult.Canceled,await model.ConfirmAsync(false));Assert.Empty(rig.Port.Asked);Assert.False(rig.Consent.Permits(ConsentPurpose.Opinions,ProviderKind.Codex));
        Assert.Equal(AskSendResult.NeedsConfirmation,await model.SendAsync("secret"));Assert.False(model.Confirmation!.DontAskAgain);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public async Task Dont_ask_again_is_persisted_only_by_acceptance_and_is_scoped_to_this_action_and_service()
    {
        var rig=new OpinionsRig();using(var model=rig.Model())
        {
            model.Toggle(rig.C1);await model.SendAsync("q");model.SetDontAskAgain(true);await model.ConfirmAsync(true);
        }
        var restored=new OtherServiceConsentStore(rig.Accounts.Settings);Assert.True(restored.Permits(ConsentPurpose.Opinions,ProviderKind.Codex));
        Assert.False(restored.Permits(ConsentPurpose.Link,ProviderKind.Codex));Assert.False(restored.Permits(ConsentPurpose.Opinions,ProviderKind.Local));
        using var next=new AskOthersModel(rig.Thread,rig.Claude,[rig.Claude,rig.Codex,rig.Local],rig.Port,restored);
        next.Toggle(rig.C1);Assert.Equal(AskSendResult.Sent,await next.SendAsync("q2"));next.Toggle(rig.L1);Assert.Equal(AskSendResult.NeedsConfirmation,await next.SendAsync("q3"));Assert.Equal(2,rig.Port.Asked.Count);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public async Task A_newly_off_account_invalidates_a_pending_confirmation_and_never_sends_its_snapshot()
    {
        var rig=new OpinionsRig();using var model=rig.Model();model.Toggle(rig.C1);await model.SendAsync("q");
        rig.Accounts.Accounts.SetEnabled(rig.C1,false);rig.Codex.Refresh();Assert.Null(model.Confirmation);Assert.Empty(model.Picked);
        Assert.Equal(AskSendResult.NoSelection,await model.ConfirmAsync(true));Assert.Empty(rig.Port.Asked);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public async Task Reentrant_send_and_picks_cannot_duplicate_or_mutate_an_in_flight_message()
    {
        var rig=new OpinionsRig();using var model=rig.Model();model.Toggle(rig.M2);AskSendResult? nested=null;
        rig.Port.Changed+=_=>{model.Toggle(rig.C1);nested=model.SendAsync("wrong").GetAwaiter().GetResult();};
        Assert.Equal(AskSendResult.Sent,await model.SendAsync("right"));Assert.Equal(AskSendResult.Busy,nested);Assert.Single(rig.Port.Asked);Assert.Equal("right",rig.Port.Asked[0].Message);Assert.Equal(new[]{rig.M2},rig.Port.Asked[0].Accounts);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public void Bad_saved_consent_fails_closed_and_the_original_value_is_preserved()
    {
        var rig=new OpinionsRig();byte[] corrupt=[255,0,42];rig.Accounts.Settings.Set(OtherServiceConsentStore.SettingsKey,corrupt);
        var store=new OtherServiceConsentStore(rig.Accounts.Settings);Assert.False(store.Permits(ConsentPurpose.Opinions,ProviderKind.Codex));Assert.True(store.SaveBlocked);
        Assert.Throws<InvalidDataException>(()=>store.Remember(ConsentPurpose.Opinions,ProviderKind.Codex));Assert.Equal(corrupt,rig.Accounts.Settings.Object(OtherServiceConsentStore.SettingsKey));
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public void Disposal_releases_account_notifications_and_checkbox_cannot_be_set_without_a_pending_request()
    {
        var rig=new OpinionsRig();var model=rig.Model();var count=0;model.Changed+=()=>count++;model.SetDontAskAgain(true);Assert.Null(model.Confirmation);
        rig.Claude.Refresh();Assert.Equal(1,count);model.Dispose();rig.Codex.Refresh();Assert.Equal(1,count);
    }

    [Fact, Trait("Rule","R-TEST-4")]
    public async Task Refresh_notifications_cannot_reenter_send_before_it_reserves_the_operation()
    {
        var rig=new OpinionsRig();using var model=rig.Model();model.Toggle(rig.M2);var nestedStarted=false;AskSendResult? nested=null;
        model.Changed+=()=>{if(nestedStarted)return;nestedStarted=true;nested=model.SendAsync("wrong").GetAwaiter().GetResult();};
        Assert.Equal(AskSendResult.Sent,await model.SendAsync("right"));Assert.Equal(AskSendResult.Busy,nested);Assert.Single(rig.Port.Asked);Assert.Equal("right",rig.Port.Asked[0].Message);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public async Task Canceled_send_keeps_the_picks_and_can_retry_without_recording_an_answer()
    {
        var rig=new OpinionsRig();using var model=rig.Model();model.Toggle(rig.M2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>model.SendAsync("q",new CancellationToken(true)));
        Assert.False(model.Busy);Assert.Equal(new[]{rig.M2},model.Picked);Assert.Empty(rig.Port.Asked);
        Assert.Equal(AskSendResult.Sent,await model.SendAsync("q"));Assert.Single(rig.Port.Asked);
    }
}
