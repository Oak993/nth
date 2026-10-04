using Marrow.Core.Port.Voice.Dsp;

namespace Marrow.Core.Tests.Port.Voice;

// VT-2 (voice-SPEC V-5.4, V-T19): the noise floor is the rolling 2 s minimum of 50 ms RMS windows. A constant (DC) block of value v has
// RMS exactly v, so the windows below are known by construction.
public sealed class NoiseFloorTests
{
    private const int Rate = 48000;

    private static float[] Dc(double value, double seconds) => Enumerable.Repeat((float)value, (int)Math.Round(Rate * seconds)).ToArray();

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: nothing is known before the first 50 ms window completes
    public void The_floor_is_zero_until_a_50_ms_window_has_completed()
    {
        var floor = new NoiseFloor(Rate);
        Assert.Equal(0.0, floor.Value);
        floor.Push(Dc(0.1, 0.049));
        Assert.Equal(0.0, floor.Value);
        floor.Push(Dc(0.1, 0.001));
        Assert.Equal(0.1, floor.Value, 6);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: the minimum of the windows
    public void The_floor_is_the_quietest_window_of_the_last_2_seconds()
    {
        var floor = new NoiseFloor(Rate);
        floor.Push(Dc(0.1, 0.5));
        floor.Push(Dc(0.02, 0.05));
        floor.Push(Dc(0.1, 0.5));
        Assert.Equal(0.02, floor.Value, 6);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: rolling 2 s: a quiet window is forgotten 2 s later
    public void A_quiet_window_is_forgotten_after_2_seconds()
    {
        var floor = new NoiseFloor(Rate);
        floor.Push(Dc(0.02, 0.5));
        floor.Push(Dc(0.1, 1.9));   // the quiet block ended 1.9 s ago
        Assert.Equal(0.02, floor.Value, 6);
        floor.Push(Dc(0.1, 0.2));   // 2.1 s ago
        Assert.Equal(0.1, floor.Value, 6);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: the windows do not depend on how the samples arrive
    public void The_push_size_does_not_change_the_floor()
    {
        var input = new List<float>();
        input.AddRange(Dc(0.1, 0.3));
        input.AddRange(Dc(0.04, 0.2));
        input.AddRange(Dc(0.07, 0.4));
        var whole = new NoiseFloor(Rate);
        whole.Push(input.ToArray());

        var pieces = new NoiseFloor(Rate);
        var rng = new Random(3);
        for (var at = 0; at < input.Count;)
        {
            var n = Math.Min(rng.Next(1, 900), input.Count - at);
            pieces.Push(input.GetRange(at, n).ToArray());
            at += n;
        }

        Assert.Equal(whole.Value, pieces.Value);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: the window is 50 ms at the device rate
    public void The_window_is_50_ms_at_the_device_rate()
    {
        var floor = new NoiseFloor(44100);
        floor.Push(new float[2204].Select(_ => 0.3f).ToArray());   // 44100 * 0.05 = 2205 samples per window
        Assert.Equal(0.0, floor.Value);
        floor.Push([0.3f]);
        Assert.Equal(0.3, floor.Value, 6);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // a new session starts with no floor
    public void Reset_forgets_every_window()
    {
        var floor = new NoiseFloor(Rate);
        floor.Push(Dc(0.1, 0.5));
        floor.Reset();
        Assert.Equal(0.0, floor.Value);
        floor.Push(Dc(0.2, 0.049));
        Assert.Equal(0.0, floor.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-44100)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // a device rate that is not positive is no device
    public void A_sample_rate_that_is_not_positive_is_refused(int rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoiseFloor(rate));
    }
}
