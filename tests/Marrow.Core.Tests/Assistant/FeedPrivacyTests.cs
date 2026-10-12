using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

// AS-2 (DESIGN.md 5.4 Privacy across providers): the rules that decide how much of a chat's words the brain may see,
// and how the facts of a chat are read (ED-NEW-dextra-as3a-1 item 5). Expected values are written out here.
public sealed class FeedPrivacyTests
{
    private static FeedBrain Brain(ProviderKind? provider = ProviderKind.Claude, bool local = false, bool same = true, bool also = false) =>
        new(provider, local, same, also);

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    [InlineData(ProviderKind.Claude, false, false, ProviderKind.Claude, true, false, FeedTextAccess.Full)]
    [InlineData(ProviderKind.Codex, false, false, ProviderKind.Claude, true, false, FeedTextAccess.MetadataOnly)]
    [InlineData(ProviderKind.Codex, false, false, ProviderKind.Claude, true, true, FeedTextAccess.Full)]
    [InlineData(ProviderKind.Claude, false, false, ProviderKind.Claude, false, true, FeedTextAccess.MetadataOnly)]
    [InlineData(ProviderKind.Codex, false, false, ProviderKind.Claude, false, true, FeedTextAccess.MetadataOnly)]
    public void Another_providers_chat_is_passed_on_only_when_the_owner_allowed_it(ProviderKind chat, bool local, bool privateAccount,
        ProviderKind brain, bool same, bool also, FeedTextAccess expected) =>
        Assert.Equal(expected, FeedPrivacy.Decide(FeedSamples.Facts(provider: chat, local: local, privateAccount: privateAccount), Brain(brain, false, same, also)));

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-3")]
    [InlineData(false, true, FeedTextAccess.MetadataOnly)]
    [InlineData(true, true, FeedTextAccess.Full)]
    [InlineData(false, false, FeedTextAccess.MetadataOnly)]
    public void A_chat_on_a_local_model_is_never_summarised_to_a_cloud_brain(bool brainRunsLocally, bool alsoOtherProviders, FeedTextAccess expected) =>
        Assert.Equal(expected, FeedPrivacy.Decide(FeedSamples.Facts(provider: ProviderKind.Local, local: true),
            Brain(brainRunsLocally ? ProviderKind.Local : ProviderKind.Claude, brainRunsLocally, true, alsoOtherProviders)));

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-3")]
    [InlineData(false, FeedTextAccess.MetadataOnly)]
    [InlineData(true, FeedTextAccess.Full)]
    public void A_chat_on_a_private_account_is_never_summarised_to_a_cloud_brain_even_on_the_same_provider(bool brainRunsLocally, FeedTextAccess expected) =>
        Assert.Equal(expected, FeedPrivacy.Decide(FeedSamples.Facts(provider: ProviderKind.Claude, privateAccount: true),
            Brain(ProviderKind.Claude, brainRunsLocally, true, true)));

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-3")]
    public void A_local_brain_reads_a_cloud_chat_only_by_the_ordinary_provider_rule()
    {
        var chat = FeedSamples.Facts(provider: ProviderKind.Claude);
        Assert.Equal(FeedTextAccess.MetadataOnly, FeedPrivacy.Decide(chat, Brain(ProviderKind.Local, local: true)));
        Assert.Equal(FeedTextAccess.Full, FeedPrivacy.Decide(chat, Brain(ProviderKind.Local, local: true, also: true)));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_hidden_chat_sends_nothing_whatever_the_switches_say()
    {
        var hidden = FeedSamples.Facts(hidden: true);
        Assert.Equal(FeedTextAccess.Hidden, FeedPrivacy.Decide(hidden, Brain(also: true)));
        Assert.Equal(FeedTextAccess.Hidden, FeedPrivacy.Decide(hidden, Brain(ProviderKind.Local, local: true, also: true)));
        Assert.Equal(FeedTextAccess.Hidden, FeedPrivacy.Decide(hidden, FeedBrain.Unknown));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_brain_that_is_not_set_up_yet_sees_metadata_only()
    {
        Assert.Equal(FeedTextAccess.MetadataOnly, FeedPrivacy.Decide(FeedSamples.Facts(), FeedBrain.Unknown));
        Assert.Equal(FeedTextAccess.MetadataOnly, FeedPrivacy.Decide(FeedSamples.Facts(), new FeedBrain(null, false, true, true)));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_chat_marked_hidden_is_hidden_and_an_unmarked_one_is_not()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var plain = FeedSamples.Chat();
        var marked = FeedSamples.Chat(hidden: true);
        Assert.False(FeedChatFacts.From(plain, FeedSamples.Lookup(plain)).Hidden);
        Assert.True(FeedChatFacts.From(marked, FeedSamples.Lookup(marked)).Hidden);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_mission_chat_or_an_assistant_chat_cannot_be_hidden()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var mission = FeedSamples.Chat(hidden: true, mission: Guid.NewGuid());
        var home = FeedSamples.Chat(hidden: true, assistant: Guid.NewGuid());
        Assert.False(FeedChatFacts.From(mission, FeedSamples.Lookup(mission)).Hidden);
        Assert.False(FeedChatFacts.From(home, FeedSamples.Lookup(home)).Hidden);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_head_of_a_chat_hidden_after_the_head_was_made_is_hidden_too()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var lead = FeedSamples.Chat(hidden: true);
        var head = FeedSamples.Chat(parent: lead.Id);
        var grandHead = FeedSamples.Chat(parent: head.Id);
        var lookup = FeedSamples.Lookup(lead, head, grandHead);
        Assert.True(FeedChatFacts.From(head, lookup).Hidden);
        Assert.True(FeedChatFacts.From(grandHead, lookup).Hidden);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_head_of_a_visible_chat_stays_visible_and_a_mission_head_ignores_a_hidden_parent_mark()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var lead = FeedSamples.Chat();
        var head = FeedSamples.Chat(parent: lead.Id);
        var hiddenParent = FeedSamples.Chat(hidden: true);
        var missionHead = FeedSamples.Chat(parent: hiddenParent.Id, mission: Guid.NewGuid());
        Assert.False(FeedChatFacts.From(head, FeedSamples.Lookup(lead, head)).Hidden);
        Assert.False(FeedChatFacts.From(missionHead, FeedSamples.Lookup(hiddenParent, missionHead)).Hidden);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_mission_chat_in_the_chain_does_not_carry_its_own_hidden_mark_up_to_its_heads()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var lead = FeedSamples.Chat(hidden: true, mission: Guid.NewGuid());
        var head = FeedSamples.Chat(parent: lead.Id);
        Assert.False(FeedChatFacts.From(head, FeedSamples.Lookup(lead, head)).Hidden);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_loop_or_a_missing_parent_in_the_data_ends_the_walk_without_hanging()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var a = FeedSamples.Chat();
        var b = FeedSamples.Chat(parent: a.Id);
        var loopedA = a with { ParentThreadID = b.Id };
        var orphan = FeedSamples.Chat(parent: Guid.NewGuid());
        Assert.False(FeedChatFacts.From(loopedA, FeedSamples.Lookup(loopedA, b)).Hidden);
        Assert.False(FeedChatFacts.From(orphan, FeedSamples.Lookup(orphan)).Hidden);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-3")]
    public void A_chat_on_the_local_provider_counts_as_a_local_model_and_the_account_mark_is_asked_of_the_caller()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var local = FeedSamples.Chat(provider: ProviderKind.Local);
        var cloud = FeedSamples.Chat();
        Assert.True(FeedChatFacts.From(local, FeedSamples.Lookup(local)).IsLocalModel);
        Assert.False(FeedChatFacts.From(cloud, FeedSamples.Lookup(cloud)).IsLocalModel);
        Assert.False(FeedChatFacts.From(cloud, FeedSamples.Lookup(cloud)).IsPrivateAccount);
        Assert.True(FeedChatFacts.From(cloud, FeedSamples.Lookup(cloud), thread => thread.Id == cloud.Id).IsPrivateAccount);
    }
}
