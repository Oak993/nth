using System.Runtime.InteropServices;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-11 (voice-SPEC V-2.2 items 2 and 4): the level and bands are computed where V-2.2 puts them, on the processing thread. The pipeline's Pump hands each batch it drains to the sink's
// block tap (IAudioCaptureSink.OnAudioBlock) before it posts the delivery to the main executor, so the analysis never waits for, or runs on, the UI thread.
public sealed class CapturePipelineTapTests
{
    private static readonly DeviceFormat Stereo48 = new(48000, 2, SampleEncoding.Float32);

    private static byte[] Floats(IEnumerable<float> values) => MemoryMarshal.AsBytes<float>(values.ToArray()).ToArray();

    private static byte[] Ramp(int frames, int channels, float start = 0.001f) =>
        Floats(Enumerable.Range(0, frames * channels).Select(i => start + i * 0.0001f));

    private static void OnThread(Action work)
    {
        var thread = new Thread(() => work());
        thread.Start();
        thread.Join();
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-2.2: the tap runs on the thread that pumps (the processing thread), with the device's format and the floats, before anything reaches the main executor
    public void The_block_tap_runs_on_the_pumping_thread_before_the_main_executors_delivery()
    {
        using var runtime = new TestRuntime();
        var sink = new TappedSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Ramp(100, 2, 0.001f), silent: false);
        pipeline.OnBuffer(Ramp(50, 2, 0.5f), silent: false);
        var pumping = 0;
        OnThread(() =>
        {
            pumping = Environment.CurrentManagedThreadId;
            pipeline.Pump();
        });

        var block = Assert.Single(sink.Blocks);
        Assert.Equal(pumping, block.Thread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, block.Thread);
        Assert.Equal(new CaptureFormat(48000, 2), block.Format);
        Assert.Equal(Enumerable.Range(0, 200).Select(i => 0.001f + i * 0.0001f).Concat(Enumerable.Range(0, 100).Select(i => 0.5f + i * 0.0001f)), block.Samples);
        Assert.Empty(sink.Delivered); // the main executor has not run yet

        runtime.RunUntilIdle();
        Assert.Equal(300, sink.Delivered.Count);
        Assert.All(sink.DeliveryThreads, id => Assert.Equal(Environment.CurrentManagedThreadId, id));
        Assert.Single(sink.Blocks); // delivery does not tap again
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // each pump taps what it drained: two pumps are two blocks, in order, an empty pump none
    public void Each_pump_taps_what_it_drained_and_an_empty_pump_taps_nothing()
    {
        using var runtime = new TestRuntime();
        var sink = new TappedSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.Pump();
        Assert.Empty(sink.Blocks);
        pipeline.OnBuffer(Ramp(10, 2, 0.1f), silent: false);
        pipeline.Pump();
        pipeline.OnBuffer(Ramp(20, 2, 0.2f), silent: false);
        pipeline.Pump();
        pipeline.Pump();
        Assert.Equal([20, 40], sink.Blocks.Select(b => b.Samples.Length));
        runtime.RunUntilIdle();
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // a silent-flagged buffer is tapped as zeros too: the meter reads the same samples the session gets
    public void A_silent_buffer_is_tapped_as_zeros()
    {
        using var runtime = new TestRuntime();
        var sink = new TappedSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Ramp(10, 2), silent: true);
        pipeline.Pump();
        Assert.All(Assert.Single(sink.Blocks).Samples, v => Assert.Equal(0f, v));
        runtime.RunUntilIdle();
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // Flush (the last audio when capture stops) also taps what it drains, so the reading ends with the audio
    public void Flush_taps_what_it_delivers()
    {
        using var runtime = new TestRuntime();
        var sink = new TappedSink();
        using var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.OnBuffer(Ramp(30, 2), silent: false);
        pipeline.Flush();
        Assert.Equal(60, Assert.Single(sink.Blocks).Samples.Length);
        Assert.Equal(60, sink.Delivered.Count);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // nothing is tapped once the pipeline is disposed
    public void After_Dispose_nothing_is_tapped()
    {
        using var runtime = new TestRuntime();
        var sink = new TappedSink();
        var pipeline = new CapturePipeline(Stereo48, sink, runThread: false);
        pipeline.Dispose();
        pipeline.OnBuffer(Ramp(100, 2), silent: false);
        pipeline.Pump();
        Assert.Empty(sink.Blocks);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // the running processing thread taps on its own thread, never on the capture thread or the main thread
    public void The_processing_thread_taps_on_its_own_thread()
    {
        using var runtime = new TestRuntime();
        var sink = new TappedSink();
        var pipeline = new CapturePipeline(Stereo48, sink);
        var capture = 0;
        OnThread(() =>
        {
            capture = Environment.CurrentManagedThreadId;
            for (var i = 0; i < 20; i++)
            {
                pipeline.OnBuffer(Ramp(480, 2), silent: false);
                Thread.Sleep(2);
            }
        });
        runtime.Loop.RunUntil(() => sink.Delivered.Count >= 20 * 960, TimeSpan.FromSeconds(20));
        pipeline.Dispose();
        Assert.NotEmpty(sink.Blocks);
        Assert.Equal(20 * 960, sink.Blocks.Sum(b => b.Samples.Length));
        Assert.All(sink.Blocks, b =>
        {
            Assert.NotEqual(Environment.CurrentManagedThreadId, b.Thread);
            Assert.NotEqual(capture, b.Thread);
        });
    }
}
