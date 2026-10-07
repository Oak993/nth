namespace Marrow.App.Views.Composer;

public readonly record struct StrikeTiming(int K, double At, int Style, int Event, int Family, bool Partial,
    double Reach, double Strength, double Lead, int Steps, double Charge, int FlickerCount,
    StrikeFlicker F0, StrikeFlicker F1, StrikeFlicker F2, StrikeFlicker F3, bool IsTwin = false)
{
    public StrikeFlicker Flicker(int i) => i >= FlickerCount ? throw new ArgumentOutOfRangeException(nameof(i)) : i switch
    {
        0 => F0, 1 => F1, 2 => F2, 3 => F3, _ => throw new ArgumentOutOfRangeException(nameof(i))
    };

    public double Pulse(double age)
    {
        double intensity = 0;
        for (var i = 0; i < FlickerCount; i++)
        {
            var f = Flicker(i);
            if (age >= f.At) intensity += Math.Exp(-(age - f.At) / f.Decay) * f.Gain;
        }
        return Math.Min(1.1, intensity) * Strength * (Partial ? 0.62 : 1);
    }
}
