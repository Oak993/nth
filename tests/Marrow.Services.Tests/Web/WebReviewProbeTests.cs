using Marrow.Core.Web;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-review-14")]
public sealed class WebReviewProbeTests
{
    [Theory]
    [InlineData(WebFailureAction.Wait)]
    [InlineData(WebFailureAction.Reload)]
    public void A_user_retry_retires_the_old_hung_probe_and_resumes_monitoring(WebFailureAction action)
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        fake.AnswersFrames = false;
        rig.Surface.Tick();
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(WebFailureKind.RendererHung, view.Failure?.Kind);
        view.InvokeFailureAction(action);
        Assert.Null(view.Failure);
        rig.Surface.Tick();
        Assert.Equal(2, fake.Log.Count(line => line == "Frames 1"));
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(WebFailureKind.RendererHung, view.Failure?.Kind);
    }
}
