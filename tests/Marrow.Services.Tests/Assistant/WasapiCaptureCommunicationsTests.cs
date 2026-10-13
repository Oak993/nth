using Marrow.Services.Port.Voice.Capture;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "Barge-in": the mic is opened in communications mode so Windows' echo cancellation runs): the voice conversation's capture opens
// its stream in the communications category, dictation's does not, and a missing pick falls back to the default endpoint in the same category.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-1")]
public sealed class WasapiCaptureCommunicationsTests
{
    [Fact]
    public void A_communications_capture_opens_the_owners_pick_in_the_communications_category()
    {
        var host = new ScriptedCaptureHost(("mic-1", "Desk"), ("mic-2", "Headset"));
        var capture = new WasapiCapture(host, () => "mic-2", runThread: false, communications: true);

        capture.Start(new NullAudioSink());
        capture.Stop();

        Assert.Equal(["mic-2"], host.CommunicationsRequests);
        Assert.Equal(["mic-2"], host.OpenRequests);
    }

    [Fact]
    public void Dictation_keeps_the_ordinary_stream_and_never_asks_for_the_communications_one()
    {
        var host = new ScriptedCaptureHost(("mic-1", "Desk"));
        var capture = new WasapiCapture(host, () => null, runThread: false);

        capture.Start(new NullAudioSink());
        capture.Stop();

        Assert.Empty(host.CommunicationsRequests);
        Assert.Single(host.OpenRequests);
    }

    [Fact]
    public void A_pick_that_has_gone_falls_back_to_the_default_endpoint_in_the_same_category()
    {
        var host = new ScriptedCaptureHost(("mic-1", "Desk"));
        var capture = new WasapiCapture(host, () => "unplugged", runThread: false, communications: true);

        capture.Start(new NullAudioSink());
        capture.Stop();

        Assert.All(host.CommunicationsRequests, request => Assert.Null(request));
        Assert.NotEmpty(host.CommunicationsRequests);
    }
}
