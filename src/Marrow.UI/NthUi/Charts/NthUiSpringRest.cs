// PORT-ONLY: web surfaces W16b (DESIGN.md 2.6 "no snaps"; ED-NEW-web-w16b-1). No Swift counterpart. A critically damped spring
// only reaches its target in the limit; the page's rest test (within 0.0005 and slower than 0.001 a second) calls it settled
// while the value is still a hair off. A chart reads springs through Rest, which is exactly the target once settled, so a chart
// at rest is drawn at its end state to the pixel (as NthUiSlot does for heights) and its axis ends on 10, not 9.99991.
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Charts;

internal static class NthUiSpringRest
{
    /// <summary>The spring's value at <paramref name="now"/>, exactly its target once it has settled.</summary>
    public static double Rest(this NthSpring spring, double now) => spring.Settled(now) ? spring.Target : spring.Value(now);
}
