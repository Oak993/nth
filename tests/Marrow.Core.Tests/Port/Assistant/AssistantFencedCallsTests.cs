using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-4 (DESIGN.md rev 5, 0 point 12, 15.5 "Transport"; ED-NEW-dextra-as4-6): the fenced-text fallback's reader.
// Only a closed top-level block whose info line starts with nth-dextra is a call; an example quoted inside another fence
// or a quote, an indented code block, and a block left open are text.
public sealed class AssistantFencedCallsTests
{
    private const string Fence = "```";

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-6")]
    public void A_closed_block_names_its_tool_and_holds_its_arguments()
    {
        var answer = "I will look it up.\n\n" + Fence + "nth-dextra recall\n{\"query\": \"invoice address\"}\n" + Fence + "\nDone for now.";
        var call = Assert.Single(AssistantFencedCalls.Parse(answer));
        Assert.Equal(new AssistantFencedCall("recall", "{\"query\": \"invoice address\"}", 3), call);

        var crlf = Assert.Single(AssistantFencedCalls.Parse("x\r\n  " + Fence + " NTH-DEXTRA   usage_status  \r\n{}\r\n   " + Fence + "````\r\n"));
        Assert.Equal(("usage_status", "{}"), (crlf.Tool, crlf.Body));
        var tilde = Assert.Single(AssistantFencedCalls.Parse("~~~~nth-dextra report\n{\"summary\": \"s\", \"status\": \"done\"}\n~~~~~\n"));
        Assert.Equal("report", tilde.Tool);
        var empty = Assert.Single(AssistantFencedCalls.Parse(Fence + "nth-dextra list_sessions\n" + Fence));
        Assert.Equal("", empty.Body);
        Assert.Equal("", Assert.Single(AssistantFencedCalls.Parse(Fence + "nth-dextra\n{}\n" + Fence)).Tool);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-6")]
    public void Other_blocks_quoted_examples_indented_code_and_open_blocks_are_text()
    {
        Assert.Empty(AssistantFencedCalls.Parse(null));
        Assert.Empty(AssistantFencedCalls.Parse("No tools here."));
        Assert.Empty(AssistantFencedCalls.Parse(Fence + "json\n{\"tool\": \"nth-dextra\"}\n" + Fence));
        Assert.Empty(AssistantFencedCalls.Parse(Fence + "nth-dextrax recall\n{}\n" + Fence));
        // An example inside a longer fence is content of that fence.
        Assert.Empty(AssistantFencedCalls.Parse("````markdown\n" + Fence + "nth-dextra forget\n{\"notes\": [\"n-1\"]}\n" + Fence + "\n````"));
        // A quote, and a block indented four spaces (code), are not fences.
        Assert.Empty(AssistantFencedCalls.Parse("> " + Fence + "nth-dextra forget\n> {\"notes\": [\"n-1\"]}\n> " + Fence));
        Assert.Empty(AssistantFencedCalls.Parse("    " + Fence + "nth-dextra forget\n    {}\n    " + Fence));
        // A block left open runs nothing, and no fence after it opens (it holds the rest of the answer).
        Assert.Empty(AssistantFencedCalls.Parse(Fence + "nth-dextra forget\n{}\n"));
        Assert.Empty(AssistantFencedCalls.Parse(Fence + "python\nprint(1)\n\n" + Fence + "nth-dextra forget\n{}\n"));
        // A closing line with words after the marks does not close; a shorter one does not close a longer fence.
        Assert.Empty(AssistantFencedCalls.Parse(Fence + "nth-dextra forget\n{}\n" + Fence + " more"));
        Assert.Empty(AssistantFencedCalls.Parse("````nth-dextra forget\n{}\n" + Fence));
        // A backtick fence's info line holds no backtick.
        Assert.Empty(AssistantFencedCalls.Parse(Fence + "nth-dextra `recall`\n{}\n" + Fence));
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-6")]
    public void Blocks_come_in_order_up_to_the_limit_and_skip_other_fences_between_them()
    {
        var answer = string.Join("\n",
            Fence + "nth-dextra recall", "{\"query\": \"a\"}", Fence,
            Fence + "csharp", "var x = 1;", Fence,
            Fence + "nth-dextra usage_status", Fence,
            Fence + "nth-dextra report", "{}", Fence);
        Assert.Equal(new[] { "recall", "usage_status", "report" }, AssistantFencedCalls.Parse(answer).Select(c => c.Tool));
        Assert.Equal(new[] { 1, 7, 9 }, AssistantFencedCalls.Parse(answer).Select(c => c.Line));
        Assert.Equal(new[] { "recall", "usage_status" }, AssistantFencedCalls.Parse(answer, limit: 2).Select(c => c.Tool));
        Assert.Equal(4, AssistantFencedCalls.MaxPerAnswer);
    }
}
