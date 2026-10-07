using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// OD-0116, OD-0122, ED-439, research section 3: OpenCode stores the context with a `noReply` message on its own server when the running server's OpenAPI offers it;
// the conversation's ACP session loads that stored session at the handover. Anything missing falls back. Offline: a fake server and a fake ACP session.
[Trait("Rule", "R-TEST-4")]
public sealed class OpenCodeStandbyTests
{
    [Theory]
    [InlineData(FakeOpenCodeServer.WithNoReply, true)]
    [InlineData(FakeOpenCodeServer.ViaReference, true)]
    [InlineData(FakeOpenCodeServer.WithoutNoReply, false)]
    [InlineData("{\"paths\":{}}", false)]
    [InlineData("not json", false)]
    [InlineData("", false)]
    public void The_capability_check_needs_noReply_on_the_session_message_operation(string document, bool expected) =>
        Assert.Equal(expected, OpenCodeHistoryInjection.OpenApiSupportsNoReply(document));

    private static SessionConfiguration Config(string label) => new()
    {
        Provider = ProviderKind.Opencode,
        Executable = SwiftURL.Create("marrow-native-path:C:/offline/" + label + "/opencode.exe")!,
        WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/offline/" + label)!,
        Environment = ValueMap<string, string>.Empty, Model = "offline-model", RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };

    [Fact]
    public async Task The_standby_stores_the_context_on_the_server_and_the_handover_loads_it_into_the_ACP_session()
    {
        var server = new FakeOpenCodeServer(FakeOpenCodeServer.WithNoReply);
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => Task.FromResult<IOpenCodeServer>(server));
        var inner = new HandoffSession { SessionId = "ses_standby" };
        SessionConfiguration? opened = null;
        var session = new OpenCodeInjectionSession(Config("happy"), configuration => { opened = configuration; return inner; });
        Assert.Equal("ses_standby", await session.StartInjectedStandby(CancellationToken.None));
        Assert.True(session.IsRunning);
        await session.InjectStandbyHistory("recorded context", CancellationToken.None);
        Assert.Equal(["recorded context"], server.Injected);
        Assert.Equal(["doc", "create", "inject", "read"], server.Calls);
        await Assert.ThrowsAsync<ProviderError>(() => session.Send(new() { Text = "too early", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build }));
        Assert.Null(opened);

        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        await session.EndStandbyAsync(CancellationToken.None);
        Assert.Equal("ses_standby", opened!.ResumeID);
        Assert.Equal(1, server.Stops);
        Assert.Empty(server.Deleted);
        Assert.True(session.IsRunning);
        inner.OnEvent!(new ProviderEvent.MessageDelta("m", "forwarded"));
        Assert.Single(events);
        await session.Send(new() { Text = "the user's message", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        Assert.Equal("the user's message", Assert.Single(inner.Inputs).Text);
    }

    [Fact]
    public async Task A_server_without_noReply_has_no_standby_is_stopped_and_is_remembered()
    {
        var server = new FakeOpenCodeServer(FakeOpenCodeServer.WithoutNoReply);
        var launches = 0;
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => { launches++; return Task.FromResult<IOpenCodeServer>(server); });
        var configuration = Config("no-reply");
        var session = new OpenCodeInjectionSession(configuration, _ => throw new InvalidOperationException("no ACP session"));
        await Assert.ThrowsAsync<StandbyUnavailableException>(() => session.StartInjectedStandby(CancellationToken.None));
        Assert.Equal(1, server.Stops);
        Assert.Empty(server.Created);
        Assert.False(await new OpenCodeInjectionSession(configuration, _ => throw new InvalidOperationException()).SupportsStandbyInjection(CancellationToken.None));
        await Assert.ThrowsAsync<StandbyUnavailableException>(() => new OpenCodeInjectionSession(configuration, _ => throw new InvalidOperationException()).StartInjectedStandby(CancellationToken.None));
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task The_run_time_check_starts_a_server_once_per_executable_and_stops_it()
    {
        var server = new FakeOpenCodeServer(FakeOpenCodeServer.WithNoReply);
        var launches = 0;
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => { launches++; return Task.FromResult<IOpenCodeServer>(server); });
        var configuration = Config("support");
        Assert.True(await new OpenCodeInjectionSession(configuration, _ => throw new InvalidOperationException()).SupportsStandbyInjection(CancellationToken.None));
        Assert.True(await new OpenCodeInjectionSession(configuration, _ => throw new InvalidOperationException()).SupportsStandbyInjection(CancellationToken.None));
        Assert.Equal(1, launches);
        Assert.Equal(1, server.Stops);
        Assert.Empty(server.Created);
    }

    [Fact]
    public async Task A_server_that_cannot_start_means_no_standby_and_no_exception_from_the_check()
    {
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => throw new IOException("opencode would not start"));
        var session = new OpenCodeInjectionSession(Config("broken"), _ => throw new InvalidOperationException());
        Assert.False(await session.SupportsStandbyInjection(CancellationToken.None));
        await Assert.ThrowsAsync<StandbyUnavailableException>(() => session.StartInjectedStandby(CancellationToken.None));
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("not-stored")]
    public async Task A_message_that_is_not_proven_stored_deletes_the_session_and_fails_the_standby(string failure)
    {
        var server = new FakeOpenCodeServer(FakeOpenCodeServer.WithNoReply) { InjectFails = failure == "rejected", StoresNothing = failure == "not-stored" };
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => Task.FromResult<IOpenCodeServer>(server));
        var session = new OpenCodeInjectionSession(Config("unproven-" + failure), _ => throw new InvalidOperationException());
        await session.StartInjectedStandby(CancellationToken.None);
        await Assert.ThrowsAsync<StandbyUnavailableException>(() => session.InjectStandbyHistory("recorded context", CancellationToken.None));
        Assert.Equal(["ses_standby"], server.Deleted);
        Assert.Equal(1, server.Stops);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task A_handover_whose_ACP_session_loads_something_else_or_fails_stops_it_and_throws()
    {
        var server = new FakeOpenCodeServer(FakeOpenCodeServer.WithNoReply);
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => Task.FromResult<IOpenCodeServer>(server));
        var other = new HandoffSession { SessionId = "ses_other" };
        var session = new OpenCodeInjectionSession(Config("handover-other"), _ => other);
        await session.StartInjectedStandby(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.EndStandbyAsync(CancellationToken.None));
        Assert.True(other.Stopped);
        Assert.False(session.IsRunning);

        var broken = new HandoffSession { FailStart = true };
        var failing = new OpenCodeInjectionSession(Config("handover-fails"), _ => broken);
        await failing.StartInjectedStandby(CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => failing.EndStandbyAsync(CancellationToken.None));
        Assert.True(broken.Stopped);
    }

    [Fact]
    public async Task Cancelling_deletes_the_stored_session_stops_the_server_and_opens_no_ACP_session()
    {
        var server = new FakeOpenCodeServer(FakeOpenCodeServer.WithNoReply);
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => Task.FromResult<IOpenCodeServer>(server));
        var session = new OpenCodeInjectionSession(Config("cancel"), _ => throw new InvalidOperationException("no ACP session"));
        await session.StartInjectedStandby(CancellationToken.None);
        await session.InjectStandbyHistory("recorded context", CancellationToken.None);
        await session.DiscardStandby();
        Assert.Equal(["ses_standby"], server.Deleted);
        Assert.Equal(1, server.Stops);
        Assert.False(session.IsRunning);
    }
}
