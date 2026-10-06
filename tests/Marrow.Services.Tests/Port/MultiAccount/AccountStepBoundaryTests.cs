using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Rule", "R-TEST-4")]
public sealed class AccountStepBoundaryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_provider_stays_paused_after_every_tool_result_until_the_account_boundary_returns(bool resumeOriginal)
    {
        using var runtime = new TestRuntime();
        var config = new OpenAICompatibleSession.Config { Kind = ProviderKind.Deepseek, Title = "Offline", AgentIdentity = _ => "Offline",
            DefaultModel = "offline", ChatURL = new("https://offline.invalid/chat"), ContextWindow = 64000, IsVisionModel = _ => false };
        var session = new OpenAICompatibleSessionTestSeam(config, new() { Provider = ProviderKind.Deepseek, WorkingDirectory = SwiftURL.Create("file:///C:/offline")!,
            Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build, ApiKey = "offline" }).Wired();
        session.Rounds.Enqueue(new() { ToolCalls = new([new("one", "write_file", "{}"), new("two", "write_file", "{}")]) });
        session.Rounds.Enqueue(new() { Content = "Only if retained." });
        var paused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var boundaries = 0;
        var property = typeof(OpenAICompatibleSession).GetProperty("ContinueAfterAccountStep", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property.SetValue(session, (Func<CancellationToken, Task<bool>>)(_ => { boundaries++; return paused.Task; }));
        runtime.Loop.Run(async () => await session.Start());
        Task send = null!;
        runtime.OnMain(() => send = session.Send(new() { Text = "Work", Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build }));
        runtime.Loop.RunUntil(() => boundaries == 1);
        Assert.False(send.IsCompleted);
        Assert.Equal(2, session.ToolCallsSeen.Count);
        Assert.Single(session.RoundRequests);
        Assert.DoesNotContain(session.Events, value => value is ProviderEvent.TurnCompleted);
        var messages = (List<Marrow.Core.Support.JSONValue>)typeof(OpenAICompatibleSession).GetField("messages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        Assert.Equal(2, messages.Count(value => value["role"]?.JSONValueString == "tool"));
        paused.SetResult(resumeOriginal);
        runtime.Loop.Run(() => send);
        Assert.Equal(resumeOriginal ? 2 : 1, session.RoundRequests.Count);
        Assert.Equal(resumeOriginal ? 1 : 0, session.Events.Count(value => value is ProviderEvent.TurnCompleted));
        Assert.Equal(2, session.ToolCallsSeen.Count);
        session.Stop();
    }
}
