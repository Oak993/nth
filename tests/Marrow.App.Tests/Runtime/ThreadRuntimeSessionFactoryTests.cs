using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// Adopted from N8 commit 7ac72544d948ebbc60f31e14ef885d21923182b2: only its DeepSeek test and helper.
// Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B3 52bc7926), :1470-1497.
// PR-RUNTIME-028: tests the DeepSeek construction clause only, not every configuration field.
// The CLI factory's DeepSeek arm (:1557, PR-RUNTIME-034) is unreachable under the pinned provider
// classification; this test does not claim that arm or either whole record is verified.
// AppModelHarness overrides SecretStore with its in-memory credential backend; the key is synthetic.
// EnsureSession starts the API session but never sends a turn or invokes a live provider endpoint.
public sealed class ThreadRuntimeSessionFactoryTests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-028")]
    public void The_API_factory_builds_a_DeepSeekSession_for_a_Deepseek_thread_with_a_key()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("api factory deepseek"));
        var thread = AppModelSamples.Thread(project) with { Provider = ProviderKind.Deepseek };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => model.Settings.SetAPIKeyInput("sk-test-key", ProviderKind.Deepseek));

        var session = EnsureSession(h, runtime, project.Path);

        Assert.IsType<DeepSeekSession>(session); // :1490
        Assert.True(session.IsRunning);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-034")] // ThreadRuntime.swift:1554; CommandCodeSession.swift:99-102
    public void The_CLI_factory_constructs_CommandCode_before_its_missing_resume_check()
    {
        Assert.Equal("Command Code no longer has this conversation; starting a new one.",
            FactoryStartError(ProviderKind.Commandcode, "factory-missing-cmd-session"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-034")] // ThreadRuntime.swift:1555; PiSession.swift:82-85
    public void The_CLI_factory_constructs_Pi_before_its_missing_resume_check()
    {
        Assert.Equal("Pi no longer has this conversation; starting a new one.",
            FactoryStartError(ProviderKind.Pi, "factory-missing-pi-session"));
    }

    // Adopted from N8's committed factory tests. A fake executable resolves the factory; the nonexistent
    // resume fails before either session installs support files or launches a process. No retry or turn.
    private static string FactoryStartError(ProviderKind provider, string missingSessionID)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("cli factory " + provider));
        var thread = AppModelSamples.Thread(project) with { Provider = provider, ProviderSessionID = missingSessionID };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => Marrow.Core.Port.SwiftURL.Create("file:///factory-test/cli")!);

        var ensure = typeof(ThreadRuntime).GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = h.Main.OnMain(() => (Task)ensure.Invoke(runtime, [project.Path, false])!);
        var thrown = Assert.Throws<ProviderError>(() => h.Main.Loop.Run(() => task, AppModelHarness.Bound));
        return Assert.IsType<ProviderError.Case.Failed>(thrown.ErrorCase).Message;
    }

    private static ProviderSession EnsureSession(AppModelHarness h, ThreadRuntime runtime, string directory)
    {
        var ensure = typeof(ThreadRuntime).GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = h.Main.OnMain(() => (Task)ensure.Invoke(runtime, [directory, true])!);
        h.Main.Loop.Run(() => task, AppModelHarness.Bound);
        return (ProviderSession)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
}
