using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/Provider.swift (B3 52bc792), :202-214, :293-323.
// Expectations below are transcribed from Swift, not from the C# implementation.
// PR-COMPOSER-042 explicitly cites suggestions(:306-323). The three identity/title members have
// no requirement in the Provider packets; their direct source parity uses the requested R-FILE-1 trait.
public sealed class ProviderStubsTests
{
    [Trait("Rule", "R-FILE-1")] // Core/Models/Provider.swift:202-206
    [Theory]
    [InlineData(WorkspaceMode.Local, "local")]
    [InlineData(WorkspaceMode.Worktree, "worktree")]
    public void Workspace_id_is_its_exact_raw_value(WorkspaceMode mode, string expected) =>
        Assert.Equal(expected, mode.Id);

    [Trait("Rule", "R-FILE-1")] // Core/Models/Provider.swift:208-213
    [Theory]
    [InlineData(WorkspaceMode.Local, "Local")]
    [InlineData(WorkspaceMode.Worktree, "New worktree")]
    public void Workspace_title_is_the_exact_switch_literal(WorkspaceMode mode, string expected) =>
        Assert.Equal(expected, mode.Title);

    [Trait("Rule", "R-FILE-1")] // Core/Models/Provider.swift:294-299
    [Theory]
    [InlineData("Review")]
    [InlineData("/cafe\u0301")]
    [InlineData("")]
    public void Command_id_returns_the_stored_name_without_rewriting(string name)
    {
        var command = new SlashCommand { Name = name, Detail = "different", IsBuiltIn = true, SkillPath = "/skill" };
        Assert.Equal(name, command.Id);
    }

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:306-323
    [Fact]
    public void Empty_commands_return_no_suggestions_even_with_query_and_recent_names() =>
        Assert.Empty(SlashCommand.Suggestions([], "rev", ["review"]));

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:307,314-315,318-322
    [Theory]
    [InlineData("VIEW", "preview,Review")]
    [InlineData("rEv", "Review,revise,preview")]
    [InlineData("", "other,preview,Review,revise")]
    [InlineData("rvw", "")]
    [InlineData("absent", "")]
    public void Matching_is_lowercased_contiguous_substring_not_fuzzy(string query, string expected)
    {
        SlashCommand[] commands = [Command("revise"), Command("Review"), Command("other"), Command("preview")];
        var actual = SlashCommand.Suggestions(commands, query, []);
        Assert.Equal(expected.Length == 0 ? [] : expected.Split(','), actual.Select(c => c.Name));
    }

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:308,316,318-322
    [Fact]
    public void Prefix_precedes_recency_then_first_recent_position_then_lowercased_name()
    {
        SlashCommand[] commands = [Command("preview"), Command("review"), Command("revision"), Command("rev"), Command("revert")];
        var actual = SlashCommand.Suggestions(commands, "rev", ["preview", "revision", "review", "revision"]);
        // Prefixes revision(rank 1), review(rank 2), unseen rev then revert; substring preview(rank 0) last.
        Assert.Equal(["revision", "review", "rev", "revert", "preview"], actual.Select(c => c.Name));
    }

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:308,316,320
    [Fact]
    public void Repeated_recent_name_uses_the_earliest_rank_and_unseen_names_follow()
    {
        SlashCommand[] commands = [Command("alpha"), Command("zulu"), Command("beta")];
        var actual = SlashCommand.Suggestions(commands, "", ["zulu", "alpha", "zulu"]);
        Assert.Equal(["zulu", "alpha", "beta"], actual.Select(c => c.Name));
    }

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:308-316,322
    [Fact]
    public void First_duplicate_wins_with_all_metadata_preserved_and_name_keys_are_case_sensitive()
    {
        var first = new SlashCommand { Name = "Review", Detail = "first", IsBuiltIn = true, SkillPath = "/first" };
        var duplicate = new SlashCommand { Name = "Review", Detail = "second", SkillPath = "/second" };
        var lower = new SlashCommand { Name = "review", Detail = "lower" };
        var actual = SlashCommand.Suggestions([first, duplicate, lower], "rev", ["review"]);
        Assert.Equal(2, actual.Count);
        Assert.Same(lower, actual[0]); // Original-case recent key ranks only review.
        Assert.Same(first, actual[1]); // The first Review survives, unchanged.
        Assert.Equal("first", actual[1].Detail);
        Assert.True(actual[1].IsBuiltIn);
        Assert.Equal("/first", actual[1].SkillPath);
    }

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:308-316,320
    [Fact]
    public void Canonically_equal_names_deduplicate_match_and_share_the_recent_rank()
    {
        var first = Command("caf\u00e9");
        var duplicate = Command("cafe\u0301");
        var other = Command("caf\u00e9teria");
        var actual = SlashCommand.Suggestions([other, first, duplicate], "CAFE\u0301", ["cafe\u0301"]);
        Assert.Equal(2, actual.Count);
        Assert.Same(first, actual[0]); // Canonical recent key gives this rank 0.
        Assert.Same(other, actual[1]);
        Assert.Equal("caf\u00e9", actual[0].Name); // Stored spelling is never normalized.
    }

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:314-316
    [Theory]
    [InlineData("e\u0301", "e", false)]
    [InlineData("a\r\nb", "\n", false)]
    [InlineData("👩‍💻", "👩", false)]
    [InlineData("a\r\nb", "\r\n", true)]
    [InlineData("x\u00e9y", "e\u0301", true)]
    public void Substring_matching_compares_whole_Swift_characters_canonically(string name, string query, bool matches)
    {
        var command = Command(name);
        var actual = SlashCommand.Suggestions([command], query, []);
        if (matches) Assert.Same(command, Assert.Single(actual));
        else Assert.Empty(actual);
    }

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:318-322
    [Fact]
    public void Alphabetical_fallback_uses_Unicode_scalar_order_instead_of_UTF16_or_culture_order()
    {
        var actual = SlashCommand.Suggestions([Command("😀"), Command("\uE000"), Command("z"), Command("\u00e9")], "", []);
        // NFC scalar order U+007A, U+00E9, U+E000, U+1F600 (:320, R-STR-2).
        Assert.Equal(["z", "\u00e9", "\uE000", "😀"], actual.Select(c => c.Name));
    }

    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:313-322
    [Fact]
    public void Equal_lowercased_sort_keys_keep_input_order_without_merging_distinct_case_names()
    {
        SlashCommand[] commands = [Command("a"), Command("A"), Command("b"), Command("B")];
        var actual = SlashCommand.Suggestions(commands, "", []);
        Assert.Equal(["a", "A", "b", "B"], actual.Select(c => c.Name)); // Stable Swift sort, R-COLL-1.
    }

    private static SlashCommand Command(string name) => new() { Name = name, Detail = "fixture" };
}
