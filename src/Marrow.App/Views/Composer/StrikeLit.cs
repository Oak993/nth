namespace Marrow.App.Views.Composer;
public readonly record struct StrikeLit(StrikeTrace Trace, double HalfWidth, double WidthScale = 1)
{
    public double ContactHalfWidth => HalfWidth * WidthScale;
}
