using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.App.Tests.Runtime;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-1). The provider events of an MCP tool carry the structured
// part of its result (ToolCall.Payload) and the thread runtime stores it on the tool row, next to the text output: a row
// that starts with the UI resource a tool names keeps it, the finished result adds its images and structure, and a later
// update that has no payload never takes one away.
[Trait("Decision", "ED-NEW-web-w18-1")]
public sealed class ThreadRuntimeMcpPayloadTests
{
    private const string Id = "mcp-1";

    private static ToolPayload Started() => new() { Server = "shipyard", Tool = "board", UiResourceUri = "ui://board/view.html" };

    private static ToolPayload Finished() => Started()
        .WithImage("image/png", "iVBORw0KGgo=", 8)
        .WithStructuredContent("{\"count\":3}");

    private static ToolCall Row(ThreadRuntimePart12Harness h) => h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.Tool>(
        h.Runtime.Entries.Single(entry => entry.Id == Id).Item.TimelineItemContent).Value);

    private static ProviderEvent Start(ToolPayload? payload) => new ProviderEvent.ToolStarted(Id, new ToolCall
    {
        ToolCallKind = ToolCall.Kind.Mcp,
        Title = "shipyard · board",
        Payload = payload,
    });

    [Fact]
    public void A_row_that_starts_with_a_payload_keeps_it_and_a_finished_result_replaces_it()
    {
        using var h = new ThreadRuntimePart12Harness();

        h.Main.OnMain(() => h.Runtime.Rehearse(Start(Started())));
        Assert.Equal(Started(), Row(h).Payload);
        Assert.Equal(ToolCall.Status.Running, Row(h).ToolCallStatus);

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated(Id,
            new ToolUpdate { Output = "ran:board", Status = ToolCall.Status.Completed, Payload = Finished() })));
        var done = Row(h);
        Assert.Equal(Finished(), done.Payload);
        Assert.Equal("ran:board", done.Output);
        Assert.Equal(ToolCall.Status.Completed, done.ToolCallStatus);
    }

    [Fact]
    public void An_update_without_a_payload_never_takes_the_rows_payload_away()
    {
        using var h = new ThreadRuntimePart12Harness();

        h.Main.OnMain(() => h.Runtime.Rehearse(Start(Started())));
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated(Id, new ToolUpdate { Output = "more output" })));
        Assert.Equal(Started(), Row(h).Payload);

        // A second announcement of the same tool without a payload (a provider repeating its start) keeps it too.
        h.Main.OnMain(() => h.Runtime.Rehearse(Start(null)));
        Assert.Equal(Started(), Row(h).Payload);

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated(Id, new ToolUpdate { Status = ToolCall.Status.Completed })));
        Assert.Equal(Started(), Row(h).Payload);
    }

    [Fact]
    public void An_unannounced_tool_update_with_a_payload_creates_the_row_with_it()
    {
        using var h = new ThreadRuntimePart12Harness();

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated(Id,
            new ToolUpdate { Kind = ToolCall.Kind.Mcp, Status = ToolCall.Status.Completed, Payload = Finished() })));

        var row = Row(h);
        Assert.Equal(Finished(), row.Payload);
        Assert.Equal(ToolCall.Kind.Mcp, row.ToolCallKind);
    }

    [Fact]
    public void A_plain_tool_row_has_no_payload()
    {
        using var h = new ThreadRuntimePart12Harness();

        h.Main.OnMain(() => h.Runtime.Rehearse(Start(null)));
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated(Id,
            new ToolUpdate { Output = "text only", Status = ToolCall.Status.Completed })));

        Assert.Null(Row(h).Payload);
    }
}
