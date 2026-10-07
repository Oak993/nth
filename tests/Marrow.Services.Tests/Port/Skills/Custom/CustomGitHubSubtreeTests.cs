using System.Net.Http;
using System.Text;
using System.Text.Json;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

[Trait("Rule", "R-TEST-4")]
[Trait("Feature", "SK-2")]
public sealed class CustomGitHubSubtreeTests
{
    private static readonly string Commit = new('b', 40);
    private static readonly string Contents = "https://api.github.com/repos/fixture/repository/contents/skills/one?ref=" + Commit;

    [Theory]
    [InlineData("fixture/repository/skills/one")]
    [InlineData("fixture/repository/skills\\one")]
    [InlineData("https://github.com/fixture/repository/skills/one")]
    public async Task Only_the_selected_subtree_is_fetched_and_installed_with_persisted_provenance(string location)
    {
        using var transport = Fixture();
        using var client = new HttpClient(transport);
        var disk = new CustomSourceFake();
        var source = new CustomSources("/fake/own", disk, new PublicGitHubSource(client), TimeProvider.System);
        var candidate = Assert.Single(await source.Check(new(CustomAddKind.Skills, CustomAddSource.GitHub, location, "", ""), CancellationToken.None));
        Assert.Null(candidate.Problem);
        var item = await source.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        var saved = Assert.Single(disk.Open().List(CustomAddKind.Skills));
        Assert.Equal(location, saved.Provenance.Location);
        Assert.Equal(Commit, saved.Provenance.Commit);
        Assert.Equal(Contents, saved.Provenance.ArtifactUrl);
        using var registry = JsonDocument.Parse(disk.Registry!);
        Assert.Equal("skills/one", registry.RootElement[0].GetProperty("Provenance").GetProperty("SubPath").GetString());
        Assert.Equal(new[] { "payload/SKILL.md", "payload/scripts/hello.py" }, disk.Installed[item.Id].Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] {
            "https://api.github.com/repos/fixture/repository/commits/HEAD", Contents,
            "https://raw.githubusercontent.com/fixture/repository/" + Commit + "/skills/one/SKILL.md",
            "https://api.github.com/repos/fixture/repository/contents/skills/one/scripts?ref=" + Commit,
            "https://raw.githubusercontent.com/fixture/repository/" + Commit + "/skills/one/scripts/hello.py"
        }, transport.Requests);
        Assert.DoesNotContain(transport.Requests, url => url.Contains("codeload", StringComparison.Ordinal));
        Assert.Equal(0, disk.GitHubCalls);
    }

    [Theory]
    [InlineData("fixture/repository/../escape")]
    [InlineData("fixture/repository/skills/../../escape")]
    [InlineData("fixture/repository//absolute")]
    [InlineData("fixture/repository/\\absolute")]
    [InlineData("fixture/repository/C:\\absolute")]
    [InlineData("fixture/repository/./one")]
    [InlineData("fixture/repository/skills/%2e%2e/escape")]
    [InlineData("https://github.com/fixture/repository/../escape")]
    [InlineData("https://github.com/fixture/repository/skills/%2e%2e/escape")]
    [InlineData("fixture/repository/skills/one/")]
    public async Task Unsafe_subtree_paths_fail_before_any_HTTP_request(string location)
    {
        using var transport = Fixture();
        using var client = new HttpClient(transport);
        var disk = new CustomSourceFake();
        var source = new CustomSources("/fake/own", disk, new PublicGitHubSource(client), TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentException>(() => source.Check(new(CustomAddKind.Skills, CustomAddSource.GitHub, location, "", ""), CancellationToken.None));
        Assert.Empty(transport.Requests);
        Assert.Null(disk.Registry);
        Assert.Empty(disk.Installed);
    }

    [Theory]
    [InlineData("[{\"type\":\"file\",\"path\":\"other/SKILL.md\",\"size\":1}]")]
    [InlineData("[{\"type\":\"symlink\",\"path\":\"skills/one/link\",\"size\":1}]")]
    [InlineData("[{\"type\":\"file\",\"path\":\"skills/one/mod\",\"size\":1,\"submodule_git_url\":\"https://example.test/module\"}]")]
    [InlineData("[{\"type\":\"dir\",\"path\":\"skills/one/nested/escape\"}]")]
    [InlineData("[{\"type\":\"file\",\"path\":\"skills/one/A\",\"size\":0},{\"type\":\"file\",\"path\":\"skills/one/a\",\"size\":0}]")]
    [InlineData("{\"type\":\"file\",\"path\":\"skills/one\",\"size\":1}")]
    public async Task Server_paths_links_submodules_and_case_collisions_cannot_escape_the_selected_folder(string listing)
    {
        using var transport = Fixture();
        transport.Responses[Contents] = Encoding.UTF8.GetBytes(listing);
        using var client = new HttpClient(transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => new PublicGitHubSource(client).Fetch("fixture/repository/skills/one", CancellationToken.None));
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task A_subtree_file_must_match_the_pinned_listing_size()
    {
        using var transport = Fixture();
        transport.Responses["https://raw.githubusercontent.com/fixture/repository/" + Commit + "/skills/one/SKILL.md"] = [0];
        using var client = new HttpClient(transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => new PublicGitHubSource(client).Fetch("fixture/repository/skills/one", CancellationToken.None));
        Assert.Equal(3, transport.Requests.Count);
    }

    private static CustomGitHubHttpFake Fixture()
    {
        var transport = new CustomGitHubHttpFake();
        var skill = Encoding.UTF8.GetBytes("---\nname: one\ndescription: Subtree fixture.\n---\nBody");
        transport.Json("https://api.github.com/repos/fixture/repository/commits/HEAD", "{\"sha\":\"" + Commit + "\"}");
        transport.Json(Contents, "[{\"type\":\"file\",\"path\":\"skills/one/SKILL.md\",\"size\":" + skill.Length + "},{\"type\":\"dir\",\"path\":\"skills/one/scripts\"}]");
        transport.Responses.Add("https://raw.githubusercontent.com/fixture/repository/" + Commit + "/skills/one/SKILL.md", skill);
        transport.Json("https://api.github.com/repos/fixture/repository/contents/skills/one/scripts?ref=" + Commit, "[{\"type\":\"file\",\"path\":\"skills/one/scripts/hello.py\",\"size\":3}]");
        transport.Responses.Add("https://raw.githubusercontent.com/fixture/repository/" + Commit + "/skills/one/scripts/hello.py", [1, 2, 3]);
        return transport;
    }
}
