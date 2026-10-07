namespace Marrow.App.Views.Composer;

// P:1391-1434. One slider tube owns one start group, independent of bubble/page clocks.
public sealed class StrikeReentry
{
    private readonly StrikeStarts starts;
    private readonly IStrikeDraws draws;
    private bool initialized;
    public double Offset { get; private set; }
    public bool Pending { get; private set; }

    public StrikeReentry(int seed, IStrikeDraws draws, double? initialOffset = null)
    {
        starts = new(seed); this.draws = draws;
        if (initialOffset is { } offset) { Offset = offset; initialized = true; }
    }

    public void Request() => Pending = true;
    public void StartNow(double pageTime) { Offset = starts.RandomClean(draws).U - pageTime; initialized = true; }
    public void FastOn(double pageTime, double fast) { if (fast < 0.01) StartNow(pageTime); else Request(); }
    public bool BeforeFrame(double pageTime)
    {
        if (!initialized) { StartNow(pageTime); return true; }
        if (!Pending || !starts.IsQuiet(pageTime + Offset)) return false;
        Pending = false; StartNow(pageTime); return true;
    }
}
