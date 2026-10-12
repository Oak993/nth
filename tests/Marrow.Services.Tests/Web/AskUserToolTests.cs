using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 ask_user). The tool itself: what the model is told, what it reads back for each way a
// question can end, and that a call that cannot be read never reaches the person.
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class AskUserToolTests
{
    private static readonly JsonNode Database = JsonNode.Parse("""{"questions":[{"question":"Which database?","type":"single","options":["PostgreSQL","SQLite"]}]}""")!;

    [Fact]
    public async Task An_answer_goes_back_as_text_and_as_structured_content()
    {
        var host = new FakeAskUserHost();
        var run = AskUserTool.Run(Database, host, CancellationToken.None);
        await host.WaitForAsked(1);
        Assert.Equal(AskState.Waiting, host.Asked[0].State);
        host.Answer(0, ("q1", "SQLite"));
        var result = await run;
        Assert.False(result.Failed);
        Assert.Equal("The user answered your question.\n\nQuestion 1: Which database?\nAnswer 1: SQLite", result.Text);
        var answer = result.Structured!["answers"]![0]!;
        Assert.Equal(("q1", "Which database?"), (answer["id"]!.GetValue<string>(), answer["question"]!.GetValue<string>()));
        Assert.Equal("SQLite", answer["answer"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task A_number_answer_is_structured_as_a_number()
    {
        var host = new FakeAskUserHost();
        var run = AskUserTool.Run(JsonNode.Parse("""{"questions":[{"question":"How many GB?","type":"number","unit":"GB"}]}"""), host, CancellationToken.None);
        await host.WaitForAsked(1);
        host.Answer(0, ("q1", "64"));
        var result = await run;
        Assert.Equal(64, result.Structured!["answers"]![0]!["answer"]![0]!.GetValue<double>());
        Assert.Contains("Answer 1: 64 GB", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Skipping_and_stopping_are_told_to_the_model_in_their_own_words()
    {
        var host = new FakeAskUserHost();
        var skipped = AskUserTool.Run(Database, host, CancellationToken.None);
        await host.WaitForAsked(1);
        host.Skip(0);
        var skip = await skipped;
        Assert.False(skip.Failed);
        Assert.Equal(AskUserWords.SkippedResult, skip.Text);

        var stopped = AskUserTool.Run(Database, host, CancellationToken.None);
        await host.WaitForAsked(2);
        host.Stop(1);
        var stop = await stopped;
        Assert.False(stop.Failed);
        Assert.Equal(AskUserWords.StoppedResult, stop.Text);
    }

    [Fact]
    public async Task A_call_the_provider_ends_leaves_the_wait_detached_and_the_card_asked()
    {
        var host = new FakeAskUserHost();
        using var cancel = new CancellationTokenSource();
        var run = AskUserTool.Run(Database, host, cancel.Token);
        await host.WaitForAsked(1);
        await cancel.CancelAsync();
        var result = await run;
        Assert.True(result.Failed);
        Assert.Equal(AskUserWords.DetachedResult, result.Text);
        Assert.Equal(AskState.Waiting, host.Asked[0].State);
    }

    [Fact]
    public async Task A_call_that_cannot_be_read_returns_the_errors_and_asks_nobody()
    {
        var host = new FakeAskUserHost();
        var result = await AskUserTool.Run(JsonNode.Parse("""{"questions":[{"question":"Q?","type":"single","options":["Only"]}]}"""), host, CancellationToken.None);
        Assert.True(result.Failed);
        Assert.StartsWith("Error: Question 1 (\"Q?\"): a single choice needs 2 to 12 options", result.Text, StringComparison.Ordinal);
        Assert.EndsWith("Call ask_user again with this fixed.", result.Text, StringComparison.Ordinal);
        Assert.Empty(host.Asked);
    }

    [Fact]
    public async Task A_chat_with_no_host_gets_an_error_that_says_to_ask_in_the_reply()
    {
        var result = await AskUserTool.Run(Database, null, CancellationToken.None);
        Assert.True(result.Failed);
        Assert.Contains("Ask in your reply instead", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_schema_matches_what_the_reader_accepts()
    {
        var schema = AskUserTool.InputSchema();
        Assert.Equal(["questions"], schema["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        var question = schema["properties"]!["questions"]!["items"]!;
        Assert.Equal(["question"], question["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal(["single", "multiple", "text", "number"], question["properties"]!["type"]!["enum"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal(AskUserSchema.MaxQuestions, schema["properties"]!["questions"]!["maxItems"]!.GetValue<int>());
        // Every field the schema names is one the reader understands: a card built from a call that sets them all keeps them.
        var all = JsonNode.Parse("""
            {"questions":[{"question":"Q?","header":"H","type":"number","options":["A","B"],"allow_other":false,"required":false,"placeholder":"p","multiline":true,
            "min":1,"max":9,"step":2,"integer":true,"unit":"u","default":"3"}]}
            """)!;
        var parsed = AskUserSchema.Parse(all, "c").Card!.Questions[0];
        Assert.Equal(("H", false, "p", 1d, 9d, 2d, true, "u", "3"), (parsed.Header, parsed.Required, parsed.Placeholder, parsed.Min, parsed.Max, parsed.Step, parsed.WholeNumber, parsed.Unit, parsed.Default));
        foreach (var field in question["properties"]!.AsObject().Select(pair => pair.Key))
            Assert.Contains(field, new[] { "question", "header", "type", "options", "allow_other", "required", "placeholder", "multiline", "min", "max", "step", "integer", "unit", "default" });
    }

    [Fact]
    public void The_description_names_the_limits_the_reader_enforces()
    {
        Assert.Contains("one to four questions", AskUserTool.Description, StringComparison.Ordinal);
        Assert.Contains("two to twelve options", AskUserTool.Description, StringComparison.Ordinal);
        Assert.Equal("ask_user", AskUserTool.Name);
    }

    [Theory]
    [InlineData("nth-ui · ask_user", null, true)]
    [InlineData("Call nth-ui ask_user", null, true)]
    [InlineData("Tool", "nth-ui:ask_user {\"questions\":[]}", true)]
    [InlineData("nth-ui · artifact_create", null, false)]
    [InlineData("github · ask_user", null, false)]
    public void A_providers_request_to_allow_this_tool_is_known(string title, string? detail, bool expected)
    {
        var request = new ApprovalRequest("r", ApprovalRequest.Kind.Tool, title, new ValueList<ApprovalRequest.Option>([new("allow", "Approve", ApprovalRequest.Option.Role.Approve)]), detail);
        Assert.Equal(expected, AskUserTool.IsApprovalFor(request));
        Assert.False(AskUserTool.IsApprovalFor(request with { RequestKind = ApprovalRequest.Kind.Command }));
    }

    [Theory]
    [InlineData(ToolCall.Kind.Mcp, "nth-ui · ask_user", true)]
    [InlineData(ToolCall.Kind.Mcp, "nth-ui·ask_user", true)]
    [InlineData(ToolCall.Kind.Mcp, "nth-ui  ·  ask_user", true)]
    [InlineData(ToolCall.Kind.Other, "mcp__nth-ui__ask_user", true)]
    [InlineData(ToolCall.Kind.Mcp, "nth-ui · artifact_create", false)]
    [InlineData(ToolCall.Kind.Mcp, "other · ask_user", false)]
    [InlineData(ToolCall.Kind.Command, "nth-ui · ask_user", false)]
    public void The_row_a_provider_draws_for_the_tool_is_known_so_the_card_can_take_its_place(ToolCall.Kind kind, string title, bool expected)
    {
        Assert.Equal(expected, AskUserTool.IsProviderRow(new ToolCall { ToolCallKind = kind, Title = title }));
    }
}
