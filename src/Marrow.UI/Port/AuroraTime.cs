// PORT-ONLY: the locked D Aurora module's t object (OD-0112).
namespace Marrow.UI.Port;

/// <param name="LaunchProgress">Linear launch progress, 0..1.</param>
/// <param name="IdleSeconds">Seconds since idle began, as in ICON-MODULE.md.</param>
/// <param name="IsStill">The module's convenience flag; D itself does not inspect it.</param>
public readonly record struct AuroraTime(double LaunchProgress, double IdleSeconds, bool IsStill = false)
{
    public static AuroraTime Still => new(1, 0, true);
    public static AuroraTime Launch(double seconds) => new(Math.Clamp(seconds * 1000 / AuroraMark.LaunchDurationMs, 0, 1), 0);
    public static AuroraTime Idle(double seconds) => new(1, seconds);
}
