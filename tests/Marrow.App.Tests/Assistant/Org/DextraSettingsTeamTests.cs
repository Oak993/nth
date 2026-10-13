using Marrow.App.Assistant;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 2.1 "The disclosure is remembered"; ED-NEW-dextra-as17-6, -7): the sidebar's Asturs group is open until the owner folds it, the fold is stored only when it is on (so the stored
// JSON of every earlier setting is unchanged), a file from before reads as open, and a value of the wrong kind is ignored.
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-7")]
public sealed class DextraSettingsTeamTests
{
    [Fact]
    public void The_group_is_open_by_default_and_the_default_leaves_the_stored_json_without_the_key()
    {
        Assert.False(DextraPreferences.Default.AstursCollapsed);
        Assert.DoesNotContain("astursCollapsed", DextraSettings.ToJson(DextraPreferences.Default), StringComparison.Ordinal);
    }

    [Fact]
    public void A_folded_group_is_stored_and_read_back_and_unfolding_takes_the_key_out_again()
    {
        var folded = DextraPreferences.Default with { AstursCollapsed = true };
        var json = DextraSettings.ToJson(folded);

        Assert.Contains("\"astursCollapsed\":true", json, StringComparison.Ordinal);
        Assert.True(DextraSettings.FromJson(json).AstursCollapsed);
        Assert.Equal(folded, DextraSettings.FromJson(json));
        Assert.DoesNotContain("astursCollapsed", DextraSettings.ToJson(folded with { AstursCollapsed = false }), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_from_before_means_open_and_a_value_of_the_wrong_kind_is_ignored()
    {
        Assert.False(DextraSettings.FromJson("{\"on\":true,\"missionsCollapsed\":false}").AstursCollapsed);
        Assert.False(DextraSettings.FromJson("{\"astursCollapsed\":\"yes\"}").AstursCollapsed);
        Assert.False(DextraSettings.FromJson("{\"astursCollapsed\":1}").AstursCollapsed);
        Assert.False(DextraSettings.FromJson("not json").AstursCollapsed);
    }

    [Fact]
    public void The_other_choices_survive_next_to_the_fold()
    {
        var all = DextraPreferences.Default with { On = true, FirstRunDone = true, MissionsCollapsed = false, AstursCollapsed = true };

        Assert.Equal(all, DextraSettings.FromJson(DextraSettings.ToJson(all)));
    }
}
