using System.Text.Json;

namespace Marrow.App.Tests.Fixtures;

// TESTS.md Phase E: missing evidence is a failure, never an implicit quiet-window pass.
public static class StrikeMeasurementGate
{
    public static string[] Failures(JsonElement report)
    {
        var failed = new List<string>();
        void Flag(string key) { if (!report.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.True) failed.Add(key); }
        void Number(string key, Func<double, bool> accepts)
        {
            if (!report.TryGetProperty(key, out var v) || !v.TryGetDouble(out var n) || !double.IsFinite(n) || !accepts(n)) failed.Add(key);
        }
        Flag("serverGc"); Flag("quietWindow"); Flag("nativeLockHeld");
        Number("seconds", n => n >= 60);
        Number("updatesPerSecond", n => Math.Abs(n - 60) <= 0.1);
        Number("mainFrames", n => n == 0); Number("recordings", n => n == 0);
        Number("longestObservedContentGapMs", n => n <= 21);
        Number("cpuBeforePercent", n => n >= 0 && n < 20);
        Number("cpuMaxPercent", n => n >= 0 && n < 35);
        Number("inputIdleBeforeSeconds", n => n >= 30);
        Number("presentCadencePercent", n => n >= 99 && n <= 100);
        if (!report.TryGetProperty("collections", out var counts) || counts.ValueKind != JsonValueKind.Array ||
            counts.GetArrayLength() != 3 || counts.EnumerateArray().Any(v => !v.TryGetInt32(out var n) || n != 0)) failed.Add("collections");
        return failed.ToArray();
    }
}
