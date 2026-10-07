// PORT-ONLY: ST-0 counts DWM's vblank numbers instead of rounded callback gaps (RISKS R-04).
namespace Marrow.UI.Port;

public sealed class DisplayRefreshCounter
{
    public static long VblankNumber(long timestamp, long period)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        return checked((long)Math.Floor(timestamp / (double)period + 0.5));
    }

    private long? originTimestamp;
    private long initialPeriod;

    public long ObserveVblank(long timestamp, long period)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        if (originTimestamp is null) { originTimestamp = timestamp; initialPeriod = period; }
        // A one-tick change in DWM's period estimate must not rescale many hours of uptime.
        return Observe(VblankNumber(timestamp - originTimestamp.Value, initialPeriod));
    }

    public long ObserveVblank(long timestamp, long period, long now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        // DWM can retain its last composition timestamp while the desktop has no new drawing.
        // Anchor the fallback to that physical vblank; do not sum rounded callback gaps.
        if (originTimestamp is null) { originTimestamp = timestamp; initialPeriod = period; }
        return ObserveVblank(now - timestamp >= initialPeriod ? now : timestamp, period);
    }
    private long? first;
    private long latest;

    public long Observe(long number)
    {
        first ??= number;
        latest = Math.Max(latest, number - first.Value);
        return latest;
    }
}
