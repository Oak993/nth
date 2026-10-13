using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "Choose a delegate ... globally, per project or per chat"; ED-NEW-dextra-as22-9): who answers and the chats that chose
// for themselves are kept in the one "dextra" settings key. The defaults leave both out, a settings value from before this unit reads as the defaults, a
// damaged entry is skipped, and the chat choices are bounded.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-9")]
public sealed class DelegateSettingsTests
{
    private static readonly Guid ChatA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ChatB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    [Fact]
    public void The_defaults_write_neither_key_and_a_settings_value_from_before_reads_as_off()
    {
        var json = DextraSettings.ToJson(DextraPreferences.Default);
        Assert.DoesNotContain("delegateWith", json, StringComparison.Ordinal);
        Assert.DoesNotContain("delegateChats", json, StringComparison.Ordinal);
        var before = DextraSettings.FromJson("{\"on\":true,\"firstRunDone\":true,\"delegate\":\"off\"}");
        Assert.Equal(DelegateScope.Off, before.Delegate);
        Assert.Null(before.DelegateWith);
        Assert.Empty(before.DelegateChats);
    }

    [Fact]
    public void Who_answers_and_the_chat_choices_round_trip()
    {
        var chosen = DextraPreferences.Default with
        {
            Delegate = DelegateScope.ThisProject,
            DelegateWith = new DelegateWho(ProviderKind.Codex, "gpt-x"),
            DelegateChats = new ValueList<DelegateChatChoice>([new(ChatA, true), new(ChatB, false)]),
        };
        var again = DextraSettings.FromJson(DextraSettings.ToJson(chosen));
        Assert.Equal(chosen, again);
        Assert.Equal(new DelegateWho(ProviderKind.Codex, "gpt-x"), again.DelegateWith);
        Assert.Equal([new DelegateChatChoice(ChatA, true), new DelegateChatChoice(ChatB, false)], again.DelegateChats);
        Assert.Null(DextraSettings.FromJson(DextraSettings.ToJson(chosen with { DelegateWith = new DelegateWho(ProviderKind.Claude) })).DelegateWith!.Model);
    }

    [Fact]
    public void A_damaged_who_or_chat_entry_is_skipped_and_the_rest_is_kept()
    {
        var json = "{\"delegate\":\"everywhere\",\"delegateWith\":{\"provider\":\"NoSuchProvider\"},\"delegateChats\":["
            + "{\"chat\":\"" + ChatA.ToString("D") + "\",\"on\":true},{\"chat\":\"nope\",\"on\":true},{\"chat\":\"" + ChatB.ToString("D") + "\"},5,"
            + "{\"chat\":\"" + ChatB.ToString("D") + "\",\"on\":false}]}";
        var read = DextraSettings.FromJson(json);
        Assert.Equal(DelegateScope.Everywhere, read.Delegate);
        Assert.Null(read.DelegateWith);
        Assert.Equal([new DelegateChatChoice(ChatA, true), new DelegateChatChoice(ChatB, false)], read.DelegateChats);
        Assert.Null(DextraSettings.FromJson("{\"delegateWith\":[1],\"delegateChats\":\"x\"}").DelegateWith);
    }

    [Fact]
    public void A_chats_menu_choice_is_set_changed_and_cleared_and_only_the_newest_are_kept()
    {
        var settings = new DextraSettings(new InMemorySettingsStore());
        settings.SetDelegateChat(ChatA, true);
        Assert.True(DelegateReach.ChatChoice(settings.Value, ChatA));
        settings.SetDelegateChat(ChatA, false);
        Assert.False(DelegateReach.ChatChoice(settings.Value, ChatA));
        settings.SetDelegateChat(ChatA, null);
        Assert.Null(DelegateReach.ChatChoice(settings.Value, ChatA));
        Assert.Empty(settings.Value.DelegateChats);

        var first = Guid.NewGuid();
        settings.SetDelegateChat(first, true);
        for (var i = 0; i < DextraSettings.DelegateChatsKept; i++) settings.SetDelegateChat(Guid.NewGuid(), i % 2 == 0);
        Assert.Equal(DextraSettings.DelegateChatsKept, settings.Value.DelegateChats.Count);
        Assert.Null(DelegateReach.ChatChoice(settings.Value, first));             // the oldest was let go
        Assert.Equal(DextraSettings.DelegateChatsKept, DextraSettings.FromJson(DextraSettings.ToJson(settings.Value)).DelegateChats.Count);
    }

    [Fact]
    public void A_settings_change_is_stored_and_announced()
    {
        var store = new InMemorySettingsStore();
        var settings = new DextraSettings(store);
        var changed = 0;
        settings.Changed += () => changed++;
        settings.SetDelegateChat(ChatA, true);
        Assert.Equal(1, changed);
        Assert.Contains(ChatA.ToString("D"), (string)store.Object(DextraSettings.SettingsKey)!, StringComparison.Ordinal);
    }
}
