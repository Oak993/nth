using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

// MA-8 (T11 step 2): the Handshake group of Settings > Models: the switch, "When to start" (the general point of the AccountStore) and where the switch is kept.
[Trait("Feature", "MA-8")]
[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeSettingsTests
{
    private static AccountsRig Two()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        return rig;
    }

    [Fact]
    public void The_handshake_is_on_by_default_and_the_switch_row_says_so()
    {
        var rig = Two();
        var row = rig.Model.Row("hs");
        Assert.Equal((SettingKind.Switch, true, MultiAccountWords.UseHandshake, MultiAccountWords.UseHandshakeOn), (row.Kind, row.On, row.Title, row.Description));
        Assert.True(rig.Handshake.Enabled);
    }

    [Fact]
    public void Switching_it_off_is_kept_hides_when_to_start_and_changes_the_sentence()
    {
        var rig = Two();
        Assert.True(rig.Model.Row("hsWhen").Visible);
        rig.Model.SetSwitch("hs", false);
        Assert.False(rig.Handshake.Enabled);
        Assert.Equal(MultiAccountWords.UseHandshakeOff, rig.Model.Row("hs").Description);
        Assert.False(rig.Model.Row("hsWhen").Visible);
        Assert.False(new HandshakeSwitch(rig.Settings).Enabled);
        rig.Model.SetSwitch("hs", true);
        Assert.True(rig.Model.Row("hsWhen").Visible);
    }

    [Fact]
    public void When_to_start_offers_automatically_recommended_and_five_points_and_writes_the_general_point()
    {
        var rig = Two();
        var row = rig.Model.Row("hsWhen");
        Assert.Equal(MultiAccountWords.WhenToStart, row.Title);
        Assert.Equal([MultiAccountWords.WhenSmart, "At 90%", "At 95%", "At 97%", "At 98%", "At 99%"], row.Options.Select(o => o.Label));
        Assert.True(row.Options[0].Recommended);
        Assert.Equal((MultiAccountWords.WhenSmart, MultiAccountWords.WhenSmartDescription), (row.ValueLabel, row.Description));
        rig.Model.Choose("hsWhen", "97");
        Assert.Equal(new HandshakePoint(97), rig.Accounts.GeneralHandshakePoint);
        row = rig.Model.Row("hsWhen");
        Assert.Equal(("At 97%", MultiAccountWords.WhenAtDescription(97)), (row.ValueLabel, row.Description));
        rig.Model.Choose("hsWhen", "smart");
        Assert.Equal(HandshakePoint.Smart, rig.Accounts.GeneralHandshakePoint);
    }

    [Fact]
    public void A_general_point_set_elsewhere_that_the_menu_does_not_list_is_still_shown_truthfully()
    {
        var rig = Two();
        rig.Accounts.GeneralHandshakePoint = new HandshakePoint(85);
        Assert.Equal("At 85%", rig.Model.Row("hsWhen").ValueLabel);
        Assert.DoesNotContain(rig.Model.Row("hsWhen").Options, o => o.Chosen);
    }

    [Fact]
    public void The_slider_stops_are_smart_then_80_to_99()
    {
        Assert.Equal(21, HandshakeStops.Count);
        Assert.Equal(HandshakePoint.Smart, HandshakeStops.At(0));
        Assert.Equal(new HandshakePoint(80), HandshakeStops.At(1));
        Assert.Equal(new HandshakePoint(99), HandshakeStops.At(20));
        Assert.Equal(20, HandshakeStops.IndexOf(new HandshakePoint(99)));
        Assert.Equal(0, HandshakeStops.IndexOf(HandshakePoint.Smart));
        Assert.Equal("Smart", HandshakeStops.Text(HandshakePoint.Smart));
        Assert.Equal("85%", HandshakeStops.Text(new HandshakePoint(85)));
    }

    [Fact]
    public void The_switch_defaults_on_and_is_stored_under_its_own_key()
    {
        var settings = new InMemorySettingsStore();
        Assert.True(new HandshakeSwitch(settings).Enabled);
        new HandshakeSwitch(settings).Enabled = false;
        Assert.Equal([HandshakeSwitch.SettingsKey], settings.Writes);
        Assert.False(new HandshakeSwitch(settings).Enabled);
    }
}
