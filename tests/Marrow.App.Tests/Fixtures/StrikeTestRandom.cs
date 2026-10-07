namespace Marrow.App.Tests.Fixtures;

// gen-vectors.mjs:115, :453-466. The vector generator's deterministic sample source.
public sealed class StrikeTestRandom(uint seed)
{
    private uint state = seed;
    public double Next()
    {
        unchecked
        {
            state += 0x6d2b79f5;
            var t = (state ^ (state >> 15)) * (1 | state);
            t = (t + (t ^ (t >> 7)) * (61 | t)) ^ t;
            return (t ^ (t >> 14)) / 4294967296d;
        }
    }
}
