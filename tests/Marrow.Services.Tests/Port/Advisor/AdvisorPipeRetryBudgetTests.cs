using Marrow.Services.Port.Advisor;

namespace Marrow.Services.Tests.Port.Advisor;

// PORT-ONLY: lean pass budget (ED-NEW-lean-18b): how often the advisor pipe listens again in one simulated minute of a
// persistent pipe error. The base waited a flat 250 ms: 240 retries a minute, read from the code (the old loop had no
// seam to run it through). Pure schedule arithmetic, no clock; Debug or Release do not matter.
[Trait("Decision", "ED-NEW-lean-18b")]
public sealed class AdvisorPipeRetryBudgetTests
{
    [Fact]
    public void Pipe_retries_in_one_simulated_minute_of_a_persistent_error_stay_within_budget()
    {
        var waited = TimeSpan.Zero;
        var retries = 0;
        while (waited + AdvisorMcpBridge.RetryDelay(retries + 1) <= TimeSpan.FromMinutes(1)) waited += AdvisorMcpBridge.RetryDelay(++retries);
        // After: 18 (250 ms, 500 ms, 1 s, 2 s, then 4 s each). Limit about 1.3x.
        Assert.InRange(retries, 1, 23);
    }

    [Fact]
    public void The_first_retry_keeps_250_ms_and_no_wait_outlasts_the_relay_connect_window()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), AdvisorMcpBridge.RetryDelay(1));
        Assert.Equal(new[] { 250, 500, 1000, 2000, 4000, 4000 }, Enumerable.Range(1, 6).Select(n => (int)AdvisorMcpBridge.RetryDelay(n).TotalMilliseconds));
        Assert.True(AdvisorMcpBridge.RetryDelay(1000) < TimeSpan.FromSeconds(5));
    }
}
