namespace Marrow.Core.Port.SelfUpdate;

/// <summary>Velopack reports a download as whole percent; the install pill takes a fraction (AppUpdater.swift:290-291's byte ratio).</summary>
public static class SelfUpdateProgress
{
    /// <summary>The fraction 0 to 1 for a percent; a value outside 0 to 100 is clamped.</summary>
    public static double Fraction(int percent) => Math.Clamp(percent, 0, 100) / 100.0;
}
