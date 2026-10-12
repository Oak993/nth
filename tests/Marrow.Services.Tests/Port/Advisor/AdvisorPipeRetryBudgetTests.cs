using Marrow.Services.Port.Advisor;

namespace Marrow.Services.Tests.Port.Advisor;

// PORT-ONLY: lean pass budget (ED-NEW-lean-18b): how often the advisor pipe listens again in one simulated minute of a
// persistent pipe error. Runs the bridge's real serve loop (AdvisorMcpBridge.Serve) through its seam: a scripted listen
// that fails or connects, and a simulated wait that adds up time instead of sleeping. No pipe, no clock; Debug or
// Release do not matter.
[Trait("Decision", "ED-NEW-lean-18b")]
public sealed class AdvisorPipeRetryBudgetTests
{
    /// <summary>Drives the serve loop over a script (true: a relay connects, false: the pipe fails) and records each wait.</summary>
    private static async Task<(List<TimeSpan> Waits, int Served)> Run(IEnumerable<bool> script, TimeSpan? budget = null)
    {
        using var stop = new CancellationTokenSource();
        using var steps = script.GetEnumerator();
        var waits = new List<TimeSpan>();
        var served = 0;
        var waited = TimeSpan.Zero;
        Task<int> Listen()
        {
            if (!steps.MoveNext()) { stop.Cancel(); return Task.FromException<int>(new ObjectDisposedException("pipe")); }
            return steps.Current ? Task.FromResult(1) : Task.FromException<int>(new IOException("pipe broke"));
        }
        Task Wait(TimeSpan delay)
        {
            // The script's end stops the loop with one last error; its wait is not part of the script.
            if (stop.IsCancellationRequested) return Task.CompletedTask;
            if (budget is { } limit && waited + delay > limit) { stop.Cancel(); return Task.CompletedTask; }
            waited += delay;
            waits.Add(delay);
            return Task.CompletedTask;
        }
        await AdvisorMcpBridge.Serve(Listen, _ => served++, Wait, stop.Token).WaitAsync(TimeSpan.FromSeconds(10));
        return (waits, served);
    }

    [Fact]
    public async Task Pipe_retries_in_one_simulated_minute_of_a_persistent_error_stay_within_budget()
    {
        var (waits, _) = await Run(Enumerable.Repeat(false, 100_000), TimeSpan.FromMinutes(1));
        // Measured through this seam on the base's flat 250 ms wait: 240 (limit about 1.5x: 360). After: 18 (250 ms,
        // 500 ms, 1 s, 2 s, then 4 s each). Limit about 1.3x.
        Assert.InRange(waits.Count, 1, 23);
    }

    [Fact]
    public async Task The_serve_loop_backs_off_on_errors_in_a_row_and_a_connection_resets_it()
    {
        var (waits, served) = await Run([false, false, false, false, false, false, true, false, false, true, true, false]);
        Assert.Equal(new[] { 250, 500, 1000, 2000, 4000, 4000, 250, 500, 250 }, waits.Select(w => (int)w.TotalMilliseconds));
        Assert.Equal(3, served);
    }

    [Fact]
    public void The_first_retry_keeps_250_ms_and_no_wait_outlasts_the_relay_connect_window()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), AdvisorMcpBridge.RetryDelay(1));
        Assert.Equal(new[] { 250, 500, 1000, 2000, 4000, 4000 }, Enumerable.Range(1, 6).Select(n => (int)AdvisorMcpBridge.RetryDelay(n).TotalMilliseconds));
        Assert.True(AdvisorMcpBridge.RetryDelay(1000) < TimeSpan.FromSeconds(5));
    }
}
