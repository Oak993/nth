using Marrow.Setup.Motion;

namespace Marrow.Setup.Ui;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4, "The bar follows a critically damped spring and never goes backwards"; the canvas's
/// "Progress" row and its runner, stiffness 110; unit U3, ED-NEW-inst-u3-1). The bar's drawn value: a critically damped spring
/// (stiffness 110, so a response of 2π/√110 ≈ 0.60 s) toward the engine's target, drawn as the highest value it has
/// reached and never above the target, so it never goes back and never overshoots. Once the target is 100 and the value
/// passes 99.6 it shows 100, as the canvas does. The percentage is the value rounded down. Calm (Animation effects off):
/// it shows the target at once. Drawn through a transform by the screen (the fill's ScaleX), never by width.
/// </summary>
public sealed class ProgressMotion : IMotion
{
    /// <summary>The canvas's stiffness.</summary>
    public const double Stiffness = 110;

    /// <summary>The spring's response for that stiffness: 2π/√110.</summary>
    public static readonly double Response = 2 * Math.PI / Math.Sqrt(Stiffness);

    private readonly Spring spring = new(Response, 1, 0, 0.0005);
    private readonly Func<bool> calm;
    private Animator? animator;

    public ProgressMotion(Func<bool> calm) => this.calm = calm ?? throw new ArgumentNullException(nameof(calm));

    /// <summary>The drawn value, 0 to 100.</summary>
    public double Value { get; private set; }

    /// <summary>Where the bar is heading.</summary>
    public double Target => spring.Target;

    /// <summary>The percentage shown beside the bar: the drawn value rounded down.</summary>
    public int Percent => (int)Math.Floor(Value + 1e-9);

    /// <summary>Called after each drawn change.</summary>
    public event Action? Changed;

    public void Attach(Animator frames) => animator = frames;

    /// <summary>Heads for <paramref name="target"/>; a lower target than the one already set is ignored.</summary>
    public void SetTarget(double target)
    {
        target = Math.Clamp(target, 0, 100);
        if (target <= spring.Target && Value >= target) return;
        spring.Retarget(Math.Max(target, spring.Target));
        if (calm() || animator is null)
        {
            spring.Snap(spring.Target);
            Draw(spring.Target);
            return;
        }
        animator.Start(this);
    }

    /// <summary>Starts again from <paramref name="value"/> at rest (a new run on a fresh screen).</summary>
    public void Now(double value)
    {
        value = Math.Clamp(value, 0, 100);
        spring.Snap(value);
        Value = value;
        Changed?.Invoke();
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = spring.Advance(seconds);
        Draw(spring.Value);
        if (Value >= 100) return false;
        return moving;
    }

    private void Draw(double raw)
    {
        var next = Math.Max(Value, Math.Min(raw, spring.Target));
        if (spring.Target >= 100 && next > 99.6) next = 100;
        if (next == Value) return;
        Value = next;
        Changed?.Invoke();
    }
}
