// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.5; unit AS-13; ED-NEW-dextra-as13-5). No Swift twin.
// The two allowances the check adds to the formula of 11.5 so that a floor holds in the simulation's world: a whole-point reading
// can hide a point, and a turn can cost more than its p90. Each is a plain number the tests set to zero to read the formula alone.
namespace Marrow.Core.Port.Assistant;

public sealed record ReserveSafety
{
    /// <summary>Added to the projection when the last reading is a whole number: such a reading can be a point under the truth.</summary>
    public double QuantumAllowance { get; init; } = 1.0;

    /// <summary>The new turn is counted at this many times its p90: the p90 is the 90th percentile, and the tail above it is real.</summary>
    public double NewTurnTail { get; init; } = 1.5;

    /// <summary>The engine as it runs.</summary>
    public static ReserveSafety Production { get; } = new();

    /// <summary>The bare formula of 11.5 and nothing more, for the tests that read it.</summary>
    public static ReserveSafety Formula { get; } = new() { QuantumAllowance = 0, NewTurnTail = 1.0 };
}
