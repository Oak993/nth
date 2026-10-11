using System.Collections;
using System.Reflection;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Models;
using Marrow.Services.Port.Advisor;

namespace Marrow.Services.Tests.Port.Advisor;

// PORT-ONLY: lean pass budget (ED-NEW-lean-18b): registrations the advisor bridge still holds after 100 advised
// sessions end. A session is its relay: it connects through the real pipe, sends its token and ends. No AdvisorSeams
// are set, so AdvisorHostedRouteTests' seams are never raced. Headless; a count, so Debug or Release do not matter.
[Trait("Decision", "ED-NEW-lean-18b")]
public sealed class AdvisorRegistrationBudgetTests
{
    private static readonly AdvisorRegistration Advised = new(Guid.Parse("7b1e4c2a-90d3-4f6e-8a15-2c4d6e8f0a1b"), ProviderKind.Codex, "sonnet", new AdvisorChoice(ProviderKind.Codex, "astra-6"));

    [Fact]
    public async Task Registrations_left_after_one_hundred_advised_sessions_end_stay_within_budget()
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
        var held = (IDictionary)typeof(AdvisorMcpBridge).GetField("registrations", Private)!.GetValue(null)!;
        var pipe = ((Lazy<string>)typeof(AdvisorMcpBridge).GetField("pipeName", Private)!.GetValue(null)!).Value;
        var tokens = new List<string>();
        for (var i = 0; i < 100; i++)
        {
            var token = Guid.NewGuid().ToString("N");
            held[token] = Advised;
            tokens.Add(token);
            Assert.Equal(0, await AdvisorMcpBridge.RunClient([AdvisorMcpBridge.Command, pipe, token], new MemoryStream(), Stream.Null));
        }
        // Measured on the unchanged base: 100 (nothing is ever dropped). Limit about 1.5x.
        Assert.InRange(tokens.Count(held.Contains), 0, 150);
    }
}
