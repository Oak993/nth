namespace Marrow.App.Tests.Fixtures;

// A deterministic failure after the controller binds, before duties finish attaching.
internal sealed class FailingStartupClock(Action fail) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() { fail(); throw new IOException("fake duty startup"); }
}
