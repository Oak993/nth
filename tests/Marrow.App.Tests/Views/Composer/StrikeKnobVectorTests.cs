using System.Reflection;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeKnobVectorTests
{
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);

    [Fact]
    [Trait("Decision", "ED-248")]
    public void All_sixty_hz_mix_charge_flare_phase_exit_and_chosen_strikes_match_the_locked_signal()
    {
        var read = typeof(KnobSignal).GetMethod("ReadMix", BindingFlags.Public | BindingFlags.Instance); Assert.NotNull(read);
        using var doc = StrikeVectors.Read("knob-signal.json");
        foreach (var run in doc.RootElement.GetProperty("runs").EnumerateArray())
        {
            var g = run.GetProperty("geometry").GetString() == "appMax" ? new StrikeGeometry(298, 34, 272, 24) : new StrikeGeometry(520, 40, 335.25, 28);
            var signal = new KnobSignal(); var pool = new StrikePlans(run.GetProperty("seed").GetInt32()); dynamic state = signal;
            var i = 0;
            foreach (var row in run.GetProperty("rows").EnumerateArray())
            {
                // gen-vectors.mjs:504 constructs n/60 before serialising; use the same input for every row.
                var clock = i++ / 60d; Near(row[0].GetDouble(), clock);
                read.Invoke(signal, [g, pool, clock + run.GetProperty("off").GetDouble(), clock, 1d]);
                try
                {
                    Near(row[1].GetDouble(), signal.RawCharge); Near(row[2].GetDouble(), signal.RawFlare);
                    Assert.Equal(row[3].GetString(), signal.Phase.ToString().ToLowerInvariant()); Near(row[4].GetDouble(), signal.Charge); Near(row[5].GetDouble(), signal.Flare); Near(row[6].GetDouble(), signal.ExitAngle);
                    if (row[7].ValueKind == JsonValueKind.Null) { Assert.Null((int?)state.MixK); Assert.Null((bool?)state.MixTwin); }
                    else { Assert.Equal(row[7].GetInt32(), (int?)state.MixK); Assert.Equal(row[8].GetBoolean(), (bool?)state.MixTwin); }
                }
                catch (Xunit.Sdk.XunitException error) { throw new Xunit.Sdk.XunitException($"geometry={run.GetProperty("geometry")} t={clock:R}: {error.Message}"); }
            }
        }
    }
}
