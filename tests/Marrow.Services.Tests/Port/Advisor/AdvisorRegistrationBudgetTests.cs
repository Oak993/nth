using System.Runtime.CompilerServices;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Models;
using Marrow.Services.Port.Advisor;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Advisor;

// PORT-ONLY: lean pass budget (ED-NEW-lean-18b): registrations the advisor bridge still holds after 100 advised
// sessions end. A session is its relay: it connects through the real pipe, sends its token and ends. No AdvisorSeams
// are set, so AdvisorHostedRouteTests' seams are never raced. The grace runs on a ManualTimeProvider; the 10 s waits
// only bound the in-process pipe. A count, so Debug or Release do not matter. The session's hold on its relay entry
// (ACPSession and CopilotSession keep theirs in a field for every session they open; Claude keeps its launch's) is an
// object a NoInlining helper creates, so no Debug stack slot keeps it alive; dropping it and collecting is the session
// ending. The guards beside the budgets: a relay reconnecting inside the grace, and a held entry reused long after its
// first relay ended, keep their registration. One class, because all of them set the bridge's clock.
[Trait("Decision", "ED-NEW-lean-18b")]
public sealed class AdvisorRegistrationBudgetTests
{
    private static readonly AdvisorRegistration Advised = new(Guid.Parse("7b1e4c2a-90d3-4f6e-8a15-2c4d6e8f0a1b"), ProviderKind.Codex, "sonnet", new AdvisorChoice(ProviderKind.Codex, "astra-6"));

    private static readonly AdvisorRegistration Reconnecting = new(Guid.Parse("7b1e4c2a-90d3-4f6e-8a15-2c4d6e8f0a1c"), ProviderKind.Claude, "sonnet", new AdvisorChoice(ProviderKind.Claude, "opus"));

    private static readonly AdvisorRegistration Reused = new(Guid.Parse("7b1e4c2a-90d3-4f6e-8a15-2c4d6e8f0a1d"), ProviderKind.Copilot, "sonnet", new AdvisorChoice(ProviderKind.Claude, "opus"));

    /// <summary>A session's hold on its relay entry, as ACPSession's and CopilotSession's advisorServer field.</summary>
    private sealed class Session
    {
        public MCPResolvedServer? Server;
    }

    /// <summary>Registers and keeps nothing of the entry but its arguments (Codex: the entry only rides into one launch).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] Launch(AdvisorRegistration registration) => [.. AdvisorMcpBridge.Register(registration).Args];

    /// <summary>Registers and keeps the entry for the session's life.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Session Open(AdvisorRegistration registration) => new() { Server = AdvisorMcpBridge.Register(registration) };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] Arguments(Session session) => [.. session.Server!.Args];

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public async Task Registrations_left_after_one_hundred_advised_sessions_end_stay_within_budget()
    {
        var clock = new ManualTimeProvider();
        var previous = AdvisorMcpBridge.Clock;
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        void Ended(string token)
        {
            lock (tokens) if (tokens.Contains(token) && ++count == 100) ended.TrySetResult();
        }
        AdvisorMcpBridge.Clock = clock;
        AdvisorMcpBridge.RelayEnded += Ended;
        try
        {
            var sessions = new List<string[]>();
            for (var i = 0; i < 100; i++)
            {
                string[] session = Launch(Advised);
                sessions.Add(session);
                lock (tokens) tokens.Add(session[2]);
            }
            // Nothing holds the entries any more, as after a Codex launch: each token still waits for its relay.
            Collect();
            Assert.All(tokens, token => Assert.True(AdvisorMcpBridge.Holds(token)));
            foreach (var session in sessions) Assert.Equal(0, await AdvisorMcpBridge.RunClient(session, new MemoryStream(), Stream.Null));
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(tokens, token => Assert.True(AdvisorMcpBridge.Holds(token)));
            clock.Advance(AdvisorMcpBridge.Grace);
            // Measured on the unchanged base: 100 (nothing was ever dropped; the limit was 150). After: 0, now the limit.
            Assert.Equal(0, tokens.Count(AdvisorMcpBridge.Holds));
        }
        finally
        {
            AdvisorMcpBridge.RelayEnded -= Ended;
            AdvisorMcpBridge.Clock = previous;
        }
    }

    [Fact]
    public void Registrations_left_after_one_hundred_launches_whose_relay_never_connects_stay_within_budget()
    {
        var clock = new ManualTimeProvider();
        var previous = AdvisorMcpBridge.Clock;
        AdvisorMcpBridge.Clock = clock;
        try
        {
            // A CLI that failed to start, or a relaunch that never reached its relay: the token is registered and its
            // entry dropped, and no relay ever connects with it.
            var tokens = Enumerable.Range(0, 100).Select(_ => Launch(Advised)[2]).ToList();
            Collect();
            clock.Advance(TimeSpan.FromMinutes(10));
            // Measured on the first L18b commit (41475c14a): 100 (a token no relay reached was kept forever; limit about
            // 1.5x: 150). After: 0 once the unclaimed window has passed, now the limit.
            Assert.Equal(0, tokens.Count(AdvisorMcpBridge.Holds));
        }
        finally { AdvisorMcpBridge.Clock = previous; }
    }

    [Fact]
    public async Task A_held_entry_keeps_its_registration_for_a_new_relay_long_after_the_first_ended()
    {
        var clock = new ManualTimeProvider();
        var previous = AdvisorMcpBridge.Clock;
        AdvisorMcpBridge.Clock = clock;
        var copilot = Open(Reused);
        string[] session = Arguments(copilot);
        using var ended = new SemaphoreSlim(0);
        void Ended(string token)
        {
            if (token == session[2]) ended.Release();
        }
        AdvisorMcpBridge.RelayEnded += Ended;
        try
        {
            // The first MCP session ends; the chat opens a new one (a reset, a resume fallback) minutes later with the
            // same cached entry and so the same token.
            Assert.Equal(0, await AdvisorMcpBridge.RunClient(session, new MemoryStream(), Stream.Null));
            Assert.True(await ended.WaitAsync(TimeSpan.FromSeconds(10)));
            Collect();
            clock.Advance(AdvisorMcpBridge.Grace + TimeSpan.FromMinutes(10));
            Assert.Equal(Reused, AdvisorMcpBridge.Registration(session[2]));

            Assert.Equal(0, await AdvisorMcpBridge.RunClient(session, new MemoryStream(), Stream.Null));
            Assert.True(await ended.WaitAsync(TimeSpan.FromSeconds(10)));
            clock.Advance(AdvisorMcpBridge.Grace + TimeSpan.FromMinutes(10));
            Assert.Equal(Reused, AdvisorMcpBridge.Registration(session[2]));

            // The session ends: its entry is gone, and with no relay connected the registration goes with it.
            copilot.Server = null;
            Collect();
            clock.Advance(AdvisorMcpBridge.Grace);
            Assert.False(AdvisorMcpBridge.Holds(session[2]));
            Assert.Null(AdvisorMcpBridge.Registration(session[2]));
        }
        finally
        {
            AdvisorMcpBridge.RelayEnded -= Ended;
            AdvisorMcpBridge.Clock = previous;
        }
    }

    [Fact]
    public async Task A_relay_reconnecting_within_the_grace_keeps_its_registration()
    {
        var clock = new ManualTimeProvider();
        var previous = AdvisorMcpBridge.Clock;
        AdvisorMcpBridge.Clock = clock;
        string[] session = Launch(Reconnecting);
        using var ended = new SemaphoreSlim(0);
        void Ended(string token)
        {
            if (token == session[2]) ended.Release();
        }
        AdvisorMcpBridge.RelayEnded += Ended;
        try
        {
            Collect();
            Assert.Equal(0, await AdvisorMcpBridge.RunClient(session, new MemoryStream(), Stream.Null));
            Assert.True(await ended.WaitAsync(TimeSpan.FromSeconds(10)));
            clock.Advance(AdvisorMcpBridge.Grace - TimeSpan.FromTicks(1));
            Assert.Equal(Reconnecting, AdvisorMcpBridge.Registration(session[2]));

            Assert.Equal(0, await AdvisorMcpBridge.RunClient(session, new MemoryStream(), Stream.Null));
            Assert.True(await ended.WaitAsync(TimeSpan.FromSeconds(10)));
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.Equal(Reconnecting, AdvisorMcpBridge.Registration(session[2]));

            clock.Advance(AdvisorMcpBridge.Grace);
            Assert.False(AdvisorMcpBridge.Holds(session[2]));
            Assert.Null(AdvisorMcpBridge.Registration(session[2]));
        }
        finally
        {
            AdvisorMcpBridge.RelayEnded -= Ended;
            AdvisorMcpBridge.Clock = previous;
        }
    }
}
