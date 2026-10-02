using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/ACPSession.swift:148-151 and Core/Models/Provider.swift:25-28,32.
// Start() is driven end to end against the compiled ACP stand-in (tests/fixtures/acp-cli/AcpStandIn.cs, R-TEST-8, R-TEST-10:
// offline, never a real AI CLI), which answers session/new with whatever result the test scripts. ACPSession's launch gate is
// static, so the class runs in the process collection and opens the gate before each launch (as ACPSessionProbeLaunchTests does).
[Collection(PlanLimitsProcessCollection.Name)] // launches a stand-in process: runs with the other process tests, never beside them under load
public sealed class ACPSessionStartGuardTests
{
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static ACPSession SessionFor(ProviderKind provider, AcpScript script) => new(new SessionConfiguration
    {
        Provider = provider,
        Executable = FileUrl(script.ExecutablePath),
        WorkingDirectory = FileUrl(script.Folder),
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    });

    [Theory]
    [InlineData(ProviderKind.Cursor, "{}", "Cursor did not start a session.")]
    [InlineData(ProviderKind.Opencode, "{}", "OpenCode Zen did not start a session.")]
    [InlineData(ProviderKind.OpencodeGo, "{}", "OpenCode Go did not start a session.")]
    [InlineData(ProviderKind.Grok, "{}", "Grok did not start a session.")]
    [InlineData(ProviderKind.Devin, "{}", "Devin did not start a session.")]
    [InlineData(ProviderKind.Grok, "{\"sessionId\":42}", "Grok did not start a session.")] // an id that is not a string is no id
    [InlineData(ProviderKind.Cursor, "{\"sessionId\":null}", "Cursor did not start a session.")]
    [Trait("Req", "PR-PROVACP-033")] // ACPSession.swift:148-151 (the final guard names the provider; the child process is left running)
    public async Task Start_fails_with_the_provider_named_message_when_session_new_produced_no_session_id(ProviderKind provider, string newResult, string expected)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        AcpStandIns.ResetLaunchGate();
        var script = AcpStandIns.CopyFor("no-session-id").SetModels(newResult);
        var session = SessionFor(provider, script);
        try
        {
            var error = await Assert.ThrowsAsync<ProviderError>(() =>
                Task.Run(() => runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60))));

            var failed = Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
            Assert.Equal(expected, failed.Message);
            Assert.Equal(expected, error.ErrorDescription);
            Assert.Equal(1, script.Launches);
        }
        finally
        {
            runtime.OnMain(session.Stop);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-033")] // side effect named in PR-PROVACP-033: "The child process is left running; this guard does not stop it."
    public async Task Start_leaves_the_agent_process_running_after_the_guard_throws()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        AcpStandIns.ResetLaunchGate();
        var script = AcpStandIns.CopyFor("left-running").SetModels("{}");
        var session = SessionFor(ProviderKind.Cursor, script);
        try
        {
            await Assert.ThrowsAsync<ProviderError>(() => Task.Run(() => runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60))));

            // A stopped agent would close its end within moments and the read task would mark the connection closed: give
            // that every chance to happen, then require the connection still open.
            for (var step = 0; step < 40; step++)
            {
                runtime.RunUntilIdle();
                Thread.Sleep(50);
            }
            Assert.True(session.IsRunning);
        }
        finally
        {
            runtime.OnMain(session.Stop);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-033")] // ACPSession.swift:148-151 ("Otherwise the session id string is returned.")
    public async Task Start_returns_the_session_id_the_agent_produced()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        AcpStandIns.ResetLaunchGate();
        var script = AcpStandIns.CopyFor("with-session-id").SetModels("{\"sessionId\":\"agent-session-7\"}");
        var session = SessionFor(ProviderKind.Devin, script);
        try
        {
            var id = await Task.Run(() => runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60)));

            Assert.Equal("agent-session-7", id);
        }
        finally
        {
            runtime.OnMain(session.Stop);
        }
    }
}
