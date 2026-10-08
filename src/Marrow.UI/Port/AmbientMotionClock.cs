namespace Marrow.UI.Port;

// T44 owner ruling. Separate decoration time from interaction and transition time.
public sealed class AmbientMotionClock
{
    public const double IdleSeconds = 20, EaseSeconds = .6, FrameSeconds = 1.0 / 60;
    public double Time { get; private set; }
    public double Speed { get; private set; } = 1;
    public bool Moving { get; private set; } = true;
    private double? sampled;
    private double input = double.NegativeInfinity, resume = double.NegativeInfinity, resumeSpeed;
    private double requested = double.NegativeInfinity;
    private bool wasAvailable = true;

    public void Sample(double now, double lastInput, bool available, bool interaction = false)
    {
        var previous = sampled;
        sampled = now;
        if (!available)
        {
            Speed = 0; Moving = false; wasAvailable = false; return;
        }
        var newInput = lastInput > input;
        var leavingIdle = now >= input + IdleSeconds;
        input = Math.Max(input, lastInput);
        if (!wasAvailable || (newInput && Speed < 1 && leavingIdle))
        {
            resume = now; resumeSpeed = Speed; previous = null;
        }
        wasAvailable = true;
        var priorSpeed = Speed;
        var ramp = Smooth((now - resume) / EaseSeconds);
        var up = resumeSpeed + (1 - resumeSpeed) * ramp;
        var down = 1 - Smooth((now - lastInput - IdleSeconds) / EaseSeconds);
        Speed = interaction ? 1 : Math.Min(up, down);
        if (previous is { } at && priorSpeed > 0)
            Time += Math.Min(.1, Math.Max(0, now - at)) * (priorSpeed + Speed) * .5;
        Moving = Speed > 0 || now < lastInput + IdleSeconds + EaseSeconds;
    }

    public double RequestDelay(double now, double lastInput, bool available, bool interaction = false) =>
        !available || (!interaction && now >= lastInput + IdleSeconds + EaseSeconds)
            ? double.PositiveInfinity : interaction ? 0 : Math.Max(0, requested + FrameSeconds - now);

    public void Requested(double now) => requested = now;
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
}
