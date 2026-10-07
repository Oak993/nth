using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

// TESTS A-01/A-02, SPEC K-013..K-017; expected values are only the locked engine's.
public sealed class StrikeSharedMathVectorTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Hash_and_noise_match_every_locked_numerator_and_sample()
    {
        using var doc = StrikeVectors.Read("noise.json");
        foreach (var row in doc.RootElement.GetProperty("hash").EnumerateArray())
            Assert.Equal(row.GetProperty("u32").GetUInt32(), (uint)(Strike.Hash(unchecked((int)row.GetProperty("a").GetInt64()), unchecked((int)row.GetProperty("b").GetInt64())) * 4294967296d));
        foreach (var key in new[] { "vn", "fbm" })
            foreach (var row in doc.RootElement.GetProperty(key).EnumerateArray())
            {
                var x = row.GetProperty("x").GetDouble(); var seed = unchecked((int)row.GetProperty("s").GetInt64());
                var actual = key == "vn" ? Strike.Noise(x, seed) : Strike.Fbm(x, seed);
                Assert.InRange(Math.Abs(actual - row.GetProperty("v").GetDouble()), 0, 1e-7);
            }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Smoothstep_matches_every_locked_sample()
    {
        var method = typeof(Strike).GetMethod("Ss", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        using var doc = StrikeVectors.Read("noise.json");
        foreach (var row in doc.RootElement.GetProperty("ss").EnumerateArray())
        {
            var actual = (double)method.Invoke(null, [row.GetProperty("e0").GetDouble(), row.GetProperty("e1").GetDouble(), row.GetProperty("x").GetDouble()])!;
            Assert.InRange(Math.Abs(actual - row.GetProperty("v").GetDouble()), 0, 1e-7);
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_ease_table_entry_is_exact_and_interpolation_matches_the_locked_samples()
    {
        var method = typeof(Strike).GetMethod("Ease", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        using var doc = StrikeVectors.Read("noise.json");
        var entries = doc.RootElement.GetProperty("easeLut"); Assert.Equal(1025, entries.GetArrayLength());
        for (var i = 0; i <= 1024; i++) Assert.Equal(entries[i].GetDouble(), (double)method.Invoke(null, [i / 1024d])!);
        foreach (var row in doc.RootElement.GetProperty("ease").EnumerateArray())
            Assert.InRange(Math.Abs((double)method.Invoke(null, [row.GetProperty("x").GetDouble()])! - row.GetProperty("v").GetDouble()), 0, 1e-7);
        Assert.Equal(0d, method.Invoke(null, [-1d])); Assert.Equal(1d, method.Invoke(null, [2d]));
    }
}
