using Marrow.Core.Web;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-review-11")]
public sealed class WebReviewAgentLifetimeTests
{
    [Fact]
    public async Task Disposal_reentered_from_a_page_call_cancels_both_calls_and_releases_the_owned_queue_safely()
    {
        using var rig = new WebAgentRig();
        var tab = await rig.InviteTab();
        Task<WebToolResult>? queued = null;
        var held = new TaskCompletionSource();
        tab.Page.Hold = ("Accessibility.getFullAXTree", held.Task);
        tab.Page.OnCall = (method, _) =>
        {
            if (method != "Accessibility.getFullAXTree") return;
            queued = rig.Start(WebToolSchemas.Read, "{\"tab\":\"t1\"}");
            rig.Engine.Dispose();
        };
        try
        {
            var running = rig.Start(WebToolSchemas.Read, "{\"tab\":\"t1\"}");
            Assert.Equal(WebErrorCodes.Stopped, (await running).Error);
            Assert.NotNull(queued);
            Assert.Equal(WebErrorCodes.Stopped, (await queued).Error);
        }
        finally { held.TrySetResult(); }
    }

    [Fact]
    public async Task A_call_after_disposal_returns_stopped_without_reopening_the_engine()
    {
        using var rig = new WebAgentRig();
        rig.Engine.Dispose();
        Assert.Equal(WebErrorCodes.Stopped, (await rig.Start(WebToolSchemas.Tabs, "{}")).Error);
    }

    [Fact]
    public async Task Disposing_during_a_step_cancels_running_and_queued_calls_without_a_disposal_exception()
    {
        using var rig = new WebAgentRig();
        var tab = await rig.InviteTab();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tab.Page.Hold = ("Accessibility.getFullAXTree", held.Task);
        tab.Page.OnCall = (method, _) => { if (method == "Accessibility.getFullAXTree") entered.TrySetResult(); };
        var running = rig.Start(WebToolSchemas.Read, "{\"tab\":\"t1\"}");
        await entered.Task;
        var queued = rig.Start(WebToolSchemas.Read, "{\"tab\":\"t1\"}");
        try
        {
            rig.Engine.Dispose();
            Assert.Equal(WebErrorCodes.Stopped, (await running).Error);
            Assert.Equal(WebErrorCodes.Stopped, (await queued).Error);
        }
        finally { held.TrySetResult(); }
    }
}
