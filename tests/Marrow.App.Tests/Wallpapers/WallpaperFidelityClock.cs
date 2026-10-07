namespace Marrow.App.Tests.Wallpapers;

// A fixed local time source for WL-3's public WallpaperEnvironment; no system-clock read.
public sealed class WallpaperFidelityClock(double hours) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 5, (int)hours, (int)((hours % 1) * 60), 0, TimeSpan.Zero);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
