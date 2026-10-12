namespace Marrow.App.Tests.Fixtures;

public sealed class C6FlushTimeProvider : TimeProvider
{
    private int reads;
    public Action? AfterFlushCaptured { get; set; }
    public override DateTimeOffset GetUtcNow()
    {
        if (Interlocked.Increment(ref reads) == 3) AfterFlushCaptured?.Invoke();
        return new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    }
}
