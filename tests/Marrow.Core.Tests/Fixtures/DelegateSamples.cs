using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Fixtures;

// Dextra AS-22: questions, replies and timelines for the delegate's tests. Offline only: nothing here asks a model.
internal static class DelegateSamples
{
    public static QuestionRequest.Question Q(string id, string prompt, string[]? choices = null, string header = "", bool multiple = false, bool other = false, bool secret = false) =>
        new(id, header, prompt, new ValueList<QuestionRequest.Choice>((choices ?? []).Select(c => new QuestionRequest.Choice(c))), multiple, other, secret);

    public static QuestionRequest Ask(params QuestionRequest.Question[] questions) => new("req-1", new ValueList<QuestionRequest.Question>(questions));

    public static QuestionRequest Ask(string prompt, params string[] choices) => Ask(Q("q1", prompt, choices));

    public static DelegateReply Reply(string id, string[] picked, double confidence = 0.9, DelegateBasis basis = DelegateBasis.Technical,
        DelegateReason? flag = null, string evidence = "", string reason = "It follows the project.") =>
        new(new ValueMap<string, ValueList<string>>(new Dictionary<string, ValueList<string>> { [id] = new ValueList<string>(picked) }), confidence, basis, flag, evidence, reason);

    public static DelegateReply Reply(string[] picked, double confidence = 0.9, DelegateBasis basis = DelegateBasis.Technical,
        DelegateReason? flag = null, string evidence = "") => Reply("q1", picked, confidence, basis, flag, evidence);

    public static TimelineItem User(string text = "go") => new(null, new TimelineItem.Content.User(new UserMessage { Text = text }));

    public static TimelineItem Tool(ToolCall.Kind kind, string title, string? detail = null, ToolCall.Status status = ToolCall.Status.Completed) =>
        new(null, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = kind, Title = title, Detail = detail, ToolCallStatus = status }));

    public static ClassifierContext Folder(string folder) => new()
    {
        WorkingDirectory = folder,
        AccessFolders = new ValueList<string>([folder]),
        Environment = ValueMap<string, string>.Empty,
    };
}
