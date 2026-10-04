using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (voice-SPEC V-2.5, V-T46 for the Services seam): in a test runtime the endpoint seam is a stub that throws when touched, so a test can never list or open a real
// microphone by omission; a test binds the scripted host for its own flow.
public sealed class CaptureSeamTests
{
    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")]
    public void The_ambient_endpoint_seam_fails_closed_on_every_member()
    {
        var host = CaptureSeams.Host.Current;
        Assert.IsType<FailClosedCaptureHost>(host);
        Assert.Equal(nameof(ICaptureHost.CaptureEndpoints), Assert.Throws<SeamNotBoundException>(() => host.CaptureEndpoints()).Member);
        Assert.Equal(nameof(ICaptureHost.DefaultEndpointId), Assert.Throws<SeamNotBoundException>(() => host.DefaultEndpointId()).Member);
        Assert.Equal(nameof(ICaptureHost.Open), Assert.Throws<SeamNotBoundException>(() => host.Open(null)).Member);
        Assert.Equal(nameof(ICaptureHost.Watch), Assert.Throws<SeamNotBoundException>(() => host.Watch(_ => { })).Member);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // a capture built on the ambient default never reaches a device
    public void A_capture_over_the_ambient_default_fails_on_Start_and_opens_nothing()
    {
        var capture = new WasapiCapture(CaptureSeams.Host.Current, () => null);
        Assert.Throws<SeamNotBoundException>(() => capture.Start(new Fixtures.CollectingSink()));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // overriding for the current flow binds the scripted host; the process default is unchanged
    public void A_test_binds_the_scripted_host_for_its_own_flow_only()
    {
        var scripted = new ScriptedCaptureHost(("a", "A"));
        using (CaptureSeams.Host.Override(scripted))
        {
            Assert.Same(scripted, CaptureSeams.Host.Current);
            Assert.Single(CaptureSeams.Host.Current.CaptureEndpoints());
        }
        Assert.IsType<FailClosedCaptureHost>(CaptureSeams.Host.Current);
    }
}
