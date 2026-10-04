// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the resampler seam. See ED-292 (3).
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>
/// Streaming sample-rate conversion to 16 kHz (V-5.2). <c>PolyphaseResampler</c> (VT-2) implements it. A session meets it only when the
/// capture's mix format is not 16 kHz.
/// </summary>
public interface IStreamingResampler
{
    /// <summary>Converts a mono block at <paramref name="inputRate"/> to 16 kHz, appending to <paramref name="output"/>. Streaming output equals one-shot output.</summary>
    void Process(int inputRate, ReadOnlySpan<float> mono, List<float> output);

    void Reset();
}
