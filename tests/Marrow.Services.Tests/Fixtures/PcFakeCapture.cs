using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-5, ED-NEW-pcctl-7). A capture source that hands back a fixed frame (or
// nothing, or throws), and an encoder that records what it was asked (crop, size, quality, marks) and returns two JPEG
// marker bytes. Nothing is ever captured.
public sealed class PcFakeCapture(string name, PcFrame? frame, bool throws = false) : IWindowCapture
{
    public int Calls { get; private set; }
    public string Name => name;
    public PcFrame? Frame { get; set; } = frame;

    public Task<PcFrame?> Capture(nint window, PcRect frame1, CancellationToken cancellationToken)
    {
        Calls++;
        if (throws) throw new InvalidOperationException("no device");
        return Task.FromResult(Frame);
    }

    public static PcFrame Solid(int width, int height, byte value)
    {
        var bytes = new byte[width * height * 4];
        Array.Fill(bytes, value);
        return PcFrame.Packed(bytes, width, height);
    }
}

public sealed class PcFakeEncoder : IPcJpegEncoder
{
    private readonly Lock gate = new();

    public List<(PcRect Crop, int Width, int Height, int Quality)> Calls { get; } = [];

    public List<IReadOnlyList<PcMark>> Marks { get; } = [];

    public byte[] Encode(PcFrame frame, PcRect crop, int width, int height, int quality, IReadOnlyList<PcMark> marks)
    {
        lock (gate)
        {
            Calls.Add((crop, width, height, quality));
            Marks.Add(marks);
        }
        return [0xFF, 0xD8];
    }
}
