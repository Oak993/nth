using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4U7SmokeJobTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_successful_parent_exit_still_ends_its_owned_sleeping_descendant()
    {
        var result = C4SmokeTreeFixture.Run("0");
        Assert.Equal("", result.GetProperty("message").GetString());
        Assert.True(result.GetProperty("parentExitedInStart").GetBoolean());
        Assert.Equal(0, result.GetProperty("parentExitCode").GetInt32());
        Assert.False(result.GetProperty("descendantAlive").GetBoolean());
        Assert.True(result.GetProperty("handleDisposed").GetBoolean());
        Assert.InRange(result.GetProperty("elapsedMs").GetInt64(), 1, 18000);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_nonzero_parent_exit_still_ends_its_owned_sleeping_descendant()
    {
        var result = C4SmokeTreeFixture.Run("7");
        Assert.Contains("exited with code 7", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("parentExitedInStart").GetBoolean());
        Assert.Equal(7, result.GetProperty("parentExitCode").GetInt32());
        Assert.False(result.GetProperty("descendantAlive").GetBoolean());
        Assert.True(result.GetProperty("handleDisposed").GetBoolean());
        Assert.InRange(result.GetProperty("elapsedMs").GetInt64(), 1, 18000);
    }
}
