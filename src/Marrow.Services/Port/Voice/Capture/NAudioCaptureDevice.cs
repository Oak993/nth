// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): one open WASAPI capture stream (voice-SPEC V-4.1).
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// NAudio 3's <see cref="WasapiRecorder"/> on one endpoint: shared mode, event-driven, MMCSS "Audio", and no requested format, so WASAPI hands over the device's own mix format
/// (V-4.1; RESEARCH 3.2 item 1: never ask WASAPI to convert). The recorder's buffer callback is passed on as it is (a span, no copy); <see cref="Dispose"/> also disposes the endpoint.
/// </summary>
public sealed class NAudioCaptureDevice : ICaptureDevice
{
    private readonly MMDevice device;
    private readonly WasapiRecorder recorder;
    private volatile bool stopping;

    /// <summary>Takes ownership of <paramref name="endpoint"/>. Throws <see cref="CaptureDeviceException"/> (the endpoint is disposed then).</summary>
    /// <param name="communications">PORT-ONLY (Dextra AS-18, ED-NEW-dextra-as18-1): the stream is opened in the communications category (the system's echo cancellation, noise suppression and gain control).</param>
    public NAudioCaptureDevice(MMDevice endpoint, bool communications = false)
    {
        device = endpoint;
        try
        {
            var builder = new WasapiRecorderBuilder().WithDevice(endpoint).WithSharedMode().WithEventSync().WithMmcssThreadPriority("Audio");
            recorder = (communications ? builder.WithCommunicationsMode() : builder).Build();
        }
        catch (Exception error)
        {
            endpoint.Dispose();
            throw NAudioFailures.Wrap(error);
        }
        if (!NAudioFormats.TryMap(recorder.WaveFormat, out var format))
        {
            var description = recorder.WaveFormat.ToString();
            recorder.Dispose();
            endpoint.Dispose();
            throw new CaptureDeviceException(CaptureDeviceFailure.Other, "The endpoint's mix format is not one the capture accepts: " + description);
        }
        Format = format;
        EndpointId = endpoint.ID;
        recorder.DataAvailable += OnData;
        recorder.RecordingStopped += OnRecordingStopped;
    }

    public string EndpointId { get; }

    public DeviceFormat Format { get; }

    public event CaptureBufferHandler? DataAvailable;

    public event Action<CaptureDeviceFailure?>? Stopped;

    public void Start()
    {
        try
        {
            recorder.StartRecording();
        }
        catch (Exception error)
        {
            throw NAudioFailures.Wrap(error); // AudioDeviceInUseException and COM errors reach the session as a CaptureDeviceException of their kind
        }
    }

    public void Stop()
    {
        stopping = true;
        recorder.StopRecording();
    }

    public void Dispose()
    {
        recorder.Dispose(); // joins the capture thread: a buffer it is still delivering reaches OnData first
        recorder.DataAvailable -= OnData;
        recorder.RecordingStopped -= OnRecordingStopped;
        device.Dispose();
    }

    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
        DataAvailable?.Invoke(buffer, (flags & AudioClientBufferFlags.Silent) != 0);

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) =>
        Stopped?.Invoke(stopping || e.Exception is null ? null : NAudioFailures.Map(e.Exception));
}
