// Source: DroppyCode/Core/Support/HangWatchdog.swift (B2 1fbf81e), :95-112.
// PORT-ONLY: ED-162. /usr/bin/sample has no Windows counterpart, so the runtime's own diagnostics IPC (EventPipe) on
// this process captures every thread that has run managed code, from the hang-watchdog thread.
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY (ED-162). Self-capture of every managed thread's stack through EventPipe: CollectTracing2 on the
/// process's own diagnostics pipe, a continuous drain, a window on <see cref="SwiftClock"/>, StopTracing on a second
/// connection, then a nettrace parse. No DbgHelp, dump, helper executable or package. No exception leaves
/// <see cref="Capture(TimeSpan, uint?)"/>. Evidence that EventPipe samples in-process on .NET 10.0.12: the unit's
/// probe (nettrace Trace object version 4, 10 sampled threads, every frame resolved, a second capture after an
/// abandoned session succeeded).
/// </summary>
internal static class StackSampler
{
    // ED-162: the two failure bodies keep ED-003 (1)'s two cases and positions (HangWatchdog.swift:108, :111).
    internal const string CouldNotRunPrefix = "stack capture could not run: ";
    internal const string EndedEarlyPrefix = "stack capture ended early: ";
    internal const string NoEndOfTrace = "no end of trace within 10 s";
    internal const string NativeFrames = "[Native Frames]";

    internal const string SampleProfilerProvider = "Microsoft-DotNETCore-SampleProfiler";
    internal const string RuntimeProvider = "Microsoft-Windows-DotNETRuntime";
    internal const string RundownProvider = "Microsoft-Windows-DotNETRuntimeRundown";
    private const int ThreadSampleEventId = 0;
    private const int MethodLoadVerboseEventId = 143;
    private const int MethodDCEndVerboseEventId = 144;
    // ThreadSample's payload: 1 is External (the thread is in native code), 2 is Managed.
    private const uint ExternalSampleType = 1;

    // ED-162 bounds, each a CancellationTokenSource on SwiftClock's provider.
    private static readonly TimeSpan ConnectBound = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReplyBound = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopBound = TimeSpan.FromSeconds(10);

    private const uint CircularBufferMB = 256;
    private const uint NetTraceFormat = 1;
    private const byte EventPipeCommandSet = 0x02;
    private const byte StopTracingCommand = 0x01;
    private const byte CollectTracing2Command = 0x03;
    private const byte ServerCommandSet = 0xFF;
    private const byte ServerOk = 0x00;
    private const byte ServerError = 0xFF;
    private const int IpcHeaderSize = 20;
    private static readonly byte[] IpcMagic = Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0");

    // ED-162: [DllImport] as IcuNative.cs; LibraryImport would need AllowUnsafeBlocks, a project change.
    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetCurrentThreadIdNative();

    /// <summary>The calling thread's OS id, the id EventPipe reports for a sampled thread.</summary>
    internal static uint CurrentOsThreadId() => GetCurrentThreadIdNative();

    /// <summary>Captures this process for <paramref name="window"/> and returns the report body.</summary>
    internal static string Capture(TimeSpan window, uint? mainThreadOsId) =>
        Capture("dotnet-diagnostic-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture), window, mainThreadOsId);

    /// <summary>The same capture against a named pipe; tests pass a pipe of their own or one that cannot exist.</summary>
    internal static string Capture(string pipeName, TimeSpan window, uint? mainThreadOsId)
    {
        try
        {
            return CaptureUnguarded(pipeName, window, mainThreadOsId);
        }
        catch (Exception error)
        {
            // ED-162 (ED-078): nothing leaves the capturer; a failure this late still yields a body.
            return CouldNotRunPrefix + error.Message + "\n";
        }
    }

    private static string CaptureUnguarded(string pipeName, TimeSpan window, uint? mainThreadOsId)
    {
        var clock = SwiftClock.Provider.Current;
        NamedPipeClientStream? collect = null;
        ulong sessionId;
        try
        {
            collect = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using (var bound = new CancellationTokenSource(ConnectBound, clock))
                collect.ConnectAsync(bound.Token).GetAwaiter().GetResult();
            using (var bound = new CancellationTokenSource(ReplyBound, clock))
            {
                collect.WriteAsync(CollectTracing2Message(), bound.Token).AsTask().GetAwaiter().GetResult();
                var reply = ReadReply(collect, bound.Token);
                if (reply.Error is { } code)
                {
                    collect.Dispose();
                    return CouldNotRunPrefix + RuntimeError(code) + "\n";
                }
                sessionId = reply.Value;
            }
        }
        catch (Exception error)
        {
            // Never started: pipe missing, connect or reply timeout, or any exception before a session id.
            collect?.Dispose();
            return CouldNotRunPrefix + error.Message + "\n";
        }

        // The runtime's session disable waits for its streaming thread, so the stream is drained from now until its
        // end; an undrained pipe would deadlock StopTracing.
        var buffer = new TraceBuffer();
        using var abandon = new CancellationTokenSource();
        var drain = Drain(collect, buffer, abandon.Token);
        NamedPipeClientStream? stop = null;
        string? failure = null;
        var abandoned = false;
        try
        {
            Wait(window, clock);
            using var bound = new CancellationTokenSource(StopBound, clock);
            try
            {
                stop = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                stop.ConnectAsync(bound.Token).GetAwaiter().GetResult();
                stop.WriteAsync(StopTracingMessage(sessionId), bound.Token).AsTask().GetAwaiter().GetResult();
                var reply = ReadReply(stop, bound.Token);
                if (reply.Error is { } code) failure = RuntimeError(code);
                else drain.WaitAsync(bound.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (bound.IsCancellationRequested)
            {
                failure = NoEndOfTrace;
            }
        }
        catch (Exception)
        {
            // ENGINEERING DECISION: any other failure after the session started abandons it, and the trace received so
            // far decides the body: complete gives the stacks, truncated gives "malformed trace at byte <n>".
            abandoned = true;
        }
        finally
        {
            stop?.Dispose();
            if (failure is not null || abandoned)
            {
                // ED-162: on a bound both pipes are disposed, which abandons the session. The drain then ends, and the
                // wait makes every byte it already read part of the stacks parsed so far.
                abandon.Cancel();
                collect.Dispose();
            }
            ((IAsyncResult)drain).AsyncWaitHandle.WaitOne();
            collect.Dispose();
        }

        var trace = NetTrace.Parse(buffer.Snapshot());
        if (failure is null && !trace.Ended)
            failure = "malformed trace at byte " + trace.StoppedAt.ToString(CultureInfo.InvariantCulture);
        var stacks = Format(trace.Samples, trace.Methods, mainThreadOsId);
        return failure is null ? stacks : EndedEarlyPrefix + failure + "\n\n" + stacks;
    }

    // A read error ends the drain as end of stream does; the parse then tells a complete trace from a truncated one.
    private static async Task Drain(Stream pipe, TraceBuffer buffer, CancellationToken token)
    {
        var chunk = new byte[81920];
        try
        {
            while (true)
            {
                var count = await pipe.ReadAsync(chunk, token);
                if (count == 0) return;
                buffer.Append(chunk, count);
            }
        }
        catch (Exception)
        {
            // Ends the drain; see above.
        }
    }

    // ED-068: the window is a SwiftClock timer, so a test drives it with the manual clock.
    private static void Wait(TimeSpan delay, TimeProvider clock)
    {
        using var elapsed = new ManualResetEventSlim();
        using (clock.CreateTimer(Set, elapsed, delay, Timeout.InfiniteTimeSpan))
            elapsed.Wait();
    }

    private static void Set(object? state) => ((ManualResetEventSlim)state!).Set();

    private static string RuntimeError(uint code) => "runtime error 0x" + code.ToString("X8", CultureInfo.InvariantCulture);

    private readonly record struct Reply(ulong Value, uint? Error);

    // A diagnostics reply: the 20-byte header (magic, size, command set 0xFF, 0x00 OK or 0xFF error), then the
    // payload, a 64-bit session id or a 32-bit HRESULT. Only these bytes are read; the trace follows on the stream.
    private static Reply ReadReply(Stream pipe, CancellationToken token)
    {
        var header = new byte[IpcHeaderSize];
        pipe.ReadExactlyAsync(header, token).AsTask().GetAwaiter().GetResult();
        var size = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14));
        if (!header.AsSpan(0, IpcMagic.Length).SequenceEqual(IpcMagic) || size < IpcHeaderSize || header[16] != ServerCommandSet)
            throw new InvalidDataException("The diagnostics reply is not a server response.");
        var payload = new byte[size - IpcHeaderSize];
        pipe.ReadExactlyAsync(payload, token).AsTask().GetAwaiter().GetResult();
        if (header[17] == ServerError && payload.Length >= 4)
            return new Reply(0, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        if (header[17] != ServerOk || payload.Length < 8)
            throw new InvalidDataException("The diagnostics reply is not a server response.");
        return new Reply(BinaryPrimitives.ReadUInt64LittleEndian(payload), null);
    }

    private static byte[] Message(byte commandId, byte[] payload)
    {
        var message = new byte[IpcHeaderSize + payload.Length];
        IpcMagic.CopyTo(message, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(14), checked((ushort)message.Length));
        message[16] = EventPipeCommandSet;
        message[17] = commandId;
        payload.CopyTo(message, IpcHeaderSize);
        return message;
    }

    private static byte[] StopTracingMessage(ulong sessionId)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, sessionId);
        return Message(StopTracingCommand, payload);
    }

    // ED-162: circularBufferMB 256, format NetTrace, requestRundown true; SampleProfiler at level 4, and the runtime
    // provider at level 5 with keywords 0x18 (Loader | Jit) for MethodLoadVerbose.
    private static byte[] CollectTracing2Message()
    {
        var payload = new List<byte>();
        void U32(uint value) => payload.AddRange(BitConverter.GetBytes(value));
        void U64(ulong value) => payload.AddRange(BitConverter.GetBytes(value));
        void Text(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                U32(0);
                return;
            }
            U32(checked((uint)(value.Length + 1)));
            payload.AddRange(Encoding.Unicode.GetBytes(value + "\0"));
        }
        U32(CircularBufferMB);
        U32(NetTraceFormat);
        payload.Add(1);
        U32(2);
        U64(0);
        U32(4);
        Text(SampleProfilerProvider);
        Text(null);
        U64(0x18);
        U32(5);
        Text(RuntimeProvider);
        Text(null);
        return Message(CollectTracing2Command, [.. payload]);
    }

    private sealed class TraceBuffer
    {
        private readonly Lock gate = new();
        private readonly MemoryStream bytes = new();

        internal void Append(byte[] chunk, int count)
        {
            lock (gate) bytes.Write(chunk, 0, count);
        }

        internal byte[] Snapshot()
        {
            lock (gate) return bytes.ToArray();
        }
    }

    /// <summary>One ThreadSample: the sampled thread's OS id, whether it was in native code, and its managed frames.</summary>
    internal readonly record struct Sample(ulong ThreadId, bool External, ulong[] Frames);

    /// <summary>
    /// ED-162's body layout: one block per sampled thread, main first then ascending OS id, headed "Thread &lt;osid&gt;"
    /// plus " (main)" plus ":"; its distinct stacks by descending sample count, ties first-seen, each "  &lt;count&gt;
    /// samples" then its frames innermost first at a four-space indent; a blank line between blocks. A stack is
    /// distinct by its printed frames (ENGINEERING DECISION: two return addresses in one method print the same).
    /// </summary>
    internal static string Format(IReadOnlyList<Sample> samples, MethodMap methods, uint? mainThreadOsId)
    {
        var threads = new Dictionary<ulong, Dictionary<string, (int Seen, int Count)>>();
        foreach (var sample in samples)
        {
            var frames = new StringBuilder();
            // An External sample is a thread in native code, above its managed frames (the probe's parked main thread).
            if (sample.External) frames.Append("    ").Append(NativeFrames).Append('\n');
            foreach (var ip in sample.Frames) frames.Append("    ").Append(FrameName(methods, ip)).Append('\n');
            if (!threads.TryGetValue(sample.ThreadId, out var stacks)) threads[sample.ThreadId] = stacks = new(StringComparer.Ordinal);
            var key = frames.ToString();
            stacks[key] = stacks.TryGetValue(key, out var known) ? (known.Seen, known.Count + 1) : (stacks.Count, 1);
        }
        var text = new StringBuilder();
        foreach (var id in threads.Keys.OrderBy(id => id != mainThreadOsId).ThenBy(id => id))
        {
            if (text.Length > 0) text.Append('\n');
            text.Append("Thread ").Append(id.ToString(CultureInfo.InvariantCulture));
            if (id == mainThreadOsId) text.Append(" (main)");
            text.Append(":\n");
            foreach (var (frames, (_, count)) in threads[id].OrderByDescending(stack => stack.Value.Count).ThenBy(stack => stack.Value.Seen))
                text.Append("  ").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" samples\n").Append(frames);
        }
        return text.ToString();
    }

    // ENGINEERING DECISION: an unresolved address prints in upper-case hex, as the runtime error's HRESULT does.
    private static string FrameName(MethodMap methods, ulong ip) =>
        methods.Resolve(ip) ?? "[unresolved 0x" + ip.ToString("X", CultureInfo.InvariantCulture) + "]";

    /// <summary>
    /// Code ranges named by MethodLoadVerbose (methods compiled during the window) and MethodDCEndVerbose (the rundown
    /// of every method at stop), as "Namespace.Type.Method(signature)": the event's MethodNamespace (the full type
    /// name), ".", MethodName, then MethodSignature in parentheses.
    /// </summary>
    internal sealed class MethodMap
    {
        private readonly List<(ulong Start, ulong End, string Name)> ranges = [];
        private bool sorted = true;

        internal void Add(ulong start, uint size, string name)
        {
            ranges.Add((start, unchecked(start + size), name));
            sorted = false;
        }

        internal string? Resolve(ulong ip)
        {
            if (!sorted)
            {
                ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
                sorted = true;
            }
            int low = 0, high = ranges.Count - 1, found = -1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                if (ranges[middle].Start <= ip)
                {
                    found = middle;
                    low = middle + 1;
                }
                else high = middle - 1;
            }
            return found >= 0 && ip < ranges[found].End ? ranges[found].Name : null;
        }
    }

    /// <summary>
    /// The nettrace stream the runtime writes (the probe: "Nettrace", "!FastSerialization.1", a version 4 Trace object,
    /// then MetadataBlock, StackBlock, EventBlock and SPBlock objects, then the NullReference tag that ends it). Event
    /// headers are compressed; a stack block may redefine ids after a sequence point, so a sample takes the stack its
    /// id names when the sample is read.
    /// </summary>
    internal sealed class NetTrace
    {
        private const byte NullReferenceTag = 1;
        private const byte BeginPrivateObjectTag = 5;
        private const byte EndObjectTag = 6;

        private readonly Dictionary<int, (string Provider, int EventId)> metadata = [];
        private readonly Dictionary<int, ulong[]> stacks = [];
        private int pointerSize = 8;

        internal List<Sample> Samples { get; } = [];
        internal MethodMap Methods { get; } = new();
        /// <summary>Whether the end tag was read.</summary>
        internal bool Ended { get; private set; }
        /// <summary>The byte where parsing stopped: the end, or the first byte that could not be read.</summary>
        internal long StoppedAt { get; private set; }

        private sealed class MalformedException(long at) : Exception
        {
            internal long At { get; } = at;
        }

        internal static NetTrace Parse(byte[] data)
        {
            var trace = new NetTrace();
            var position = 0L;
            try
            {
                trace.Read(data, ref position);
            }
            catch (MalformedException malformed)
            {
                trace.StoppedAt = malformed.At;
            }
            catch (Exception)
            {
                trace.StoppedAt = position;
            }
            return trace;
        }

        private static void Need(byte[] data, long position, long count)
        {
            if (count < 0 || position + count > data.Length) throw new MalformedException(position);
        }

        private static int Int32(byte[] data, ref long position)
        {
            Need(data, position, 4);
            var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)position));
            position += 4;
            return value;
        }

        private static void Expect(byte[] data, ref long position, byte tag)
        {
            Need(data, position, 1);
            if (data[position] != tag) throw new MalformedException(position);
            position++;
        }

        private void Read(byte[] data, ref long position)
        {
            Need(data, 0, 8);
            if (!data.AsSpan(0, 8).SequenceEqual("Nettrace"u8)) throw new MalformedException(0);
            position = 8;
            if (Int32(data, ref position) != 20) throw new MalformedException(8);
            Need(data, position, 20);
            if (!data.AsSpan((int)position, 20).SequenceEqual("!FastSerialization.1"u8)) throw new MalformedException(position);
            position += 20;
            while (true)
            {
                StoppedAt = position;
                Need(data, position, 1);
                if (data[position] == NullReferenceTag)
                {
                    position++;
                    Ended = true;
                    StoppedAt = position;
                    return;
                }
                Expect(data, ref position, BeginPrivateObjectTag);
                Expect(data, ref position, BeginPrivateObjectTag);
                Expect(data, ref position, NullReferenceTag);
                Int32(data, ref position);
                Int32(data, ref position);
                var nameAt = position;
                var nameLength = Int32(data, ref position);
                Need(data, position, nameLength);
                var name = Encoding.ASCII.GetString(data, (int)position, nameLength);
                position += nameLength;
                Expect(data, ref position, EndObjectTag);
                if (name == "Trace")
                {
                    Need(data, position, 48);
                    pointerSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)position + 32));
                    if (pointerSize is not (4 or 8)) throw new MalformedException(position + 32);
                    position += 48;
                }
                else if (name is "EventBlock" or "MetadataBlock" or "StackBlock" or "SPBlock")
                {
                    var size = Int32(data, ref position);
                    var padding = (4 - position % 4) % 4;
                    Need(data, position, padding + size);
                    var start = position + padding;
                    var end = start + size;
                    if (name == "EventBlock") ReadEvents(data, start, end, isMetadata: false);
                    else if (name == "MetadataBlock") ReadEvents(data, start, end, isMetadata: true);
                    else if (name == "StackBlock") ReadStacks(data, start, end);
                    position = end;
                }
                else throw new MalformedException(nameAt);
                Expect(data, ref position, EndObjectTag);
            }
        }

        private static ulong VarUInt(byte[] data, ref long position, long end)
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (position >= end) throw new MalformedException(position);
                var next = data[position++];
                value |= (ulong)(next & 0x7F) << shift;
                if ((next & 0x80) == 0) return value;
            }
            throw new MalformedException(position);
        }

        private static int VarInt(byte[] data, ref long position, long end)
        {
            var at = position;
            var value = VarUInt(data, ref position, end);
            return value <= int.MaxValue ? (int)value : throw new MalformedException(at);
        }

        // A block header (its size, then flags; bit 0 is header compression), then events whose compressed headers
        // carry only what changed since the previous event of the block.
        private void ReadEvents(byte[] data, long start, long end, bool isMetadata)
        {
            if (end - start < 4) throw new MalformedException(start);
            var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)start));
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)start + 2));
            if ((flags & 1) == 0 || headerSize < 4 || headerSize > end - start) throw new MalformedException(start);
            var position = start + headerSize;
            int metadataId = 0, stackId = 0, payloadSize = 0;
            ulong threadId = 0;
            while (position < end)
            {
                var eventAt = position;
                var header = data[position++];
                if ((header & 0x01) != 0) metadataId = VarInt(data, ref position, end);
                if ((header & 0x02) != 0)
                {
                    VarUInt(data, ref position, end);
                    VarUInt(data, ref position, end);
                    VarUInt(data, ref position, end);
                }
                if ((header & 0x04) != 0) threadId = VarUInt(data, ref position, end);
                if ((header & 0x08) != 0) stackId = VarInt(data, ref position, end);
                VarUInt(data, ref position, end);
                if ((header & 0x10) != 0) position += 16;
                if ((header & 0x20) != 0) position += 16;
                if ((header & 0x80) != 0) payloadSize = VarInt(data, ref position, end);
                if (position + payloadSize > end) throw new MalformedException(eventAt);
                var payload = data.AsSpan((int)position, payloadSize);
                position += payloadSize;
                if (isMetadata) ReadMetadata(payload, eventAt);
                else ReadEvent(metadataId, threadId, stackId, payload);
            }
        }

        private static string Utf16z(ReadOnlySpan<byte> payload, ref int position)
        {
            var start = Math.Min(position, payload.Length);
            var end = start;
            while (end + 1 < payload.Length && (payload[end] != 0 || payload[end + 1] != 0)) end += 2;
            position = end + 2;
            return Encoding.Unicode.GetString(payload[start..Math.Min(end, payload.Length)]);
        }

        // A metadata payload: the id it defines, the provider name, then the event id.
        private void ReadMetadata(ReadOnlySpan<byte> payload, long at)
        {
            if (payload.Length < 4) throw new MalformedException(at);
            var id = BinaryPrimitives.ReadInt32LittleEndian(payload);
            var position = 4;
            var provider = Utf16z(payload, ref position);
            if (position + 4 > payload.Length) throw new MalformedException(at);
            metadata[id] = (provider, BinaryPrimitives.ReadInt32LittleEndian(payload[position..]));
        }

        private void ReadEvent(int metadataId, ulong threadId, int stackId, ReadOnlySpan<byte> payload)
        {
            if (!metadata.TryGetValue(metadataId, out var kind)) return;
            if (kind is (SampleProfilerProvider, ThreadSampleEventId))
            {
                // The header's thread id is the sampled thread's OS id.
                var type = payload.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(payload) : 0;
                Samples.Add(new Sample(threadId, type == ExternalSampleType, stacks.GetValueOrDefault(stackId) ?? []));
            }
            else if (kind is (RuntimeProvider, MethodLoadVerboseEventId) or (RundownProvider, MethodDCEndVerboseEventId))
            {
                // MethodID, ModuleID, MethodStartAddress, MethodSize, MethodToken, MethodFlags, then three strings.
                if (payload.Length < 36) return;
                var start = BinaryPrimitives.ReadUInt64LittleEndian(payload[16..]);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(payload[24..]);
                var position = 36;
                var methodNamespace = Utf16z(payload, ref position);
                var methodName = Utf16z(payload, ref position);
                var signature = Utf16z(payload, ref position);
                Methods.Add(start, size, methodNamespace + "." + methodName + "(" + signature + ")");
            }
        }

        // A stack block: the first id, the count, then each stack's byte size and its return addresses.
        private void ReadStacks(byte[] data, long start, long end)
        {
            var position = start;
            var firstId = Int32(data, ref position);
            var count = Int32(data, ref position);
            for (var index = 0; index < count; index++)
            {
                var size = Int32(data, ref position);
                if (size < 0 || position + size > end || size % pointerSize != 0) throw new MalformedException(position);
                var frames = new ulong[size / pointerSize];
                for (var frame = 0; frame < frames.Length; frame++)
                    frames[frame] = pointerSize == 8
                        ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan((int)position + frame * 8))
                        : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)position + frame * 4));
                stacks[firstId + index] = frames;
                position += size;
            }
        }
    }
}
