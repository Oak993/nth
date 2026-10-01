using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// HydraLiveInfo.shown (App/Views/Chat/HydraPanel.swift:475-487): a running head's stored record with the live activity and
/// step count laid over it, and deliberately never the live token total.
/// </summary>
public sealed class HydraLiveInfoTests
{
    private static HydraHeadInfo Stored(HydraHeadInfo.Status status, string? activity = "stored activity", long toolCalls = 4, long tokens = 10) =>
        new HydraHeadInfo(0, "Task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated)
        {
            HydraHeadInfoStatus = status, Activity = activity, ToolCalls = toolCalls, Tokens = tokens,
        };

    private static void WithRuntime(Action<ThreadRuntime> body)
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url);
            var thread = model.NewThread(project)!;
            body(model.Runtime(thread.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-179")] // HydraPanel.swift:481 guard stored.status == .running, let runtime else { return stored }
    public void A_head_with_no_runtime_is_shown_exactly_as_stored()
    {
        var stored = Stored(HydraHeadInfo.Status.Running);
        Assert.Same(stored, HydraLiveInfo.Shown(stored, null));
    }

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed)]
    [InlineData(HydraHeadInfo.Status.Failed)]
    [InlineData(HydraHeadInfo.Status.Stopped)]
    [Trait("Req", "PR-HYDRA-179")] // HydraPanel.swift:481 a finished head is shown as stored, whatever its runtime holds
    public void A_finished_head_is_shown_exactly_as_stored_whatever_its_runtime_holds(HydraHeadInfo.Status status)
    {
        WithRuntime(runtime =>
        {
            runtime.HydraActivity = "live activity";
            runtime.HydraToolCalls = 99;
            var stored = Stored(status);
            var shown = HydraLiveInfo.Shown(stored, runtime);
            Assert.Same(stored, shown);
            Assert.Equal("stored activity", shown.Activity);
            Assert.Equal(4, shown.ToolCalls);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-179")] // HydraPanel.swift:483 if let activity = runtime.hydraActivity { info.activity = activity }
    public void A_running_heads_activity_is_replaced_by_the_runtimes_when_it_has_one()
    {
        WithRuntime(runtime =>
        {
            var stored = Stored(HydraHeadInfo.Status.Running);
            // No live activity: the stored one stays.
            Assert.Equal("stored activity", HydraLiveInfo.Shown(stored, runtime).Activity);
            Assert.Null(HydraLiveInfo.Shown(Stored(HydraHeadInfo.Status.Running, activity: null), runtime).Activity);

            runtime.HydraActivity = "live activity";
            Assert.Equal("live activity", HydraLiveInfo.Shown(stored, runtime).Activity);
            Assert.Equal("live activity", HydraLiveInfo.Shown(Stored(HydraHeadInfo.Status.Running, activity: null), runtime).Activity);
            // The record handed in is not changed (a struct copy in Swift).
            Assert.Equal("stored activity", stored.Activity);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-179")] // HydraPanel.swift:484 info.toolCalls = max(info.toolCalls, runtime.hydraToolCalls): a late update never lowers it
    public void A_running_heads_step_count_is_the_larger_of_the_stored_and_the_live_value()
    {
        WithRuntime(runtime =>
        {
            var stored = Stored(HydraHeadInfo.Status.Running, toolCalls: 4);
            runtime.HydraToolCalls = 2;
            Assert.Equal(4, HydraLiveInfo.Shown(stored, runtime).ToolCalls);
            runtime.HydraToolCalls = 4;
            Assert.Equal(4, HydraLiveInfo.Shown(stored, runtime).ToolCalls);
            runtime.HydraToolCalls = 7;
            Assert.Equal(7, HydraLiveInfo.Shown(stored, runtime).ToolCalls);
            Assert.Equal(4, stored.ToolCalls);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-179")] // HydraPanel.swift:475-479, :480-486 the live token total is deliberately NOT read
    public void The_live_token_total_is_never_read()
    {
        WithRuntime(runtime =>
        {
            runtime.HydraTokens = 123456;
            runtime.HydraActivity = "live activity";
            var shown = HydraLiveInfo.Shown(Stored(HydraHeadInfo.Status.Running, tokens: 10), runtime);
            Assert.Equal(10, shown.Tokens);
            Assert.Equal("live activity", shown.Activity);
        });
    }
}
