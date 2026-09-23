using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using Marrow.Testing;

namespace Marrow.Core.Tests.Fixtures;

// Support for StackSamplerTests and HangWatchdogTests (ED-162, ED-163). Helper types live in fixture files, never
// inside a test class (R-TEST-3). The nettrace writer produces the layout the runtime wrote in the probe
// (probe-tmp/probe-final.log: "Nettrace", "!FastSerialization.1", a version 4 "Trace" object, then MetadataBlock,
// StackBlock, EventBlock and SPBlock objects with compressed event headers, then the NullReference end tag).

/// <summary>The live self-capture samples every thread of the process, so it runs alone.</summary>
[CollectionDefinition("Stack capture", DisableParallelization = true)]
public sealed class StackCaptureCollection;

/// <summary>Builds a synthetic nettrace stream, one object at a time.</summary>
public sealed class NetTraceWriter
{
    private readonly MemoryStream bytes = new();

    /// <summary>The offset the next byte will be written at.</summary>
    public long Length => bytes.Length;

    public byte[] ToArray() => bytes.ToArray();

    /// <summary>The stream magic, the serialization header and a version 4 Trace object with 8-byte pointers.</summary>
    public NetTraceWriter Header()
    {
        bytes.Write("Nettrace"u8);
        Int32(20);
        bytes.Write("!FastSerialization.1"u8);
        TypeHeader("Trace", 4);
        var trace = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(trace.AsSpan(32), 8);
        bytes.Write(trace);
        bytes.WriteByte(6);
        return this;
    }

    public NetTraceWriter Metadata(params (int Id, string Provider, int EventId)[] entries)
    {
        var block = BlockHeader();
        foreach (var (id, provider, eventId) in entries)
        {
            var payload = new MemoryStream();
            payload.Write(Le32(id));
            payload.Write(Utf16z(provider));
            payload.Write(Le32(eventId));
            payload.Write(Utf16z(""));
            payload.Write(new byte[8 + 4 + 4]);
            block.WriteByte(0x80);
            VarUInt(block, 0);
            VarUInt(block, (ulong)payload.Length);
            block.Write(payload.ToArray());
        }
        return Block("MetadataBlock", block.ToArray());
    }

    public NetTraceWriter Stacks(int firstId, params ulong[][] stacks)
    {
        var block = new MemoryStream();
        block.Write(Le32(firstId));
        block.Write(Le32(stacks.Length));
        foreach (var stack in stacks)
        {
            block.Write(Le32(stack.Length * 8));
            foreach (var ip in stack)
            {
                var frame = new byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(frame, ip);
                block.Write(frame);
            }
        }
        return Block("StackBlock", block.ToArray());
    }

    public NetTraceWriter Events(params (int MetadataId, ulong ThreadId, int StackId, byte[] Payload)[] events)
    {
        var block = BlockHeader();
        foreach (var (metadataId, threadId, stackId, payload) in events)
        {
            block.WriteByte(0x01 | 0x04 | 0x08 | 0x80);
            VarUInt(block, (ulong)metadataId);
            VarUInt(block, threadId);
            VarUInt(block, (ulong)stackId);
            VarUInt(block, 0);
            VarUInt(block, (ulong)payload.Length);
            block.Write(payload);
        }
        return Block("EventBlock", block.ToArray());
    }

    /// <summary>The NullReference tag that ends the stream.</summary>
    public NetTraceWriter End()
    {
        bytes.WriteByte(1);
        return this;
    }

    public NetTraceWriter Raw(params byte[] raw)
    {
        bytes.Write(raw);
        return this;
    }

    /// <summary>A SampleProfiler ThreadSample payload: 1 is External (native code), 2 is Managed.</summary>
    public static byte[] SamplePayload(uint type) => Le32(checked((int)type));

    /// <summary>A MethodLoadVerbose / MethodDCEndVerbose payload.</summary>
    public static byte[] MethodPayload(ulong start, uint size, string methodNamespace, string name, string signature)
    {
        var payload = new MemoryStream();
        payload.Write(new byte[16]);
        var address = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(address, start);
        payload.Write(address);
        payload.Write(Le32(checked((int)size)));
        payload.Write(new byte[8]);
        payload.Write(Utf16z(methodNamespace));
        payload.Write(Utf16z(name));
        payload.Write(Utf16z(signature));
        payload.Write(new byte[2]);
        return payload.ToArray();
    }

    private static MemoryStream BlockHeader()
    {
        var block = new MemoryStream();
        var header = new byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 20);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 1);
        block.Write(header);
        return block;
    }

    private NetTraceWriter Block(string name, byte[] content)
    {
        TypeHeader(name, 2);
        Int32(content.Length);
        while (bytes.Length % 4 != 0) bytes.WriteByte(0);
        bytes.Write(content);
        bytes.WriteByte(6);
        return this;
    }

    private void TypeHeader(string name, int version)
    {
        bytes.WriteByte(5);
        bytes.WriteByte(5);
        bytes.WriteByte(1);
        Int32(version);
        Int32(version);
        Int32(name.Length);
        bytes.Write(Encoding.ASCII.GetBytes(name));
        bytes.WriteByte(6);
    }

    private void Int32(int value) => bytes.Write(Le32(value));

    private static byte[] Le32(int value)
    {
        var raw = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(raw, value);
        return raw;
    }

    private static byte[] Utf16z(string text) => Encoding.Unicode.GetBytes(text + "\0");

    private static void VarUInt(Stream stream, ulong value)
    {
        do
        {
            var low = (byte)(value & 0x7F);
            value >>= 7;
            stream.WriteByte(value == 0 ? low : (byte)(low | 0x80));
        } while (value != 0);
    }
}

/// <summary>
/// A stand-in for the runtime's diagnostics server on a pipe of its own: answers CollectTracing2 on the first
/// connection, streams the given trace bytes, answers StopTracing on the second connection, then streams the rest
/// and closes the first connection when told to. Each write waits until the client has read it.
/// </summary>
public sealed class ScriptedDiagnosticsServer : IDisposable
{
    public const ulong SessionId = 0x1122334455667788;

    private readonly NamedPipeServerStream collect;
    private NamedPipeServerStream? stop;
    private readonly Task serving;

    public ScriptedDiagnosticsServer(uint? collectError = null, byte[]? beforeStop = null, uint? stopError = null,
        byte[]? afterStop = null, bool closeAfterStop = true)
    {
        collect = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        serving = Task.Run(() => Serve(collectError, beforeStop ?? [], stopError, afterStop ?? [], closeAfterStop));
    }

    public string PipeName { get; } = "marrow-test-diagnostics-" + Guid.NewGuid().ToString("N");

    public byte[]? CollectMessage { get; private set; }

    public byte[]? StopMessage { get; private set; }

    /// <summary>Set once the client has read the CollectTracing2 reply.</summary>
    public ManualResetEventSlim ReplyRead { get; } = new();

    /// <summary>Set once StopTracing has arrived.</summary>
    public ManualResetEventSlim StopArrived { get; } = new();

    public Exception? Failure { get; private set; }

    private void Serve(uint? collectError, byte[] beforeStop, uint? stopError, byte[] afterStop, bool closeAfterStop)
    {
        try
        {
            collect.WaitForConnection();
            CollectMessage = ReadMessage(collect);
            Write(collect, Reply(collectError));
            ReplyRead.Set();
            if (collectError is not null) return;
            Write(collect, beforeStop);
            stop = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            stop.WaitForConnection();
            StopMessage = ReadMessage(stop);
            StopArrived.Set();
            Write(stop, Reply(stopError));
            if (stopError is not null) return;
            Write(collect, afterStop);
            if (closeAfterStop) collect.Disconnect();
        }
        catch (IOException error)
        {
            Failure = error;
        }
        catch (ObjectDisposedException error)
        {
            Failure = error;
        }
    }

    private static void Write(NamedPipeServerStream pipe, byte[] data)
    {
        if (data.Length == 0) return;
        pipe.Write(data);
        pipe.Flush();
        pipe.WaitForPipeDrain();
    }

    private static byte[] ReadMessage(Stream pipe)
    {
        var header = new byte[20];
        pipe.ReadExactly(header);
        var message = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14))];
        header.CopyTo(message, 0);
        pipe.ReadExactly(message.AsSpan(20));
        return message;
    }

    /// <summary>A server reply: command set 0xFF, 0x00 with the session id, or 0xFF with an HRESULT.</summary>
    private static byte[] Reply(uint? error)
    {
        var payload = new byte[error is null ? 8 : 4];
        if (error is { } code) BinaryPrimitives.WriteUInt32LittleEndian(payload, code);
        else BinaryPrimitives.WriteUInt64LittleEndian(payload, SessionId);
        var reply = new byte[20 + payload.Length];
        Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0").CopyTo(reply, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(14), (ushort)reply.Length);
        reply[16] = 0xFF;
        reply[17] = error is null ? (byte)0x00 : (byte)0xFF;
        payload.CopyTo(reply, 20);
        return reply;
    }

    public void Dispose()
    {
        collect.Dispose();
        stop?.Dispose();
        serving.Wait(TimeSpan.FromSeconds(10));
        ReplyRead.Dispose();
        StopArrived.Dispose();
    }
}

/// <summary>Waits on real time for another thread to reach a state, and moves the manual clock meanwhile.</summary>
public static class Poll
{
    public static void Until(Func<bool> condition)
    {
        if (!SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(20)))
            throw new TimeoutException("the other thread did not reach the expected state within 20 s");
    }

    /// <summary>Advances <paramref name="clock"/> 100 ms at a time, pausing on real time, until the condition holds.</summary>
    public static void AdvanceUntil(ManualTimeProvider clock, Func<bool> condition)
    {
        for (var step = 0; step < 400; step++)
        {
            if (condition()) return;
            clock.Advance(TimeSpan.FromMilliseconds(100));
            Thread.Sleep(25);
        }
        throw new TimeoutException("the other thread did not reach the expected state");
    }
}
