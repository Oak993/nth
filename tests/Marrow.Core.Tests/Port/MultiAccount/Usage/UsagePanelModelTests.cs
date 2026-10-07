using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Usage;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing.MultiAccountSettings;
using Xunit;
namespace Marrow.Core.Tests.Port.MultiAccount.Usage;
// PORT-ONLY: Usage B :4297-4350; OD-0115.
public sealed class UsagePanelModelTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Current_account_page_has_page_words_resets_and_other_account_count()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max");
        var b = rig.ConnectAnother(ProviderKind.Claude, "Max"); rig.Connect(ProviderKind.Codex);
        rig.Usage[a.Id] = new(.9, new(2026,10,7,15,40,0,TimeSpan.Zero), .61, new(2026,10,8,9,0,0,TimeSpan.Zero), false,false);
        var c = new FakeThreadAccountPort(rig, ProviderKind.Claude); var x = new FakeThreadAccountPort(rig, ProviderKind.Codex);
        using var model = new UsagePanelModel(c, [c,x], zone: TimeZoneInfo.Utc);
        Assert.Equal("Usage",model.Title); Assert.Equal("this thread’s account",model.Subtitle);
        Assert.Equal("5-hour window",model.FiveHourLabel); Assert.Equal("This week",model.WeekLabel);
        Assert.Equal(a.Id,model.Current!.Account.Id); Assert.True(model.Current.FiveHour.Hot);
        Assert.Equal("resets 3:40 PM",model.Current.FiveHourReset); Assert.Equal("resets 9:00 AM",model.Current.WeekReset);
        Assert.Equal("Next if it runs out: Claude Max 2",model.NextText);
        Assert.Equal("All accounts · 2 more",model.AllAccountsText); Assert.Equal("‹ Usage",model.BackText);
        Assert.Equal(2,model.Groups.Count); Assert.Equal(b.Id,model.Groups[0].Rows[1].Account.Id);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Next_skips_off_and_exhausted_but_all_rows_remain_on_page_two()
    {
        var rig=new AccountsRig(); var a=rig.Connect(ProviderKind.Claude); var b=rig.ConnectAnother(ProviderKind.Claude);
        var d=rig.ConnectAnother(ProviderKind.Claude); var e=rig.ConnectAnother(ProviderKind.Claude);
        rig.Accounts.SetEnabled(b.Id,false); rig.Usage[d.Id]=new(null,null,1,new(2026,10,7,15,40,0,TimeSpan.Zero),true,false);
        var c=new FakeThreadAccountPort(rig,ProviderKind.Claude);
        using var model=new UsagePanelModel(c,[c],zone:TimeZoneInfo.Utc);
        Assert.Equal("Next if it runs out: Claude 4",model.NextText); Assert.Equal(4,model.Groups[0].Rows.Count);
        Assert.Equal("out · resets 3:40 PM",model.Groups[0].Rows[2].Summary); Assert.True(model.Groups[0].Rows[2].Dimmed);
        Assert.Equal("No 5-hour limit",model.Groups[0].Rows[2].NoFiveHourText);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Local_and_unreported_usage_never_draw_fake_usage()
    {
        var rig=new AccountsRig(); var a=rig.Connect(ProviderKind.Local); rig.Accounts.Rename(a.Id,"Qwen Coder 32B");
        var c=new FakeThreadAccountPort(rig,ProviderKind.Local);
        using var model=new UsagePanelModel(c,[c]);
        Assert.Equal("No limit · this PC",model.Current!.Summary); Assert.Null(model.Current.FiveHour.Fraction); Assert.Null(model.Current.Week.Fraction);
        Assert.Equal("All accounts · 0 more",model.AllAccountsText); Assert.Null(model.NextText);
    }
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(8.4,"$8.40 left",false)] [InlineData(0,"no credits",true)]
    public void OpenRouter_credits_use_page_money_words(double credit,string text,bool dim)
    {
        var rig=new AccountsRig(); rig.Connect(ProviderKind.Claude); var a=rig.Connect(ProviderKind.OpenRouter);
        var c=new FakeThreadAccountPort(rig,ProviderKind.Claude); var o=new FakeThreadAccountPort(rig,ProviderKind.OpenRouter);
        using var model=new UsagePanelModel(c,[c,o], credits:_=>credit);
        Assert.Equal("OpenRouter",model.Groups[1].Title); Assert.Equal(text,model.Groups[1].Rows[0].Summary); Assert.Equal(dim,model.Groups[1].Rows[0].Dimmed);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Unknown_credits_are_unknown_and_refresh_disposal_follow_all_seams()
    {
        var rig=new AccountsRig(); rig.Connect(ProviderKind.Claude); rig.Connect(ProviderKind.OpenRouter);
        var c=new FakeThreadAccountPort(rig,ProviderKind.Claude); var o=new FakeThreadAccountPort(rig,ProviderKind.OpenRouter);
        var model=new UsagePanelModel(c,[c,o]); var calls=0; model.Changed+=()=>calls++;
        Assert.Equal("",model.Groups[1].Rows[0].Summary); Assert.False(model.Groups[1].Rows[0].Dimmed);
        Assert.Null(model.Current!.FiveHour.Fraction); o.Refresh(); Assert.Equal(1,calls); model.Dispose(); c.Refresh(); Assert.Equal(1,calls);
    }
}
