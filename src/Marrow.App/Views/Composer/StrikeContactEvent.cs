namespace Marrow.App.Views.Composer;

public sealed class StrikeContactEvent
{
    public double Age { get; internal set; }
    public int Count { get; internal set; }
    public StrikeContactSegment[] Segments { get; } = new StrikeContactSegment[10];
    internal void Add(int reference, int first, int last)
    {
        if (Count == Segments.Length)
        {
#if DEBUG
            throw new InvalidOperationException("Strike event segment capacity exceeded");
#else
            return;
#endif
        }
        Segments[Count++] = new(reference, first, last);
    }
}
