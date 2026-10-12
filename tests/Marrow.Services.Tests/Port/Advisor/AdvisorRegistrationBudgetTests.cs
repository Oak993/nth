using Marrow.Core.Port.Advisor;
using Marrow.Core.Models;
using Marrow.Services.Port.Advisor;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Advisor;

// PORT-ONLY: lean pass budget (ED-NEW-lean-18b): registrations the advisor bridge still holds after 100 advised
// sessions end. A session is its relay: it connects through the real pipe, sends its token and ends. No AdvisorSeams
// are set, so AdvisorHostedRouteTests' seams are never raced. The grace runs on a ManualTimeProvider; the 10 s waits
// only bound the in-process pipe. A count, so Debug or Release do not matter. The guard beside it: a relay that
// reconnects with its token inside the grace keeps its registration. One class, because both set the bridge's clock.
[Trait("Decision", "ED-NEW-lean-18b")]
public sealed class AdvisorRegistrationBudgetTests
{
    private static readonly AdvisorRegistration Advised = new(Guid.Parse("7b1e4c2a-90d3-4f6e-8a15-2c4d6e8f0a1b"), ProviderKind.Codex, "sonnet", new AdvisorChoice(ProviderKind.Codex, "astra-6"));

    private static readonly AdvisorRegistration Reconnecting = new(Guid.Parse("7b1e4c2a-90d3-4f6e-8a15-2c4d6e8f0a1c"), ProviderKind.Claude, "sonnet", new AdvisorChoice(ProviderKind.Claude, "opus"));

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
            for (var i = 0; i < 100; i++)
            {
                string[] session = [.. AdvisorMcpBridge.Register(Advised).Args];
                lock (tokens) tokens.Add(session[2]);
                Assert.Equal(0, await AdvisorMcpBridge.RunClient(session, new MemoryStream(), Stream.Null));
            }
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
    public async Task A_relay_reconnecting_within_the_grace_keeps_its_registration()
    {
        var clock = new ManualTimeProvider();
        var previous = AdvisorMcpBridge.Clock;
        string[] session = [.. AdvisorMcpBridge.Register(Reconnecting).Args];
        using var ended = new SemaphoreSlim(0);
        void Ended(string token)
        {
            if (token == session[2]) ended.Release();
        }
        AdvisorMcpBridge.Clock = clock;
        AdvisorMcpBridge.RelayEnded += Ended;
        try
        {
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
