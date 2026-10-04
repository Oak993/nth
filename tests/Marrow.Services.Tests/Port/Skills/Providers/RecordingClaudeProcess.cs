// PORT-ONLY: fail-closed offline transport for SK-11a; cannot spawn a CLI or open a socket.
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
namespace Marrow.Services.Tests.Port.Skills.Providers;

internal sealed class RecordingClaudeProcess : IClaudeProcess
{
    private readonly AsyncStream<JSONValue>.Continuation output;
    public RecordingClaudeProcess(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        Arguments = arguments.ToArray(); Environment = new Dictionary<string, string>(environment);
        (Messages, output) = AsyncStream<JSONValue>.MakeStream();
    }
    public string[] Arguments { get; }
    public Dictionary<string, string> Environment { get; }
    public List<byte[]> Sent { get; } = [];
    public AsyncStream<JSONValue> Messages { get; }
    public bool IsRunning { get; private set; }
    public string ErrorTail => "";
    public Action? OnStart { get; set; }
    public void Start() { OnStart?.Invoke(); IsRunning = true; }
    public void Send(JSONValue message)
    {
        Sent.Add(message.Data().Concat(new byte[] { 10 }).ToArray());
        if (message["request"]?["subtype"]?.JSONValueString != "initialize") return;
        var commands = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("name", "pdf"), ("description", "User pdf")),
            JSONValue.FromDictionaryLiteral(("name", "marrow:pdf"), ("description", "Fixture pdf")));
        var response = JSONValue.FromDictionaryLiteral(("request_id", message["request_id"]!),
            ("response", JSONValue.FromDictionaryLiteral(("commands", commands))));
        output.Yield(JSONValue.FromDictionaryLiteral(("type", "control_response"), ("response", response)));
    }
    public void Terminate() { IsRunning = false; output.Finish(); }
}
