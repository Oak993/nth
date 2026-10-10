// PORT-ONLY: nth motion pack (OD-0136 amendment 2). What a site reads back from a primitive: the value as drawn now (a spring), for the sites whose own API says "the fraction drawn".
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port.Updates;

internal static class NthReadings
{
    /// <summary>The ring's arc as drawn now (0 to 1): its spring read at the clock the ring runs on.</summary>
    public static double Drawn(this NthRing ring)
    {
        var frame = ring.Snapshot();
        return frame.State is NthRingState state ? state.Value.Value(NthFrame.Seconds(frame.Clock)) : 0;
    }
}
