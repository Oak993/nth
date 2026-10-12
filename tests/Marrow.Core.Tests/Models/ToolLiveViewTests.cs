using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

// PORT-ONLY: web surfaces W24 (DESIGN.md 7.1, 7.3; ED-NEW-web-w24-1). A live answer rides on the tool row's payload, so it is saved
// with the thread, comes back after a restart and a damaged one never makes a thread unreadable.
[Trait("Decision", "ED-NEW-web-w24-1")]
public sealed class ToolLiveViewTests
{
    private static string Encode<T>(T value) => Encoding.UTF8.GetString(SwiftJson.Encode(value, Coding.StorageOptions, SwiftJsonStyle.Storage));

    private static T Decode<T>(string json) => Coding.Decode<T>(Encoding.UTF8.GetBytes(json))!;

    private static ToolCall Row(ToolLiveView? view) => new()
    {
        ToolCallKind = ToolCall.Kind.Other,
        Title = "Live answers",
        StartedAt = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero),
        Payload = view is null ? null : new ToolPayload { LiveView = view },
    };

    [Fact]
    public void A_payload_with_only_a_live_view_is_not_empty_and_round_trips_with_every_field()
    {
        var view = new ToolLiveView
        {
            Id = "hosting",
            Kind = LiveViewKind.Ui,
            Title = "Hosting estimate",
            Text = "card \"Hosting estimate\"\n  state n = 3\n",
            Version = 4,
            Complete = true,
            Lite = true,
            State = "{\"n\":5}",
            Superseded = true,
            Problem = "none",
        };
        Assert.False(new ToolPayload { LiveView = view }.IsEmpty);
        var read = Decode<ToolCall>(Encode(Row(view)));
        Assert.Equal(view, read.Payload!.LiveView);

        var html = new ToolLiveView { Id = "pong", Kind = LiveViewKind.Html, Text = "<p>hi</p>", Height = 360, Complete = true };
        Assert.Equal(html, Decode<ToolCall>(Encode(Row(html))).Payload!.LiveView);
        Assert.True(html.IsHtml);
    }

    [Fact]
    public void Flags_that_are_off_are_not_written_and_a_row_without_a_view_writes_none()
    {
        var json = Encode(Row(new ToolLiveView { Id = "v", Text = "text \"a\"" }));
        Assert.DoesNotContain("complete", json, StringComparison.Ordinal);
        Assert.DoesNotContain("superseded", json, StringComparison.Ordinal);
        Assert.DoesNotContain("lite", json, StringComparison.Ordinal);
        Assert.DoesNotContain("liveView", Encode(Row(null)), StringComparison.Ordinal);
        Assert.Null(Decode<ToolCall>(Encode(Row(null))).Payload);
    }

    [Fact]
    public void A_damaged_view_is_read_leniently_and_never_costs_the_thread()
    {
        var json = Encode(Row(new ToolLiveView { Id = "v", Text = "text \"a\"", Complete = true }))
            .Replace("\"version\":1", "\"version\":\"two\"", StringComparison.Ordinal)
            .Replace("\"complete\":true", "\"complete\":[1]", StringComparison.Ordinal);
        var read = Decode<ToolCall>(json).Payload!.LiveView!;
        Assert.Equal(("v", "text \"a\"", 1, false), (read.Id, read.Text, read.Version, read.Complete));

        var notAnObject = Encode(Row(new ToolLiveView { Id = "v", Text = "t" })).Replace("\"liveView\":{", "\"liveView\":{\"junk\":1,", StringComparison.Ordinal);
        Assert.Equal("v", Decode<ToolCall>(notAnObject).Payload!.LiveView!.Id);
        var kept = new ToolLiveView { Id = "v", Text = "t" };
        var gone = Encode(Row(kept)).Replace(Encode(kept), "5", StringComparison.Ordinal);
        Assert.Contains("\"liveView\":5", gone, StringComparison.Ordinal);
        Assert.Null(Decode<ToolCall>(gone).Payload);
    }
}
