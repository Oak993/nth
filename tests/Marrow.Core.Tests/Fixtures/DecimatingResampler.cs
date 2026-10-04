using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Tests.Fixtures;

// VT-3 test fixture: a stand-in for PolyphaseResampler (VT-2) that averages groups of inputRate / 16000 samples. It makes a 48 kHz capture
// reach the session at 16 kHz without the real filter; the real one is tested in VT-2 (V-T17).
public sealed class DecimatingResampler : IStreamingResampler
{
    private readonly List<float> carry = [];

    public int Calls { get; private set; }

    public int LastInputRate { get; private set; }

    public void Process(int inputRate, ReadOnlySpan<float> mono, List<float> output)
    {
        Calls += 1;
        LastInputRate = inputRate;
        var factor = inputRate / 16000;
        foreach (var sample in mono) carry.Add(sample);
        var groups = carry.Count / factor;
        for (var g = 0; g < groups; g++)
        {
            float sum = 0;
            for (var i = 0; i < factor; i++) sum += carry[g * factor + i];
            output.Add(sum / factor);
        }
        carry.RemoveRange(0, groups * factor);
    }

    public void Reset() => carry.Clear();
}
