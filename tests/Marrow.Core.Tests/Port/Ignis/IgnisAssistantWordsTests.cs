using Marrow.Core.Port.Ignis;

namespace Marrow.Core.Tests.Port.Ignis;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Ignis"; ED-NEW-dextra-as15-8): the popover's one sentence says what is true of the chats Dextra owns. The home thread
// is never kept warm; a mission's chat is left alone until the mission is done. Every other line is unchanged.
[Trait("Decision", "ED-NEW-dextra-as15-8")]
[Trait("Feature", "AS-15")]
public sealed class IgnisAssistantWordsTests
{
    [Fact]
    public void The_home_thread_s_line_says_it_is_not_kept_warm_and_that_it_is_still_tidied_only_when_it_is()
    {
        Assert.Equal("This is your own conversation, so Ignis never keeps it warm. After a break the first reply is a little slower.", IgnisWords.Next(IgnisNext.HomeThread));
        Assert.Equal(IgnisWords.Next(IgnisNext.HomeThread) + " It still tidies up between tasks.", IgnisWords.Next(IgnisNext.HomeThreadTidy));
        Assert.DoesNotContain("keeps the quick memory warm", IgnisWords.Next(IgnisNext.HomeThread), StringComparison.Ordinal);
        Assert.DoesNotContain("keeps the quick memory warm", IgnisWords.Next(IgnisNext.HomeThreadTidy), StringComparison.Ordinal);
    }

    [Fact]
    public void A_task_s_chat_line_says_it_is_left_alone_until_the_task_is_done()
    {
        Assert.Equal("A task is still running in this chat, so Ignis leaves it alone until the task is done.", IgnisWords.Next(IgnisNext.MissionRunning));
        Assert.Equal("Today Ignis saved $3.18. A task is still running in this chat, so Ignis leaves it alone until the task is done.", IgnisWords.Summary(3.18m, IgnisNext.MissionRunning));
    }

    [Fact]
    public void Every_line_has_words_and_none_of_the_older_ones_changed()
    {
        foreach (var next in Enum.GetValues<IgnisNext>()) Assert.False(string.IsNullOrWhiteSpace(IgnisWords.Next(next)), next.ToString());
        Assert.Equal("It keeps the quick memory warm while you work and tidies up the chat before it gets costly, always between tasks.", IgnisWords.Next(IgnisNext.WarmAndTidy));
        Assert.Equal("Ignis is off in this chat, so it isn’t saving anything right now.", IgnisWords.Next(IgnisNext.Off));
    }
}
