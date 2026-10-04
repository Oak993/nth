using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (voice-SPEC V-4.1 to V-4.4, OD-0099 Q-V-19 B): the production IAudioCapture over the scripted endpoint host. No hardware is touched. Tests that need the device's
// callbacks to reach the sink run the processing step by hand (runThread: false).
public sealed class WasapiCaptureTests
{
    private static ScriptedCaptureHost Host() => new(("mic-a", "Microphone Array"), ("mic-b", "USB Headset"));

    private static WasapiCapture Capture(ScriptedCaptureHost host, string? pick = null) => new(host, () => pick, runThread: false);

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.1: the default endpoint, in the device's own mix format, started once
    public void Start_opens_the_default_endpoint_in_its_own_format_and_reports_it()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        host.SetFormat("mic-a", new DeviceFormat(44100, 1, SampleEncoding.Pcm24));
        var capture = Capture(host);
        var format = capture.Start(new CollectingSink());
        Assert.Equal(new CaptureFormat(44100, 1), format);
        Assert.Equal([(string?)null], host.OpenRequests);
        var device = Assert.Single(host.Devices);
        Assert.Equal("mic-a", device.EndpointId);
        Assert.Equal(1, device.StartCount);
        Assert.True(device.IsStarted);
        Assert.Equal(1, host.WatcherCount);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // Q-V-19 B: the Microphone row's pick is the endpoint that opens
    public void Start_opens_the_chosen_endpoint_when_it_is_there()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host, "mic-b");
        capture.Start(new CollectingSink());
        Assert.Equal(["mic-b"], host.OpenRequests);
        Assert.Equal("mic-b", host.LastDevice!.EndpointId);
        Assert.Equal(new MicrophoneResolution("mic-b", false), capture.LastResolution);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // Q-V-19 B ("the chosen device missing falls back to the default")
    public void A_chosen_endpoint_that_is_missing_falls_back_to_the_default()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host, "unplugged-dock-mic");
        capture.Start(new CollectingSink());
        Assert.Equal([(string?)null], host.OpenRequests);
        Assert.Equal("mic-a", host.LastDevice!.EndpointId);
        Assert.Equal(new MicrophoneResolution(null, true), capture.LastResolution);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // Q-V-19 B: listed a moment ago, gone when opened: still the default
    public void A_chosen_endpoint_that_vanishes_between_listing_and_opening_falls_back_to_the_default()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        host.FailOpen("mic-b", CaptureDeviceFailure.NotFound);
        var capture = Capture(host, "mic-b");
        capture.Start(new CollectingSink());
        Assert.Equal(["mic-b", null], host.OpenRequests);
        Assert.Equal("mic-a", host.LastDevice!.EndpointId);
        Assert.True(capture.LastResolution.FellBack);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: another app holds the device: Start throws (the session shows the capture note); no second device is tried
    public void A_device_in_use_fails_the_start_without_trying_another()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        host.FailOpen("mic-b", CaptureDeviceFailure.InUse);
        var capture = Capture(host, "mic-b");
        var error = Assert.Throws<CaptureDeviceException>(() => capture.Start(new CollectingSink()));
        Assert.Equal(CaptureDeviceFailure.InUse, error.Failure);
        Assert.Equal(["mic-b"], host.OpenRequests);
        Assert.Equal(0, host.WatcherCount);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // a PC with no capture endpoint at all
    public void No_endpoint_at_all_fails_the_start()
    {
        using var runtime = new TestRuntime();
        var host = new ScriptedCaptureHost();
        var capture = Capture(host);
        var error = Assert.Throws<CaptureDeviceException>(() => capture.Start(new CollectingSink()));
        Assert.Equal(CaptureDeviceFailure.NotFound, error.Failure);
        Assert.Equal(0, host.WatcherCount);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-7.6: stopping flushes the last audio into the sink, synchronously, then closes the device and unsubscribes
    public void Stop_flushes_the_last_audio_closes_the_device_and_goes_quiet()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        capture.Start(sink);
        var device = host.LastDevice!;
        device.PushSignal(_ => 0.25f, TimeSpan.FromMilliseconds(30)); // 1440 frames of stereo at 48k, never pumped
        capture.Stop();
        Assert.Equal(["audio:2880"], sink.Journal);
        Assert.Equal(1, device.StopCount);
        Assert.True(device.IsDisposed);
        Assert.Equal(0, host.WatcherCount);
        device.Push([0.5f, 0.5f]); // a stopped device delivers nothing
        host.Notify(new EndpointNotice(EndpointNoticeKind.Removed, "mic-a"));
        runtime.RunUntilIdle();
        Assert.Equal(["audio:2880"], sink.Journal);
        capture.Stop(); // twice is fine
        Assert.Equal(1, device.StopCount);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // one capture can be started again after a stop, on a new device
    public void A_stopped_capture_can_start_again()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        capture.Start(new CollectingSink());
        capture.Stop();
        capture.Start(new CollectingSink());
        Assert.Equal(2, host.Devices.Count);
        Assert.True(host.Devices[1].IsStarted);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: the endpoint in use is removed mid-session
    public void Removing_the_endpoint_in_use_tells_the_sink_after_the_audio_it_had()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        capture.Start(sink);
        host.LastDevice!.PushSignal(_ => 0.25f, TimeSpan.FromMilliseconds(20));
        host.Remove("mic-a");
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Equal(["audio:1920", "event:DeviceRemoved"], sink.Journal);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: another endpoint going away is not this session's business
    public void Removing_another_endpoint_tells_nobody()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        capture.Start(sink);
        host.Remove("mic-b");
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Empty(sink.Journal);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: following the Windows default, a change of default ends the session
    public void A_changed_default_ends_a_capture_that_follows_the_default()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        capture.Start(sink);
        host.Notify(new EndpointNotice(EndpointNoticeKind.DefaultChanged, "mic-b"));
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Equal([CaptureDeviceEventKind.DefaultDeviceChanged], sink.Events);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // Q-V-19 B: with a microphone picked, the Windows default changing does not matter
    public void A_changed_default_does_not_end_a_capture_on_a_chosen_endpoint()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host, "mic-b");
        var sink = new CollectingSink();
        capture.Start(sink);
        host.Notify(new EndpointNotice(EndpointNoticeKind.DefaultChanged, "mic-a"));
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Empty(sink.Events);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: the mix format of the endpoint in use changed
    public void A_format_change_of_the_endpoint_in_use_is_reported_and_of_another_is_not()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        capture.Start(sink);
        host.Notify(new EndpointNotice(EndpointNoticeKind.FormatChanged, "mic-b"));
        host.Notify(new EndpointNotice(EndpointNoticeKind.Added, "mic-c"));
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Empty(sink.Events);
        host.Notify(new EndpointNotice(EndpointNoticeKind.FormatChanged, "mic-a"));
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Equal([CaptureDeviceEventKind.FormatChanged], sink.Events);
        capture.Stop();
    }

    [Theory]
    [InlineData(CaptureDeviceFailure.Removed, CaptureDeviceEventKind.DeviceRemoved)]
    [InlineData(CaptureDeviceFailure.NotFound, CaptureDeviceEventKind.DeviceRemoved)]
    [InlineData(CaptureDeviceFailure.Other, CaptureDeviceEventKind.DeviceRemoved)]
    [InlineData(CaptureDeviceFailure.InUse, CaptureDeviceEventKind.InUse)]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: the device stops by itself: gone is DeviceRemoved, taken is InUse (AUDCLNT_E_DEVICE_IN_USE)
    public void A_device_that_stops_by_itself_is_reported_by_its_reason(CaptureDeviceFailure failure, CaptureDeviceEventKind expected)
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        capture.Start(sink);
        host.LastDevice!.Fail(failure);
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Equal([expected], sink.Events);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // the same loss is one event, whichever way it is noticed first
    public void A_removal_noticed_twice_is_reported_once()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        capture.Start(sink);
        host.Remove("mic-a");
        host.LastDevice!.Fail(CaptureDeviceFailure.Removed);
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Equal([CaptureDeviceEventKind.DeviceRemoved], sink.Events);
        capture.Stop();
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // closing the device ourselves is not an event
    public void Stopping_the_capture_raises_no_device_event()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        capture.Start(sink);
        capture.Stop();
        runtime.RunUntilIdle();
        Assert.Empty(sink.Events);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // a session stops its capture from inside the device event (VoiceSession.Stop): re-entry must neither throw nor deliver twice
    public void A_sink_that_stops_the_capture_from_inside_a_device_event_is_safe()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = Capture(host);
        var sink = new CollectingSink();
        sink.OnEvent = _ => capture.Stop();
        capture.Start(sink);
        var device = host.LastDevice!;
        device.PushSignal(_ => 0.25f, TimeSpan.FromMilliseconds(10));
        host.Remove("mic-a");
        device.PushSignal(_ => 0.25f, TimeSpan.FromMilliseconds(10)); // arrives after the removal
        capture.Pump();
        runtime.RunUntilIdle();
        Assert.Equal(["audio:960", "event:DeviceRemoved", "audio:960"], sink.Journal);
        Assert.True(device.IsDisposed);
        runtime.RunUntilIdle();
        Assert.Equal(3, sink.Journal.Count);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-2.2: the real processing thread is the default; audio the device pushes reaches the sink on the main loop without anyone calling Pump
    public void The_default_capture_delivers_through_its_own_processing_thread()
    {
        using var runtime = new TestRuntime();
        var host = Host();
        var capture = new WasapiCapture(host, () => null);
        var sink = new CollectingSink();
        capture.Start(sink);
        host.LastDevice!.PushSignal(_ => 0.25f, TimeSpan.FromMilliseconds(40));
        runtime.Loop.RunUntil(() => sink.Samples.Count >= 3840, TimeSpan.FromSeconds(20));
        capture.Stop();
        Assert.Equal(3840, sink.Samples.Count);
    }
}
