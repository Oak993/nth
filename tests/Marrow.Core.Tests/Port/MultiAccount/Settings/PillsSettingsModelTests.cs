using Marrow.Testing.MultiAccountSettings;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

// MA-8 (T11 step 2, OD-0117): Settings > Pills, the list. It is always there; with fewer than two accounts connected it is disabled under a plain explanation, and it enables the moment a second one is connected.
[Trait("Feature", "MA-8")]
[Trait("Rule", "R-TEST-4")]
public sealed class PillsSettingsModelTests
{
    [Fact]
    public void With_fewer_than_two_connected_accounts_the_page_is_disabled_and_says_what_a_pill_is_and_what_turns_it_on()
    {
        var rig = new PillsRig(connectAll: false);
        Assert.False(rig.Model.Enabled);
        Assert.Equal(MultiAccountWords.DisabledWhat, rig.Model.WhatText);
        Assert.Equal(MultiAccountWords.DisabledNeed, rig.Model.NeedText);
        rig.Connect(rig.Claude1, "Max");
        Assert.False(rig.Model.Enabled);
        rig.Connect(rig.Codex1, "Plus");
        Assert.True(rig.Model.Enabled);
    }

    [Fact]
    public void A_second_account_of_one_service_enables_it_as_well()
    {
        var rig = new PillsRig(connectAll: false);
        rig.Connect(rig.Claude1, "Max");
        Assert.False(rig.Model.Enabled);
        rig.Connect(rig.Accounts.Add(ProviderKind.Claude, "Pro"));
        Assert.True(rig.Model.Enabled);
    }

    [Fact]
    public void While_disabled_nothing_can_be_created_or_chosen()
    {
        var rig = new PillsRig(connectAll: false);
        Assert.Null(rig.Model.Create());
        Assert.Empty(rig.Pills.Pills);
        Assert.False(rig.Model.UseRow.Visible && rig.Model.Enabled);
    }

    [Fact]
    public void A_new_pill_is_called_New_pill_and_starts_on_the_first_two_usable_accounts_with_one_smart_handshake_for_every_step()
    {
        var rig = new PillsRig();
        var pill = rig.Model.Create()!;
        Assert.Equal(MultiAccountWords.DefaultPillName, pill.Name);
        Assert.Equal([PillsRig.Id(rig.Claude1), PillsRig.Id(rig.Claude2)], pill.Steps.Select(s => s.AccountId));
        Assert.Equal(PillHandshakeMode.SameForEvery, pill.HandshakeMode);
        Assert.Equal(HandshakePoint.Smart, pill.SameHandshake);
        Assert.Equal("New pill 2", rig.Model.Create()!.Name);
    }

    [Fact]
    public void A_new_pill_skips_accounts_that_are_off()
    {
        var rig = new PillsRig();
        rig.Accounts.SetEnabled(rig.Claude1.Id, false);
        var pill = rig.Model.Create()!;
        Assert.Equal([PillsRig.Id(rig.Claude2), PillsRig.Id(rig.Codex1)], pill.Steps.Select(s => s.AccountId));
    }

    [Fact]
    public void Rows_draw_the_pill_as_its_steps_in_order_with_the_services_counted()
    {
        var rig = new PillsRig();
        rig.Make("Work", rig.Claude1, rig.Claude2, rig.Codex1);
        var row = Assert.Single(rig.Model.Rows);
        Assert.Equal("Work", row.Name);
        Assert.Equal("Claude Max 1 → Claude Max 2 → Codex", row.Summary);
        Assert.Equal(MultiAccountWords.PillSteps(3, 2), row.Tooltip);
        Assert.Equal(3, row.Steps.Count);
    }

    [Fact]
    public void Rename_checks_the_name_the_way_the_page_does_and_the_store_keeps_it()
    {
        var rig = new PillsRig();
        var a = rig.Make("Work", rig.Claude1, rig.Claude2);
        rig.Make("Play", rig.Claude1, rig.Codex1);
        Assert.Equal(PillRenameResult.Empty, rig.Model.Rename(a.Id, "   "));
        Assert.Equal(PillRenameResult.Taken, rig.Model.Rename(a.Id, "play"));
        Assert.Equal(PillRenameResult.TooLong, rig.Model.Rename(a.Id, new string('x', 25)));
        Assert.Equal(PillRenameResult.Ok, rig.Model.Rename(a.Id, "  Deep work "));
        Assert.Equal("Deep work", rig.Pills.Find(a.Id)!.Name);
        Assert.Equal(PillRenameResult.Ok, rig.Model.Rename(a.Id, "Deep work"));
    }

    [Fact]
    public void Duplicate_puts_a_copy_after_the_pill_and_Delete_removes_it_and_the_use_for_new_threads_choice()
    {
        var rig = new PillsRig();
        var a = rig.Make("Work", rig.Claude1, rig.Claude2);
        var copy = rig.Model.Duplicate(a.Id)!;
        Assert.Equal("Work copy", copy.Name);
        Assert.Equal([a.Id, copy.Id], rig.Pills.Pills.Select(p => p.Id));
        rig.Model.ChooseUse(copy.Id.ToString("D"));
        Assert.Equal(copy.Id, rig.Pills.UseForNewThreads);
        Assert.True(rig.Model.Delete(copy.Id));
        Assert.Null(rig.Pills.UseForNewThreads);
        Assert.Single(rig.Model.Rows);
        Assert.False(rig.Model.Delete(copy.Id));
    }

    [Fact]
    public void Use_for_new_threads_offers_None_and_every_pill_and_explains_the_choice()
    {
        var rig = new PillsRig();
        var a = rig.Make("Work", rig.Claude1, rig.Claude2);
        rig.Make("Play", rig.Claude1, rig.Codex1);
        var row = rig.Model.UseRow;
        Assert.Equal((MultiAccountWords.UseForNew, MultiAccountWords.UseNone, MultiAccountWords.UseNoneDescription), (row.Title, row.ValueLabel, row.Description));
        Assert.Equal([MultiAccountWords.UseNone, "Work", "Play"], row.Options.Select(o => o.Label));
        Assert.True(row.Options[0].Chosen);
        rig.Model.ChooseUse(a.Id.ToString("D"));
        row = rig.Model.UseRow;
        Assert.Equal(("Work", MultiAccountWords.UsePillDescription), (row.ValueLabel, row.Description));
        Assert.Equal(a.Id, rig.Pills.UseForNewThreads);
        rig.Model.ChooseUse("none");
        Assert.Null(rig.Pills.UseForNewThreads);
    }

    [Fact]
    public void Changes_to_the_store_tell_the_page_to_draw_again()
    {
        var rig = new PillsRig();
        var changes = 0;
        rig.Model.Changed += () => changes++;
        rig.Make("Work", rig.Claude1, rig.Claude2);
        Assert.Equal(1, changes);
        rig.Model.Refresh();
        Assert.Equal(2, changes);
    }
}
