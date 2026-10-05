using Marrow.Core.Port.Voice.Dsp;

namespace Marrow.Core.Tests.Fixtures;

// VT-11 test fixture: the reads of a LevelAnalyser fed one frame at a time, at the fixed 60 Hz of the signal (read k happens when k x rate / 60 frames, by integer division, have been fed):
// what the level meter must publish, worked out frame by frame instead of block by block.
public sealed class LevelReference(int rate)
{
    private readonly LevelAnalyser analyser = new(rate);
    private long fed;
    private long reads;

    public LevelBands Last { get; private set; }

    public void Feed(float mono)
    {
        analyser.Push([mono]);
        fed++;
        while ((reads + 1) * rate / 60 <= fed)
        {
            reads++;
            Last = analyser.Read();
        }
    }
}
