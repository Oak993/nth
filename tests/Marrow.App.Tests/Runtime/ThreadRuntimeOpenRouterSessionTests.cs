using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234). The API-key session factory of ThreadRuntime.swift:1470-1497 builds the
// native session of a key provider from the key AppSettings holds; for an OpenRouter thread that session is the OpenRouter
// agent and the key is the one in the credential store. AppModelHarness puts an in-memory Credential Manager behind the
// secret store and the key below is made up: EnsureSession starts the session but never sends a turn, so no request leaves
// the process and no real credential store is read.
public sealed class ThreadRuntimeOpenRouterSessionTests
{
    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_API_factory_builds_an_OpenRouterSession_for_an_OpenRouter_thread_with_the_key_from_the_credential_store()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("api factory openrouter"));
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.OpenRouter };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => model.Settings.SetAPIKeyInput("sk-or-made-up-key", ProviderKind.OpenRouter));

        var session = EnsureSession(h, runtime, project.Path);

        var openRouter = Assert.IsType<OpenRouterSession>(session);
        Assert.True(openRouter.IsRunning);
        Assert.Equal("sk-or-made-up-key", openRouter.Configuration.ApiKey); // the stored key, handed to the session
        Assert.Equal(ProviderKind.OpenRouter, openRouter.Configuration.Provider);
        Assert.Null(openRouter.Configuration.Executable); // an API provider has no CLI
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void An_OpenRouter_thread_with_no_key_does_not_start_and_says_a_key_is_needed()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("api factory openrouter no key"));
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.OpenRouter };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        var ensure = typeof(ThreadRuntime).GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = h.Main.OnMain(() => (Task)ensure.Invoke(runtime, [project.Path, true])!);
        var error = Assert.Throws<ProviderError>(() => h.Main.Loop.Run(() => task, AppModelHarness.Bound));

        var notInstalled = Assert.IsType<ProviderError.Case.NotInstalled>(error.ErrorCase);
        Assert.Equal(ProviderKind.OpenRouter, notInstalled.Provider);
        Assert.Equal("OpenRouter needs an API key. Add one in Settings → Providers → OpenRouter.", error.ErrorDescription);
    }

    private static ProviderSession EnsureSession(AppModelHarness h, ThreadRuntime runtime, string directory)
    {
        var ensure = typeof(ThreadRuntime).GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = h.Main.OnMain(() => (Task)ensure.Invoke(runtime, [directory, true])!);
        h.Main.Loop.Run(() => task, AppModelHarness.Bound);
        return (ProviderSession)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
}
