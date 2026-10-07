using System.Net;
using System.Net.Http;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.Core.Tests.Port.Downloads;

// SK-0 (DL-0, skills-ONDEMAND-PLAN.md section 7): the faults the fake download server can switch on: a drop after N bytes (and the
// resume the plan's DL-3 checks in the server's log), a wrong byte, a wrong length, a slow trickle on a manual clock.
[Trait("Spec", "DL-0")]
public sealed class FakeDownloadFaultTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(10_000, seed: 11);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_drop_sends_exactly_N_body_bytes_after_promising_the_full_length_and_the_client_sees_a_cut_stream()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact, new FakeBehavior { DropAfterBytes = 4000 });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        using var response = await client.GetAsync(server.Url("/a.bin"), HttpCompletionOption.ResponseHeadersRead);
        var received = new MemoryStream();
        await using var body = await response.Content.ReadAsStreamAsync();
        await Assert.ThrowsAnyAsync<IOException>(() => body.CopyToAsync(received).WaitAsync(FakeDownloadServer.Guard));

        Assert.Equal(10_000, response.Content.Headers.ContentLength);
        Assert.Equal(Artifact.Bytes[..4000], received.ToArray());
        var entry = Assert.Single(await server.WaitForRequests(1));
        Assert.Equal((10_000L, 4000L, true), (entry.BodyBytesPromised, entry.BodyBytesSent, entry.Dropped));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task After_a_drop_a_range_request_resumes_and_the_log_shows_no_byte_was_sent_twice()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact, new FakeBehavior { DropAfterBytes = 4000 });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);
        var part = new MemoryStream();
        using (var cut = await client.GetAsync(server.Url("/a.bin"), HttpCompletionOption.ResponseHeadersRead))
        {
            await using var body = await cut.Content.ReadAsStreamAsync();
            await Assert.ThrowsAnyAsync<IOException>(() => body.CopyToAsync(part).WaitAsync(FakeDownloadServer.Guard));
        }
        server.Behave("/a.bin", new FakeBehavior());

        var (resumed, rest) = await DownloadFixtures.Get(client, server.Url("/a.bin"), $"bytes={part.Length}-");

        Assert.Equal(HttpStatusCode.PartialContent, resumed.StatusCode);
        var whole = part.ToArray().Concat(rest).ToArray();
        Assert.Equal(Artifact.Bytes, whole);
        var log = await server.WaitForRequests(2);
        Assert.Equal((null, 4000L), (log[0].RangeStart, log[1].RangeStart));
        Assert.Equal(10_000L, log.Sum(r => r.BodyBytesSent));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task Faults_can_be_limited_to_the_first_requests_so_the_retry_is_answered_plainly()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact,
            new FakeBehavior { DropAfterBytes = 100, FaultyRequests = 1 });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        // Buffering the body wraps the client's cut-stream IOException in an HttpRequestException.
        await Assert.ThrowsAsync<HttpRequestException>(() => DownloadFixtures.Get(client, server.Url("/a.bin")));
        var (second, body) = await DownloadFixtures.Get(client, server.Url("/a.bin"));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(Artifact.Bytes, body);
        var log = await server.WaitForRequests(2);
        Assert.Equal([true, false], log.Select(r => r.Dropped));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_wrong_byte_keeps_the_length_and_changes_exactly_that_byte()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact, new FakeBehavior { WrongByteAt = 1234 });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (_, body) = await DownloadFixtures.Get(client, server.Url("/a.bin"));

        Assert.Equal(Artifact.Length, body.Length);
        Assert.Equal([1234], Enumerable.Range(0, body.Length).Where(i => body[i] != Artifact.Bytes[i]));
        Assert.Equal((byte)(Artifact.Bytes[1234] ^ 0xFF), body[1234]);
        Assert.Equal(Artifact.Bytes, (await ServedAfterReset(server, client)).Body);
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    [InlineData(17)]
    [InlineData(-17)]
    [InlineData(-10_000)]
    public async Task A_wrong_length_is_a_complete_well_formed_answer_of_a_different_size(int delta)
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact, new FakeBehavior { WrongLengthDelta = delta });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (response, body) = await DownloadFixtures.Get(client, server.Url("/a.bin"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Artifact.Length + delta, body.Length);
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
        var shared = Math.Min(body.Length, Artifact.Bytes.Length);
        Assert.Equal(Artifact.Bytes[..shared], body[..shared]);
        Assert.Equal(body.Length, (await server.WaitForRequests(1))[0].BodyBytesSent);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_slow_trickle_sends_one_chunk_per_interval_of_the_servers_clock()
    {
        var clock = new ManualTimeProvider();
        var artifact = FakeArtifact.Pattern(300, seed: 2);
        await using var server = FakeDownloadServer.StartHttps(clock: clock)
            .Serve("/slow.bin", artifact, new FakeBehavior { Trickle = new FakeTrickle(100, TimeSpan.FromSeconds(1)) });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);
        using var response = await client.GetAsync(server.Url("/slow.bin"), HttpCompletionOption.ResponseHeadersRead);
        await using var body = await response.Content.ReadAsStreamAsync();

        var first = new byte[100];
        await body.ReadExactlyAsync(first).AsTask().WaitAsync(FakeDownloadServer.Guard);
        Assert.Equal(artifact.Bytes[..100], first);

        foreach (var chunk in new[] { 1, 2 })
        {
            var next = new byte[100];
            var reading = body.ReadExactlyAsync(next).AsTask();
            await DownloadFixtures.UntilTimerArmed(clock);
            Assert.False(reading.IsCompleted, "the next chunk waits for the interval");
            Assert.Empty(server.Requests);
            clock.Advance(TimeSpan.FromSeconds(1));
            await reading.WaitAsync(FakeDownloadServer.Guard);
            Assert.Equal(artifact.Bytes[(chunk * 100)..((chunk + 1) * 100)], next);
        }

        var entry = Assert.Single(await server.WaitForRequests(1));
        Assert.Equal((300L, 300L), (entry.BodyBytesPromised, entry.BodyBytesSent));
    }

    private static async Task<(HttpResponseMessage Response, byte[] Body)> ServedAfterReset(FakeDownloadServer server, HttpClient client)
    {
        server.Behave("/a.bin", new FakeBehavior());
        return await DownloadFixtures.Get(client, server.Url("/a.bin"));
    }
}
