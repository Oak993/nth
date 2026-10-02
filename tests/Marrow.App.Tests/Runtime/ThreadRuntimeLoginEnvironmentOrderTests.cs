using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Tests.Runtime;

// ThreadRuntime.swift:1459-1462 (B3 1.7.0; the record cites the B2 lines): when the reuse test fails, ensureSession awaits
// LoginEnvironment.load() first and only then releases the old session (releaseSession(stop: true)). The load is held through the
// PORT-ONLY seam LoginEnvironment.LoadForTesting (Marrow.Core/Support/Shell.cs), reached by reflection as HomeForTesting is.
public sealed class ThreadRuntimeLoginEnvironmentOrderTests
{
    private static IDisposable OverrideLoad(Func<Task> load)
    {
        var seam = typeof(LoginEnvironment).GetProperty("LoadForTesting", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(seam);
        return Assert.IsType<Ambient<Func<Task>>>(seam.GetValue(null)).Override(load);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-025")] // :1459-1462 `await LoginEnvironment.load(); releaseSession(stop: true)` - the old session lives until the load is done
    public void The_login_environment_is_loaded_before_the_old_session_is_stopped_and_released()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var gate = new TaskCompletionSource();
        var loads = 0;
        var stopsWhenLoadStarted = -1;
        using var seam = OverrideLoad(() =>
        {
            loads++;
            stopsWhenLoadStarted = rig.Session.StopCalls;
            return gate.Task;
        });
        // The setting is part of the session's launch signature (:1456): the reuse test fails and a session must be launched.
        rig.H.Main.OnMain(() => rig.App.Settings.AntiSlopEnabled = !rig.App.Settings.AntiSlopEnabled);

        var turn = rig.StartTurn("hello");
        rig.H.Main.Loop.RunUntil(() => loads == 1, AppModelHarness.Bound);
        for (var round = 0; round < 10; round++)
        {
            rig.H.Main.RunUntilIdle();
            Thread.Sleep(20);
        }

        // The load is in flight: the old session has not been stopped and is still the runtime's.
        Assert.Equal(1, loads);
        Assert.Equal(0, stopsWhenLoadStarted);
        Assert.Equal(0, rig.Session.StopCalls);

        gate.SetResult();
        rig.H.Main.Loop.RunUntil(() => rig.Session.StopCalls == 1, AppModelHarness.Bound);
        Assert.Equal(1, rig.Session.StopCalls);
        Assert.Equal(1, loads);
        // Launching the replacement fails closed in the harness (no provider executable); the turn ends without reaching one.
        rig.Finish(turn);
        Assert.Empty(rig.Session.Inputs);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-025")] // :1458 `if let session, session.isRunning, sessionSignature == signature { return session }` - a reusable session loads nothing
    public void A_reusable_session_neither_waits_for_the_login_environment_nor_is_released()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var loads = 0;
        using var seam = OverrideLoad(() =>
        {
            loads++;
            return Task.CompletedTask;
        });

        rig.Send("hello");

        Assert.Equal(0, loads);
        Assert.Equal(0, rig.Session.StopCalls);
        Assert.Single(rig.Session.Inputs);
    }
}
