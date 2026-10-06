// PORT-ONLY: offline stdio only; cannot launch a process, tool, model or network request.
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
namespace Marrow.Services.Tests.Fixtures;
internal sealed class ClaudePrimingProcess : IClaudeProcess
{
    private readonly AsyncStream<JSONValue>.Continuation output;
    private readonly TaskCompletionSource<int> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool restored;
    public ClaudePrimingProcess(IReadOnlyList<string> arguments, string failure)
    {
        Arguments = arguments.ToArray(); Failure = failure;
        (Messages, output) = AsyncStream<JSONValue>.MakeStream();
        var settings = Array.IndexOf(Arguments, "--settings");
        Agent = settings >= 0 ? JSONValue.Parse(Arguments[settings + 1])?["agent"]?.JSONValueString ?? "missing" : "missing";
    }
    public string[] Arguments { get; }
    public string Agent { get; }
    public string Failure { get; set; }
    public List<JSONValue> Sent { get; } = [];
    public AsyncStream<JSONValue> Messages { get; }
    public bool IsRunning { get; private set; }
    public bool Terminated { get; private set; }
    public string ErrorTail => "";
    public void Start() => IsRunning = true;
    public void Send(JSONValue message)
    {
        Sent.Add(message);
        if (message["type"]?.JSONValueString == "user")
        {
            Emit(JSONValue.FromDictionaryLiteral(("type", "system"), ("subtype", "init"),
                ("session_id", Failure == "identity" ? new JSONValue.Null() : new JSONValue.String("actual-native-session")),
                ("claude_code_version", Failure == "init-version" ? "2.1.160" : "2.1.284"),
                ("tools", Failure == "tools" ? JSONValue.FromArrayLiteral("Read") : JSONValue.FromArrayLiteral())));
            if (Failure == "tool-request")
                Emit(JSONValue.FromDictionaryLiteral(("type", "control_request"), ("request_id", "forbidden"),
                    ("request", JSONValue.FromDictionaryLiteral(("subtype", "can_use_tool"), ("tool_name", "Read"),
                        ("input", JSONValue.FromDictionaryLiteral(("file_path", "secret")))))));
            Emit(JSONValue.Parse("""{"type":"assistant","message":{"id":"readback","content":[{"type":"text","text":"Config.cs: preserve the latest instruction."}]}}""")!);
            Emit(JSONValue.FromDictionaryLiteral(("type", "result"), ("subtype", "success")));
            return;
        }
        var subtype = message["request"]?["subtype"]?.JSONValueString;
        if (subtype is null) return;
        if (subtype == "get_settings" && Failure == "start-silent" || subtype == "apply_flag_settings" && Failure == "restore-silent") return;
        var reject = subtype == "apply_flag_settings" && Failure == "restore-rejected"
            || subtype == "set_permission_mode" && restored && Failure == "restore-mode-rejected";
        JSONValue response = JSONValue.FromDictionaryLiteral();
        if (subtype == "apply_flag_settings" && !reject) restored = true;
        if (subtype == "get_settings")
        {
            var effective = JSONValue.FromDictionaryLiteral(
                ("agent", restored ? (Failure == "restore-latched" ? Agent : Failure == "restore-wrong-agent" ? "another-agent" : "user-agent") : (Failure == "agent" ? "unrestricted" : Agent)),
                ("disableAllHooks", restored ? new JSONValue.Bool(Failure == "restore-hook-latched") : new JSONValue.Bool(Failure != "hooks")));
            response = Failure == "schema" ? JSONValue.FromDictionaryLiteral() : JSONValue.FromDictionaryLiteral(
                ("effective", effective), ("sources", JSONValue.FromDictionaryLiteral(
                    ("userSettings", JSONValue.FromDictionaryLiteral(("agent", "user-agent"))),
                    ("policySettings", Failure == "managed-hooks" ? JSONValue.FromDictionaryLiteral(("hooks", JSONValue.FromDictionaryLiteral(("UserPromptSubmit", JSONValue.FromArrayLiteral("must-not-run"))))) : JSONValue.FromDictionaryLiteral()),
                    ("flagSettings", restored ? JSONValue.FromDictionaryLiteral(("fastMode", true))
                        : JSONValue.FromDictionaryLiteral(("agent", Agent), ("disableAllHooks", true), ("fastMode", true))))));
        }
        Emit(JSONValue.FromDictionaryLiteral(("type", "control_response"), ("response", JSONValue.FromDictionaryLiteral(
            ("request_id", message["request_id"]!), ("subtype", reject ? "error" : "success"),
            ("error", "offline restoration rejected"), ("response", response)))));
    }
    public void Emit(JSONValue message) => output.Yield(message);
    public void Terminate() { Terminated = true; IsRunning = false; output.Finish(); exit.TrySetResult(0); }
    public Task<int> WaitForExit() => exit.Task;
}
