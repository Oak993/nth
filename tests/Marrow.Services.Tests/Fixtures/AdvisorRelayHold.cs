using Marrow.Core.Models;
using Marrow.Core.Port.Advisor;

namespace Marrow.Services.Tests.Fixtures;

// Helper for Port/Advisor/AdvisorRegistrationBudgetTests.cs (R-TEST-2: a test class holds no nested type). Not port code.
/// <summary>A session's hold on its relay entry, as ACPSession's and CopilotSession's advisorServer field.</summary>
public sealed class AdvisorRelayHold
{
    public MCPResolvedServer? Server;
}
