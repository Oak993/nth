using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel.swift (B2 1fbf81e), :129-140, :148-177, :440-445, :515-523, :1089-1092,
// :1331-1355.
public sealed class AppModelPart3Tests
{
    [Fact]
    [Trait("Req", "PR-STORE-085")] // App/AppModel.swift:1331-1344
    public void Project_file_reader_keeps_only_scripts_with_string_names_and_commands_and_requires_boolean_flags()
    {
        using var h = new AppModelHarness();
        var directory = h.NewFolder("project scripts");
        AppModelPart3Fixtures.WriteScripts(directory,
            """
            {
              "scripts": [
                { "name": "Tests", "command": "dotnet test", "icon": "test", "runOnWorktreeCreate": true },
                { "name": 7, "command": "skip name" },
                { "name": "Missing command" },
                { "name": "Wrong command type", "command": false },
                { "name": "String flag", "command": "echo string", "runOnWorktreeCreate": "true" },
                { "name": "Number flag", "command": "echo number", "runOnWorktreeCreate": 1 },
                { "name": "Missing flag", "command": "echo missing" }
              ]
            }
            """);

        var scripts = ProjectFile.Scripts(AppModelPart3Fixtures.ProjectURL(directory));

        Assert.Equal(4, scripts.Count);
        Assert.Equal("Tests", scripts[0].Name);
        Assert.Equal("dotnet test", scripts[0].Command);
        Assert.Equal("checkmark.seal", scripts[0].Symbol);
        Assert.True(scripts[0].RunOnWorktreeCreate);
        Assert.Equal("String flag", scripts[1].Name);
        Assert.Equal("echo string", scripts[1].Command);
        Assert.False(scripts[1].RunOnWorktreeCreate);
        Assert.Equal("Number flag", scripts[2].Name);
        Assert.Equal("echo number", scripts[2].Command);
        Assert.False(scripts[2].RunOnWorktreeCreate);
        Assert.Equal("Missing flag", scripts[3].Name);
        Assert.Equal("echo missing", scripts[3].Command);
        Assert.False(scripts[3].RunOnWorktreeCreate);
        Assert.Equal("play", scripts[3].Symbol);
    }

    [Fact]
    [Trait("Req", "PR-STORE-085")] // App/AppModel.swift:1332-1344
    public void Project_file_reader_returns_no_scripts_for_missing_unreadable_or_non_array_data()
    {
        using var h = new AppModelHarness();
        var directory = h.NewFolder("empty project scripts");
        var url = AppModelPart3Fixtures.ProjectURL(directory);

        Assert.Empty(ProjectFile.Scripts(url));

        AppModelPart3Fixtures.WriteScripts(directory, "{");
        Assert.Empty(ProjectFile.Scripts(url));

        AppModelPart3Fixtures.WriteScripts(directory, "{}");
        Assert.Empty(ProjectFile.Scripts(url));

        AppModelPart3Fixtures.WriteScripts(directory, """{"scripts": {"name": "wrong shape", "command": "echo"}}""");
        Assert.Empty(ProjectFile.Scripts(url));

        AppModelPart3Fixtures.WriteScripts(directory, """{"scripts": []}""");
        Assert.Empty(ProjectFile.Scripts(url));
    }

    [Fact]
    [Trait("Req", "PR-TERM-061")] // App/AppModel.swift:1346-1354
    public void Project_file_icon_keys_are_case_sensitive_and_every_other_value_uses_play()
    {
        Assert.Equal("checkmark.seal", ProjectFile.Symbol("test"));
        Assert.Equal("wand.and.stars", ProjectFile.Symbol("lint"));
        Assert.Equal("gearshape", ProjectFile.Symbol("configure"));
        Assert.Equal("hammer", ProjectFile.Symbol("build"));
        Assert.Equal("ant", ProjectFile.Symbol("debug"));
        Assert.Equal("play", ProjectFile.Symbol("Test"));
        Assert.Equal("play", ProjectFile.Symbol("unknown"));
        Assert.Equal("play", ProjectFile.Symbol(null));
    }

    [Fact]
    [Trait("Req", "PR-TERM-060")] // App/AppModel.swift:515-523, :1331-1344
    [Trait("Decision", "ED-049")]
    public void Adding_a_project_imports_root_scripts_once_and_readding_the_path_keeps_the_imported_values()
    {
        using var h = new AppModelHarness();
        var directory = h.NewFolder("import scripts");
        AppModelPart3Fixtures.WriteScripts(directory,
            """{"scripts": [{"name": "First", "command": "dotnet test", "runOnWorktreeCreate": true}]}""");
        var model = h.MakeModel();
        var url = AppModelPart3Fixtures.ProjectURL(directory);

        var added = h.Main.OnMain(() => model.AddProject(url));

        Assert.Single(added.Scripts);
        Assert.Equal("First", added.Scripts[0].Name);
        Assert.Equal("dotnet test", added.Scripts[0].Command);
        Assert.True(added.Scripts[0].RunOnWorktreeCreate);
        Assert.Equal("play", added.Scripts[0].Symbol);

        h.Main.Advance(TimeSpan.FromMilliseconds(400));
        DiskWriter.Shared.WaitForQueuedWrites();
        var savedScript = Assert.Single(Assert.Single(AppModelHarness.LoadedLibrary().Projects).Scripts);
        Assert.Equal("First", savedScript.Name);
        Assert.Equal("dotnet test", savedScript.Command);
        Assert.True(savedScript.RunOnWorktreeCreate);

        const string changedFile = """{"scripts": [{"name": "Changed", "command": "dotnet build"}]}""";
        AppModelPart3Fixtures.WriteScripts(directory, changedFile);
        var changedBytes = File.ReadAllBytes(SwiftPath.AppendingPathComponent(directory, "droppy-code.json"));
        var again = h.Main.OnMain(() => model.AddProject(url));

        Assert.Equal(added.Id, again.Id);
        Assert.Single(again.Scripts);
        Assert.Equal("First", again.Scripts[0].Name);
        Assert.Equal("dotnet test", again.Scripts[0].Command);
        Assert.Equal(1, h.Main.OnMain(() => model.Projects.Count));
        Assert.Equal(changedBytes, File.ReadAllBytes(SwiftPath.AppendingPathComponent(directory, "droppy-code.json")));
    }

    [Fact]
    [Trait("Req", "PR-STORE-171")] // App/AppModel.swift:137-140, :148-177
    [Trait("Decision", "ED-070")]
    public void Initial_cells_are_observable_per_item_and_list_changes_write_only_changed_cells()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project cell"));
        var siblingProject = AppModelSamples.Project("Sibling project", h.NewFolder("sibling project cell"));
        var first = AppModelSamples.Thread(project) with { Title = "First" };
        var sibling = AppModelSamples.Thread(project) with { Title = "Sibling" };
        h.WriteLibrary([project, siblingProject], [first, sibling]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            var projectChanges = 0;
            var siblingProjectChanges = 0;
            var firstChanges = 0;
            var siblingChanges = 0;
            var projectRead = ObservationTracking.Track(() => model.Project(project.Id), () => projectChanges++);
            var siblingProjectRead = ObservationTracking.Track(() => model.Project(siblingProject.Id), () => siblingProjectChanges++);
            var firstRead = ObservationTracking.Track(() => model.Thread(first.Id), () => firstChanges++);
            var siblingRead = ObservationTracking.Track(() => model.Thread(sibling.Id), () => siblingChanges++);
            Assert.Equal("Project", projectRead.Value?.Name);
            Assert.Equal("Sibling project", siblingProjectRead.Value?.Name);
            Assert.Equal("First", firstRead.Value?.Title);
            Assert.Equal("Sibling", siblingRead.Value?.Title);

            model.UpdateProject(project.Id, value => value with { Name = "Project renamed" });

            Assert.Equal(1, projectChanges);
            Assert.Equal(0, siblingProjectChanges);
            Assert.Equal("Project renamed", model.Project(project.Id)?.Name);

            model.UpdateThread(first.Id, thread => thread with { Title = "First renamed" });

            Assert.Equal(1, firstChanges);
            Assert.Equal(0, siblingChanges);
            Assert.Equal("First renamed", model.Thread(first.Id)?.Title);

            var secondSiblingRead = ObservationTracking.Track(() => model.Thread(sibling.Id), () => siblingChanges++);
            var added = AppModelSamples.Thread(project) with { Title = "Added", IsArchived = true };
            model.InsertThread(added);
            var addedChanges = 0;
            var addedRead = ObservationTracking.Track(() => model.Thread(added.Id), () => addedChanges++);
            Assert.Equal("Added", addedRead.Value?.Title);

            model.UpdateThread(added.Id, thread => thread with { Title = "Added renamed" });

            Assert.Equal(0, siblingChanges);
            Assert.Equal(1, addedChanges);
            Assert.Equal("Added renamed", model.Thread(added.Id)?.Title);

            var removedChanges = 0;
            var removedRead = ObservationTracking.Track(() => model.Thread(added.Id), () => removedChanges++);
            Assert.NotNull(removedRead.Value);
            model.DeleteArchivedThreads();

            Assert.Equal(1, removedChanges);
            Assert.Null(model.Thread(added.Id));
            Assert.Equal(0, siblingChanges);
            projectRead.Tracker.Cancel();
            siblingProjectRead.Tracker.Cancel();
            firstRead.Tracker.Cancel();
            siblingRead.Tracker.Cancel();
            secondSiblingRead.Tracker.Cancel();
            addedRead.Tracker.Cancel();
            removedRead.Tracker.Cancel();
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-067")] // App/AppModel.swift:1089-1092
    public void Top_script_is_the_first_script_only_for_a_selected_thread_with_a_project()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("With scripts", h.NewFolder("with scripts")) with
        {
            Scripts = new ValueList<ProjectScript>([
                new ProjectScript("First", "dotnet test"),
                new ProjectScript("Second", "dotnet build"),
            ]),
        };
        var emptyProject = AppModelSamples.Project("No scripts", h.NewFolder("no scripts"));
        var firstThread = AppModelSamples.Thread(project);
        var emptyThread = AppModelSamples.Thread(emptyProject);
        h.WriteLibrary([project, emptyProject], [firstThread, emptyThread]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Null(model.TopScript);
            model.SelectedThreadID = emptyThread.Id;
            Assert.Null(model.TopScript);
            model.SelectedThreadID = firstThread.Id;
            Assert.Equal("First", model.TopScript?.Name);
            Assert.Equal("dotnet test", model.TopScript?.Command);
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-065")] // App/AppModel.swift:1094-1097
    public void Running_a_script_for_a_missing_thread_or_project_does_nothing()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("unresolved script project"));
        var thread = AppModelSamples.Thread(project) with { ProjectID = Guid.NewGuid() };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var script = new ProjectScript("Build", "throw 'must not run'");

        h.Main.OnMain(() =>
        {
            model.RunScript(script, Guid.NewGuid());
            model.RunScript(script, thread.Id);

            Assert.Empty(model.Terminals.Sessions(thread.Id));
            Assert.Null(model.ExistingRuntime(thread.Id));
            Assert.Null(model.Alert);
        });
    }
}
