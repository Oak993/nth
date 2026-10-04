using System.Runtime.InteropServices;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (voice-SPEC V-2.2, V-4.2, V-4.4, V-T40): the capture thread's callback fills the ring and allocates nothing; the processing step moves the ring to the main executor
// in batches, in order, with the device events behind the audio they follow. Deterministic tests run the pipeline without its thread and call Pump themselves.
public sealed class CapturePipelineTests
{
    private static readonly DeviceFormat Stereo48 = new(48000, 2, SampleEncoding.Float32);

    private static byte[] Floats(IEnumerable<float> values) => MemoryMarshal.AsBytes<float>(values.ToArray()).ToArray();

    private static byte[] Ramp(int frames, int channels, float start = 0.001f) =>
        Floats(Enumerable.Range(0, frames * channels).Select(i => start + i * 0.0001f));

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-2.2 item 1: "No allocation, no lock, nothing else"
    public void The_capture_callback_allocates_nothing_for_every_encoding()
    {
        foreach (var encoding in Enum.GetValues<SampleEncoding>())
        {
            var format = new DeviceFormat(48000, 2, encoding);
            using var runtime = new TestRuntime();
            using var pipeline = new CapturePipeline(format, new CollectingSink(), runThread: false);
            var buffer = new byte[480 * format.BytesPerFrame]; // 10 ms
            for (var i = 0; i < buffer.Length; i++) buffer[i] = (byte)((i * 7 + 3) & 0xFF);
            // The runtime's own tiered-JIT bookkeeping allocates on the calling thread while a method is being promoted; the steady state is what V-2.2 asks for, so the
            // first rounds settle it and the last three must each allocate nothing (a callback that allocated per call would show in every round).
            var rounds = new List<long>();
            for (var round = 0; round < 6; round++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 100; i++) pipeline.OnBuffer(buffer, silent: i % 10 == 9);
                rounds.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                pipeline.Pump(); // empties the ring between rounds (100 buffers of 10 ms are 1 s of its 2 s)
            }
            Assert.True(rounds.Skip(3).All(bytes => bytes == 0), $"{encoding}: the capture callback allocated {string.Join(", ", rounds)} B per 100 calls");
        }
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.2: convert to float; V-2.3: the sink gets interleaved floats in the device's format, on the main loop, in order
    public void Pump_hands_the_buffers_to_the_sink_on_the_main_loop_in_order_as_one_batch()
    {
        using var runtime = new TestRuntime();
        var sink = new CollectingSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Ramp(100, 2, 0.001f), silent: false);
        pipeline.OnBuffer(Ramp(50, 2, 0.5f), silent: false);
        Assert.Empty(sink.Samples); // nothing reaches the sink off the main loop
        pipeline.Pump();
        Assert.Empty(sink.Samples);
        runtime.RunUntilIdle();
        Assert.Equal(["audio:300"], sink.Journal);
        Assert.Equal(Enumerable.Range(0, 200).Select(i => 0.001f + i * 0.0001f).Concat(Enumerable.Range(0, 100).Select(i => 0.5f + i * 0.0001f)), sink.Samples);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // a silent-flagged buffer is delivered as zeros, same length
    public void A_buffer_flagged_silent_is_delivered_as_zeros_of_the_same_length()
    {
        using var runtime = new TestRuntime();
        var sink = new CollectingSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Ramp(10, 2), silent: true);
        pipeline.OnBuffer(Ramp(10, 2), silent: false);
        pipeline.Pump();
        runtime.RunUntilIdle();
        Assert.Equal(40, sink.Samples.Count);
        Assert.All(sink.Samples.Take(20), v => Assert.Equal(0f, v));
        Assert.All(sink.Samples.Skip(20), v => Assert.NotEqual(0f, v));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.4: the final pass runs on what was captured, so a device event comes after the audio before it
    public void A_device_event_is_delivered_after_the_audio_that_preceded_it()
    {
        using var runtime = new TestRuntime();
        var sink = new CollectingSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Ramp(100, 2), silent: false);
        pipeline.RaiseEvent(CaptureDeviceEventKind.DeviceRemoved);
        pipeline.OnBuffer(Ramp(100, 2), silent: false);
        pipeline.Pump();
        runtime.RunUntilIdle();
        Assert.Equal(["audio:200", "event:DeviceRemoved", "audio:200"], sink.Journal);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-7.6: the last audio is flushed when capture stops; the sink is still listening then, so the flush is synchronous
    public void Flush_delivers_everything_now_and_what_was_posted_later_adds_nothing()
    {
        using var runtime = new TestRuntime();
        var sink = new CollectingSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Ramp(100, 2), silent: false);
        pipeline.Pump(); // posted, not yet run
        pipeline.OnBuffer(Ramp(30, 2), silent: false); // still in the ring
        pipeline.RaiseEvent(CaptureDeviceEventKind.DefaultDeviceChanged);
        pipeline.Flush();
        Assert.Equal(["audio:200", "audio:60", "event:DefaultDeviceChanged"], sink.Journal);
        runtime.RunUntilIdle();
        Assert.Equal(["audio:200", "audio:60", "event:DefaultDeviceChanged"], sink.Journal);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-2.2: nothing is delivered once the pipeline is disposed; the capture callback is inert
    public void After_Dispose_nothing_reaches_the_sink()
    {
        using var runtime = new TestRuntime();
        var sink = new CollectingSink();
        var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Ramp(100, 2), silent: false);
        pipeline.Pump();
        pipeline.Dispose();
        pipeline.OnBuffer(Ramp(100, 2), silent: false);
        pipeline.RaiseEvent(CaptureDeviceEventKind.DeviceRemoved);
        pipeline.Pump();
        runtime.RunUntilIdle();
        Assert.Empty(sink.Journal);
        pipeline.Dispose(); // twice is fine
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-2.2: the ring holds 2 s; beyond that whole buffers are dropped and counted, frames never split
    public void An_overflowing_ring_drops_whole_buffers_and_counts_them()
    {
        using var runtime = new TestRuntime();
        var sink = new CollectingSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        var oneSecond = Ramp(48000, 2);
        pipeline.OnBuffer(oneSecond, silent: false);
        pipeline.OnBuffer(oneSecond, silent: false); // 2 s: full
        Assert.Equal(0, pipeline.DroppedSamples);
        pipeline.OnBuffer(Ramp(480, 2), silent: false);
        Assert.Equal(960, pipeline.DroppedSamples);
        pipeline.Flush();
        Assert.Equal(2 * 48000 * 2, sink.Samples.Count);
        Assert.Equal(0, sink.Samples.Count % 2);
        pipeline.OnBuffer(Ramp(480, 2), silent: false); // room again
        Assert.Equal(960, pipeline.DroppedSamples);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.2: a trailing partial frame is not delivered
    public void A_trailing_partial_frame_is_not_delivered()
    {
        using var runtime = new TestRuntime();
        var sink = new CollectingSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Floats([0.1f, 0.2f, 0.3f]), silent: false); // one stereo frame and half of another
        pipeline.Flush();
        Assert.Equal([0.1f, 0.2f], sink.Samples);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-2.2 item 2: one dedicated processing thread drains the ring on its own, every 10 ms
    public void The_processing_thread_delivers_what_a_capture_thread_writes_in_order_and_stops_on_Dispose()
    {
        using var runtime = new TestRuntime();
        var sink = new CollectingSink();
        var pipeline = new CapturePipeline(Stereo48, sink);
        Assert.True(pipeline.ThreadRunning);
        var capture = new Thread(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                pipeline.OnBuffer(Floats(Enumerable.Range(0, 960).Select(s => i * 960f + s)), silent: false); // 10 ms stereo at 48k, counting samples
                Thread.Sleep(2);
            }
        });
        capture.Start();
        runtime.Loop.RunUntil(() => sink.Samples.Count >= 50 * 960, TimeSpan.FromSeconds(20));
        capture.Join();
        Assert.Equal(Enumerable.Range(0, 50 * 960).Select(i => (float)i), sink.Samples);
        pipeline.Dispose();
        Assert.False(pipeline.ThreadRunning);
    }
}
