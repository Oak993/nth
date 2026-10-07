using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

[Trait("Feature", "SK-2")]
[Trait("Rule", "R-TEST-4")]
public sealed class CustomToolBundleTests
{
    [Fact]
    public async Task A_bundle_selects_the_requested_primary_executable_and_preserves_helper_executables()
    {
        var fake = new CustomSourceFake { Files = new() { ["bin/fixture.exe"] = [77, 90], ["bin/helper.exe"] = [77, 90, 3] } };
        var service = fake.Open();
        var candidate = Assert.Single(await service.Check(new(CustomAddKind.Tools, CustomAddSource.Folder, "fixture", "Fixture", "fixture"), CancellationToken.None));
        Assert.Null(candidate.Problem);
        var item = await service.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        Assert.Equal("bin/fixture.exe", item.Executable);
        Assert.Contains("payload/bin/helper.exe", fake.Installed[item.Id].Keys);
        Assert.Single(fake.Open().Commands());
    }

    [Fact]
    public async Task An_ambiguous_bundle_is_listed_with_a_problem_and_cannot_be_added()
    {
        var fake = new CustomSourceFake { Files = new() { ["one.exe"] = [77, 90], ["two.exe"] = [77, 90] } };
        var service = fake.Open();
        var candidate = Assert.Single(await service.Check(new(CustomAddKind.Tools, CustomAddSource.Folder, "fixture", "Fixture", "alias"), CancellationToken.None));
        Assert.NotNull(candidate.Problem);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Add(candidate, new CustomSourceProgress(), CancellationToken.None));
        Assert.Empty(fake.Installed);
    }
}
