using SkiaSharp;

namespace Marrow.UI.Tests.Fixtures;

// Lead 2026-10-05: acceptance is premultiplied RGBA, each channel independently.
internal sealed class AuroraPixelMetrics
{
    public double[] Peak { get; } = new double[4];
    public double[] Total { get; } = new double[4];
    public double[] StraightPeak { get; } = new double[4];
    public double[] StraightTotal { get; } = new double[4];
    public int Count { get; private set; }
    public void Add(SKColor p, SKColor q)
    {
        AddChannel(0, p.Red, q.Red, p.Alpha, q.Alpha);
        AddChannel(1, p.Green, q.Green, p.Alpha, q.Alpha);
        AddChannel(2, p.Blue, q.Blue, p.Alpha, q.Alpha);
        AddChannel(3, p.Alpha, q.Alpha, 255, 255);
        Count++;
    }
    private void AddChannel(int i, byte p, byte q, byte pa, byte qa)
    {
        var delta = Math.Abs(p * pa / 255.0 - q * qa / 255.0);
        Peak[i] = Math.Max(Peak[i], delta); Total[i] += delta;
        delta = Math.Abs(p - q);
        StraightPeak[i] = Math.Max(StraightPeak[i], delta); StraightTotal[i] += delta;
    }
    public double[] Mean => Total.Select(x => x / Count).ToArray();
    public double[] StraightMean => StraightTotal.Select(x => x / Count).ToArray();
    public bool Accepted => Peak.All(x => x <= 24) && Mean.All(x => x <= .5);
}
