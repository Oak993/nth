using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// OD-0116: only a request-body capability may authorize a no-turn standby.
[Trait("Rule", "R-TEST-4")]
public sealed class OpenCodeStandbyCapabilityReviewTests
{
    [Theory]
    [InlineData("{\"description\":\"The response contains \\\"noReply\\\"\",\"responses\":{}}")]
    [InlineData("{\"responses\":{\"200\":{\"content\":{\"application/json\":{\"schema\":{\"properties\":{\"noReply\":{\"type\":\"boolean\"}}}}}}}}")]
    [InlineData("{\"responses\":{\"200\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Result\"}}}}}}")]
    [InlineData("{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"properties\":{\"parts\":{\"items\":{\"properties\":{\"noReply\":{\"type\":\"boolean\"}}}}}}}}}}")]
    [InlineData("{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"properties\":{\"noReply\":{\"type\":\"string\"}}}}}}}")]
    public async Task A_response_or_nested_or_non_boolean_noReply_does_not_authorize_a_standby(string operation)
    {
        var document = "{\"paths\":{\"/session/{id}/message\":{\"post\":" + operation
            + "}},\"components\":{\"schemas\":{\"Result\":{\"properties\":{\"noReply\":{\"type\":\"boolean\"}}}}}}";
        var server = new FakeOpenCodeServer(document);
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => Task.FromResult<IOpenCodeServer>(server));
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Opencode,
            Executable = SwiftURL.Create("marrow-native-path:C:/offline/review-" + Guid.NewGuid().ToString("N") + "/opencode.exe")!,
            WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/offline")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        var session = new OpenCodeInjectionSession(configuration, _ => throw new InvalidOperationException("No ACP launch is allowed."));

        await Assert.ThrowsAsync<StandbyUnavailableException>(() => session.StartInjectedStandby(CancellationToken.None));

        Assert.Empty(server.Created);
        Assert.Empty(server.Injected);
        Assert.False(session.IsRunning);
        Assert.Equal(1, server.Stops);
    }
}
