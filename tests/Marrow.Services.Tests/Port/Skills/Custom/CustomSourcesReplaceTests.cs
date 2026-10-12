using System.Text;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

// ED-NEW-mcpimport-6 (review of claude/mcp-import): Replace publishes the new copy first and swaps second, so an add that fails never loses the plugin nth had.
public sealed class CustomSourcesReplaceTests
{
    private static byte[] Manifest(string name, string description = "A fixture plugin.") => Encoding.UTF8.GetBytes("{\"name\":\"" + name + "\",\"description\":\"" + description + "\"}");

    private static async Task<(CustomSourceFake Rig, FailingCustomSourceFiles Files, CustomSources Service, OwnItem Old)> WithInstalled(string name = "foo")
    {
        var rig = new CustomSourceFake { Files = new() { [".claude-plugin/plugin.json"] = Manifest(name), ["hooks/old.txt"] = [1] } };
        var files = new FailingCustomSourceFiles(rig);
        var service = new CustomSources("/fake/own", files, files, TimeProvider.System);
        var candidate = Assert.Single(await service.Check(new(CustomAddKind.Plugins, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None));
        var old = await service.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        return (rig, files, service, old);
    }

    private static async Task<CustomAddCandidate> CheckAgain(CustomSourceFake rig, CustomSources service, string name, string marker)
    {
        rig.Files = new() { [".claude-plugin/plugin.json"] = Manifest(name, "The new copy."), ["hooks/new.txt"] = Encoding.UTF8.GetBytes(marker) };
        return Assert.Single(await service.Check(new(CustomAddKind.Plugins, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public async Task Replace_puts_the_new_copy_in_and_drops_the_old_one_after_keeping_its_enabled_state()
    {
        var (rig, files, service, old) = await WithInstalled();
        service.SetEnabled(old.Id, false);
        var candidate = await CheckAgain(rig, service, "foo", "v2");

        var item = await service.Replace(old.Id, candidate, new CustomSourceProgress(), CancellationToken.None);

        var listed = Assert.Single(service.List(CustomAddKind.Plugins));
        Assert.Equal(item.Id, listed.Id);
        Assert.NotEqual(old.Id, listed.Id);
        Assert.False(listed.Enabled); // the old one was off; so is the new one
        Assert.Equal("The new copy.", listed.Description);
        Assert.Equal([item.Id], rig.Installed.Keys);
        Assert.Contains("payload/hooks/new.txt", rig.Installed[item.Id].Keys);
        Assert.Equal(item.Id, Assert.Single(new CustomSources("/fake/own", files, files, TimeProvider.System).List(CustomAddKind.Plugins)).Id); // and it is what is on disk
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public async Task A_publish_that_fails_leaves_the_old_plugin_listed_with_its_files_and_enabled_state()
    {
        var (rig, files, service, old) = await WithInstalled();
        var candidate = await CheckAgain(rig, service, "foo", "v2");
        files.FailPublish = true;

        await Assert.ThrowsAsync<IOException>(() => service.Replace(old.Id, candidate, new CustomSourceProgress(), CancellationToken.None));

        var listed = Assert.Single(service.List(CustomAddKind.Plugins));
        Assert.Equal((old.Id, true), (listed.Id, listed.Enabled));
        Assert.Equal([old.Id], rig.Installed.Keys);
        Assert.Contains("payload/hooks/old.txt", rig.Installed[old.Id].Keys);
        Assert.Equal(old.Id, Assert.Single(new CustomSources("/fake/own", files, files, TimeProvider.System).List(CustomAddKind.Plugins)).Id);
        // and the same check can be replaced once the disk works again
        files.FailPublish = false;
        var item = await service.Replace(old.Id, candidate, new CustomSourceProgress(), CancellationToken.None);
        Assert.Equal(item.Id, Assert.Single(service.List(CustomAddKind.Plugins)).Id);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public async Task A_registry_write_that_fails_drops_only_the_new_copy()
    {
        var (rig, _, service, old) = await WithInstalled();
        var candidate = await CheckAgain(rig, service, "foo", "v2");
        rig.FailCommit = true;

        await Assert.ThrowsAsync<IOException>(() => service.Replace(old.Id, candidate, new CustomSourceProgress(), CancellationToken.None));

        Assert.Equal(old.Id, Assert.Single(service.List(CustomAddKind.Plugins)).Id);
        Assert.Equal([old.Id], rig.Installed.Keys);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public async Task A_cancelled_replace_leaves_the_old_plugin()
    {
        var (rig, _, service, old) = await WithInstalled();
        var candidate = await CheckAgain(rig, service, "foo", "v2");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.Replace(old.Id, candidate, new CustomSourceProgress(), cancelled.Token));
        Assert.Equal(old.Id, Assert.Single(service.List(CustomAddKind.Plugins)).Id);
        Assert.Equal([old.Id], rig.Installed.Keys);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public async Task Taking_the_name_of_a_third_plugin_is_refused_and_both_stay()
    {
        var (rig, _, service, foo) = await WithInstalled("foo");
        rig.Files = new() { [".claude-plugin/plugin.json"] = Manifest("bar") };
        var bar = await service.Add(Assert.Single(await service.Check(new(CustomAddKind.Plugins, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None)), new CustomSourceProgress(), CancellationToken.None);
        var wantsBar = await CheckAgain(rig, service, "bar", "v2");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Replace(foo.Id, wantsBar, new CustomSourceProgress(), CancellationToken.None));

        Assert.Equal([foo.Id, bar.Id], service.List(CustomAddKind.Plugins).Select(i => i.Id));
        Assert.Equal(2, rig.Installed.Count);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public async Task Old_files_that_cannot_be_deleted_after_the_swap_do_not_undo_it()
    {
        var (rig, files, service, old) = await WithInstalled();
        var candidate = await CheckAgain(rig, service, "foo", "v2");
        files.LockedId = old.Id; // the old folder is locked (an antivirus scan, say)

        var item = await service.Replace(old.Id, candidate, new CustomSourceProgress(), CancellationToken.None);

        Assert.Equal(item.Id, Assert.Single(service.List(CustomAddKind.Plugins)).Id); // the registry lists only the new one; the old folder is an orphan nobody loads
        Assert.Contains(old.Id, rig.Installed.Keys);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public async Task A_replace_of_something_that_is_gone_is_a_plain_add_and_a_replace_across_kinds_is_refused()
    {
        var (rig, _, service, old) = await WithInstalled();
        var candidate = await CheckAgain(rig, service, "baz", "v2");
        var item = await service.Replace("0123456789abcdef0123456789abcdef", candidate, new CustomSourceProgress(), CancellationToken.None);
        Assert.Equal(2, service.List(CustomAddKind.Plugins).Count);
        Assert.Contains(item.Id, service.List(CustomAddKind.Plugins).Select(i => i.Id));

        rig.Files = new() { ["one/SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: one\ndescription: Fixture skill.\n---\nBody") };
        var skill = Assert.Single(await service.Check(new(CustomAddKind.Skills, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Replace(old.Id, skill, new CustomSourceProgress(), CancellationToken.None));
        Assert.Contains(old.Id, service.List(CustomAddKind.Plugins).Select(i => i.Id));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public async Task Replace_asks_for_an_id()
    {
        var (rig, _, service, _) = await WithInstalled();
        var candidate = await CheckAgain(rig, service, "foo", "v2");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.Replace("", candidate, new CustomSourceProgress(), CancellationToken.None));
    }
}
