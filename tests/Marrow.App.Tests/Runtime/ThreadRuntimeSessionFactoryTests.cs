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

    private static ProviderSession EnsureSession(AppModelHarness h, ThreadRuntime runtime, string directory)
    {
        var ensure = typeof(ThreadRuntime).GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = h.Main.OnMain(() => (Task)ensure.Invoke(runtime, [directory, true])!);
        h.Main.Loop.Run(() => task, AppModelHarness.Bound);
        return (ProviderSession)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
}
