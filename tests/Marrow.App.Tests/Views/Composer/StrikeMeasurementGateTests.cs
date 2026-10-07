using System.Reflection;
using System.Text.Json;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeMeasurementGateTests
{
    private static string[] Failures(Dictionary<string, object> values)
    {
        var type = typeof(StrikeMeasurementGateTests).Assembly.GetType("Marrow.App.Tests.Fixtures.StrikeMeasurementGate");
        Assert.NotNull(type);
        var method = type.GetMethod("Failures", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return (string[])method.Invoke(null, [JsonSerializer.SerializeToElement(values)])!;
    }

    private static Dictionary<string, object> Complete() => new()
    {
        ["serverGc"] = true, ["seconds"] = 60d, ["updatesPerSecond"] = 60d,
        ["mainFrames"] = 0L, ["recordings"] = 0L, ["collections"] = new[] { 0, 0, 0 },
        ["longestObservedContentGapMs"] = 21d, ["cpuBeforePercent"] = 19.9,
        ["cpuMaxPercent"] = 34.9, ["inputIdleBeforeSeconds"] = 30d,
        ["presentCadencePercent"] = 99d, ["nativeLockHeld"] = true, ["quietWindow"] = true
    };

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Unmeasured_quiet_window_and_presentation_cannot_pass_the_gate()
    {
        var values = Complete();
        Assert.Empty(Failures(values));
        foreach (var key in new[] { "cpuBeforePercent", "cpuMaxPercent", "inputIdleBeforeSeconds", "presentCadencePercent", "quietWindow", "nativeLockHeld" })
        {
            var incomplete = Complete(); incomplete.Remove(key);
            Assert.Contains(key, Failures(incomplete));
        }
    }

    [Theory]
    [InlineData("serverGc", false)]
    [InlineData("updatesPerSecond", 59.89)]
    [InlineData("updatesPerSecond", 60.11)]
    [InlineData("seconds", 59.99)]
    [InlineData("mainFrames", 1d)]
    [InlineData("recordings", 1d)]
    [InlineData("longestObservedContentGapMs", 21.001)]
    [InlineData("cpuBeforePercent", 20d)]
    [InlineData("cpuMaxPercent", 35d)]
    [InlineData("inputIdleBeforeSeconds", 29.99)]
    [InlineData("presentCadencePercent", 98.999)]
    [InlineData("nativeLockHeld", false)]
    [InlineData("quietWindow", false)]
    [Trait("Decision", "ED-248")]
    public void Every_quiet_gate_bound_is_required(string key, object value)
    {
        var values = Complete(); values[key] = value;
        Assert.Contains(key, Failures(values));
        values = Complete(); values["collections"] = new[] { 0, 1, 0 };
        Assert.Contains("collections", Failures(values));
    }
}
