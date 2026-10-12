// PORT-ONLY: a fake for the Tactus pill's animator (ED-NEW-pcctl-24): records every key-frame start and every value set
// at once, per control, so tests read the poses the pill hands the compositor without a compositor.
using Avalonia;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Fixtures;

public sealed record PillAnimatorCall(string Kind, Visual Target, PillTrack? X = null, PillTrack? Y = null, double A = double.NaN, double B = double.NaN);

public sealed class RecordingPillAnimator : IPillAnimator
{
    private readonly List<PillAnimatorCall> calls = [];

    public IReadOnlyList<PillAnimatorCall> Calls => calls;

    public IEnumerable<PillAnimatorCall> On(Visual target) => calls.Where(c => c.Target == target);

    public PillAnimatorCall? Last(Visual target, string kind) => calls.LastOrDefault(c => c.Target == target && c.Kind == kind);

    public void Clear() => calls.Clear();

    public void Translate(Visual target, PillTrack x, PillTrack y) => calls.Add(new("Translate", target, x, y));

    public void Resize(Visual target, PillTrack width, PillTrack height) => calls.Add(new("Resize", target, width, height));

    public void Scale(Visual target, PillTrack scale) => calls.Add(new("Scale", target, scale));

    public void SetTranslation(Visual target, double x, double y) => calls.Add(new("SetTranslation", target, A: x, B: y));

    public void SetSize(Visual target, double width, double height) => calls.Add(new("SetSize", target, A: width, B: height));

    public void SetScale(Visual target, double scale) => calls.Add(new("SetScale", target, A: scale));

    public void SetCentre(Visual target, double x, double y) => calls.Add(new("SetCentre", target, A: x, B: y));

    public void Fade(Visual target, PillTrack opacity) => calls.Add(new("Fade", target, opacity));

    public void SetOpacity(Visual target, double opacity) => calls.Add(new("SetOpacity", target, A: opacity));

    public void Breathe(Visual target, double low, double seconds) => calls.Add(new("Breathe", target, A: low, B: seconds));

    public void StopBreathing(Visual target) => calls.Add(new("StopBreathing", target));
}
