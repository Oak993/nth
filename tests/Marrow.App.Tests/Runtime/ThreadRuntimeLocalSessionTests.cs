using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Port.Local;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

[Trait("Rule", "R-TEST-10")]
[Trait("Feature", "MA-3")]
public sealed class ThreadRuntimeLocalSessionTests
{
    [Fact]
    public async Task The_factory_binds_the_selected_server_without_a_key_and_restarts_when_the_server_changes()
    {
        await using var first = new LocalProviderLoopback();
        await using var second = new LocalProviderLoopback();
        using var discovery = new LocalDiscovery([first.BaseURL, second.BaseURL]);
        foreach (var server in new[] { first, second })
            server.Reply("/v1/models", """{"data":[{"id":"same","supports_tool_calling":true,"context_length":8192}]}""");
        var entries = await discovery.Discover();
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("local factory"));
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.Local, Model = entries[0].Models[0].SelectionId };
        h.WriteLibrary([project], [thread]);
        var app = h.MakeModel();
        h.Main.OnMain(() => app.Providers.InitializeLocal(discovery));
        var runtime = h.Main.OnMain(() => app.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var session = Assert.IsType<LocalSession>(EnsureSession(h, runtime, project.Path));
        Assert.True((bool)typeof(ThreadRuntime).GetMethod("KeepsHydraPolicyInSystemPrompt", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [ProviderKind.Local])!);
        Assert.Null(session.Configuration.ApiKey);
        Assert.Null(session.Configuration.Executable);
        Assert.Equal(first.BaseURL.Port, session.OpenAICompatibleSessionConfig.ChatURL.Port);
        Assert.Same(session, EnsureSession(h, runtime, project.Path));
        h.Main.OnMain(() => app.UpdateThread(thread.Id, current => current with { Model = entries[1].Models[0].SelectionId }));
        var next = Assert.IsType<LocalSession>(EnsureSession(h, runtime, project.Path));
        Assert.NotSame(session, next);
        Assert.False(session.IsRunning);
        Assert.Equal(second.BaseURL.Port, next.OpenAICompatibleSessionConfig.ChatURL.Port);
        foreach (var server in new[] { first, second })
            server.Reply("/v1/models", """{"data":[{"id":"same","supports_tool_calling":false,"context_length":4096}]}""");
        await discovery.Discover();
        var refreshed = Assert.IsType<LocalSession>(EnsureSession(h, runtime, project.Path));
        Assert.NotSame(next, refreshed);
        Assert.False(next.IsRunning);
        Assert.Equal(4096, refreshed.OpenAICompatibleSessionConfig.ContextWindow);
        Assert.DoesNotContain(first.Requests.Concat(second.Requests), request => request.Target.EndsWith("/chat/completions", StringComparison.Ordinal));
    }

    private static ProviderSession EnsureSession(AppModelHarness h, ThreadRuntime runtime, string directory)
    {
        var ensure = typeof(ThreadRuntime).GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = h.Main.OnMain(() => (Task<ProviderSession>)ensure.Invoke(runtime, [directory, true])!);
        h.Main.Loop.Run(() => task, AppModelHarness.Bound);
        return task.Result;
    }
}
