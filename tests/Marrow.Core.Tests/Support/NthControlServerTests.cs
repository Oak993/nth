using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The listening side of nth's
// control pipe, on a real named pipe with a scripted host: one request and one reply per connection, a request that is not
// version 1 refused without reaching the host, a silent or oversized or vanishing client dropped, a slow client never holding
// up the next, the quit action running only after the answer was read, and a second server of the same name refused. The
// pipe names are random, so no test touches a real nth.
public sealed class NthControlServerTests
{
    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(10);

    private static ScriptedControlHost Host(Func<NthControlRequest, NthControlAnswer>? answer = null) =>
        new(answer ?? (_ => new NthControlAnswer(ScriptedControlHost.Ok())));

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_request_reaches_the_host_and_its_answer_comes_back_on_the_same_connection()
    {
        var name = NthControlTestClient.NewPipeName();
        var busy = new NthControlResponse(NthControlReply.Busy, 4242, "nth.exe", [new NthWorkItem("Refactor the sign-in module", NthWorkKind.Thread, 240)], 1);
        var host = Host(request => new NthControlAnswer(request == NthControlRequest.QuitIfIdle ? busy : ScriptedControlHost.Ok()));
        using var server = NthControlServer.Start(name, host);

        var answer = await NthControlTestClient.AskAsync(name, NthControlRequest.QuitIfIdle);

        Assert.NotNull(answer);
        Assert.Equal(NthControlReply.Busy, answer.Reply);
        Assert.Equal("Refactor the sign-in module", Assert.Single(answer.Work).Name);
        Assert.Equal([NthControlRequest.QuitIfIdle], host.Requests);
        Assert.Equal(NthControlReply.Ok, (await NthControlTestClient.AskAsync(name, NthControlRequest.Status))!.Reply);
        Assert.Equal(2, host.Requests.Count);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_request_that_is_not_version_1_is_refused_as_unsupported_and_never_reaches_the_host()
    {
        var name = NthControlTestClient.NewPipeName();
        var host = Host();
        using var server = NthControlServer.Start(name, host);

        var bytes = await NthControlTestClient.SendRawAsync(name, NthControl.Frame("{\"v\":2,\"cmd\":\"quit\",\"mode\":\"stop\"}"));

        var answer = NthControlResponse.Parse(bytes!);
        Assert.NotNull(answer);
        Assert.Equal(NthControlReply.Refused, answer.Reply);
        Assert.Equal("unsupported", answer.Reason);
        Assert.Empty(host.Requests);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    [InlineData(new byte[] { 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x01, 0x00, 0x01, 0x00, 1 })]
    [InlineData(new byte[] { 9, 0, 0, 0, 1, 2 })]
    public async Task A_message_with_a_bad_length_or_a_short_body_is_dropped_without_an_answer(byte[] bytes)
    {
        var name = NthControlTestClient.NewPipeName();
        var host = Host();
        using var server = NthControlServer.Start(name, host);

        using (var client = await NthControlTestClient.ConnectAsync(name))
        {
            await client.WriteAsync(bytes);
            await client.FlushAsync();
            client.Close(); // a short body ends here
        }

        // The server is still there for the next, proper request.
        Assert.Equal(NthControlReply.Ok, (await NthControlTestClient.AskAsync(name, NthControlRequest.Status))!.Reply);
        Assert.Equal([NthControlRequest.Status], host.Requests);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_client_that_stays_silent_is_dropped_after_the_request_window_and_the_next_client_is_served_meanwhile()
    {
        var name = NthControlTestClient.NewPipeName();
        var host = Host();
        using var server = NthControlServer.Start(name, host, TimeSpan.FromMilliseconds(300));

        using var silent = await NthControlTestClient.ConnectAsync(name);
        var served = await NthControlTestClient.AskAsync(name, NthControlRequest.Status); // not held up by the silent one
        Assert.Equal(NthControlReply.Ok, served!.Reply);

        // After the window the silent client reads the end of the stream: the server let go of it.
        using var wait = new CancellationTokenSource(Patient);
        Assert.Null(await NthControl.ReadFrameAsync(silent, wait.Token));
        Assert.Equal([NthControlRequest.Status], host.Requests);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_slow_request_does_not_hold_up_another_connection()
    {
        var name = NthControlTestClient.NewPipeName();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new ScriptedControlHost(_ => new NthControlAnswer(ScriptedControlHost.Ok()))
        {
            Gate = async (request, _) =>
            {
                if (request != NthControlRequest.QuitIfIdle) return;
                entered.TrySetResult();
                await release.Task;
            },
        };
        using var server = NthControlServer.Start(name, host);

        var slow = NthControlTestClient.AskAsync(name, NthControlRequest.QuitIfIdle);
        await entered.Task.WaitAsync(Patient);
        var quick = await NthControlTestClient.AskAsync(name, NthControlRequest.Status);

        Assert.Equal(NthControlReply.Ok, quick!.Reply);
        Assert.False(slow.IsCompleted);
        release.SetResult();
        Assert.Equal(NthControlReply.Ok, (await slow.WaitAsync(Patient))!.Reply);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task What_follows_a_quit_runs_after_the_answer_was_read_and_not_before()
    {
        var name = NthControlTestClient.NewPipeName();
        var order = new List<string>();
        var after = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = Host(_ => new NthControlAnswer(ScriptedControlHost.Ok(), () =>
        {
            lock (order) order.Add("after");
            after.TrySetResult();
        }));
        using var server = NthControlServer.Start(name, host);

        using var client = await NthControlTestClient.ConnectAsync(name);
        await NthControl.WriteFrameAsync(client, NthControlRequest.QuitAndStop.ToJson(), CancellationToken.None);
        await Task.Delay(300); // the answer is waiting in the pipe, unread
        lock (order) Assert.Empty(order);
        var bytes = await NthControl.ReadFrameAsync(client, CancellationToken.None);
        lock (order) order.Add("read");
        await after.Task.WaitAsync(Patient);

        Assert.NotNull(NthControlResponse.Parse(bytes!));
        lock (order) Assert.Equal(["read", "after"], order);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_client_that_leaves_before_reading_the_answer_does_not_trigger_the_quit()
    {
        var name = NthControlTestClient.NewPipeName();
        var afterRan = 0;
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new ScriptedControlHost(_ => new NthControlAnswer(ScriptedControlHost.Ok(), () => Interlocked.Increment(ref afterRan)))
        {
            Gate = (_, _) =>
            {
                asked.TrySetResult();
                return Task.CompletedTask;
            },
        };
        using var server = NthControlServer.Start(name, host);

        using (var client = await NthControlTestClient.ConnectAsync(name))
        {
            await NthControl.WriteFrameAsync(client, NthControlRequest.QuitAndStop.ToJson(), CancellationToken.None);
            await asked.Task.WaitAsync(Patient);
        } // gone without reading
        await Task.Delay(500);

        Assert.Equal(0, Volatile.Read(ref afterRan));
        Assert.Equal(NthControlReply.Ok, (await NthControlTestClient.AskAsync(name, NthControlRequest.Status))!.Reply);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_host_that_throws_closes_that_connection_and_the_server_goes_on()
    {
        var name = NthControlTestClient.NewPipeName();
        var host = Host(request => request == NthControlRequest.QuitAndStop ? throw new InvalidOperationException("boom") : new NthControlAnswer(ScriptedControlHost.Ok()));
        using var server = NthControlServer.Start(name, host);

        Assert.Null(await NthControlTestClient.AskAsync(name, NthControlRequest.QuitAndStop));
        Assert.Equal(NthControlReply.Ok, (await NthControlTestClient.AskAsync(name, NthControlRequest.Status))!.Reply);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Many_clients_at_once_each_get_their_own_answer()
    {
        var name = NthControlTestClient.NewPipeName();
        var host = Host(request => new NthControlAnswer(request == NthControlRequest.Status ? ScriptedControlHost.Ok() : NthControlResponse.Refused(1, "x", "other")));
        using var server = NthControlServer.Start(name, host);

        var answers = await Task.WhenAll(Enumerable.Range(0, 24).Select(i =>
            NthControlTestClient.AskAsync(name, i % 2 == 0 ? NthControlRequest.Status : NthControlRequest.Withdraw)));

        for (var i = 0; i < answers.Length; i++)
            Assert.Equal(i % 2 == 0 ? NthControlReply.Ok : NthControlReply.Refused, answers[i]!.Reply);
        Assert.Equal(24, host.Requests.Count);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_second_server_of_the_same_name_is_refused_and_a_stopped_server_frees_the_name()
    {
        var name = NthControlTestClient.NewPipeName();
        var first = NthControlServer.Start(name, Host());

        Assert.Null(NthControlServer.TryStart(name, Host()));
        var refusal = Record.Exception(() => NthControlServer.Start(name, Host()));
        Assert.True(refusal is IOException or UnauthorizedAccessException, "Windows refuses a second first instance of the name");
        first.Dispose();
        first.Dispose(); // twice is harmless

        using var again = NthControlServer.TryStart(name, Host());
        Assert.NotNull(again);
        Assert.Equal(name, again.Name);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_stopped_server_no_longer_answers()
    {
        var name = NthControlTestClient.NewPipeName();
        var server = NthControlServer.Start(name, Host());
        Assert.NotNull(await NthControlTestClient.AskAsync(name, NthControlRequest.Status));

        server.Dispose();

        await Assert.ThrowsAnyAsync<Exception>(() => NthControlTestClient.AskAsync(name, NthControlRequest.Status, timeoutMs: 400));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task The_pipe_exists_as_soon_as_the_server_starts_so_an_early_installer_waits_instead_of_missing_it()
    {
        var name = NthControlTestClient.NewPipeName();
        using var server = NthControlServer.Start(name, Host());

        Assert.Equal(NthControlReply.Ok, (await NthControlTestClient.AskAsync(name, NthControlRequest.Status))!.Reply);
    }
}
