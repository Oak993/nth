// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-19 B; first-release voice typing, VT-5): the microphone (voice-SPEC V-4).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// <see cref="IAudioCapture"/> over the endpoint seam: opens the Microphone row's pick (or the Windows default, also when the pick is missing, Q-V-19 B) in the device's own mix
/// format (V-4.1), runs a <see cref="CapturePipeline"/> into the session's sink, and reports the device events of V-4.4. <see cref="Start"/> and <see cref="Stop"/> are called on
/// the main thread; <see cref="Stop"/> flushes the last audio into the sink before it returns.
/// </summary>
public sealed class WasapiCapture : IAudioCapture
{
    private readonly ICaptureHost host;
    private readonly Func<string?> microphone;
    private readonly bool runThread;
    private readonly bool communications;
    private Run? current;

    /// <param name="host">The endpoints.</param>
    /// <param name="microphone">The stored Microphone pick (<c>voiceTyping.mic</c>), read at each start; null is the Windows default.</param>
    /// <param name="runThread">True in production: the pipeline's own processing thread. False in a test, which calls <see cref="Pump"/> itself.</param>
    /// <param name="communications">PORT-ONLY (Dextra AS-18, DESIGN.md rev 5, 2.4, ED-NEW-dextra-as18-1): open the stream in the communications category, so Windows' echo cancellation runs (the voice conversation); dictation leaves it off.</param>
    public WasapiCapture(ICaptureHost host, Func<string?> microphone, bool runThread = true, bool communications = false)
    {
        this.host = host;
        this.microphone = microphone;
        this.runThread = runThread;
        this.communications = communications;
    }

    private ICaptureDevice OpenDevice(string? endpointId) => communications ? host.OpenCommunications(endpointId) : host.Open(endpointId);

    /// <summary>What the last <see cref="Start"/> resolved the Microphone pick to.</summary>
    public MicrophoneResolution LastResolution { get; private set; }

    public CaptureFormat Start(IAudioCaptureSink sink)
    {
        Stop();
        var resolution = MicrophoneChoices.Resolve(host, microphone());
        ICaptureDevice device;
        try
        {
            device = OpenDevice(resolution.EndpointId);
        }
        catch (CaptureDeviceException error) when (resolution.EndpointId is not null && error.Failure == CaptureDeviceFailure.NotFound)
        {
            resolution = new MicrophoneResolution(null, true); // listed a moment ago, gone now: the default
            device = OpenDevice(null);
        }
        LastResolution = resolution;
        var run = new Run(device, new CapturePipeline(device.Format, sink, runThread), resolution.EndpointId is null);
        try
        {
            device.DataAvailable += run.Pipeline.OnBuffer;
            device.Stopped += run.OnStopped;
            run.Watch = host.Watch(run.OnNotice);
            device.Start();
        }
        catch
        {
            run.Close(flush: false);
            throw;
        }
        current = run;
        return device.Format.ToCaptureFormat();
    }

    /// <summary>The processing step by hand (a capture built with <c>runThread: false</c>).</summary>
    public void Pump() => current?.Pipeline.Pump();

    public void Stop()
    {
        var run = current;
        current = null;
        run?.Close(flush: true);
    }

    private sealed class Run(ICaptureDevice device, CapturePipeline pipeline, bool followsDefault)
    {
        private int raised;
        private volatile bool closing;

        public ICaptureDevice Device { get; } = device;

        public CapturePipeline Pipeline { get; } = pipeline;

        public IDisposable? Watch { get; set; }

        public void OnStopped(CaptureDeviceFailure? failure)
        {
            if (failure is null) return; // asked for by Stop
            Raise(failure == CaptureDeviceFailure.InUse ? CaptureDeviceEventKind.InUse : CaptureDeviceEventKind.DeviceRemoved);
        }

        public void OnNotice(EndpointNotice notice)
        {
            var mine = string.Equals(notice.EndpointId, Device.EndpointId, StringComparison.OrdinalIgnoreCase);
            switch (notice.Kind)
            {
                case EndpointNoticeKind.Removed when mine:
                    Raise(CaptureDeviceEventKind.DeviceRemoved);
                    break;
                case EndpointNoticeKind.FormatChanged when mine:
                    Raise(CaptureDeviceEventKind.FormatChanged);
                    break;
                case EndpointNoticeKind.DefaultChanged when followsDefault && !mine:
                    Raise(CaptureDeviceEventKind.DefaultDeviceChanged);
                    break;
            }
        }

        // Order matters (V-7.6, V-4.7): the capture thread is stopped and joined first (Stop asks, Dispose waits for it), so its last buffer is in the ring; only then is the ring
        // flushed into the sink and cleared. Clearing first would lose that buffer or leave it written into a cleared ring.
        public void Close(bool flush)
        {
            closing = true;
            Watch?.Dispose();
            try
            {
                Device.Stop();
            }
            catch (Exception)
            {
                // The device is gone already; nothing more to stop.
            }
            try
            {
                Device.Dispose();
            }
            catch (Exception)
            {
                // As above.
            }
            if (flush) Pipeline.Flush();
            Pipeline.Dispose();
        }

        // The same loss reaches us twice (the endpoint notice and the stream's end): one event per kind.
        private void Raise(CaptureDeviceEventKind kind)
        {
            if (closing) return;
            var bit = 1 << (int)kind;
            if ((Interlocked.Or(ref raised, bit) & bit) != 0) return;
            Pipeline.RaiseEvent(kind);
        }
    }
}
