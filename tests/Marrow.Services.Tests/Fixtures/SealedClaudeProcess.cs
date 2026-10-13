// Dextra AS-5 (ED-NEW-dextra-as5-3): an offline Claude CLI stand-in for the sealed-launch tests. It answers initialize
// and, as each test sets it, the get_settings control request (a reply, an error, or silence); it spawns nothing.
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>A Claude CLI stand-in that answers initialize and, as configured, get_settings; it can spawn nothing.</summary>
internal sealed class SealedClaudeProcess(IReadOnlyList<string> arguments) : IClaudeProcess
{
    private readonly (AsyncStream<JSONValue> Stream, AsyncStream<JSONValue>.Continuation Output) channel = AsyncStream<JSONValue>.MakeStream();
    private readonly TaskCompletionSource<int> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string[] Arguments { get; } = [.. arguments];
    public List<JSONValue> Sent { get; } = [];
    /// <summary>The get_settings reply: a response body, "error", or null to stay silent.</summary>
    public JSONValue? Settings { get; set; } = JSONValue.Parse("""{"effective":{"permissions":{"allow":[],"defaultMode":"default"}}}""");
    public bool SettingsError { get; set; }
    public AsyncStream<JSONValue> Messages => channel.Stream;
    public bool IsRunning { get; private set; }
    public string ErrorTail => "";
    public void Start() => IsRunning = true;
    public void Send(JSONValue message)
    {
        Sent.Add(message);
        var subtype = message["request"]?["subtype"]?.JSONValueString;
        var id = message["request_id"];
        if (id is null) return;
        if (subtype == "initialize") Reply(id, JSONValue.FromDictionaryLiteral(("commands", JSONValue.FromArrayLiteral())));
        else if (subtype == "get_settings" && SettingsError)
            channel.Output.Yield(JSONValue.FromDictionaryLiteral(("type", "control_response"),
                ("response", JSONValue.FromDictionaryLiteral(("request_id", id), ("subtype", "error"), ("error", "Unsupported control request subtype: get_settings")))));
        else if (subtype == "get_settings" && Settings is { } settings) Reply(id, settings);
    }
    private void Reply(JSONValue id, JSONValue body) => channel.Output.Yield(JSONValue.FromDictionaryLiteral(("type", "control_response"),
        ("response", JSONValue.FromDictionaryLiteral(("request_id", id), ("subtype", "success"), ("response", body)))));
    public Task<int> WaitForExit() => exit.Task;
    public void Terminate() { IsRunning = false; channel.Output.Finish(); exit.TrySetResult(0); }
}
