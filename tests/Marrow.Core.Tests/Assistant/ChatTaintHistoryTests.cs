using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.DelegateSamples;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 12.4 "Taint ... is cleared only by the owner's own message"; ED-NEW-dextra-as22-7): what an owner's chat has read
// since the owner last wrote in it, placed with the same wiring a mission's chat is. A delegate in a chat that read something untrusted
// answers nothing.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-7")]
public sealed class ChatTaintHistoryTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "nth-as22-history");

    private static Taint Since(params TimelineItem[] items) => ChatTaintHistory.Since(items, Folder(Root), ProviderKind.Claude);

    [Fact]
    public void An_empty_timeline_and_one_of_clean_work_are_clean()
    {
        Assert.False(Since().IsTainted);
        Assert.False(Since(User(), Tool(ToolCall.Kind.Read, Path.Combine(Root, "src", "a.cs")), Tool(ToolCall.Kind.Edit, Path.Combine(Root, "src", "a.cs"))).IsTainted);
    }

    [Fact]
    public void A_web_page_a_connector_and_a_file_outside_the_project_taint_the_chat()
    {
        Assert.Equal(ContentOrigin.Web, Assert.Single(Since(User(), Tool(ToolCall.Kind.Web, "https://news.example/a")).Sources).Origin);
        Assert.Equal(ContentOrigin.Connector, Assert.Single(Since(User(), Tool(ToolCall.Kind.Mcp, "gmail · read_message")).Sources).Origin);
        Assert.True(Since(User(), Tool(ToolCall.Kind.Read, Path.Combine(Path.GetTempPath(), "elsewhere", "notes.txt"))).IsTainted);
        Assert.True(Since(User(), Tool(ToolCall.Kind.Read, Path.Combine(Root, "docs", "brief.pdf"))).IsTainted);
    }

    [Fact]
    public void The_owners_own_message_clears_it_and_only_what_came_after_counts()
    {
        var web = Tool(ToolCall.Kind.Web, "https://news.example/a");
        Assert.False(Since(User("first"), web, User("the owner wrote again")).IsTainted);
        Assert.True(Since(User("first"), User("second"), web).IsTainted);
        Assert.True(Since(web).IsTainted);
        Assert.True(Since(User("first"), web, User("second"), web).IsTainted);
    }

    [Fact]
    public void A_sub_agents_reads_are_not_in_the_history_so_a_sub_agent_counts_as_having_read_something()
    {
        var taint = Since(User(), Tool(ToolCall.Kind.Agent, "Explore the repo"));
        Assert.Equal(ChatTaintHistory.SubAgent, taint);
    }

    [Fact]
    public void A_call_the_owner_declined_read_nothing()
    {
        Assert.False(Since(User(), Tool(ToolCall.Kind.Web, "https://news.example/a", status: ToolCall.Status.Declined)).IsTainted);
        Assert.True(Since(User(), Tool(ToolCall.Kind.Web, "https://news.example/a", status: ToolCall.Status.Failed)).IsTainted);
    }

    [Fact]
    public void More_calls_than_are_placed_count_as_untrusted_and_the_taint_stays_capped()
    {
        var items = new List<TimelineItem> { User() };
        for (var i = 0; i < ChatTaintHistory.MaxCalls + 5; i++) items.Add(Tool(ToolCall.Kind.Read, Path.Combine(Root, "src", "f" + i + ".cs")));
        Assert.Equal(ChatTaintHistory.TooMany, ChatTaintHistory.Since(items, Folder(Root), ProviderKind.Claude));

        var many = new List<TimelineItem> { User() };
        for (var i = 0; i < 40; i++) many.Add(Tool(ToolCall.Kind.Web, "https://site" + i + ".example/"));
        Assert.True(Since([.. many]).Sources.Count <= TaintWiring.MaxSources);
    }

    [Fact]
    public void A_tool_nth_does_not_know_taints_like_a_mission_chats_does()
    {
        Assert.True(Since(User(), Tool(ToolCall.Kind.Other, "SomeNewTool")).IsTainted);
        Assert.False(Since(User(), Tool(ToolCall.Kind.Other, "TodoWrite")).IsTainted);
    }
}
