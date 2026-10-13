using Marrow.App.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-10): the desktop as two settings a test flips: locked or not, and how long since the owner touched it.</summary>
public sealed class FakeDesktop : IDesktopWatch
{
    public bool IsLocked { get; set; }

    public TimeSpan IdleFor { get; set; } = TimeSpan.Zero;
}
