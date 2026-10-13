using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "The brain is cheap at rest", 5.4; ED-NEW-dextra-as15-6): feed triage and the digest use the cheapest
// capable model, planning and merging use the strong one (the brain's own). The cheapest capable model is a usable one with room, a known
// price and the context to read a batch, never dearer than the brain's own; text that may not leave the PC goes only to a model on it.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-6")]
public sealed class BrainPickerTests
{
    private static BrainModel Model(string name, decimal? input, decimal? output = null, string provider = "claude", double? room = null, long? context = 200_000, bool usable = true, bool local = false) =>
        new(provider, name, provider + ":acct", provider + " account")
        {
            InputPerMillion = input,
            OutputPerMillion = output,
            Room = room,
            ContextWindow = context,
            Usable = usable,
            RunsLocally = local,
        };

    private static readonly BrainModel Brain = Model("big", 15m, 75m);

    [Fact]
    public void Triage_goes_to_the_cheapest_capable_model_by_a_blend_of_three_parts_input_to_one_part_output()
    {
        Assert.Equal(30m, Brain.BlendedPerMillion);                      // 15 x 0.75 + 75 x 0.25
        Assert.Equal(2.0m, Model("b", 1m, 5m).BlendedPerMillion);        // 0.75 + 1.25
        Assert.Equal(1.75m, Model("c", 1m).BlendedPerMillion);           // no output price: four times the input, 0.75 + 1.0
        Assert.Null(Model("d", null, 5m).BlendedPerMillion);
        Assert.Equal(0m, Model("e", null, null, local: true).BlendedPerMillion);
    }

    [Fact]
    public void The_pick_is_the_cheapest_usable_model_with_room_a_price_and_context()
    {
        var cheap = Model("small", 1m, 5m);
        var pick = BrainPicker.Pick(BrainJob.Triage, [Brain, cheap, Model("mid", 3m, 15m)], Brain);
        Assert.Same(cheap, pick.Model);
        Assert.False(pick.IsBrain);
        Assert.Equal("The cheapest capable model: small on claude account.", pick.Reason);
    }

    [Fact]
    public void A_model_that_cannot_be_used_or_has_no_room_or_no_price_or_too_little_context_is_never_picked()
    {
        var options = new[]
        {
            Model("cheap-but-signed-out", 0.1m, 0.4m, usable: false),
            Model("cheap-but-at-its-floor", 0.1m, 0.4m, room: 0),
            Model("cheap-but-over-its-floor", 0.1m, 0.4m, room: -2),
            Model("cheap-but-unpriced", null),
            Model("cheap-but-tiny-context", 0.1m, 0.4m, context: 8_000),
            Model("ok", 2m, 10m, room: 5),
        };
        var pick = BrainPicker.Pick(BrainJob.Triage, options, Brain);
        Assert.Equal("ok", pick.Model!.Model);
        Assert.Equal(BrainPicker.MinTriageContext, 16_000);
        Assert.Equal("exactly-16k", BrainPicker.Pick(BrainJob.Triage, [Model("exactly-16k", 0.5m, 1m, context: 16_000)], Brain).Model!.Model);
        Assert.Equal("unknown-context", BrainPicker.Pick(BrainJob.Triage, [Model("unknown-context", 0.5m, 1m, context: null)], Brain).Model!.Model);
    }

    [Fact]
    public void A_tie_goes_to_the_account_with_more_room_and_then_to_the_names_so_the_same_inputs_always_pick_the_same_model()
    {
        var roomy = Model("same", 1m, 5m, provider: "codex", room: 20);
        var tight = Model("same", 1m, 5m, provider: "claude", room: 3);
        Assert.Same(roomy, BrainPicker.Pick(BrainJob.Triage, [tight, roomy], Brain).Model);
        Assert.Same(roomy, BrainPicker.Pick(BrainJob.Triage, [roomy, tight], Brain).Model);

        var a = Model("alpha", 1m, 5m, provider: "claude");
        var b = Model("beta", 1m, 5m, provider: "claude");
        Assert.Same(a, BrainPicker.Pick(BrainJob.Triage, [b, a], Brain).Model);
        Assert.Same(a, BrainPicker.Pick(BrainJob.Triage, [a, b], Brain).Model);
        var known = Model("same", 1m, 5m, provider: "claude", room: 1);
        var unknown = Model("same", 1m, 5m, provider: "claude", room: null);
        Assert.Same(known, BrainPicker.Pick(BrainJob.Triage, [unknown, known], Brain).Model);   // a room that is known beats one that is not
    }

    [Fact]
    public void Triage_is_never_dearer_than_the_brain_so_when_the_brain_is_the_cheapest_it_reads_the_feed_itself()
    {
        var dearer = Model("dear", 30m, 150m);
        var cheapBrain = Model("brain", 1m, 5m);
        var pick = BrainPicker.Pick(BrainJob.Triage, [dearer, cheapBrain], cheapBrain);
        Assert.Same(cheapBrain, pick.Model);
        Assert.True(pick.IsBrain);
        Assert.Equal("The brain's model is already the cheapest that can read it.", pick.Reason);
        var same = Model("twin", 1m, 5m);
        Assert.True(BrainPicker.Pick(BrainJob.Triage, [same], cheapBrain).IsBrain);             // the same price: no needless switch, the cache stays warm
        Assert.Same(same, BrainPicker.Pick(BrainJob.Triage, [same], Model("unpriced-brain", null)).Model);   // a brain with no price does not hold a cheaper model back
    }

    [Fact]
    public void With_nothing_cheaper_or_nothing_at_all_the_brain_reads_it()
    {
        var pick = BrainPicker.Pick(BrainJob.Triage, [], Brain);
        Assert.Same(Brain, pick.Model);
        Assert.True(pick.IsBrain);
        Assert.Equal("No cheaper model is available, so the brain's own model reads it.", pick.Reason);
        var none = BrainPicker.Pick(BrainJob.Triage, [], null);
        Assert.Null(none.Model);
        Assert.Equal("No model is available to read it.", none.Reason);
    }

    [Fact]
    public void Text_that_may_not_leave_the_pc_goes_only_to_a_model_on_it_and_a_model_on_it_is_not_picked_for_being_free()
    {
        var local = Model("llama", null, null, provider: "local", local: true);
        var cloud = Model("small", 0.2m, 1m);
        Assert.Same(cloud, BrainPicker.Pick(BrainJob.Triage, [cloud, local], Brain).Model);        // free, but no judge of the owner's day
        var pick = BrainPicker.Pick(BrainJob.Triage, [cloud, local], Brain, requireLocal: true);
        Assert.Same(local, pick.Model);
        Assert.Equal(0m, local.BlendedPerMillion);

        var none = BrainPicker.Pick(BrainJob.Triage, [cloud], Brain, requireLocal: true);
        Assert.Null(none.Model);                                       // the cloud brain may not read it either
        Assert.Equal("No model on this PC can read it, and this text may not leave it.", none.Reason);
        var localBrain = Model("brain-local", null, null, provider: "local", local: true);
        Assert.Same(localBrain, BrainPicker.Pick(BrainJob.Triage, [cloud], localBrain, requireLocal: true).Model);
    }

    [Fact]
    public void Planning_and_merging_use_the_strong_model_which_is_the_brains_own()
    {
        var strong = BrainPicker.Pick(BrainJob.Planning, [Model("small", 1m, 5m)], Brain);
        Assert.Same(Brain, strong.Model);
        Assert.True(strong.IsBrain);
        Assert.Equal("Planning and merging use the strong model.", strong.Reason);
        Assert.Null(BrainPicker.Pick(BrainJob.Planning, [], null).Model);
        Assert.Equal("No model is chosen for the brain.", BrainPicker.Pick(BrainJob.Planning, [], null).Reason);
        Assert.Null(BrainPicker.Pick(BrainJob.Planning, [], Brain, requireLocal: true).Model);   // text that must stay here is not planned by a cloud model
    }
}
