using System.Runtime.CompilerServices;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
namespace Marrow.Services.Tests.Fixtures;
internal sealed class CommandCodeTextHeadFixture(string proof, string? failure = null) : ICommandCodeTextProcess
{
    private string nonce = "";
    public string? Prompt { get; private set; }
    public bool ProofRead { get; private set; }
    public bool ProofReadBeforeWrite { get; private set; }
    public bool Stopped { get; private set; }
    public bool Started { get; private set; }
    public Action? OnStop { get; set; }
    public Func<Task>? OnStopAsync { get; set; }
    public Action? OnExit { get; set; }
    public IReadOnlyList<string> Arguments { get; private set; } = [];
    public IReadOnlyDictionary<string,string> Environment { get; private set; } = new Dictionary<string,string>();
    public SessionConfiguration Configuration { get; } = new()
    {
        Provider = ProviderKind.Commandcode, Model = "chosen-model", Executable = SwiftURL.Create("marrow-native-path:C:/fixture/cmd.exe")!,
        WorkingDirectory = SwiftURL.Create("marrow-native-path:" + Marrow.Testing.TestDataDirectory.Read()!.Replace('\\', '/'))!,
        Environment = new ValueMap<string, string>(new Dictionary<string,string> { ["COMMAND_CODE_API_KEY"] = "fixture-credential", ["DROPPY_CODE_MCP_BRIDGE"] = "NEVER-INHERIT" }), RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };
    public ICommandCodeTextProcess Make(SessionConfiguration config, IReadOnlyList<string> argv, IReadOnlyDictionary<string,string> env)
    {
        Arguments = argv; Environment = env; nonce = env[CommandCodeTextHead.NonceKey]; return this;
    }
    public void Start() => Started = true;
    public void Write(string text) { ProofReadBeforeWrite = ProofRead; Prompt = text; }
    public void CloseInput() { }
    public Task<int> Exit() { OnExit?.Invoke(); return Task.FromResult(0); }
    public async Task Stop() { Stopped = true; OnStop?.Invoke(); if (OnStopAsync is not null) await OnStopAsync(); }
    public IAsyncEnumerable<JSONValue> Messages => Read();
    private async IAsyncEnumerable<JSONValue> Read([EnumeratorCancellation] CancellationToken cancel = default)
    {
        await Task.CompletedTask;
        if (proof == "missing") yield break;
        if (proof == "early-model") { yield return JSONValue.Parse("{\"type\":\"event\",\"event\":{\"type\":\"message_start\"}}")!; yield break; }
        ProofRead = true;
        yield return JSONValue.Parse("{\"type\":\"nth_text_policy\",\"nonce\":\"" + (proof == "wrong-nonce" ? "old" : nonce) + "\",\"tools\":" + (proof == "tools" ? "[\"read\"]" : "[]") + "}")!;
        if (failure == "tool") { yield return JSONValue.Parse("{\"type\":\"event\",\"event\":{\"type\":\"tool_queued\"}}")!; yield break; }
        yield return JSONValue.Parse("{\"type\":\"event\",\"event\":{\"type\":\"model_request_end\",\"model\":\"chosen-model\",\"usage\":{\"inputTokens\":5,\"outputTokens\":3}}}")!;
        if (failure != "recorded-final")
            yield return JSONValue.FromDictionaryLiteral(("type", new JSONValue.String("event")), ("event", JSONValue.FromDictionaryLiteral(("type",new JSONValue.String("text_delta")),("delta",new JSONValue.String(failure == "long" ? new string('x',32_001) : "answer")))));
        if (failure == "recorded-final") yield return JSONValue.Parse("""{"type":"event","event":{"type":"run_end","result":{"finalText":"answer"}}}""")!;
        if (failure == "content-action") yield return JSONValue.Parse("""{"type":"event","event":{"type":"message_end","content":[{"type":"tool_use","name":"read","input":{"path":"SECRET-SENTINEL"}}]}}""")!;
        yield return JSONValue.Parse("{\"type\":\"result\",\"subtype\":\""+(failure == "error" ? "error" : "success")+"\"}")!;
    }
}

