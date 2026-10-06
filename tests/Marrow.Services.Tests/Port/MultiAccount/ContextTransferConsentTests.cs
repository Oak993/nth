using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class ContextTransferConsentTests
{
    [Theory]
    [InlineData(ProviderKind.Deepseek, false)]
    [InlineData(ProviderKind.Deepseek, true)]
    [InlineData(ProviderKind.Local, false)]
    [InlineData(ProviderKind.Local, true)]
    public void A_revoked_private_transfer_is_rejected_before_send_and_after_async_payload_encoding(ProviderKind provider, bool duringPayload)
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var storage = new StorageScratchFolder();
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200,
            "data: {\"choices\":[{\"delta\":{\"content\":\"Ack\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream") };
        using var transport = URLSession.Transport.Override(http);
        var allowed = true;
        var configuration = new SessionConfiguration { Provider = provider, WorkingDirectory = SwiftURL.Create(folder.Path)!,
            Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build, ApiKey = "offline" };
        var session = new ConsentBoundarySession(configuration, () => { if (duringPayload) allowed = false; });
        runtime.Loop.Run(async () =>
        {
            await using var standby = await HandshakeStandbySession.StartAsync(session, configuration, CancellationToken.None, () => allowed);
            if (!duringPayload) allowed = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() => standby.ReceiveAsync(new(Bring.MarrowSummary, new("PRIVATE-CONTEXT", null, []), null), true, CancellationToken.None));
        });
        Assert.Empty(http.Received);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Adoption_releases_the_old_sources_transfer_guard_before_the_next_normal_request()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var storage = new StorageScratchFolder();
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200,
            "data: {\"choices\":[{\"delta\":{\"content\":\"Ack\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream") };
        using var transport = URLSession.Transport.Override(http);
        var allowed = true;
        var configuration = new SessionConfiguration { Provider = ProviderKind.Deepseek, WorkingDirectory = SwiftURL.Create(folder.Path)!,
            Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build, ApiKey = "offline" };
        var session = new ConsentBoundarySession(configuration, () => { });
        runtime.Loop.Run(async () =>
        {
            await using var standby = await HandshakeStandbySession.StartAsync(session, configuration, CancellationToken.None, () => allowed);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, new("PRIVATE-CONTEXT", null, []), null), true, CancellationToken.None);
            Assert.Same(session, standby.TakeSession());
            allowed = false;
            await session.Send(new() { Text = "The next actual message.", Images = ValueList<Attachment>.Empty,
                RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build });
        });
        Assert.Equal(2, http.Received.Count);
        session.Stop();
    }
}
