using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// PORT-ONLY: security gate S7 (ED-NEW-secfinal-3). Only nth-dextra's own call, its exact server name and a tool of its catalog,
// is exempt from taint; a server that only looks like it (another case, a longer name, a tool nth-dextra does not have) taints.
[Trait("Decision", "ED-NEW-secfinal-3")]
public sealed class SecFinalTaintIdentityTests
{
    private static Taint Of(string title) =>
        TaintWiring.Of(new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = title }, InjectionSelfCheck.Classifier, ProviderKind.Claude);

    [Fact]
    public void Nth_dextras_own_tool_stays_clean()
    {
        Assert.False(Of(AssistantNames.Server + " \u00B7 recall").IsTainted);
    }

    [Theory]
    [InlineData("NTH-DEXTRA \u00B7 recall")]
    [InlineData("Nth-Dextra \u00B7 recall")]
    [InlineData("nth-dextra \u00B7 delete_files")]
    [InlineData("nth-dextra \u00B7 ")]
    public void A_look_alike_of_nth_dextra_taints(string title)
    {
        var taint = Of(title);
        Assert.True(taint.IsTainted);
        Assert.Equal(ContentOrigin.Connector, taint.Sources.Single().Origin);
    }
}
