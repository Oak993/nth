using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// ED-162: the port-only replacement for /usr/bin/sample (Core/Support/HangWatchdog.swift:95-112). These tests carry
// the decision and no Req trait (the ruling); the HangWatchdog tests prove the records through a fake capturer.
[Collection("Stack capture")]
public sealed class StackSamplerTests
{
    private const string RuntimeProvider = "Microsoft-Windows-DotNETRuntime";
    private const string RundownProvider = "Microsoft-Windows-DotNETRuntimeRundown";
    private const string SampleProfiler = "Microsoft-DotNETCore-SampleProfiler";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    [Fact]
    [Trait("Decision", "ED-162")]
    public void Body_lists_main_first_then_ascending_ids_and_stacks_by_count_with_ties_first_seen()
    {
        var methods = new StackSampler.MethodMap();
        methods.Add(0x1000, 0x100, "Ns.Type.A(void  (int32))");
        methods.Add(0x2000, 0x80, "Ns.Type.B(void  ())");
        methods.Add(0x3000, 0x10, "Ns.Main.Run(void  ())");
        StackSampler.Sample[] samples =
        [
            new(42, false, [0x1010, 0x2050]),
            new(7, false, [0xDEAD]),
            new(42, true, [0x1010]),
            new(100, false, [0x3004]),
            new(42, true, [0x1020]),
            new(42, false, [0x10FF, 0x2000]),
            new(42, false, [0x2001]),
        ];
        var body = StackSampler.Format(samples, methods, mainThreadOsId: 100);
        var expected =
            "Thread 100 (main):\n" +
            "  1 samples\n" +
            "    Ns.Main.Run(void  ())\n" +
            "\n" +
            "Thread 7:\n" +
            "  1 samples\n" +
            "    [unresolved 0xDEAD]\n" +
            "\n" +
            "Thread 42:\n" +
            "  2 samples\n" +
            "    Ns.Type.A(void  (int32))\n" +
            "    Ns.Type.B(void  ())\n" +
            "  2 samples\n" +
            "    [Native Frames]\n" +
            "    Ns.Type.A(void  (int32))\n" +
            "  1 samples\n" +
            "    Ns.Type.B(void  ())\n";
        Assert.Equal(expected, body);
    }

    [Fact]
    [Trait("Decision", "ED-162")]
    public void Body_without_a_known_main_thread_is_in_ascending_id_order_and_empty_without_samples()
    {
        var methods = new StackSampler.MethodMap();
        StackSampler.Sample[] samples = [new(9, true, []), new(3, false, [0x1ABC])];
        Assert.Equal("Thread 3:\n  1 samples\n    [unresolved 0x1ABC]\n\nThread 9:\n  1 samples\n    [Native Frames]\n",
            StackSampler.Format(samples, methods, mainThreadOsId: null));
        Assert.Equal("", StackSampler.Format([], methods, mainThreadOsId: 5));
    }

    [Fact]
    [Trait("Decision", "ED-162")]
    public void Capture_parses_the_streamed_trace_and_names_frames_from_load_and_rundown_ranges()
    {
        using var runtime = new TestRuntime();
        var trace = new NetTraceWriter().Header()
            .Metadata((1, SampleProfiler, 0), (2, RuntimeProvider, 143))
            .Stacks(1, [0x1010, 0x2050], [0x2004])
            .Events((2, 42, 0, NetTraceWriter.MethodPayload(0x1000, 0x100, "Ns.Type", "A", "void  (int32)")),
                (1, 42, 1, NetTraceWriter.SamplePayload(2)), (1, 100, 2, NetTraceWriter.SamplePayload(1)),
                (1, 42, 1, NetTraceWriter.SamplePayload(2)));
        // The rundown after StopTracing continues the same stream, so block padding counts from the stream start.
        var split = checked((int)trace.Length);
        var bytes = trace.Metadata((3, RundownProvider, 144))
            .Events((3, 0, 0, NetTraceWriter.MethodPayload(0x2000, 0x80, "Ns.Type", "B", "void  ()")))
            .End().ToArray();
        using var server = new ScriptedDiagnosticsServer(beforeStop: bytes[..split], afterStop: bytes[split..]);

        var body = CaptureAgainst(server, runtime, mainThreadOsId: 100);

        Assert.Equal(
            "Thread 100 (main):\n  1 samples\n    [Native Frames]\n    Ns.Type.B(void  ())\n\n" +
            "Thread 42:\n  2 samples\n    Ns.Type.A(void  (int32))\n    Ns.Type.B(void  ())\n", body);
        AssertCollectTracing2(server.CollectMessage!);
        Assert.Equal(Message(0x01, BitConverter.GetBytes(ScriptedDiagnosticsServer.SessionId)), server.StopMessage);
    }

    [Fact]
    [Trait("Decision", "ED-162")]
    public void A_runtime_error_before_the_session_starts_is_the_could_not_run_body()
    {
        using var runtime = new TestRuntime();
        using var server = new ScriptedDiagnosticsServer(collectError: 0x80131384);
        var body = StackSampler.Capture(server.PipeName, Window, 1);
        Assert.Equal(Encoding.UTF8.GetBytes("stack capture could not run: runtime error 0x80131384\n"), Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    [Trait("Decision", "ED-162")]
    public void A_missing_pipe_is_the_could_not_run_body_after_the_two_second_connect_bound()
    {
        using var runtime = new TestRuntime();
        var start = SwiftClock.Now;
        string? body = null;
        var capture = new Thread(() => body = StackSampler.Capture("marrow-test-no-such-pipe-" + Guid.NewGuid(), Window, 1));
        capture.Start();
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        runtime.Time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.False(capture.Join(TimeSpan.FromMilliseconds(200)));
        runtime.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(capture.Join(TimeSpan.FromSeconds(20)));
        Assert.Equal(TimeSpan.FromSeconds(2), SwiftClock.Now - start);
        Assert.Equal("stack capture could not run: " + new OperationCanceledException().Message + "\n", body);
    }

    [Fact]
    [Trait("Decision", "ED-162")]
    public void A_runtime_error_on_stop_is_the_ended_early_body_with_the_stacks_parsed_so_far()
    {
        using var runtime = new TestRuntime();
        var trace = OneSampleTrace().ToArray();
        using var server = new ScriptedDiagnosticsServer(beforeStop: trace, stopError: 0x80004005);
        var body = CaptureAgainst(server, runtime, mainThreadOsId: 5);
        Assert.Equal(Encoding.UTF8.GetBytes(
            "stack capture ended early: runtime error 0x80004005\n\nThread 5 (main):\n  1 samples\n    [unresolved 0xABC]\n"),
            Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    [Trait("Decision", "ED-162")]
    public void A_malformed_trace_is_the_ended_early_body_naming_the_byte_with_the_stacks_parsed_so_far()
    {
        using var runtime = new TestRuntime();
        var writer = OneSampleTrace();
        var bad = writer.Length;
        var trace = writer.Raw(0x09).ToArray();
        using var server = new ScriptedDiagnosticsServer(beforeStop: trace);
        var body = CaptureAgainst(server, runtime, mainThreadOsId: 5);
        Assert.Equal(Encoding.UTF8.GetBytes(
            "stack capture ended early: malformed trace at byte " + bad.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "\n\nThread 5 (main):\n  1 samples\n    [unresolved 0xABC]\n"),
            Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    [Trait("Decision", "ED-162")]
    public void A_trace_that_does_not_end_within_ten_seconds_of_stop_is_the_ended_early_body()
    {
        using var runtime = new TestRuntime();
        var trace = OneSampleTrace().ToArray();
        using var server = new ScriptedDiagnosticsServer(beforeStop: trace, closeAfterStop: false);
        string? body = null;
        var capture = new Thread(() => body = StackSampler.Capture(server.PipeName, Window, 5));
        capture.Start();
        Poll.Until(() => server.ReplyRead.IsSet);
        Poll.AdvanceUntil(runtime.Time, () => server.StopArrived.IsSet);
        Thread.Sleep(200);
        runtime.Time.Advance(TimeSpan.FromMilliseconds(9000));
        Assert.False(capture.Join(TimeSpan.FromMilliseconds(300)));
        runtime.Time.Advance(TimeSpan.FromMilliseconds(1100));
        Assert.True(capture.Join(TimeSpan.FromSeconds(20)));
        Assert.Equal(Encoding.UTF8.GetBytes(
            "stack capture ended early: no end of trace within 10 s\n\nThread 5 (main):\n  1 samples\n    [unresolved 0xABC]\n"),
            Encoding.UTF8.GetBytes(body!));
    }

    [Fact]
    [Trait("Decision", "ED-162")]
    public void Live_self_capture_names_the_parked_main_thread_and_returns_within_sixteen_seconds()
    {
        using var parked = new ManualResetEventSlim();
        using var ready = new ManualResetEventSlim();
        uint osId = 0;
        var main = new Thread(() =>
        {
            osId = StackSampler.CurrentOsThreadId();
            ready.Set();
            ParkUntil(parked);
        }) { IsBackground = true };
        main.Start();
        ready.Wait();
        try
        {
            var start = SwiftClock.Now;
            var body = StackSampler.Capture(Window, osId);
            var elapsed = SwiftClock.Now - start;
            Assert.True(elapsed <= TimeSpan.FromSeconds(16), $"capture took {elapsed}");
            Assert.True(elapsed >= Window, $"capture took {elapsed}");
            Assert.StartsWith("Thread " + osId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (main):\n  ", body, StringComparison.Ordinal);
            var mainBlock = body.Split("\n\n")[0];
            Assert.Contains("    Marrow.Core.Tests.Port.StackSamplerTests.ParkUntil(", mainBlock, StringComparison.Ordinal);
            Assert.DoesNotContain("stack capture", body, StringComparison.Ordinal);
        }
        finally
        {
            parked.Set();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ParkUntil(ManualResetEventSlim release)
    {
        while (!release.IsSet) Thread.Sleep(1);
    }

    private static NetTraceWriter OneSampleTrace() => new NetTraceWriter().Header()
        .Metadata((1, SampleProfiler, 0))
        .Stacks(1, [0xABC])
        .Events((1, 5, 1, NetTraceWriter.SamplePayload(2)));

    private static string CaptureAgainst(ScriptedDiagnosticsServer server, TestRuntime runtime, uint mainThreadOsId)
    {
        string? body = null;
        var capture = new Thread(() => body = StackSampler.Capture(server.PipeName, Window, mainThreadOsId));
        capture.Start();
        Poll.Until(() => server.ReplyRead.IsSet);
        Poll.AdvanceUntil(runtime.Time, () => !capture.IsAlive || server.StopArrived.IsSet);
        Assert.True(capture.Join(TimeSpan.FromSeconds(20)));
        return body!;
    }

    private static void AssertCollectTracing2(byte[] message)
    {
        var payload = new List<byte>();
        payload.AddRange(BitConverter.GetBytes(256u));
        payload.AddRange(BitConverter.GetBytes(1u));
        payload.Add(1);
        payload.AddRange(BitConverter.GetBytes(2u));
        payload.AddRange(BitConverter.GetBytes(0UL));
        payload.AddRange(BitConverter.GetBytes(4u));
        payload.AddRange(IpcString(SampleProfiler));
        payload.AddRange(BitConverter.GetBytes(0u));
        payload.AddRange(BitConverter.GetBytes(0x18UL));
        payload.AddRange(BitConverter.GetBytes(5u));
        payload.AddRange(IpcString(RuntimeProvider));
        payload.AddRange(BitConverter.GetBytes(0u));
        Assert.Equal(Message(0x03, [.. payload]), message);
    }

    private static byte[] IpcString(string text)
    {
        var raw = new List<byte>(BitConverter.GetBytes((uint)(text.Length + 1)));
        raw.AddRange(Encoding.Unicode.GetBytes(text + "\0"));
        return [.. raw];
    }

    private static byte[] Message(byte commandId, byte[] payload)
    {
        var message = new byte[20 + payload.Length];
        Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0").CopyTo(message, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(14), (ushort)message.Length);
        message[16] = 0x02;
        message[17] = commandId;
        payload.CopyTo(message, 20);
        return message;
    }
}
