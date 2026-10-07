using System.Text.RegularExpressions;
using Marrow.Core.Port;

namespace Marrow.Services.Port.Usage;

// PORT-ONLY: OD-0114 / ED-406; observational rates for future Smart Handshake timing, not quota prediction.
// External clients may spend the same budget. These rates are measured during this thread's use of an account.
public sealed partial class UsageRateEstimator
{
    private sealed record Sample(DateTimeOffset At, double Percent, DateTimeOffset? Reset, long? Duration, string? Plan, double? Rate);
    private readonly Lock gate = new();
    private readonly Dictionary<(Guid Thread, UsageAccountKey Account, string Window), Sample> samples = [];
    private readonly Dictionary<(Guid Thread, UsageAccountKey Account), DateTimeOffset> observed = [];

    public void Observe(Guid thread, AccountPlanLimitsSnapshot snapshot)
    {
        lock (gate)
        {
            var owner = (thread, snapshot.Account);
            if (observed.TryGetValue(owner, out var latest) && snapshot.ObservedAt <= latest) return;
            observed[owner] = snapshot.ObservedAt;
            var windows = snapshot.Limits?.Windows;
            var present = windows?.Select(window => window.Id).ToHashSet(StringComparer.Ordinal) ?? [];
            foreach (var key in samples.Keys.Where(key => key.Thread == thread && key.Account == snapshot.Account && !present.Contains(key.Window)).ToArray())
                samples.Remove(key);
            foreach (var window in windows ?? ValueList<Providers.PlanLimits.Window>.Empty)
            {
                var key = (thread, snapshot.Account, window.Id);
                if (!double.IsFinite(window.Percent) || window.Percent < 0 || window.Percent > 100 || window.ResetsAt <= snapshot.ObservedAt)
                {
                    samples.Remove(key);
                    continue;
                }
                double? rate = null;
                if (samples.TryGetValue(key, out var previous) && previous.Reset == window.ResetsAt &&
                    previous.Duration == window.DurationMinutes && string.Equals(previous.Plan, snapshot.Limits?.PlanName, StringComparison.Ordinal) &&
                    window.Percent >= previous.Percent)
                {
                    rate = (window.Percent - previous.Percent) / (snapshot.ObservedAt - previous.At).TotalMinutes;
                }
                samples[key] = new(snapshot.ObservedAt, window.Percent, window.ResetsAt, window.DurationMinutes, snapshot.Limits?.PlanName, rate);
            }
        }
    }

    public double? PercentPerMinute(Guid thread, UsageAccountKey account, string windowId)
    {
        lock (gate) return samples.TryGetValue((thread, account, windowId), out var sample) ? sample.Rate : null;
    }

    public void Forget(Guid thread)
    {
        lock (gate)
        {
            foreach (var key in samples.Keys.Where(key => key.Thread == thread).ToArray()) samples.Remove(key);
            foreach (var key in observed.Keys.Where(key => key.Thread == thread).ToArray()) observed.Remove(key);
        }
    }

    [GeneratedRegex(@"\b(?:5|10|20)x\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LargerPlan();

    // Owner's no-data defaults. Unknown plan uses the conservative 95; this creates no service windows.
    public static double DefaultHandshakePercent(string? planName) => planName is not null && LargerPlan().IsMatch(planName) ? 98 : 95;
}
