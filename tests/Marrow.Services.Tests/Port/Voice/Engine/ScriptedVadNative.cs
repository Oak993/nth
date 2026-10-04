using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

/// <summary>VT-6: <see cref="IVadNative"/> over a function: it records every window it is given and answers one probability per 512-sample frame.</summary>
public sealed class ScriptedVadNative(Func<ReadOnlyMemory<float>, float[]> answer) : IVadNative
{
    public List<float[]> Windows { get; } = [];

    public int Disposals { get; private set; }

    /// <summary>The probabilities of a window of N frames: a function of the frame's index in the call, so a test can tell which frame answered.</summary>
    public static ScriptedVadNative ByIndex(Func<int, float> probability) =>
        new(window => Enumerable.Range(0, window.Length / 512).Select(probability).ToArray());

    public float[] Detect(ReadOnlySpan<float> samples)
    {
        Windows.Add(samples.ToArray());
        return answer(Windows[^1]);
    }

    public void Dispose() => Disposals += 1;
}
