// PORT-ONLY: SK-13. A clock that says one instant, or one that a test advances.
namespace Marrow.Services.Tests.Fixtures;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;

    public void Advance(TimeSpan by) => current += by;

    public override DateTimeOffset GetUtcNow() => current;
}
