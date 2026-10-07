using System.Text;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

[Trait("Feature", "SK-2")]
[Trait("Rule", "R-TEST-4")]
public sealed class CustomSourcesTests
{
    [Theory]
    [InlineData(CustomAddSource.Folder)]
    [InlineData(CustomAddSource.Zip)]
    [InlineData(CustomAddSource.GitHub)]
    public async Task Checked_skills_install_frozen_bytes_and_reopen_with_provenance(CustomAddSource source)
    {
        var rig = new CustomSourceFake();
        rig.Files = new() { ["one/SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: one\ndescription: Fixture skill.\n---\nOriginal"), ["one/scripts/a.py"] = [1, 2, 3] };
        var service = rig.Open();
        var request = new CustomAddRequest(CustomAddKind.Skills, source, source == CustomAddSource.GitHub ? "fixture/repository" : "fixture", "", "");
        var candidate = Assert.Single(await service.Check(request, CancellationToken.None));
        Assert.Null(candidate.Problem);
        rig.Files["one/SKILL.md"] = Encoding.UTF8.GetBytes("changed after check");
        var item = await service.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        var reopened = rig.Open();
        var saved = Assert.Single(reopened.List(CustomAddKind.Skills));
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal(source, saved.Provenance.Source);
        Assert.Equal(request.Location, saved.Provenance.Location);
        Assert.Equal(source == CustomAddSource.GitHub ? new string('a', 40) : null, saved.Provenance.Commit);
        Assert.EndsWith("Original", Encoding.UTF8.GetString(Assert.Single(reopened.Snapshot()).Files["SKILL.md"]), StringComparison.Ordinal);
        Assert.Empty(reopened.Commands());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Add(candidate, new CustomSourceProgress(), CancellationToken.None));
    }

    [Theory]
    [InlineData("../escape/SKILL.md")]
    [InlineData("C:/escape/SKILL.md")]
    [InlineData("one/CON.txt")]
    [InlineData("one/file:stream")]
    [InlineData("one/a%name")]
    public async Task Unsafe_paths_never_reach_installation(string path)
    {
        var rig = new CustomSourceFake { Files = new() { [path] = [1] } };
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Open().Check(new(CustomAddKind.Skills, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None));
        Assert.Null(rig.Registry);
        Assert.Empty(rig.Installed);
    }

    [Fact]
    public async Task Plugins_require_a_manifest_and_keep_hooks_resources_and_provenance()
    {
        var rig = new CustomSourceFake { Files = new() { ["plugin/.codex-plugin/plugin.json"] = Encoding.UTF8.GetBytes("{\"name\":\"fixture-plugin\",\"description\":\"Fixture plugin.\"}"), ["plugin/hooks/hooks.json"] = Encoding.UTF8.GetBytes("{}") } };
        var service = rig.Open();
        var candidate = Assert.Single(await service.Check(new(CustomAddKind.Plugins, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None));
        Assert.Null(candidate.Problem);
        var item = await service.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        Assert.Contains("payload/hooks/hooks.json", rig.Installed[item.Id].Keys);
        Assert.Single(rig.Open().List(CustomAddKind.Plugins));
        Assert.Empty(service.Snapshot());
    }

    [Fact]
    public async Task Tools_register_the_entered_command_only_after_commit_and_unregister_when_disabled()
    {
        var rig = new CustomSourceFake { Files = new() { ["vendor.exe"] = [77, 90, 1], ["vendor.dll"] = [4, 5] } };
        var service = rig.Open();
        var candidate = Assert.Single(await service.Check(new(CustomAddKind.Tools, CustomAddSource.Folder, "fixture", "Fixture tool", "my-tool"), CancellationToken.None));
        Assert.Null(candidate.Problem);
        Assert.Empty(service.Commands());
        var item = await service.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        var command = Assert.Single(rig.Open().Commands());
        Assert.Equal("my-tool", command.Name);
        Assert.EndsWith("/bin/my-tool.cmd", command.Entry.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("payload/vendor.dll", rig.Installed[item.Id].Keys);
        service.SetEnabled(item.Id, false);
        Assert.Empty(rig.Open().Commands());
    }

    [Fact]
    public async Task Failed_registry_commit_rolls_back_files_and_registers_nothing_and_allows_retry()
    {
        var rig = new CustomSourceFake { Files = new() { ["vendor.exe"] = [77, 90] }, FailCommit = true };
        var service = rig.Open();
        var candidate = Assert.Single(await service.Check(new(CustomAddKind.Tools, CustomAddSource.Folder, "fixture", "Fixture", "fixture"), CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => service.Add(candidate, new CustomSourceProgress(), CancellationToken.None));
        Assert.Empty(service.Commands());
        Assert.Empty(rig.Installed);
        rig.FailCommit = false;
        await service.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        Assert.Single(rig.Open().Commands());
    }

    [Fact]
    public async Task Forged_candidates_duplicate_commands_and_changed_installed_files_fail_closed()
    {
        var rig = new CustomSourceFake { Files = new() { ["fixture.exe"] = [77, 90] } };
        var service = rig.Open();
        var request = new CustomAddRequest(CustomAddKind.Tools, CustomAddSource.Folder, "fixture", "Fixture", "fixture");
        var candidate = Assert.Single(await service.Check(request, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Add(candidate with { Name = "forged" }, new CustomSourceProgress(), CancellationToken.None));
        var item = await service.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        var duplicate = Assert.Single(await service.Check(request, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Add(duplicate, new CustomSourceProgress(), CancellationToken.None));
        rig.Installed[item.Id]["payload/fixture.exe"] = [0];
        Assert.Throws<InvalidDataException>(() => rig.Open().Commands());
    }

    [Theory]
    [InlineData("http://github.com/owner/repo")]
    [InlineData("https://github.com.evil.test/owner/repo")]
    [InlineData("https://user:password@github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo?token=secret")]
    public async Task Invalid_GitHub_locations_do_not_call_the_transport(string location)
    {
        var rig = new CustomSourceFake();
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Open().Check(new(CustomAddKind.Skills, CustomAddSource.GitHub, location, "", ""), CancellationToken.None));
        Assert.Equal(0, rig.GitHubCalls);
    }

    [Fact]
    public async Task Concurrent_selected_items_are_all_preserved_and_cancellation_publishes_nothing()
    {
        var rig = new CustomSourceFake { Files = new() { ["one/SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: one\ndescription: One.\n---\nBody"), ["two/SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: two\ndescription: Two.\n---\nBody") } };
        var service = rig.Open();
        var candidates = await service.Check(new(CustomAddKind.Skills, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.Add(candidates[0], new CustomSourceProgress(), cancelled.Token));
        await Task.WhenAll(candidates.Select(c => service.Add(c, new CustomSourceProgress(), CancellationToken.None)));
        Assert.Equal(2, rig.Open().Snapshot().Count);
    }
}
