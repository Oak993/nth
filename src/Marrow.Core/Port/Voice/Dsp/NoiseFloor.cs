// PORT-ONLY, owner-ruled addition (OD-0091, OD-0091 first amendment; first-release voice typing, VT-2): voice-SPEC V-5.4.
namespace Marrow.Core.Port.Voice.Dsp;

/// <summary>
/// The ambient noise floor: the rolling 2 s minimum of 50 ms RMS windows (RESEARCH 3.2 item 3). The windows are consecutive and do not
/// overlap; a window counts once it is complete, so nothing is known (the floor is 0) for the first 50 ms.
/// </summary>
public sealed class NoiseFloor
{
    public const double WindowSeconds = 0.05;
    public const double MemorySeconds = 2.0;

    private readonly int windowSamples;
    private readonly double[] windows;      // RMS of the last completed windows, a ring
    private int filled;
    private int next;
    private int inWindow;
    private double sumSquares;

    public NoiseFloor(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        windowSamples = Math.Max(1, (int)Math.Round(sampleRate * WindowSeconds));
        windows = new double[(int)Math.Round(MemorySeconds / WindowSeconds)];
    }

    /// <summary>The smallest RMS among the windows of the last 2 s; 0 before the first window completes.</summary>
    public double Value { get; private set; }

    public void Push(ReadOnlySpan<float> mono)
    {
        foreach (var raw in mono)
        {
            var sample = float.IsFinite(raw) ? raw : 0f;
            sumSquares += (double)sample * sample;
            if (++inWindow < windowSamples) continue;

            windows[next] = Math.Sqrt(sumSquares / windowSamples);
            next = (next + 1) % windows.Length;
            if (filled < windows.Length) filled++;
            inWindow = 0;
            sumSquares = 0;

            var min = double.MaxValue;
            for (var i = 0; i < filled; i++) min = Math.Min(min, windows[i]);
            Value = min;
        }
    }

    public void Reset()
    {
        filled = next = inWindow = 0;
        sumSquares = 0;
        Value = 0;
    }
}
