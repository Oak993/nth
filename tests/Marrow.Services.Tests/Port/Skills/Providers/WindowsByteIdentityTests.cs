using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// Real Windows stdio framing through the existing compiled offline stand-in; never the Copilot CLI.
// Kept as ordinary tests (no Skip); run this class on Windows, exclude it from the bounded Linux runner.
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class WindowsByteIdentityTests
{
    [Theory, InlineData(false), InlineData(true), Trait("Feature", "SK-11c"), Trait("Rule", "R-TEST-4")]
    public void Empty_Copilot_Start_sends_the_existing_argv_and_every_startup_RPC_body_byte_for_byte(bool resume)
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var script = new CopilotScript("sk11c-wire-identity");
        var configuration = CopilotSessionHarness.Config(script, resumeID: resume ? "fixture-session" : null);
        var session = new CopilotSession(configuration);
        try
        {
            var id = runtime.Loop.Run(session.Start, CopilotSessionHarness.Bound);
            runtime.Loop.RunUntil(() => script.Methods().Contains("session.commands.list"), CopilotSessionHarness.Bound);
            var parameters = new BaselineWire(configuration).CopilotParameters();
            parameters["sessionId"] = id; // Compare the source's generated ID without normalizing any transmitted bytes.
            byte[][] expected = [
                RPC(1, "ping", JSONValue.FromDictionaryLiteral(("message", "nth"))),
                RPC(2, resume ? "session.resume" : "session.create", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters, SwiftStringComparer.Instance))),
                RPC(3, "session.commands.list", JSONValue.FromDictionaryLiteral(("sessionId", id)))
            ];
            var actual = script.Frames();
            Assert.Equal(expected.Length, actual.Count);
            for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], Encoding.UTF8.GetBytes(actual[i]));
            Assert.Equal(Encoding.UTF8.GetBytes(string.Join("\0", BaselineWire.CopilotArguments())), Encoding.UTF8.GetBytes(string.Join("\0", Assert.Single(script.Calls()))));
        }
        finally
        {
            runtime.OnMain(session.Stop);
            runtime.Advance(TimeSpan.FromSeconds(3));
            runtime.Loop.RunUntil(() => !session.IsRunning, CopilotSessionHarness.Bound);
        }
    }

    private static byte[] RPC(long id, string method, JSONValue parameters) =>
        JSONValue.FromDictionaryLiteral(("id", id), ("method", method), ("params", parameters), ("jsonrpc", "2.0")).Data();
}
