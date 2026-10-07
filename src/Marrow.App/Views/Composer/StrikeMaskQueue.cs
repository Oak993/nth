namespace Marrow.App.Views.Composer;

public sealed class StrikeMaskQueue
{
    private readonly StrikeMask[] locked = new StrikeMask[1200], plume = new StrikeMask[1200];
    private int lockCount, plumeCount;
    public int Count => lockCount + plumeCount;
    public StrikeMask At(int i) => i < lockCount ? locked[i] : plume[i - lockCount];
    internal void Clear() => lockCount = plumeCount = 0;
    internal void Add(StrikeMask mask)
    {
        if (Count == 1200)
        {
#if DEBUG
            throw new InvalidOperationException("Strike mask capacity exceeded");
#else
            return;
#endif
        }
        if (mask.Set == 0) locked[lockCount++] = mask; else plume[plumeCount++] = mask;
    }
}
