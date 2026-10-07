namespace Marrow.UI.Tests.Fixtures;

/// <summary>A local clock a test sets by hand, to the hour (local time is UTC).</summary>
internal sealed class SettableWallpaperClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
