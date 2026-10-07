using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeStartsVectorTests
{
    private static dynamic Starts(int seed)
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeStarts"); Assert.NotNull(type);
        return Activator.CreateInstance(type, [seed])!;
    }
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_merged_and_clean_window_and_each_ten_ms_boundary_matches_the_locked_engine()
    {
        using var doc = StrikeVectors.Read("clean-windows.json");
        foreach (var row in doc.RootElement.GetProperty("ranges").EnumerateArray())
        {
            var seed = row.TryGetProperty("seed", out var s) ? s.GetInt32() : doc.RootElement.GetProperty("seed").GetInt32(); dynamic starts = Starts(seed);
            var from = row.GetProperty("T0").GetDouble(); var to = row.GetProperty("T1").GetDouble();
            Compare(row.GetProperty("strikeWindows"), starts.Windows(from, to, false)); Compare(row.GetProperty("cleanWindows"), starts.Windows(from, to, true));
            if (!row.TryGetProperty("isCleanEvery10ms", out var sampled)) continue;
            var clean = sampled.GetString()!; var quiet = row.GetProperty("isQuietEvery10ms").GetString()!;
            // gen-vectors.mjs:432 repeatedly adds 0.01; preserve its binary sampling times.
            var time = from;
            for (var i = 0; i < clean.Length; i++, time += 0.01) { Assert.Equal(clean[i] == '1', (bool)starts.IsClean(time)); Assert.Equal(quiet[i] == '1', (bool)starts.IsQuiet(time)); }
        }
        dynamic vis = Starts(1274);
        foreach (var row in doc.RootElement.GetProperty("visEnd").EnumerateArray()) Near(row.GetProperty("v").GetDouble(), (double)vis.VisibleEnd(row.GetProperty("partial").GetBoolean()));
        static void Compare(JsonElement expected, dynamic actual)
        {
            Assert.Equal(expected.GetArrayLength(), (int)actual.Count);
            for (var i = 0; i < expected.GetArrayLength(); i++) { Near(expected[i][0].GetDouble(), (double)actual.Items[i].From); Near(expected[i][1].GetDouble(), (double)actual.Items[i].To); }
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void All_eighty_scripted_random_entries_choose_windows_uniformly_and_consume_three_draws()
    {
        using var doc = StrikeVectors.Read("random-entry.json");
        foreach (var row in doc.RootElement.GetProperty("picks").EnumerateArray())
        {
            dynamic starts = Starts(row.GetProperty("seed").GetInt32()); var draws = new ScriptedStrikeDraws(row.GetProperty("draws").EnumerateArray().Select(x => x.GetDouble()).ToArray());
            dynamic result = starts.RandomClean(draws);
            Assert.Equal(row.GetProperty("k").GetInt32(), (int)result.K); Near(row.GetProperty("T0").GetDouble(), (double)result.T0);
            Assert.Equal(row.GetProperty("windows").GetInt32(), (int)result.Windows); Assert.Equal(row.GetProperty("index").GetInt32(), (int)result.Index);
            Near(row.GetProperty("window")[0].GetDouble(), (double)result.From); Near(row.GetProperty("window")[1].GetDouble(), (double)result.To);
            Near(row.GetProperty("u").GetDouble(), (double)result.U); Assert.Equal(row.GetProperty("isClean").GetBoolean(), (bool)starts.IsClean((double)result.U)); Assert.Equal(3, draws.Consumed);
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void All_eighty_model_change_waits_end_on_the_first_quiet_frame_before_the_scripted_jump()
    {
        using var doc = StrikeVectors.Read("random-entry.json"); dynamic starts = Starts(1274);
        var caseIndex = 0;
        foreach (var row in doc.RootElement.GetProperty("waitExamples").EnumerateArray())
        {
            // gen-vectors.mjs:455-458 constructs phases before save() rounds JSON reals.
            // Lead ruling: replay that construction uniformly, preserving every stored oracle.
            var plan = StrikeMix.Timing(row.GetProperty("k").GetInt32(), 1274);
            var phase = (caseIndex++ % 8) switch { 0 => -plan.Lead - plan.Charge - 0.4, 1 => -plan.Lead - plan.Charge + 0.01, 2 => -0.05, 3 => 0, 4 => 0.5, 5 => 1.0, 6 => 1.85, _ => 1.95 };
            Near(row.GetProperty("a").GetDouble(), phase);
            var time = plan.At + phase; Near(row.GetProperty("Tstart").GetDouble(), time); var frames = 0;
            while (!(bool)starts.IsQuiet(time + frames / 60d)) { frames++; Assert.InRange(frames, 0, 2000); }
            try { Assert.Equal(row.GetProperty("framesWaited").GetInt32(), frames); }
            catch (Xunit.Sdk.XunitException error) { throw new Xunit.Sdk.XunitException($"k={row.GetProperty("k")} a={row.GetProperty("a")} Tstart={row.GetProperty("Tstart")} : {error.Message}"); }
            Near(row.GetProperty("waitS").GetDouble(), frames / 60d); Near(row.GetProperty("Tfrom").GetDouble(), time + frames / 60d);
            var draws = new ScriptedStrikeDraws(row.GetProperty("draws").EnumerateArray().Select(x => x.GetDouble()).ToArray()); dynamic result = starts.RandomClean(draws);
            Near(row.GetProperty("u").GetDouble(), (double)result.U); Near(row.GetProperty("newOffMinusT").GetDouble(), (double)result.U - frames / 60d); Assert.Equal(3, draws.Consumed);
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void The_twenty_thousand_change_wait_distribution_matches_the_locked_generator()
    {
        using var doc = StrikeVectors.Read("random-entry.json"); var expected = doc.RootElement.GetProperty("waitStats"); dynamic starts = Starts(1274);
        var random = new StrikeTestRandom(99); for (var i = 0; i < 80 * 3; i++) random.Next();
        var waits = new double[expected.GetProperty("samples").GetInt32()];
        for (var i = 0; i < waits.Length; i++)
        {
            var start = 1000 + random.Next() * 1000000; var frames = 0;
            while (!(bool)starts.IsQuiet(start + frames / 60d)) { frames++; Assert.InRange(frames, 0, 2000); }
            waits[i] = frames / 60d;
        }
        Array.Sort(waits); var alreadyQuiet = Array.FindLastIndex(waits, w => w == 0) + 1;
        Near(expected.GetProperty("quietAtOnce").GetDouble(), alreadyQuiet / (double)waits.Length);
        Compare(expected.GetProperty("all"), waits);
        var busy = waits.AsSpan(alreadyQuiet).ToArray(); Assert.Equal(expected.GetProperty("whenBusy").GetProperty("n").GetInt32(), busy.Length); Compare(expected.GetProperty("whenBusy"), busy);
        static void Compare(JsonElement expected, double[] actual)
        {
            Near(expected.GetProperty("median").GetDouble(), actual[(int)Math.Floor(actual.Length * 0.5)]); Near(expected.GetProperty("p90").GetDouble(), actual[(int)Math.Floor(actual.Length * 0.9)]);
            Near(expected.GetProperty("p99").GetDouble(), actual[(int)Math.Floor(actual.Length * 0.99)]); Near(expected.GetProperty("max").GetDouble(), actual[^1]);
        }
    }
}
