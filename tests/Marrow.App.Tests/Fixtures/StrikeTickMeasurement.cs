using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

// Samples cumulative counters at the real compositor's ticks. No drawing and no tick allocations.
public sealed class StrikeTickMeasurement : CompositionCustomVisualHandler
{
    public static readonly object Start = new();
    public readonly long[] Bytes = new long[18000];
    public readonly long[] Draws = new long[18000];
    public readonly long[] Stamps = new long[18000];
    public int Count;
    public int Thread;
    public volatile bool On;
    public volatile bool Active = true;
    public int InactiveTicks;
    public TrackEffect Effect = null!;

    public override void OnMessage(object message) => RegisterForNextAnimationFrameUpdate();

    public override void OnAnimationFrameUpdate()
    {
        if (On && Count < Bytes.Length)
        {
            if (!Active) InactiveTicks++;
            Thread = Environment.CurrentManagedThreadId;
            Bytes[Count] = GC.GetAllocatedBytesForCurrentThread();
            Draws[Count] = Effect.Draws;
            Stamps[Count] = System.Diagnostics.Stopwatch.GetTimestamp();
            Count++;
        }
        if (On) RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context) { }
}
