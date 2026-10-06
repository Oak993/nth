using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

[Trait("Feature", "MA-4")]
[Trait("Rule", "R-TEST-4")]
public sealed class CodexHandoffParametersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("C:/account B/sessions/2026/10/05/rollout-17.jsonl")]
    public void Resume_rpc_uses_the_copied_rollout_path_only_when_the_capability_supplies_it(string? path)
    {
        using var scratch = new StorageScratchFolder();
        var config = new SessionConfiguration
        {
            Provider = ProviderKind.Codex, ResumeID = "session-17", ResumePath = path,
            WorkingDirectory = SwiftURL.Create("file:///offline")!, Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        };
        var session = new CodexSession(config);
        var threadParameters = (Dictionary<string, JSONValue>)typeof(CodexSession).GetProperty("ThreadParameters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var parameters = (Dictionary<string, JSONValue>)typeof(CodexSession).GetMethod("ResumeParameters", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, [threadParameters, "session-17"])!;
        Assert.Equal("session-17", parameters["threadId"].JSONValueString);
        Assert.True(parameters["excludeTurns"].JSONValueBool);
        Assert.Equal("user", parameters["approvalsReviewer"].JSONValueString);
        // Existing supervised policy, CodexSession.swift:1040-1051 / CodexSessionTests.PolicySettings.
        Assert.Equal("untrusted", parameters["approvalPolicy"].JSONValueString);
        if (path is null) Assert.False(parameters.ContainsKey("path"));
        else Assert.Equal(path, parameters["path"].JSONValueString);
    }
}
