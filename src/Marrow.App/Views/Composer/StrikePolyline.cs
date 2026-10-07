namespace Marrow.App.Views.Composer;

public readonly record struct StrikePolyline(double[] Points, int Offset, int Count, int K, bool Twin, int Kind, int Index)
{
    public double X(int i) => Points[(Offset + i) * 2];
    public double Y(int i) => Points[(Offset + i) * 2 + 1];
}
