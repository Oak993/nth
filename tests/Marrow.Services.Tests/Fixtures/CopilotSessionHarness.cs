using System.Collections;
using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// Shared helpers for the CopilotSession test classes (R-TEST-3: helpers live in Fixtures). Private members of
// CopilotSession are reached by reflection under the names the port gives the Swift members
// (DroppyCode/Services/Providers/CopilotSession.swift, B2 1fbf81e).
public static class CopilotSessionHarness
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>A non-"file" scheme keeps the Windows path unchanged through SwiftURL.Path (the ClaudeSessionTests note).</summary>
    public static SwiftURL FileUrl(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/'))!;

    public static SessionConfiguration Config(
        CopilotScript? script = null,
        string? resumeID = null,
        string? model = null,
        string? effort = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build,
        HydraLaunch? hydra = null,
        bool antiSlopEnabled = true,
        string? workingDirectory = null) => new()
        {
            Provider = ProviderKind.Copilot,
            Executable = script is null ? null : FileUrl(script.ExecutablePath),
            WorkingDirectory = FileUrl(workingDirectory ?? (script is null ? Path.GetTempPath() : Path.GetDirectoryName(script.ExecutablePath)!)),
            Environment = script is null ? new ValueMap<string, string>([]) : new ValueMap<string, string>([script.EnvironmentEntry]),
            ResumeID = resumeID,
            Model = model,
            Effort = effort,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
            Hydra = hydra,
            AntiSlopEnabled = antiSlopEnabled,
        };

    /// <summary>A Hydra launch on Copilot's own heads (native) or on another provider's (fallback).</summary>
    public static HydraLaunch Hydra(bool native = true) => new()
    {
        HeadsProvider = native ? ProviderKind.Copilot : ProviderKind.Codex,
        RunsNatively = native,
        WorkerModel = "gpt-5",
        WorkerEffort = "medium",
        MaxHeads = 3,
    };

    public static (CopilotSession Session, List<ProviderEvent> Events) Make(SessionConfiguration configuration)
    {
        var session = new CopilotSession(configuration);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, events);
    }

    /// <summary>A session with no process whose sessionID is set, as if start() had run, so the handlers can be driven
    /// directly; replies it would send go nowhere (`connection?.respond` on nil).</summary>
    public static (CopilotSession Session, List<ProviderEvent> Events) Offline(SessionConfiguration? configuration = null, string sessionID = "s-1")
    {
        var (session, events) = Make(configuration ?? Config());
        SetField(session, "sessionID", sessionID);
        return (session, events);
    }

    public static (CopilotSession Session, List<ProviderEvent> Events, string ID) Started(TestRuntime runtime, SessionConfiguration configuration)
    {
        var (session, events) = Make(configuration);
        var id = runtime.Loop.Run(() => session.Start(), Bound);
        return (session, events, id);
    }

    public static TurnInput Turn(string text, RuntimeMode runtimeMode = RuntimeMode.Supervised, InteractionMode interactionMode = InteractionMode.Build) => new()
    {
        Text = text,
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
    };

    public static T Field<T>(CopilotSession session, string name) => (T)typeof(CopilotSession).GetField(name, Instance)!.GetValue(session)!;

    public static object? RawField(CopilotSession session, string name) => typeof(CopilotSession).GetField(name, Instance)!.GetValue(session);

    public static void SetField(CopilotSession session, string name, object? value) =>
        typeof(CopilotSession).GetField(name, Instance)!.SetValue(session, value);

    public static object? Invoke(CopilotSession session, string method, params object?[] args) =>
        typeof(CopilotSession).GetMethod(method, Instance)!.Invoke(session, args);

    public static object? InvokeStatic(string method, params object?[] args) =>
        typeof(CopilotSession).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);

    /// <summary>A pending request of the private PendingRequest union (CopilotSession.swift:31-38).</summary>
    public static object Pending(string caseName, params object?[] args)
    {
        var union = typeof(CopilotSession).GetNestedType("PendingRequest", BindingFlags.NonPublic)!;
        var type = union.GetNestedType(caseName, BindingFlags.Public | BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, args)!;
    }

    public static IDictionary PendingRequests(CopilotSession session) => (IDictionary)RawField(session, "pendingRequests")!;

    public static void AddPending(CopilotSession session, string key, object pending) => PendingRequests(session)[key] = pending;

    /// <summary>A `session.event` notification's params (CopilotSession.swift:504-518).</summary>
    public static JSONValue Event(string type, JSONValue? data = null, string? agentID = null, string? sessionID = "s-1")
    {
        var @event = new List<(string, JSONValue)> { ("type", type) };
        if (data is not null) @event.Add(("data", data));
        if (agentID is not null) @event.Add(("agentId", agentID));
        var parameters = new List<(string, JSONValue)> { ("event", JSONValue.FromDictionaryLiteral([.. @event])) };
        if (sessionID is not null) parameters.Add(("sessionId", sessionID));
        return JSONValue.FromDictionaryLiteral([.. parameters]);
    }

    /// <summary>Delivers one notification to the private handler on main, as the connection would.</summary>
    public static void Notify(TestRuntime runtime, CopilotSession session, JSONValue parameters, string method = "session.event") =>
        runtime.OnMain(() => Invoke(session, "HandleNotification", method, parameters));

    public static void Notify(TestRuntime runtime, CopilotSession session, string type, JSONValue? data = null, string? agentID = null) =>
        Notify(runtime, session, Event(type, data, agentID));

    public static JSONValue Json(string text) => JSONValue.Parse(text)!;

    public static JSONValue Params(JSONValue message) => message["params"]!;

    public static List<string> Keys(JSONValue value) => value.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>Pumps until <paramref name="condition"/> holds; false when it did not within <paramref name="within"/>.</summary>
    public static bool TryRunUntil(TestRuntime runtime, Func<bool> condition, TimeSpan within)
    {
        try
        {
            runtime.Loop.RunUntil(condition, within);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
