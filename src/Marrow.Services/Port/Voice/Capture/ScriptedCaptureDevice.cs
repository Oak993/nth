// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the open-device double (voice-SPEC V-2.4, V-14.8). It opens no hardware.
using System.Buffers.Binary;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// An <see cref="ICaptureDevice"/> that delivers what a test pushes, on the pushing thread (which stands in for the device's capture thread). Samples
/// are encoded into the device's own format, so a test can feed float, PCM16, PCM24 or PCM32 buffers at any rate and channel count.
/// </summary>
public sealed class ScriptedCaptureDevice(string endpointId, DeviceFormat format) : ICaptureDevice
{
    public string EndpointId { get; } = endpointId;

    public DeviceFormat Format { get; } = format;

    public event CaptureBufferHandler? DataAvailable;

    public event Action<CaptureDeviceFailure?>? Stopped;

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public bool IsStarted { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Frames pushed so far (one sample per channel each).</summary>
    public long FramesPushed { get; private set; }

    /// <summary>When set, <see cref="Start"/> throws it (a device that cannot start).</summary>
    public Exception? FailStart { get; set; }

    /// <summary>A buffer the capture thread is still delivering when <see cref="Stop"/> is called: it arrives inside Stop, as a real device's last buffer does.</summary>
    public float[]? InFlightDuringStop { get; set; }

    /// <summary>A buffer that arrives while <see cref="Dispose"/> joins the capture thread (after Stop returned): the last one a real device may still deliver.</summary>
    public float[]? InFlightDuringDispose { get; set; }

    public void Start()
    {
        StartCount += 1;
        if (FailStart is { } failure) throw failure;
        IsStarted = true;
    }

    public void Stop()
    {
        StopCount += 1;
        if (!IsStarted) return;
        if (InFlightDuringStop is { } last) Push(last);
        IsStarted = false;
        Stopped?.Invoke(null);
    }

    public void Dispose()
    {
        if (InFlightDuringDispose is { } last)
        {
            var bytes = new byte[last.Length * Format.BytesPerSample];
            for (var i = 0; i < last.Length; i++) Encode(last[i], bytes.AsSpan(i * Format.BytesPerSample));
            FramesPushed += bytes.Length / Format.BytesPerFrame;
            DataAvailable?.Invoke(bytes, false); // the capture thread's last buffer, delivered although Stop has returned
        }
        IsDisposed = true;
        IsStarted = false;
    }

    /// <summary>Delivers raw device bytes (whole frames) as one buffer. Nothing is delivered when the device is not started.</summary>
    public void PushBytes(ReadOnlySpan<byte> bytes, bool silent = false)
    {
        if (!IsStarted) return;
        FramesPushed += bytes.Length / Format.BytesPerFrame;
        DataAvailable?.Invoke(bytes, silent);
    }

    /// <summary>Delivers interleaved float samples, encoded into this device's format.</summary>
    public void Push(ReadOnlySpan<float> interleaved, bool silent = false)
    {
        var bytes = new byte[interleaved.Length * Format.BytesPerSample];
        for (var i = 0; i < interleaved.Length; i++) Encode(interleaved[i], bytes.AsSpan(i * Format.BytesPerSample));
        PushBytes(bytes, silent);
    }

    /// <summary>Delivers <paramref name="duration"/> of <paramref name="signal"/> (a function of the frame index since the first push), the same value in every channel.</summary>
    public void PushSignal(Func<long, float> signal, TimeSpan duration)
    {
        var frames = (int)Math.Round(duration.TotalSeconds * Format.SampleRate);
        var samples = new float[frames * Format.Channels];
        for (var f = 0; f < frames; f++)
        {
            var value = signal(FramesPushed + f);
            for (var c = 0; c < Format.Channels; c++) samples[f * Format.Channels + c] = value;
        }
        Push(samples);
    }

    /// <summary>The device stops by itself, as an unplugged or taken device does.</summary>
    public void Fail(CaptureDeviceFailure failure)
    {
        IsStarted = false;
        Stopped?.Invoke(failure);
    }

    private void Encode(float value, Span<byte> destination)
    {
        switch (Format.Encoding)
        {
            case SampleEncoding.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(destination, value);
                break;
            case SampleEncoding.Pcm16:
                BinaryPrimitives.WriteInt16LittleEndian(destination, (short)Math.Clamp(Math.Round(value * 32768.0), short.MinValue, short.MaxValue));
                break;
            case SampleEncoding.Pcm24:
                var v24 = (int)Math.Clamp(Math.Round(value * 8388608.0), -8388608, 8388607);
                destination[0] = (byte)(v24 & 0xFF);
                destination[1] = (byte)(v24 >> 8 & 0xFF);
                destination[2] = (byte)(v24 >> 16 & 0xFF);
                break;
            default:
                BinaryPrimitives.WriteInt32LittleEndian(destination, (int)Math.Clamp(Math.Round(value * 2147483648.0), int.MinValue, int.MaxValue));
                break;
        }
    }
}
