using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Local;
using Marrow.Core.Support;
using Marrow.Services.Port.Local;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>One recorded answer: the prompt, what it should produce (plain, view or artifact) and the provider's wire events.</summary>
internal sealed record TeachingRecording(string Id, string Prompt, string Expect, IReadOnlyList<JSONValue> Events);

/// <summary>A tool call the model made on the nth-ui server (the server prefix removed), with its arguments.</summary>
internal sealed record TeachingCall(string Tool, JsonObject Arguments);

/// <summary>What a replay produced: the text of the model's first message and the nth-ui calls it made.</summary>
internal sealed record TeachingOutcome(string Text, IReadOnlyList<TeachingCall> Calls);

/// <summary>
/// PORT-ONLY test fixture (web surfaces W17, ED-NEW-web-w17-1; R-TEST-10). Replays the recorded answers of tests/fixtures/web-surfaces/teaching
/// through the real session code of each provider: ClaudeSession.Handle for stream-json, CodexSession and CopilotSession notifications
/// (reached the way the other session tests reach them), an OpenAICompatibleSession against a scripted answerer on 127.0.0.1 for the
/// API providers, and a LocalSession against the Local loopback. The recordings are Marrow-authored synthetic transcripts; no model
/// provider is ever called.
/// </summary>
internal static class TeachingReplay
{
    public static readonly string[] Providers = ["claude", "codex", "copilot", "deepseek", "local"];

    private const string Prefix = "nth-ui \u00B7 ";

    public static string FixtureFolder([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "fixtures", "web-surfaces", "teaching"));

    /// <summary>The recordings of a provider: the teaching ones, or with <paramref name="prefix"/> "live-" the live-answer ones (W24).</summary>
    public static IReadOnlyList<TeachingRecording> Load(string provider, string prefix = "") =>
        [.. File.ReadAllLines(Path.Combine(FixtureFolder(), prefix + provider + ".jsonl")).Where(line => line.Length > 0).Select(line =>
        {
            var row = JSONValue.Parse(Encoding.UTF8.GetBytes(line))!;
            return new TeachingRecording(row["id"]!.JSONValueString!, row["prompt"]!.JSONValueString!, row["expect"]!.JSONValueString!, [.. row["events"]!.JSONValueArray!]);
        })];

    // ------------------------------------------------------------------------------------------------------------------
    // The events of a session, read as a model's answer.
    // ------------------------------------------------------------------------------------------------------------------

    public static TeachingOutcome Outcome(IEnumerable<ProviderEvent> events)
    {
        var order = new List<string>();
        var streamed = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var finished = new Dictionary<string, string>(StringComparer.Ordinal);
        var calls = new List<string>();
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var details = new Dictionary<string, string?>(StringComparer.Ordinal);
        void Touch(string id)
        {
            if (!streamed.ContainsKey(id)) { streamed[id] = new StringBuilder(); order.Add(id); }
        }
        foreach (var @event in events)
        {
            switch (@event)
            {
                case ProviderEvent.MessageDelta delta:
                    Touch(delta.Id);
                    streamed[delta.Id].Append(delta.Text);
                    break;
                case ProviderEvent.MessageCompleted completed:
                    Touch(completed.Id);
                    finished[completed.Id] = completed.Text;
                    break;
                case ProviderEvent.ToolStarted started:
                    if (!titles.ContainsKey(started.Id)) calls.Add(started.Id);
                    titles[started.Id] = started.Call.Title;
                    if (started.Call.Detail is { } startDetail) details[started.Id] = startDetail;
                    break;
                case ProviderEvent.ToolUpdated updated:
                    if (updated.Update.Detail is { } updateDetail) details[updated.Id] = updateDetail;
                    break;
            }
        }
        var text = order.Count == 0 ? "" : finished.TryGetValue(order[0], out var done) ? done : streamed[order[0]].ToString();
        var made = calls.Where(id => titles[id].StartsWith(Prefix, StringComparison.Ordinal))
            .Select(id => new TeachingCall(titles[id][Prefix.Length..], JsonNode.Parse(details[id] ?? "{}")!.AsObject())).ToList();
        return new TeachingOutcome(text, made);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The CLI providers.
    // ------------------------------------------------------------------------------------------------------------------

    public static TeachingOutcome Claude(TeachingRecording recording) => Outcome(ClaudeEvents(recording));

    /// <summary>The events ClaudeSession emits for a recording (W24 reads them again through the live-answer flow).</summary>
    public static List<ProviderEvent> ClaudeEvents(TeachingRecording recording)
    {
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Claude,
            WorkingDirectory = SwiftURL.Create("file:///C:/teaching-replay")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
        };
        var session = new ClaudeSession(configuration, () => null);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        foreach (var message in recording.Events) session.Handle(message);
        return events;
    }

    public static TeachingOutcome Codex(TeachingRecording recording) => Outcome(CodexEvents(recording));

    public static List<ProviderEvent> CodexEvents(TeachingRecording recording)
    {
        var session = CodexWiringFixture.Session();
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        foreach (var notification in recording.Events)
            CodexWiringFixture.Call(session, "HandleNotification", notification["method"]!.JSONValueString!, notification["params"]!);
        return events;
    }

    public static TeachingOutcome Copilot(TeachingRecording recording, TestRuntime runtime) => Outcome(CopilotEvents(recording, runtime));

    public static IReadOnlyList<ProviderEvent> CopilotEvents(TeachingRecording recording, TestRuntime runtime)
    {
        var (session, events) = CopilotSessionHarness.Offline();
        foreach (var notification in recording.Events)
            CopilotSessionHarness.Notify(runtime, session, notification["params"]!, notification["method"]!.JSONValueString!);
        return events;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The API providers: a scripted answerer, the real turn loop.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The chat-completion chunks as an event stream.</summary>
    public static string Sse(IEnumerable<JSONValue> chunks) =>
        string.Concat(chunks.Select(chunk => "data: " + Encoding.UTF8.GetString(chunk.Data()) + "\n\n")) + "data: [DONE]\n\n";

    public const string FinalAnswer = "data: {\"choices\":[{\"delta\":{\"content\":\"Done.\"}}]}\n\ndata: [DONE]\n\n";

    /// <summary>The nth-ui calls in the assistant messages of a request: what the model asked for, read back from the next request.</summary>
    public static IReadOnlyList<TeachingCall> CallsIn(JSONValue request) =>
        [.. (request["messages"]?.JSONValueArray ?? [])
            .Where(message => message["role"]?.JSONValueString == "assistant")
            .SelectMany(message => message["tool_calls"]?.JSONValueArray ?? [])
            .Select(call => new TeachingCall(call["function"]!["name"]!.JSONValueString!, JsonNode.Parse(call["function"]!["arguments"]!.JSONValueString!)!.AsObject()))];
}
