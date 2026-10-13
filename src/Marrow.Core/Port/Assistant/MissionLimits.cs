// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Concurrency", 15.4 "Helpers for a head"; unit AS-5; ED-NEW-dextra-as5-2).
// No Swift twin. The limits a mission runs within: how deep helpers may nest, how many local agents run at once, and the
// free-memory gate a new local agent waits behind. Pure: the caller reads the memory and counts the agents.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>Where an agent runs: a CLI process on this PC (counted and gated by memory), or an API or cloud provider.</summary>
public enum AgentPlace { Local, Api }

/// <summary>The machine's memory as read once: total and available physical bytes.</summary>
public sealed record MemoryReading(ulong TotalBytes, ulong AvailableBytes)
{
    public double FreeFraction => TotalBytes == 0 ? 0 : (double)AvailableBytes / TotalBytes;
}

/// <summary>Whether a new agent may start now, and if not, the line the board shows while it waits.</summary>
public sealed record AgentAdmission(bool Admitted, string? Waiting)
{
    public static AgentAdmission Go { get; } = new(true, null);

    public static AgentAdmission Wait(string why) => new(false, why);
}

/// <summary>The owner's limits (13.2). Out-of-range values are clamped, never trusted.</summary>
public sealed record MissionLimits
{
    /// <summary>A mission's own lead and heads: two levels.</summary>
    public const int TopLevel = 2;

    public const int DefaultDepth = 3;

    /// <summary>"The setting goes up to 4."</summary>
    public const int DepthCeiling = 4;

    public const int DefaultLocalAgents = 16;

    /// <summary>"No new local agent below 15% free RAM" (owner, 2026-10-07).</summary>
    public const double DefaultFreeRamFloor = 0.15;

    public int Depth { get; init; } = DefaultDepth;

    public int LocalAgents { get; init; } = DefaultLocalAgents;

    public double FreeRamFloor { get; init; } = DefaultFreeRamFloor;

    public static MissionLimits Default { get; } = new();

    /// <summary>The depth in force: at least the mission's own two levels, at most 4.</summary>
    public int EffectiveDepth => Math.Clamp(Depth, TopLevel, DepthCeiling);

    /// <summary>The local agent cap in force: 1 to 16 (the owner can lower it, not raise it past the default).</summary>
    public int EffectiveLocalAgents => Math.Clamp(LocalAgents, 1, DefaultLocalAgents);

    /// <summary>The memory floor in force: never below the owner's 15%, never above 90%.</summary>
    public double EffectiveFreeRamFloor => double.IsNaN(FreeRamFloor) ? DefaultFreeRamFloor : Math.Clamp(FreeRamFloor, DefaultFreeRamFloor, 0.9);
}

/// <summary>The gates of 13.2.</summary>
public static class MissionGate
{
    /// <summary>The level a child mission (a head's helpers) runs at: one below the mission that asked.</summary>
    public static int ChildLevel(int parentLevel) => parentLevel + 1;

    /// <summary>
    /// Null when a mission at <paramref name="level"/> fits the owner's depth; else the card's line. A level past the ceiling
    /// of 4 is never allowed, card or not (<see cref="BeyondCeiling"/>).
    /// </summary>
    public static string? OverDepth(int level, MissionLimits limits) =>
        level <= limits.EffectiveDepth
            ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"Helpers here would run {level} levels deep; your limit is {limits.EffectiveDepth}.");

    public static bool BeyondCeiling(int level) => level > MissionLimits.DepthCeiling;

    /// <summary>
    /// Whether a new agent may start. API agents are limited by the budget, not a count. A local agent waits while the
    /// cap is reached or free memory is under the floor; memory that cannot be read counts as under it (fail closed).
    /// </summary>
    public static AgentAdmission Admit(AgentPlace place, int localAgentsRunning, MemoryReading? memory, MissionLimits limits)
    {
        if (place == AgentPlace.Api) return AgentAdmission.Go;
        var cap = limits.EffectiveLocalAgents;
        if (localAgentsRunning >= cap)
            return AgentAdmission.Wait(string.Create(CultureInfo.InvariantCulture,
                $"{cap} local agents are running; this one starts when one ends."));
        var floor = limits.EffectiveFreeRamFloor;
        if (memory is null || memory.TotalBytes == 0)
            return AgentAdmission.Wait("Free memory could not be read; this agent starts once it can be.");
        if (memory.FreeFraction < floor)
            return AgentAdmission.Wait(string.Create(CultureInfo.InvariantCulture,
                $"Free memory is under {floor * 100:0}% ({memory.FreeFraction * 100:0}% free); this agent starts when more is free."));
        return AgentAdmission.Go;
    }
}
