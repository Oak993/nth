using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// PORT-ONLY: AppKit's NSSound has no .NET or Avalonia counterpart; ED-151 gives the port type, its output seam and
// the Windows waveOut output (foundation unit F.Sound).
// The NSSound sites it stands in for are the only two at B2: DroppyCode/UI/Sound/FinishChime.swift:12-22 and
// DroppyCode/UI/Sound/SettleChime.swift:11-21. Each builds one sound in a lazy static with NSSound(data:) from
// ToneSynth.wave bytes (DroppyCode/UI/Sound/ToneSynth.swift:27-68), sets a Float volume once, and plays it:
// FinishChime relies on play() declining while the sound is in flight (FinishChime.swift:19-21); SettleChime calls
// stop() then play() for a replay from the top (SettleChime.swift:55-57).

/// <summary>
/// NSSound as ED-151 gives it: one sound made from WAV bytes that plays through <see cref="SoundOutput"/>, declines
/// a play while it is playing, and replays from the top after a stop.
/// </summary>
// FinishChime.swift:12-16, SettleChime.swift:11-15: `NSSound?` built from `Data`.
public sealed class SwiftSound
{
    private readonly Lock gate = new();
    private readonly byte[] data;
    private Run? active;

    private SwiftSound(byte[] data, PcmFormat format, int samplesOffset, int samplesLength)
    {
        this.data = data;
        Format = format;
        Samples = new ReadOnlyMemory<byte>(data, samplesOffset, samplesLength);
    }

    /// <summary>
    /// NSSound(data:), which is failable: null when the bytes are not a PCM WAV the port can play (RIFF/WAVE with a
    /// fmt chunk of format 1, one or two channels, 8 or 16 bits, a sample rate, then a data chunk of whole frames that
    /// the bytes hold in full; chunks between them are skipped with their RIFF pad byte). The bytes are
    /// copied, as the Swift initializer takes a <c>Data</c> value.
    /// </summary>
    // FinishChime.swift:13, SettleChime.swift:12. The layout read is the one ToneSynth.swift:52-64 writes.
    public static SwiftSound? Create(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var copy = data.ToArray();
        ReadOnlySpan<byte> bytes = copy;
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
            return null;

        PcmFormat? format = null;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var id = bytes.Slice(position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 4, 4));
            var bodyStart = position + 8;
            var available = bytes.Length - bodyStart;
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || available < 16) return null;
                var body = bytes.Slice(bodyStart, 16);
                var formatTag = BinaryPrimitives.ReadUInt16LittleEndian(body);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
                var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                // ToneSynth.swift:57 writes 1 for PCM; waveOut plays PCM of one or two channels at 8 or 16 bits.
                if (formatTag != 1 || channels is < 1 or > 2 || sampleRate == 0 || bitsPerSample is not (8 or 16))
                    return null;
                format = new PcmFormat(channels, sampleRate, bitsPerSample, (ushort)(channels * bitsPerSample / 8));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (format is not { } found) return null;
                // No Swift line defines NSSound(data:) for a data chunk that claims more bytes than follow it or is
                // not a whole number of frames (the B2 callers pass only ToneSynth.swift:52-67 bytes, which are
                // neither): the port takes ED-151's null path for both, pending an owner ruling (OD-0021 wave).
                if (size > (uint)available || size % found.BlockAlign != 0) return null;
                return new SwiftSound(copy, found, bodyStart, (int)size);
            }
            // RIFF chunks are padded to an even length.
            var next = (long)bodyStart + size + (size & 1);
            if (next > bytes.Length) break;
            position = (int)next;
        }
        return null;
    }

    /// <summary>The bytes the sound was made from.</summary>
    public ReadOnlyMemory<byte> Data => data;

    /// <summary>The PCM format of the fmt chunk, with the block alignment waveOut needs.</summary>
    public PcmFormat Format { get; }

    /// <summary>The data chunk: the PCM samples the output plays.</summary>
    public ReadOnlyMemory<byte> Samples { get; }

    /// <summary>
    /// NSSound.volume, a Float the output applies to the samples. 1 until set: both B2 sites set it before any play
    /// (FinishChime.swift:14 to 0.35, SettleChime.swift:50 to 0.18), so the initial value is never heard. A change
    /// reaches the output with the next play.
    /// </summary>
    // FinishChime.swift:10,14; SettleChime.swift:46,50.
    public float Volume { get; set; } = 1f;

    /// <summary>NSSound.isPlaying: a play has started and has neither finished nor been stopped.</summary>
    public bool IsPlaying
    {
        get { lock (gate) return active is not null; }
    }

    /// <summary>
    /// NSSound.play(): starts the sound from the top and returns true; returns false and does nothing while the sound
    /// is already playing, and returns false when the output cannot start.
    /// </summary>
    // FinishChime.swift:19-21: "NSSound declines a play while one is in flight".
    public bool Play()
    {
        lock (gate)
        {
            if (active is not null) return false;
            var run = new Run();
            active = run;
            var handle = SoundOutput.Current.Current.Start(this, Volume, () => Finished(run));
            if (handle is null)
            {
                if (active == run) active = null;
                return false;
            }
            run.Handle = handle;
            return true;
        }
    }

    /// <summary>
    /// NSSound.stop(): ends a playback in flight, so the next play starts from the top. Returns true when it stopped
    /// a playback; on an idle sound it touches nothing and returns false. The two B2 callers discard the result
    /// (SettleChime.swift:56).
    /// </summary>
    // SettleChime.swift:55-57.
    public bool Stop()
    {
        ISoundPlayback? handle;
        lock (gate)
        {
            if (active is null) return false;
            handle = active.Handle;
            active = null;
        }
        handle?.Stop();
        return true;
    }

    /// <summary>The output played the last sample of <paramref name="run"/>; a report from a stopped run is ignored.</summary>
    private void Finished(Run run)
    {
        lock (gate)
        {
            if (active == run) active = null;
        }
    }

    /// <summary>One play: the finish report of an earlier, stopped play must not end a later one.</summary>
    private sealed class Run
    {
        public ISoundPlayback? Handle;
    }

    /// <summary>The fmt chunk the output needs: channels, frames a second, bits a sample and bytes a frame.</summary>
    // ToneSynth.swift:58-62.
    public readonly record struct PcmFormat(ushort Channels, uint SampleRate, ushort BitsPerSample, ushort BlockAlign);
}

/// <summary>Where a <see cref="SwiftSound"/> plays: the default output device in the app, a fake in tests (ED-151).</summary>
public interface ISoundOutput
{
    /// <summary>
    /// Starts one playback of the sound's samples at <paramref name="volume"/>, and calls <paramref name="finished"/>
    /// once, on any thread, when the last sample has played. Not called after the playback is stopped. Returns null
    /// when the device cannot start.
    /// </summary>
    ISoundPlayback? Start(SwiftSound sound, float volume, Action finished);
}

/// <summary>One playback an <see cref="ISoundOutput"/> started.</summary>
public interface ISoundPlayback
{
    /// <summary>Ends the playback now, without a finish report.</summary>
    void Stop();
}

/// <summary>The seam behind every <see cref="SwiftSound"/> (the MainActor.Executor pattern, ED-065, ED-151).</summary>
public static class SoundOutput
{
    /// <summary>The waveOut output, created on first use; tests override it for their own async flow.</summary>
    public static Ambient<ISoundOutput> Current { get; } = new(() => new WaveOutSoundOutput());
}

/// <summary>
/// The production output (ED-151): the default output device through the Windows waveOut API (winmm.dll by
/// P/Invoke), one device handle a playback, with the volume applied to the samples.
/// </summary>
public sealed class WaveOutSoundOutput : ISoundOutput
{
    public ISoundPlayback? Start(SwiftSound sound, float volume, Action finished)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(finished);
        if (!OperatingSystem.IsWindows()) return null;
        return WaveOutPlayback.Open(sound.Format, ApplyVolume(sound, volume), finished);
    }

    /// <summary>
    /// The samples scaled by <paramref name="volume"/> (clamped to NSSound's 0 to 1), rounded half away from zero:
    /// signed 16-bit samples around 0, unsigned 8-bit samples around their 128 midpoint.
    /// </summary>
    public static byte[] ApplyVolume(SwiftSound sound, float volume)
    {
        ArgumentNullException.ThrowIfNull(sound);
        var gain = float.IsNaN(volume) ? 0.0 : Math.Clamp((double)volume, 0.0, 1.0);
        var samples = sound.Samples.ToArray();
        if (sound.Format.BitsPerSample == 16)
        {
            for (var i = 0; i + 1 < samples.Length; i += 2)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(i));
                var scaled = (short)Math.Round(sample * gain, MidpointRounding.AwayFromZero);
                BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(i), scaled);
            }
        }
        else
        {
            for (var i = 0; i < samples.Length; i++)
            {
                var offset = samples[i] - 128;
                samples[i] = (byte)(Math.Round(offset * gain, MidpointRounding.AwayFromZero) + 128);
            }
        }
        return samples;
    }

    /// <summary>
    /// One waveOut device playing one buffer. The device signals an event (CALLBACK_EVENT); a background thread waits
    /// on it, and when the buffer is done (played through, or reset by Stop) closes the device, frees the memory and,
    /// unless stopped, reports the finish.
    /// </summary>
    private sealed class WaveOutPlayback : ISoundPlayback
    {
        private readonly Lock gate = new();
        private readonly IntPtr device;
        private readonly IntPtr header;
        private readonly IntPtr buffer;
        private readonly AutoResetEvent signal;
        private readonly Action finished;
        private bool stopped;
        private bool closed;

        private WaveOutPlayback(IntPtr device, IntPtr header, IntPtr buffer, AutoResetEvent signal, Action finished)
        {
            this.device = device;
            this.header = header;
            this.buffer = buffer;
            this.signal = signal;
            this.finished = finished;
        }

        public static WaveOutPlayback? Open(SwiftSound.PcmFormat format, byte[] samples, Action finished)
        {
            var waveFormat = new Native.WaveFormatEx
            {
                FormatTag = 1,
                Channels = format.Channels,
                SamplesPerSec = format.SampleRate,
                AvgBytesPerSec = format.SampleRate * format.BlockAlign,
                BlockAlign = format.BlockAlign,
                BitsPerSample = format.BitsPerSample,
                Size = 0,
            };
            var signal = new AutoResetEvent(false);
            if (Native.waveOutOpen(out var device, Native.WaveMapper, ref waveFormat,
                    signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, Native.CallbackEvent) != 0)
            {
                signal.Dispose();
                return null;
            }

            var buffer = Marshal.AllocHGlobal(Math.Max(samples.Length, 1));
            Marshal.Copy(samples, 0, buffer, samples.Length);
            var header = Marshal.AllocHGlobal(Native.HeaderSize);
            Marshal.StructureToPtr(new Native.WaveHdr { Data = buffer, BufferLength = (uint)samples.Length }, header, false);
            var playback = new WaveOutPlayback(device, header, buffer, signal, finished);

            if (Native.waveOutPrepareHeader(device, header, (uint)Native.HeaderSize) != 0)
            {
                Native.waveOutClose(device);
                playback.Free();
                return null;
            }
            if (Native.waveOutWrite(device, header, (uint)Native.HeaderSize) != 0)
            {
                Native.waveOutUnprepareHeader(device, header, (uint)Native.HeaderSize);
                Native.waveOutClose(device);
                playback.Free();
                return null;
            }
            new Thread(playback.WaitUntilDone) { IsBackground = true, Name = "SwiftSound waveOut" }.Start();
            return playback;
        }

        public void Stop()
        {
            lock (gate)
            {
                if (closed || stopped) return;
                stopped = true;
                // Marks the buffer done and signals the event; the waiting thread then closes the device.
                Native.waveOutReset(device);
            }
        }

        private void WaitUntilDone()
        {
            while (true)
            {
                signal.WaitOne();
                bool report;
                lock (gate)
                {
                    if ((Marshal.ReadInt32(header, Native.FlagsOffset) & Native.WhdrDone) == 0) continue;
                    Native.waveOutReset(device);
                    Native.waveOutUnprepareHeader(device, header, (uint)Native.HeaderSize);
                    Native.waveOutClose(device);
                    Free();
                    closed = true;
                    report = !stopped;
                }
                // Outside the lock, so a SwiftSound taking its own lock in the report cannot deadlock with Stop.
                if (report) finished();
                return;
            }
        }

        private void Free()
        {
            Marshal.FreeHGlobal(header);
            Marshal.FreeHGlobal(buffer);
            signal.Dispose();
        }
    }

    /// <summary>winmm.dll waveOut functions and structures (mmeapi.h, mmreg.h).</summary>
    private static class Native
    {
        public const uint WaveMapper = 0xFFFFFFFF;
        public const uint CallbackEvent = 0x00050000;
        public const int WhdrDone = 0x00000001;
        public static readonly int HeaderSize = Marshal.SizeOf<WaveHdr>();
        public static readonly int FlagsOffset = (int)Marshal.OffsetOf<WaveHdr>(nameof(WaveHdr.Flags));

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct WaveFormatEx
        {
            public ushort FormatTag;
            public ushort Channels;
            public uint SamplesPerSec;
            public uint AvgBytesPerSec;
            public ushort BlockAlign;
            public ushort BitsPerSample;
            public ushort Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WaveHdr
        {
            public IntPtr Data;
            public uint BufferLength;
            public uint BytesRecorded;
            public IntPtr User;
            public uint Flags;
            public uint Loops;
            public IntPtr Next;
            public IntPtr Reserved;
        }

        [DllImport("winmm.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint waveOutOpen(out IntPtr device, uint deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, uint flags);

        [DllImport("winmm.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);

        [DllImport("winmm.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);

        [DllImport("winmm.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);

        [DllImport("winmm.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint waveOutReset(IntPtr device);

        [DllImport("winmm.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint waveOutClose(IntPtr device);
    }
}
