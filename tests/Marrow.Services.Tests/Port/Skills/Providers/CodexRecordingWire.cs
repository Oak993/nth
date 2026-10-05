// PORT-ONLY: SK-11b helper. A scripted stand-in for the RPC connection: records every request, and fails the methods a test names.
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Port.Skills.Providers;

internal sealed class CodexRecordingWire
{
    public List<(string Method, string Params)> Sent { get; } = [];
    public Func<string, Exception?> Fail { get; set; } = _ => null;

    public Task<JSONValue> Request(string method, JSONValue parameters)
    {
        Sent.Add((method, CodexWiringFixture.Bytes(parameters)));
        if (Fail(method) is { } failure) throw failure;
        return Task.FromResult<JSONValue>(new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>())));
    }
}
