using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

// ThreadRuntime.EnsureSession's two session factories (App/Runtime/ThreadRuntime.swift:1488-1494 API arm,
// :1549-1560 CLI arm) reached through the real private entry point, EnsureSession, as
// ThreadRuntimeSecondTests already reaches it by reflection (Marrow.App has no InternalsVisibleTo, ED-119).
// Before Job STUBS the Deepseek arm (API factory) and the Commandcode and Pi arms (CLI factory) threw
// NotImplementedException("BLOCKED: ... is not present in the owned provider tree"), although DeepSeekSession,
// CommandCodeSession and PiSession are all in Marrow.Services.Providers.
public sealed class ThreadRuntimeSessionFactoryTests
{
    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Runtime/ThreadRuntime.swift:1488-1490 `case .deepseek: DeepSeekSession(configuration:)`
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
    [Trait("Rule", "R-TEST-6")] // App/Runtime/ThreadRuntime.swift:1554 `case .commandcode: CommandCodeSession(configuration:)`
    public void The_CLI_factory_builds_a_CommandCodeSession_for_a_Commandcode_thread()
    {
        // A resume id with no transcript on disk makes CommandCodeSession.Start fail with its own, unique
        // "no longer has this conversation" error before it installs anything (CommandCodeSession.cs :98-102),
        // so the failure proves which class the factory built. allowNewSession false: no start-over retry.
        var error = FactoryStartError(ProviderKind.Commandcode, "factory-missing-cmd-session");
        Assert.Equal("Command Code no longer has this conversation; starting a new one.", error);
    }

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Runtime/ThreadRuntime.swift:1555 `case .pi: PiSession(configuration:)`
    public void The_CLI_factory_builds_a_PiSession_for_a_Pi_thread()
    {
        // As above: PiSession.Start checks the resume path first (PiSession.cs :83-85), before any support
        // folder, gate file or process, and words its refusal for Pi alone.
        var error = FactoryStartError(ProviderKind.Pi, "factory-missing-pi-session");
        Assert.Equal("Pi no longer has this conversation; starting a new one.", error);
    }

    private static string FactoryStartError(ProviderKind provider, string missingSessionID)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("cli factory " + provider));
        var thread = AppModelSamples.Thread(project) with { Provider = provider, ProviderSessionID = missingSessionID };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        // The R-TEST-10 seam: a resolved executable, so EnsureSession reaches its factory instead of NotInstalled.
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => SwiftURL.Create("file:///factory-test/cli")!);

        var ensure = typeof(ThreadRuntime).GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = h.Main.OnMain(() => (Task)ensure.Invoke(runtime, [project.Path, false])!);
        var thrown = Assert.Throws<ProviderError>(() =>
        {
            h.Main.Loop.Run(() => task, AppModelHarness.Bound);
        });
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
