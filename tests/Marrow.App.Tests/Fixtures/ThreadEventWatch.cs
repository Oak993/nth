namespace Marrow.App.Tests.Fixtures;

// Helper for AppModelLeanThreadEventTests.cs (R-TEST-2: a test class holds no nested type). Not port code.
/// <summary>The observation keys that fired since the last Observe.</summary>
public sealed class ThreadEventWatch
{
    public readonly HashSet<string> Fired = [];
}
