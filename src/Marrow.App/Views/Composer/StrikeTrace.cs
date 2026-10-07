namespace Marrow.App.Views.Composer;
public readonly record struct StrikeTrace(StrikePolyline Line, int First, int Last, int JitterSeed, double Jitter)
{
    public double Y(int i) => Line.Y(i) + (Jitter != 0 && i > 0 && i < Line.Count - 1 ? (Strike.Hash(i, JitterSeed) - 0.5) * 2 * Jitter : 0);
}
