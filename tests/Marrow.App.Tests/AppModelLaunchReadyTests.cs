using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests;

// OD-0133 (amending OD-0108 for provider checks only): the launch is ready for the boot hand-over once the bootstrap reaches
// its provider refresh; the refresh (and the sign-in checks in it) runs on after that, and every provider's status still
// arrives. The refresh is held through the PORT-ONLY seam LoginEnvironment.LoadForTesting, which every CLI provider's
// refresh awaits (ProviderRegistry.Refresh, :366), reached by reflection as ThreadRuntimeLoginEnvironmentOrderTests does.
public sealed class AppModelLaunchReadyTests
{
    private static IDisposable OverrideLoad(Func<Task> load)
    {
        var seam = typeof(LoginEnvironment).GetProperty("LoadForTesting", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(seam);
        return Assert.IsType<Ambient<Func<Task>>>(seam.GetValue(null)).Override(load);
    }

    [Fact]
    [Trait("Decision", "OD-0133")]
    [Trait("Rule", "R-TEST-10")]
    public void The_launch_is_ready_while_the_provider_refresh_runs_and_every_status_still_arrives()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        bool? readyWhenTheRefreshAsked = null;
        using var seam = OverrideLoad(() =>
        {
            if (Interlocked.Increment(ref loads) == 1) return Task.CompletedTask; // the bootstrap's own read (AppModel.swift:245)
            readyWhenTheRefreshAsked ??= model.LaunchReady.IsCompleted;
            return gate.Task; // a provider's refresh: held
        });
        Task? bootstrap = null;
        h.Main.OnMain(() => bootstrap = model.Bootstrap());
        h.Main.Loop.RunUntil(() => loads >= 2, AppModelHarness.Bound);
        h.Main.Loop.RunUntil(() => model.LaunchReady.IsCompleted, AppModelHarness.Bound);
        Assert.True(readyWhenTheRefreshAsked, "the refresh began before the launch was ready");
        for (var round = 0; round < 10; round++) { h.Main.RunUntilIdle(); Thread.Sleep(20); }
        Assert.True(model.LaunchReady.IsCompletedSuccessfully);
        Assert.False(bootstrap!.IsCompleted, "the bootstrap finished although a provider's refresh was held");

        gate.SetResult();
        h.Main.Loop.RunUntil(() => bootstrap.IsCompleted, AppModelHarness.Bound);
        Assert.True(bootstrap.IsCompletedSuccessfully);
        h.Main.OnMain(() =>
        {
            foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
            {
                Assert.True(model.Providers.Statuses.ContainsKey(kind), kind + " has no status after the refresh");
                Assert.False(model.Providers.Status(kind).IsChecking, kind + " is still checking after the refresh");
            }
        });
    }

    [Fact]
    [Trait("Decision", "OD-0133")]
    public void A_model_never_bootstrapped_is_not_ready()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        Assert.False(model.LaunchReady.IsCompleted);
    }
}
