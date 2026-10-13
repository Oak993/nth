namespace Marrow.Core.Support;

/// <summary>PORT-ONLY (PLAN 3/5/8; ED-NEW-inst-c7-10): checks between filesystem calls; a single OS call cannot be preempted. Counts include enumerated entries, root/unit/container/marker admissions, and repeated preflight/deletion visits; ancestor metadata checks use the shared deadline and depth bound.</summary>
public sealed record SweepLimits(TimeSpan Duration, int MaxEntries, int MaxDepth)
{
    public static SweepLimits Normal { get; } = new(TimeSpan.FromSeconds(3), 2000, 64);
    public static SweepLimits Hook { get; } = new(TimeSpan.FromSeconds(1), 2000, 64);
    public static SweepLimits Release { get; } = new(TimeSpan.FromSeconds(1), 2000, 64);
    public static SweepLimits LastChance { get; } = new(TimeSpan.FromMilliseconds(500), 2000, 64);
}
