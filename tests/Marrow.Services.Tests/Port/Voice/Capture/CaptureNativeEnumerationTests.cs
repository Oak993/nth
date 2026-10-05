using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Tests.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5, WINDOWS-ONLY (native run): the real endpoint enumerator on the PC under test. These LIST the capture endpoints, watch for changes and ask for an id that does not exist;
// none of them opens a capture stream, starts recording or reads a sample (a test never opens a real microphone).
public sealed class CaptureNativeEnumerationTests
{
    [WindowsOnlyFact("lists the real capture endpoints through MMDeviceEnumerator")]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")]
    public void The_real_host_lists_active_capture_endpoints_with_ids_and_names_and_a_default_that_is_one_of_them()
    {
        var host = new NAudioCaptureHost();
        var endpoints = host.CaptureEndpoints();
        Assert.All(endpoints, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Id));
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
        });
        Assert.Equal(endpoints.Count, endpoints.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var defaultId = host.DefaultEndpointId();
        if (defaultId is not null) Assert.Contains(endpoints, e => string.Equals(e.Id, defaultId, StringComparison.OrdinalIgnoreCase));
        var items = MicrophoneChoices.Items(host);
        Assert.Equal(endpoints.Count + 1, items.Count);
        Assert.Null(items[0].Value);
    }

    [WindowsOnlyFact("asks the real enumerator for an endpoint id that is not there")]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")]
    public void The_real_host_says_NotFound_for_an_endpoint_that_does_not_exist_and_opens_nothing()
    {
        var host = new NAudioCaptureHost();
        var error = Assert.Throws<CaptureDeviceException>(() => host.Open("{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}"));
        Assert.Equal(CaptureDeviceFailure.NotFound, error.Failure);
        Assert.Equal(new MicrophoneResolution(null, true), MicrophoneChoices.Resolve(host, "{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}"));
    }

    [WindowsOnlyFact("registers and removes an IMMNotificationClient")]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")]
    public void The_real_host_watches_and_stops_watching_without_raising_anything_by_itself()
    {
        var host = new NAudioCaptureHost();
        var notices = new List<EndpointNotice>();
        using (host.Watch(notices.Add)) Thread.Sleep(50);
        Assert.Empty(notices);
    }
}
