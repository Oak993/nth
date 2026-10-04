// PORT-ONLY CZ-1: one retargetable CSS scalar; all timestamps use SwiftClock (ED-237).
using Marrow.Core.Port;
namespace Marrow.UI.Port.Settings;

public sealed class CzNumber(CzMotion motion, double initial, Action<double>? changed = null)
{
    private ContinuousInstant start;
    private double from, delay;
    private Animation? curve;
    public double Value { get; private set; } = initial;
    public double Target { get; private set; } = initial;
    public bool IsMoving { get; private set; }
    public void To(double target, Animation animation, double delaySeconds = 0)
    {
        if (target == Target && (IsMoving || Value == target)) return;
        Target = target;
        if (motion.Still) { Jump(target); return; }
        from = Value; curve = animation; delay = delaySeconds; start = SwiftClock.Now; IsMoving = true;
        motion.Add(this);
    }
    public void Jump(double value)
    {
        Value = Target = value; IsMoving = false; motion.Remove(this); changed?.Invoke(value);
    }
    internal bool Step()
    {
        if (!IsMoving) return true;
        var seconds = (SwiftClock.Now - start).TotalSeconds;
        if (seconds + .001 >= delay + curve!.Duration) { Jump(Target); return true; }
        var progress = seconds <= delay ? 0 : curve.Progress(seconds - delay);
        Value = from + (Target - from) * progress; changed?.Invoke(Value); return false;
    }
}
