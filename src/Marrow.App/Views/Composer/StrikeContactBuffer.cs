namespace Marrow.App.Views.Composer;

public sealed class StrikeContactBuffer
{
    public StrikeContactEvent[] Items { get; } = new StrikeContactEvent[24];
    public int Count { get; internal set; }
    internal bool Ready;
    public StrikeContactBuffer() { for (var i = 0; i < Items.Length; i++) Items[i] = new(); }
    internal StrikeContactEvent Next(double age)
    {
        if (Count == Items.Length)
        {
#if DEBUG
            throw new InvalidOperationException("Strike event capacity exceeded");
#else
            Count--;
#endif
        }
        var e = Items[Count++]; e.Age = age; e.Count = 0; return e;
    }
}
