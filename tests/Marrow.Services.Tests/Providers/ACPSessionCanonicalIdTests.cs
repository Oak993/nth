using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

// Audit fix (svc): ACPSession.swift:45-48,342-351 keep a Set<String> and a Dictionary<String, ModelOption> keyed by ids that come from the agent.
// Swift String equality is canonical equivalence (R-STR-2), so U+00E9 and U+0065 U+0301 name one id.
public sealed class ACPSessionCanonicalIdTests
{
    private const string Composed = "café";
    private const string Decomposed = "café";

    private static ACPSession Session(ProviderKind provider) => new(new SessionConfiguration
    {
        Provider = provider,
        Executable = null,
        WorkingDirectory = SwiftURL.Create("file:///" + Path.GetTempPath().Replace('\\', '/'))!,
        Environment = new ValueMap<string, string>(new Dictionary<string, string>()),
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    });

    private static void InvokePrivate(ACPSession session, string method, params object?[] args) =>
        typeof(ACPSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args);

    private static JSONValue Update(string sessionUpdate, params (string Key, JSONValue Value)[] fields)
    {
        var pairs = new List<(string, JSONValue)> { ("sessionUpdate", sessionUpdate) };
        pairs.AddRange(fields);
        return JSONValue.FromDictionaryLiteral(("update", JSONValue.FromDictionaryLiteral(pairs.ToArray())));
    }

    [Trait("Req", "PR-PROVACP-083")] // ACPSession.swift:45-48,516-524
    [Fact]
    public void A_canonically_equivalent_todo_tool_call_id_keeps_routing_to_the_checklist()
    {
        var session = Session(ProviderKind.Grok);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var todos = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("content", "step 1")));
        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call",
            ("toolCallId", Composed), ("title", "todowrite"), ("rawInput", JSONValue.FromDictionaryLiteral(("todos", todos)))));
        events.Clear();

        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call_update", ("toolCallId", Decomposed)));

        Assert.Empty(events);
    }

    [Trait("Req", "PR-PROVACP-043")] // ACPSession.swift:342-351
    [Fact]
    public void A_canonically_equivalent_model_id_keeps_the_efforts_learned_earlier()
    {
        var session = Session(ProviderKind.Grok);
        InvokePrivate(session, "ApplyModels", JSONValue.FromDictionaryLiteral(
            ("availableModels", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("modelId", Composed),
                    ("_meta", JSONValue.FromDictionaryLiteral(("reasoningEfforts", JSONValue.FromArrayLiteral(
                        JSONValue.FromDictionaryLiteral(("value", "high"), ("default", true)))))))))));
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "model-opt"), ("category", "model"),
                ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("value", Decomposed))))),
        ]));

        var models = (ValueList<ModelOption>)typeof(ACPSession).GetField("models", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

        Assert.Equal(["high"], models[0].Efforts);
        Assert.Equal("high", models[0].DefaultEffort);
    }
}
