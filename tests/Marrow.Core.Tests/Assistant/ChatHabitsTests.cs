using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 5.2 "the owner doing the same thing by hand three times", 5.4; ED-NEW-dextra-as11-4): the owner's chats read as habits. Only his own top-level
// chats count; a chat hidden from the Assistant is not read at all; one on a local model or a private account counts but stays local; titles are one kind when they say
// the same without their numbers; a kind needs three starts and a project nth knows.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-4")]
public sealed class ChatHabitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid Store = new("11111111-1111-1111-1111-111111111111");

    private static ChatThread Chat(string title, int daysAgo, Guid? project = null, ProviderKind provider = ProviderKind.Claude) =>
        new(project ?? Store, provider, "model-a", null, RuntimeMode.Supervised) { Title = title, CreatedAt = Now.AddDays(-daysAgo) };

    private static FeedChatFacts Plain(ChatThread chat) => new(chat.Title, chat.Provider, false, chat.Provider == ProviderKind.Local, false);

    private static string? Name(Guid id) => id == Store ? "Store app" : null;

    private static IReadOnlyList<ChatHabit> Habits(IEnumerable<ChatThread> chats, Func<ChatThread, FeedChatFacts>? facts = null) => ChatHabits.Of(chats, Now, facts ?? Plain, Name);

    [Fact]
    public void Titles_that_say_the_same_without_their_numbers_are_one_kind_and_three_starts_make_a_habit()
    {
        using var runtime = new TestRuntime();
        var chats = new[] { Chat("Re-price listings 14", 4), Chat("re-price listings 21", 11), Chat("Re-price listings (28)", 18), Chat("Something else entirely", 2) };
        var habit = Assert.Single(Habits(chats));
        Assert.Equal("Re-price listings 14", habit.Label);                       // the newest title is the label
        Assert.Equal(3, habit.Starts.Count);
        Assert.Equal(Store, habit.ProjectId);
        Assert.Equal("Store app", habit.ProjectName);
        Assert.False(habit.LocalOnly);
        Assert.Equal(habit.Starts.Order(), habit.Starts);
        Assert.Equal(Store.ToString("N") + ":re-price-listings", habit.Key);
    }

    [Fact]
    public void Fewer_than_three_starts_blank_titles_and_unknown_projects_are_not_habits()
    {
        using var runtime = new TestRuntime();
        Assert.Empty(Habits([Chat("Re-price listings", 4), Chat("Re-price listings", 11)]));
        Assert.Empty(Habits([Chat("New chat", 4), Chat("New chat", 11), Chat("New chat", 18)]));
        Assert.Empty(Habits([Chat("ok", 4), Chat("ok", 11), Chat("ok", 18)]));
        var other = Guid.NewGuid();
        Assert.Empty(Habits([Chat("Re-price listings", 4, other), Chat("Re-price listings", 11, other), Chat("Re-price listings", 18, other)]));
    }

    [Fact]
    public void The_same_title_in_two_projects_is_two_kinds()
    {
        using var runtime = new TestRuntime();
        var elsewhere = Guid.NewGuid();
        var chats = new[] { Chat("Weekly report", 4), Chat("Weekly report", 11), Chat("Weekly report", 18), Chat("Weekly report", 5, elsewhere), Chat("Weekly report", 12, elsewhere) };
        Assert.Single(Habits(chats));
    }

    [Fact]
    public void Only_the_owners_own_top_level_chats_in_the_last_five_weeks_count()
    {
        using var runtime = new TestRuntime();
        var mission = Chat("Weekly report", 4) with { MissionID = Guid.NewGuid() };
        var home = Chat("Weekly report", 11) with { AssistantID = Guid.NewGuid() };
        var head = Chat("Weekly report", 18) with { ParentThreadID = Guid.NewGuid() };
        var archived = Chat("Weekly report", 3) with { IsArchived = true };
        var old = Chat("Weekly report", 40);
        var future = Chat("Weekly report", -2);
        Assert.Empty(Habits([mission, home, head, archived, old, future, Chat("Weekly report", 7), Chat("Weekly report", 14)]));
    }

    [Fact]
    public void A_chat_hidden_from_the_Assistant_is_not_read_at_all()
    {
        using var runtime = new TestRuntime();
        var chats = new[] { Chat("Weekly report", 4), Chat("Weekly report", 11), Chat("Weekly report", 18), Chat("Weekly report", 25) };
        var hidden = Habits(chats, c => c.CreatedAt == chats[3].CreatedAt ? Plain(c) with { Hidden = true } : Plain(c));
        Assert.Equal(3, Assert.Single(hidden).Starts.Count);
        Assert.Empty(Habits(chats, c => Plain(c) with { Hidden = true }));
    }

    [Fact]
    public void A_chat_on_a_local_model_or_a_private_account_counts_but_keeps_the_habit_local()
    {
        using var runtime = new TestRuntime();
        var chats = new[] { Chat("Weekly report", 4), Chat("Weekly report", 11), Chat("Weekly report", 18, provider: ProviderKind.Local) };
        Assert.True(Assert.Single(Habits(chats)).LocalOnly);
        var viaAccount = new[] { Chat("Weekly report", 4), Chat("Weekly report", 11), Chat("Weekly report", 18) };
        Assert.True(Assert.Single(Habits(viaAccount, c => Plain(c) with { IsPrivateAccount = c.CreatedAt == viaAccount[2].CreatedAt })).LocalOnly);
    }

    [Theory]
    [InlineData("Re-price listings 14", "re price listings")]
    [InlineData("Fix: the login/timeout again please now", "fix the login timeout")]
    [InlineData("  Numbers 123 only 456  ", "numbers only")]
    [InlineData("1234", "")]
    public void The_kind_of_a_title_is_its_first_four_words_in_lower_case_without_numbers_or_marks(string title, string kind)
    {
        Assert.Equal(kind, ChatHabits.Kind(title));
    }

    [Fact]
    public void A_title_is_data_a_secret_in_a_label_is_masked_and_a_long_one_is_cut()
    {
        using var runtime = new TestRuntime();
        var secret = "sk-" + new string('a', 40);
        var chats = new[] { Chat("Deploy with key " + secret, 4), Chat("Deploy with key " + secret, 11), Chat("Deploy with key " + secret, 18) };
        var habit = Assert.Single(Habits(chats));
        Assert.DoesNotContain(secret, habit.Label, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, habit.Key, StringComparison.Ordinal);
        var long3 = new[] { Chat(new string('x', 400), 4), Chat(new string('x', 400), 11), Chat(new string('x', 400), 18) };
        Assert.True(Assert.Single(Habits(long3)).Label.Length <= ChatHabits.LabelChars + 1);
    }
}
