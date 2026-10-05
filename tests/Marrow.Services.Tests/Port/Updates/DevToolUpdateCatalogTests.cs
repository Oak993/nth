using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Updates;

[Trait("Feature", "UE-3")]
[Trait("Rule", "R-TEST-4")]
public sealed class DevToolUpdateCatalogTests
{
    [Fact]
    public async Task Recorded_inventory_yields_only_Git_Node_two_Pythons_and_Office_rows()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list"); replay.Add("12-gh-list"); replay.Add("11-gh-before"); replay.Add("16-node-list"); replay.Add("24-python-list"); replay.Add("23-python-launcher");
        var office = TrialHFixtures.Office("34-office-before");
        var items = await DevToolUpdateCatalog.Discover(replay.Run, _ => Task.FromResult<OfficeInstallation?>(office));
        Assert.Equal(["dev:Git.Git", "dev:OpenJS.NodeJS.22", "dev:Python.Python.3.13", "dev:Python.Python.3.14", "dev:office"], items.Select(i => i.Id));
        Assert.All(items, i => Assert.Equal(UpdateItemKind.DevTool, i.Kind)); replay.Complete();
    }

    [Fact]
    public async Task Default_catalog_cannot_reach_any_real_tool() => await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolUpdateCatalog.Discover());

    [Fact]
    public async Task A_portable_Git_without_a_winget_match_gets_an_installed_row_but_never_an_install_command()
    {
        // Synthetic missing-match inventory; the version probe replays the real recorded Git output.
        var replay = new TrialHReplay();
        replay.Add("12-gh-list", TrialHFixtures.Command("04-git-list")); replay.Add("03-git-before");
        replay.Add("12-gh-list"); replay.Add("11-gh-before");
        replay.Add("12-gh-list", TrialHFixtures.Command("16-node-list"));
        replay.Add(new DevToolCommand("node", ["--version"]), new DevToolCommandResult(null, "", "not found"));
        replay.Add("12-gh-list", TrialHFixtures.Command("24-python-list"));
        replay.Add(new DevToolCommand("py", ["-0p"]), new DevToolCommandResult(null, "", "not found"));
        replay.Add(new DevToolCommand("where.exe", ["python"]), new DevToolCommandResult(1, "", "not found"));
        var items = await DevToolUpdateCatalog.Discover(replay.Run, _ => Task.FromResult<OfficeInstallation?>(null));
        var item = Assert.Single(items); Assert.Equal("Git", item.Title);
        Assert.Equal("2.54.0.windows.1", await item.InstalledVersion(default)); Assert.Null(await item.LatestVersion(default));
        Assert.False((await item.Update(new DevToolProgress(), default)).Succeeded);
        Assert.DoesNotContain(replay.Calls, c => c.Arguments.Contains("upgrade", StringComparer.Ordinal)); replay.Complete();
    }

    [Fact]
    public async Task Installed_Office_with_a_missing_client_is_retained_as_an_installed_row()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list"); replay.Add("12-gh-list"); replay.Add("11-gh-before"); replay.Add("16-node-list"); replay.Add("24-python-list"); replay.Add("23-python-launcher");
        var office = TrialHFixtures.Office("34-office-before") with { ClientExists = false };
        var items = await DevToolUpdateCatalog.Discover(replay.Run, _ => Task.FromResult<OfficeInstallation?>(office));
        var item = items.Single(i => i.Id == "dev:office");
        Assert.Equal(office.Version, await item.InstalledVersion(default));
        Assert.False((await item.Update(new DevToolProgress(), default)).Succeeded); replay.Complete();
    }

    [Fact]
    public async Task A_source_failure_is_not_an_empty_installed_inventory()
    {
        var replay = new TrialHReplay(); replay.Add(TrialHFixtures.Command("04-git-list"), new(-1, "offline", "source failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolUpdateCatalog.Discover(replay.Run, _ => Task.FromResult<OfficeInstallation?>(null)));
        replay.Complete();
    }

    [Fact]
    public async Task Unmatched_registered_Python_installs_are_retained_without_trying_to_install_them()
    {
        // Synthetic missing package match; the legacy launcher's recorded registration and versions identify installed runtimes.
        var replay = new TrialHReplay(); replay.Add("04-git-list"); replay.Add("12-gh-list"); replay.Add("11-gh-before"); replay.Add("16-node-list");
        replay.Add("12-gh-list", TrialHFixtures.Command("24-python-list")); replay.Add("23-python-launcher");
        replay.Add("26-python314-direct-before", new DevToolCommand("C:\\Python314\\python.exe", ["--version"]));
        replay.Add("30-python313-before", new DevToolCommand("%USERPROFILE%\\AppData\\Local\\Programs\\Python\\Python313\\python.exe", ["--version"]));
        var items = await DevToolUpdateCatalog.Discover(replay.Run, _ => Task.FromResult<OfficeInstallation?>(null));
        var python = items.Where(i => i.Title.StartsWith("Python", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, python.Length);
        Assert.Equal("3.14.4", await python[0].InstalledVersion(default)); Assert.Equal("3.13.14", await python[1].InstalledVersion(default));
        foreach (var item in python) Assert.False((await item.Update(new DevToolProgress(), default)).Succeeded);
        Assert.DoesNotContain(replay.Calls, c => c.Arguments.Contains("upgrade", StringComparer.Ordinal)); replay.Complete();
    }

    [Fact]
    public async Task Python_without_the_optional_launcher_is_detected_without_running_a_Store_alias()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list"); replay.Add("12-gh-list"); replay.Add("11-gh-before"); replay.Add("16-node-list");
        replay.Add("12-gh-list", TrialHFixtures.Command("24-python-list"));
        replay.Add(new DevToolCommand("py", ["-0p"]), new DevToolCommandResult(null, "", "not found"));
        replay.Add("21-python-path");
        replay.Add("30-python313-before", new DevToolCommand("%USERPROFILE%\\AppData\\Local\\Programs\\Python\\Python313\\python.exe", ["--version"]));
        var items = await DevToolUpdateCatalog.Discover(replay.Run, _ => Task.FromResult<OfficeInstallation?>(null));
        var python = Assert.Single(items, i => i.Title.StartsWith("Python", StringComparison.Ordinal));
        Assert.Equal("3.13.14", await python.InstalledVersion(default));
        Assert.DoesNotContain(replay.Calls, c => c.Executable.Contains("WindowsApps", StringComparison.Ordinal)); replay.Complete();
    }

    [Fact]
    public async Task A_matched_Python_and_an_unmatched_registered_minor_both_get_rows_without_duplicates()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list"); replay.Add("12-gh-list"); replay.Add("11-gh-before"); replay.Add("16-node-list");
        var inventory = TrialHFixtures.Result("24-python-list");
        var partial = string.Join('\n', inventory.Stdout.Split('\n').Where(line => !line.Contains("Python.Python.3.14", StringComparison.Ordinal)));
        replay.Add(TrialHFixtures.Command("24-python-list"), inventory with { Stdout = partial }); replay.Add("23-python-launcher");
        replay.Add("26-python314-direct-before", new DevToolCommand("C:\\Python314\\python.exe", ["--version"]));
        var items = await DevToolUpdateCatalog.Discover(replay.Run, _ => Task.FromResult<OfficeInstallation?>(null));
        var python = items.Where(i => i.Title.StartsWith("Python", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, python.Length); Assert.Single(python, i => i.Id == "dev:Python.Python.3.13");
        Assert.Single(python, i => i.Id != "dev:Python.Python.3.13"); replay.Complete();
    }

    [Fact]
    public async Task An_installed_GitHub_CLI_is_discovered_but_its_untrialled_update_is_blocked()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        var command = TrialHFixtures.Command("12-gh-list");
        var result = new DevToolCommandResult(0, "Name       Id         Version Available Source\r\n---------------------------------------------\r\nGitHub CLI GitHub.cli 2.100.0 2.102.0   winget\r\n", "");
        replay.Add(command, result); replay.Add("16-node-list"); replay.Add("24-python-list"); replay.Add("23-python-launcher");
        var items = await DevToolUpdateCatalog.Discover(replay.Run, _ => Task.FromResult<OfficeInstallation?>(null));
        var gh = items.Single(i => i.Id == "dev:GitHub.cli"); replay.Add(command, result);
        Assert.False((await gh.Update(new DevToolProgress(), default)).Succeeded);
        Assert.DoesNotContain(replay.Calls, c => c.Arguments.Contains("upgrade", StringComparer.Ordinal)); replay.Complete();
    }

    [Fact]
    public void All_command_fixtures_have_capture_metadata_and_redacted_profile_paths()
    {
        Assert.True(TrialHFixtures.Names.Count >= 44);
        foreach (var name in TrialHFixtures.Names)
        {
            using var doc = TrialHFixtures.Read(name); var r = doc.RootElement;
            Assert.Equal(1, r.GetProperty("schemaVersion").GetInt32());
            Assert.NotEmpty(r.GetProperty("commandLine").GetString()!);
            Assert.True(r.GetProperty("durationMs").GetDouble() >= 0);
            Assert.NotEmpty(r.GetProperty("timestamp").GetString()!);
            Assert.EndsWith("+00:00", r.GetProperty("timestamp").GetString()!, StringComparison.Ordinal);
            Assert.True(r.TryGetProperty("exitCode", out _)); Assert.True(r.TryGetProperty("versionsBefore", out _)); Assert.True(r.TryGetProperty("versionsAfter", out _));
            Assert.DoesNotMatch(@"(?i)C:\\+Users\\+(?!Public\\+)", r.GetRawText());
        }
    }
}
