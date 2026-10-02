using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Testing;

// PORT-ONLY test fixture, no Swift twin: what MCPHubDeadlineTests run MCPHub in. A TestRuntime (manual clock), a
// scripted stdio server (mcp-deadline-fixture.ps1) whose answers the test releases through files in a scratch
// folder, and the waits that tie the real process to the manual clock: the hub's per-request deadline is a timer
// on SwiftClock (MCPWire.swift:170-186), so a test knows a request is "in flight" once the server has read it AND
// the timer is armed, and then moves the clock to just before / just after the deadline.
namespace Marrow.Services.Tests.Fixtures;

internal sealed class HubDeadlineRig : IDisposable
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly string gateDir = Directory.CreateTempSubdirectory("marrow-hub-deadline-").FullName;

    public HubDeadlineRig(int pages = 1)
    {
        Pages = pages;
        Runtime = new TestRuntime();
        Hub = new MCPHub();
    }

    public TestRuntime Runtime { get; }
    public MCPHub Hub { get; }
    public int Pages { get; }

    private static string FixtureScript([CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "mcp-deadline-fixture.ps1");

    public MCPServerSpec.Stdio Spec() => new("powershell",
        new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", FixtureScript()]),
        new ValueMap<string, string>(new Dictionary<string, string>
        {
            ["MCP_GATE_DIR"] = gateDir,
            ["MCP_PAGES"] = Pages.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, SwiftStringComparer.Instance));

    /// <summary>Starts the server and finishes initialize with one immediate call, so the timers it used are gone.</summary>
    public async Task WarmUp(string server)
    {
        await Hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance) { [server] = Spec() });
        var warm = await Hub.Call($"mcp__{server}__quick", new JSONValue.Null()).WaitAsync(Bound);
        Assert.Equal("quick", warm.Text);
    }

    /// <summary>Timers armed on the manual clock right now.</summary>
    public int ArmedTimers => Runtime.Time.ActiveTimers;

    public void Release(string name) => File.WriteAllBytes(Path.Combine(gateDir, name), []);

    /// <summary>Waits until the server has read the request that writes this signal file.</summary>
    public async Task WaitSeen(string name)
    {
        var path = Path.Combine(gateDir, name);
        var deadline = DateTime.UtcNow + Bound;
        while (!File.Exists(path))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"The server never signalled {name}.");
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Waits until the in-flight request's deadline timer is armed: exactly one more than <paramref name="baseline"/>.
    /// A short real pause first lets the previous request's timer be released, so a leftover is not mistaken for it.
    /// </summary>
    public async Task WaitArmed(int baseline)
    {
        await Task.Delay(150);
        var deadline = DateTime.UtcNow + Bound;
        while (ArmedTimers != baseline + 1)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Armed timers stayed at {ArmedTimers}, wanted {baseline + 1}.");
            await Task.Delay(10);
        }
    }

    /// <summary>Moves the manual clock and lets what that fired run.</summary>
    public void Advance(double seconds) => Runtime.Advance(TimeSpan.FromSeconds(seconds));

    /// <summary>The caller still waits after a real pause: nothing ended the request.</summary>
    public static async Task AssertStillWaiting(Task task)
    {
        await Task.Delay(300);
        Assert.False(task.IsCompleted, "the request ended before its deadline");
    }

    /// <summary>
    /// The same for a request whose deadline is a timer on the manual clock: the timer must still be armed. A request
    /// that failed at its deadline can take longer than the pause above to finish (a failed listing stops its
    /// server first), so the unfired timer is the direct sign that the deadline has not come yet.
    /// </summary>
    public async Task AssertStillWaiting(Task task, int baseline)
    {
        await AssertStillWaiting(task);
        Assert.Equal(baseline + 1, ArmedTimers);
    }

    public void Dispose()
    {
        // Let the server run out of every gate and quit, then end the client (its stop ladder needs no clock now).
        File.WriteAllBytes(Path.Combine(gateDir, "quit"), []);
        try { Hub.Reload().GetAwaiter().GetResult(); } catch { }
        Runtime.Dispose();
        try { Directory.Delete(gateDir, true); } catch { }
    }
}
