// PORT-ONLY: locked spring(240,19), SP.settle, HTML :1671-1684 / :1983. Mass 1, the same Euler samples and 44-stop linear easing.
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Popover;
internal sealed record PopoverSettleCurve : Animation
{
    internal static readonly PopoverSettleCurve Instance = new();
    private readonly double[] samples = new double[45];
    public override double Duration { get; }
    private PopoverSettleCurve()
    {
        var points = new List<double> { 0 }; double x = 0, velocity = 0, time = 0; const double dt = 1.0 / 1200;
        while (time < 3)
        {
            for (var i = 0; i < 10; i++) { velocity += (-240 * (x - 1) - 19 * velocity) * dt; x += velocity * dt; time += dt; }
            points.Add(x); if (time > .2 && Math.Abs(x - 1) < .0015 && Math.Abs(velocity) < .02) break;
        }
        Duration = Math.Floor(time * 1000 + .5) / 1000;
        for (var i = 0; i < samples.Length; i++) samples[i] = i == 44 ? 1 : Math.Round(points[(int)Math.Floor(i / 44.0 * (points.Count - 1) + .5)], 4, MidpointRounding.AwayFromZero);
    }
    public override double Progress(double seconds)
    {
        var value = Math.Clamp(seconds / Duration, 0, 1) * 44; var index = Math.Min(43, (int)value);
        return samples[index] + (samples[index + 1] - samples[index]) * (value - index);
    }
}
