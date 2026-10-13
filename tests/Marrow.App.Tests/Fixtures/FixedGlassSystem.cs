using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>OD-0147: Windows' switches as a test sets them (all off unless named), in place of the machine's own (GlassLooks.System).</summary>
internal sealed record FixedGlassSystem(bool ReduceMotion = false, bool IncreaseContrast = false, bool ReduceTransparency = false) : IGlassSystem;
