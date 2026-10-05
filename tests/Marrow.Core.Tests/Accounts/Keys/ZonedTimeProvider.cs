using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts.Keys;

/// <summary>The manual clock with a chosen local time zone, for the calendar-month tests.</summary>
public sealed class ZonedTimeProvider(ManualTimeProvider inner, TimeZoneInfo zone) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => zone;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override long TimestampFrequency => inner.TimestampFrequency;
}
