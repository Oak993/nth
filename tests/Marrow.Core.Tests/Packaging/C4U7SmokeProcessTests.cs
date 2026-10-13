using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

// Only disposable PowerShell sleepers are started. Never run an app, setup, updater or packager.
public sealed class C4U7SmokeProcessTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Smoke_deadline_throws_and_finally_ends_the_owned_sleeping_process_tree()
    {
        var result = C4SmokeTreeFixture.Run("sleep");
        Assert.Contains("timed out", result.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(result.GetProperty("parentAlive").GetBoolean());
        Assert.False(result.GetProperty("descendantAlive").GetBoolean());
        Assert.True(result.GetProperty("handleDisposed").GetBoolean());
        Assert.InRange(result.GetProperty("elapsedMs").GetInt64(), 1, 18000);
    }
}
