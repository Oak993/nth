using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Marrow.App.Views.Composer;

public sealed class StrikeWindowBuffer
{
    public StrikeWindow[] Items { get; } = new StrikeWindow[256];
    public int Count { get; internal set; }
    internal void Add(double from, double to)
    {
        if (Count == Items.Length) throw new InvalidOperationException("Strike timing window capacity exceeded");
        Items[Count++] = new(from, to);
    }
}
