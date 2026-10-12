using Marrow.Core.Port;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3, section 3.2): the first url must be https and on the host list, refused before any connection; redirects are followed only to https hosts
// (the mirror host need not be on the list: the pinned hash is the trust), the final host is logged.
[Trait("Spec", "DL-3")]
public sealed class DownloaderPolicyTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(20_000, seed: 21);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_first_url_off_the_allow_list_is_refused_before_any_connection()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact), allowedHosts: ["www.python.org", "nodejs.org"]);

        Assert.Equal(DownloadStatus.Failed, outcome.Status);
        Assert.Equal((DownloadFailureKind.Refused, DownloadCause.HostNotAllowed), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal(0, rig.Counting.Sent); // the transport was never asked
        Assert.Equal(0, rig.Server.ConnectionCount);
        Assert.Empty(rig.Handler.Refusals);
        Assert.False(Directory.Exists(rig.PartFolder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public void The_host_match_is_exact_not_a_suffix_or_a_sub_domain()
    {
        var policy = new DownloadPolicy(["github.com"]);
        Assert.True(policy.AllowsFirstUrl(new Uri("https://GitHub.com/x")));
        Assert.False(policy.AllowsFirstUrl(new Uri("https://evil.github.com/x")));
        Assert.False(policy.AllowsFirstUrl(new Uri("https://github.com.evil.example/x")));
        Assert.False(policy.AllowsFirstUrl(new Uri("http://github.com/x")));
        Assert.Equal(["www.python.org", "nodejs.org", "download.documentfoundation.org", "github.com", "raw.githubusercontent.com",
            "files.pythonhosted.org", "registry.npmjs.org", "cdn.playwright.dev"], DownloadPolicy.CatalogueHosts);
        Assert.True(DownloadPolicy.Catalogue().AllowsFirstUrl(new Uri("https://cdn.playwright.dev/builds/x.zip")));
        // ED-NEW-dl-13: the catalogue's licence-only host (its gate refuses it in every field but a licence URL) is the one host the catalogue policy adds to the plan's list.
        Assert.Equal(["gitlab.freedesktop.org"], DownloadPolicy.CatalogueLicenceHosts);
        Assert.Equal(DownloadPolicy.CatalogueHosts.Concat(DownloadPolicy.CatalogueLicenceHosts).Order(StringComparer.Ordinal), DownloadPolicy.Catalogue().AllowedHosts.Order(StringComparer.Ordinal));
        Assert.True(DownloadPolicy.Catalogue().AllowsFirstUrl(new Uri("https://gitlab.freedesktop.org/poppler/poppler/-/raw/poppler-26.09.0/COPYING")));
        Assert.False(DownloadPolicy.Catalogue().AllowsFirstUrl(new Uri("https://gitlab.com/x")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_plain_http_first_url_is_refused_even_on_an_allowed_host()
    {
        await using var rig = new DownloadRig(plain: true);
        rig.Plain!.Serve("/a.bin", Artifact);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact, on: rig.Plain));

        Assert.Equal((DownloadFailureKind.Refused, DownloadCause.NotHttps), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal(0, rig.Counting.Sent);
        Assert.Equal(0, rig.Plain.ConnectionCount);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_redirect_to_plain_http_is_refused_and_the_plain_server_is_never_contacted()
    {
        await using var rig = new DownloadRig(plain: true);
        rig.Plain!.Serve("/a.bin", Artifact);
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = rig.Plain.Url("/a.bin") });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal((DownloadFailureKind.Refused, DownloadCause.InsecureRedirect), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal(1, rig.Counting.Sent);
        Assert.Equal(0, rig.Plain.ConnectionCount);
        Assert.Empty(rig.Plain.Requests);
        Assert.False(File.Exists(rig.Dest("a.bin")));
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task A_redirect_to_another_https_host_is_followed_though_that_host_is_not_on_the_list(int status)
    {
        await using var rig = new DownloadRig(second: true);
        rig.Second!.Serve("/real.bin", Artifact);
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = rig.Second.Url("/real.bin"), RedirectStatus = status });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.True(outcome.Succeeded);
        Assert.Equal("127.0.0.2", outcome.Files[0].FinalHost);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(rig.Dest("a.bin")));
        Assert.Equal([status], rig.Server.Requests.Select(r => r.Status));
        Assert.Equal([200], rig.Second.Requests.Select(r => r.Status));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_resume_across_a_redirect_sends_the_Range_to_the_mirror()
    {
        await using var rig = new DownloadRig(second: true);
        rig.Second!.Serve("/real.bin", Artifact);
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = rig.Second.Url("/real.bin") });
        rig.SeedPart(Artifact, Artifact.Bytes[..5_000]);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.True(outcome.Succeeded);
        var mirror = Assert.Single(rig.Second.Requests);
        Assert.Equal((5_000L, 206, 15_000L), (mirror.RangeStart, mirror.Status, mirror.BodyBytesSent));
        Assert.Equal(5_000L, outcome.Files[0].ResumedFrom);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_final_host_is_logged()
    {
        await using var rig = new DownloadRig(second: true);
        rig.Second!.Serve("/real.bin", Artifact);
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = rig.Second.Url("/real.bin") });
        var lines = new List<string>();
        using var scope = Logger.Sink.Override((message, _) => { lock (lines) lines.Add(message); });

        await rig.Run(rig.Request("/a.bin", Artifact));

        lock (lines) Assert.Contains(lines, l => l.Contains("127.0.0.2", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task An_endless_redirect_is_refused_after_the_hop_limit()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/loop", Artifact, new FakeBehavior { RedirectTo = rig.Server.Url("/loop") });

        var outcome = await rig.Run(rig.Request("/loop", Artifact), new DownloadOptions { MaxRedirects = 3 });

        Assert.Equal((DownloadFailureKind.Refused, DownloadCause.TooManyRedirects), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal(4, rig.Counting.Sent); // the first request and three hops; the fourth redirect is the refusal
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_redirect_off_loopback_is_not_reachable_through_the_guarded_transport_and_reads_as_offline()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = new Uri("https://downloads.example.invalid/a.bin") });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal((DownloadFailureKind.Unreachable, DownloadCause.Offline), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Single(rig.Handler.Refusals);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_redirect_without_a_usable_location_is_refused()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = new Uri("ftp://example.invalid/a.bin") });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal(DownloadFailureKind.Refused, outcome.Failure!.Kind);
        Assert.Equal(1, rig.Counting.Sent);
    }
}
