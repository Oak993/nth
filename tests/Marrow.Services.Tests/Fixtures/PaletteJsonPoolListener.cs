using System.Diagnostics.Tracing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: observe the real serializer's retained-buffer boundary, without reflection into the pool.
internal sealed class PaletteJsonPoolListener : EventListener
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    public int LargestRental { get; private set; }
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "System.Buffers.ArrayPoolEventSource") EnableEvents(source, EventLevel.Verbose);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (Environment.CurrentManagedThreadId == thread && e.EventName == "BufferRented"
            && e.Payload is { Count: > 1 } && e.Payload[1] is int size)
            LargestRental = Math.Max(LargestRental, size);
    }
}
