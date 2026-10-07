namespace Marrow.App.Views.Composer;

// Buffers are borrowed until their ring slot is reused. Existing A2 inspection APIs keep their own snapshots.
public sealed class StrikeLine(int vertices)
{
    public double[] Points { get; } = new double[vertices * 2];
    public int Count { get; internal set; }
    public int Split { get; internal set; }
    public double Gain { get; internal set; } = 1;
    public int At { get; internal set; }
}
