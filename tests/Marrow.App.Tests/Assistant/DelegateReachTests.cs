using Marrow.App.Assistant;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "globally, per project or per chat. It is off until the owner turns it on"; ED-NEW-dextra-as22-9): where the delegate
// answers. A chat's own choice from its menu beats everything; without one, Everywhere covers every chat and This project only the named project (until a
// project is named, the one Dextra's home thread works in). With everything at its default the delegate answers nowhere.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-9")]
public sealed class DelegateReachTests
{
    private static readonly Guid ProjectA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ProjectB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid Chat = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    [Fact]
    public void Off_by_default_everywhere()
    {
        var defaults = DextraPreferences.Default;
        Assert.Equal(DelegateScope.Off, defaults.Delegate);
        Assert.Null(defaults.DelegateWith);
        Assert.Empty(defaults.DelegateChats);
        Assert.False(DelegateReach.IsOn(defaults, Chat, ProjectA, ProjectA));
        Assert.Null(DelegateReach.ChatChoice(defaults, Chat));
    }

    [Fact]
    public void Everywhere_covers_every_chat_and_this_project_only_covers_the_named_one()
    {
        var everywhere = DextraPreferences.Default with { Delegate = DelegateScope.Everywhere };
        Assert.True(DelegateReach.IsOn(everywhere, Chat, ProjectA, null));
        Assert.True(DelegateReach.IsOn(everywhere, Chat, ProjectB, null));

        var named = DextraPreferences.Default with { Delegate = DelegateScope.ThisProject, DelegateProject = ProjectA };
        Assert.True(DelegateReach.IsOn(named, Chat, ProjectA, ProjectB));
        Assert.False(DelegateReach.IsOn(named, Chat, ProjectB, ProjectB));
    }

    [Fact]
    public void Until_a_project_is_named_this_project_only_means_the_one_the_home_thread_works_in()
    {
        var unnamed = DextraPreferences.Default with { Delegate = DelegateScope.ThisProject };
        Assert.True(DelegateReach.IsOn(unnamed, Chat, ProjectA, ProjectA));
        Assert.False(DelegateReach.IsOn(unnamed, Chat, ProjectB, ProjectA));
        Assert.False(DelegateReach.IsOn(unnamed, Chat, ProjectA, null));          // nothing to mean: off
    }

    [Fact]
    public void A_chats_own_choice_beats_the_scope_both_ways()
    {
        var on = DextraPreferences.Default with { DelegateChats = new ValueList<DelegateChatChoice>([new(Chat, true)]) };
        Assert.True(DelegateReach.IsOn(on, Chat, ProjectA, null));                // the scope is off, the chat said yes
        Assert.False(DelegateReach.IsOn(on, Guid.NewGuid(), ProjectA, null));     // another chat is not asked
        Assert.True(DelegateReach.ChatChoice(on, Chat));

        var off = DextraPreferences.Default with
        {
            Delegate = DelegateScope.Everywhere,
            DelegateChats = new ValueList<DelegateChatChoice>([new(Chat, false)]),
        };
        Assert.False(DelegateReach.IsOn(off, Chat, ProjectA, null));              // everything is on, this chat said no
        Assert.True(DelegateReach.IsOn(off, Guid.NewGuid(), ProjectA, null));
        Assert.False(DelegateReach.ChatChoice(off, Chat));
    }
}
