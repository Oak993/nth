namespace Marrow.App.Tests.Fixtures;

// Counts only main-loop tap clock reads; asynchronous pump reads cannot alter the witness.
public sealed class C6cTapTimeProvider(Func<bool> isTapContext) : TimeProvider
{
    private int reads;
    public int TapReads => Volatile.Read(ref reads);
    public override DateTimeOffset GetUtcNow()
    {
        if (isTapContext()) Interlocked.Increment(ref reads);
        return new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    }
}
