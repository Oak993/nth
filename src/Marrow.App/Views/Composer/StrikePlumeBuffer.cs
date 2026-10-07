// DEVIATION OD-0093: locked plume geometry, strike-light-locked.html:426-490 (SPEC K-069..K-086).
namespace Marrow.App.Views.Composer;

// Maxima across all four locked geometries +25%, rounded up. Vertices are packed into kept arrays.
public sealed class StrikePlumeBuffer
{
    public StrikePlumeLine[] Lines { get; } = new StrikePlumeLine[249];
    public double[] Points { get; } = new double[1803 * 2];
    public double[] U { get; } = new double[1803];
    public int Count { get; internal set; }
    public int Vertices { get; internal set; }
    internal bool Ready;

    internal bool Put(double x, double y, double u)
    {
        if (Vertices == U.Length)
        {
#if DEBUG
            throw new InvalidOperationException("Strike plume vertex capacity exceeded");
#else
            return false;
#endif
        }
        Points[Vertices * 2] = x; Points[Vertices * 2 + 1] = y; U[Vertices++] = u; return true;
    }

    internal bool Line(int id, int level, double weight, int start)
    {
        if (Count == Lines.Length || Vertices - start > 188)
        {
#if DEBUG
            throw new InvalidOperationException("Strike plume line capacity exceeded");
#else
            Vertices = start; return false;
#endif
        }
        Lines[Count++] = new(id, level, weight, start, Vertices - start - 1); return true;
    }
}
