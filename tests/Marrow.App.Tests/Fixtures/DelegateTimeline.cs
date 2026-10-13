using Marrow.Core.Models;

namespace Marrow.App.Tests.Fixtures;

// Dextra AS-22: timeline items for the delegate's taint tests (a chat's history since the owner last wrote).
internal static class DelegateTimeline
{
    public static TimelineItem User(string text = "go") => new(null, new TimelineItem.Content.User(new UserMessage { Text = text }));

    public static TimelineItem Tool(ToolCall.Kind kind, string title, string? detail = null, ToolCall.Status status = ToolCall.Status.Completed) =>
        new(null, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = kind, Title = title, Detail = detail, ToolCallStatus = status }));
}
