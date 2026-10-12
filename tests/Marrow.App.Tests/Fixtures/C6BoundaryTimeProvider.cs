namespace Marrow.App.Tests.Fixtures;

// A scheduler pause between the two clock reads at a coalescing boundary.
public sealed class C6BoundaryTimeProvider(DateTimeOffset due) : TimeProvider
{
    private int reads;
    public override DateTimeOffset GetUtcNow() => Interlocked.Increment(ref reads) == 1
        ? due.AddTicks(-1)
        : due.AddMilliseconds(2);
}
