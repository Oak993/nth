using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

[Trait("Rule", "R-TEST-4")]
public sealed class CustomGitHubRepositoryTests
{
    [Theory]
    [InlineData("fixture/..git/one")]
    [InlineData("fixture/...git/one")]
    [InlineData("/fixture/repository/one")]
    [InlineData("https://github.com/fixture/..git/one")]
    public async Task Repository_components_cannot_become_dot_segments_after_git_suffix_normalization(string location)
    {
        var rig = new CustomSourceFake();
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Open().Check(new(CustomAddKind.Skills, CustomAddSource.GitHub, location, "", ""), CancellationToken.None));
        Assert.Equal(0, rig.GitHubCalls);
    }
}
