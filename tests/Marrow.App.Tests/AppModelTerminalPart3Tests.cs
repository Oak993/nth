using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests;

[Collection("Headless")]
public sealed class AppModelTerminalPart3Tests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-STORE-034")] // App/AppModel.swift:976-981, :1070-1077
    [Trait("Req", "PR-TERM-055")] // App/AppModel.swift:1070-1077
    public void Bulk_discard_drops_the_runtime_closes_every_terminal_and_removes_documents_in_one_bulk_step() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        h.UseFakeTerminalShell(); // the step reads the sessions' isRunning, which Terminate sets at once; no process is read
        var project = AppModelSamples.Project("Not loaded", h.NewFolder("not loaded"));
        var archived = AppModelSamples.Thread(project) with { IsArchived = true };
        var kept = AppModelSamples.Thread(project);
        h.WriteLibrary([], [archived, kept]);
        AppModelHarness.WriteDocument(archived.Id);
        AppModelHarness.WriteDocument(kept.Id);
        var model = new AppModel();
        var runtime = model.Runtime(archived.Id);
        var session = model.Terminals.Open(archived.Id, h.NewFolder("archived terminal"));
        try
        {
            Assert.Same(runtime, model.ExistingRuntime(archived.Id));

            model.DeleteArchivedThreads();

            Assert.Null(model.ExistingRuntime(archived.Id));
            Assert.Empty(model.Terminals.Sessions(archived.Id));
            Assert.False(session.IsRunning);
            Assert.False(File.Exists(Storage.ThreadURL(archived.Id)));
            Assert.True(File.Exists(Storage.ThreadURL(kept.Id)));
        }
        finally
        {
            model.Terminals.TerminateAll();
        }
    });

    [Fact]
    [Trait("Req", "PR-TERM-057")] // App/AppModel.swift:493-499
    public void An_exited_shell_still_blocks_idle_runtime_release_until_its_tab_is_closed() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        var project = AppModelSamples.Project("Project", h.NewFolder("idle project"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = new AppModel();
        var runtime = model.Runtime(thread.Id);
        var directory = h.NewFolder("exited terminal");
        model.Terminals.Run(new ProjectScript("Exit", "exit 0"), thread.Id, directory);
        var session = Assert.Single(model.Terminals.Sessions(thread.Id));
        Assert.True(UiPump.Until(() => !session.IsRunning, AppModelHarness.Bound));
        Assert.Single(model.Terminals.Sessions(thread.Id));

        Assert.False(model.ReleaseIdleRuntime(thread.Id));
        Assert.Same(runtime, model.ExistingRuntime(thread.Id));

        model.Terminals.Close(session);

        Assert.True(model.ReleaseIdleRuntime(thread.Id));
        Assert.Null(model.ExistingRuntime(thread.Id));
        model.Terminals.TerminateAll();
    });

    [Fact]
    [Trait("Req", "PR-TERM-065")] // App/AppModel.swift:1094-1098; App/Views/Terminal/TerminalStore.swift:127-129
    public void Running_a_script_opens_a_new_named_tab_in_the_worktree_or_project_and_shows_the_terminal() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        var projectPath = h.NewFolder("project script root");
        var worktreePath = h.NewFolder("thread worktree");
        var script = new ProjectScript("Build", "Set-Content -Path 'ran.txt' -Value 'ran' -Encoding utf8");
        var project = AppModelSamples.Project("Project", projectPath) with { Scripts = new ValueList<ProjectScript>([script]) };
        var projectThread = AppModelSamples.Thread(project);
        var worktreeThread = AppModelSamples.Thread(project) with { WorktreePath = worktreePath };
        h.WriteLibrary([project], [projectThread, worktreeThread]);
        var model = new AppModel();

        try
        {
            for (var i = 0; i < 5; i++) model.RunScript(script, projectThread.Id);
            model.RunScript(script, worktreeThread.Id);

            var projectSessions = model.Terminals.Sessions(projectThread.Id);
            Assert.Equal(5, projectSessions.Count);
            Assert.All(projectSessions, session =>
            {
                Assert.Equal("Build", session.Title);
                Assert.Equal(projectPath, session.Directory);
            });
            Assert.Same(projectSessions[^1], model.Terminals.Selected(projectThread.Id));
            var worktreeSession = Assert.Single(model.Terminals.Sessions(worktreeThread.Id));
            Assert.Equal("Build", worktreeSession.Title);
            Assert.Equal(worktreePath, worktreeSession.Directory);
            Assert.Same(worktreeSession, model.Terminals.Selected(worktreeThread.Id));
            Assert.True(model.Runtime(projectThread.Id).IsTerminalVisible);
            Assert.True(model.Runtime(worktreeThread.Id).IsTerminalVisible);
            Assert.True(UiPump.Until(
                () => File.Exists(SwiftPath.AppendingPathComponent(projectPath, "ran.txt"))
                      && File.Exists(SwiftPath.AppendingPathComponent(worktreePath, "ran.txt")),
                AppModelHarness.Bound));
        }
        finally
        {
            model.Terminals.TerminateAll();
        }
    });

    [Fact]
    [Trait("Req", "PR-TERM-066")] // App/AppModel.swift:1116-1137
    [Trait("Decision", "ED-NEW-shellleak-1")] // its cleanup proves TerminateAll ends every real shell before anything kills them
    public async Task Worktree_creation_runs_flagged_scripts_in_project_order_at_the_new_path()
    {
        Task? scenario = null;
        headless.OnUiThread(() =>
        {
            scenario = RunScenario();
            // Git.Run captures this dispatcher's synchronization context; keep the UI thread pumping through awaits.
            Assert.True(UiPump.Until(() => scenario?.IsCompleted == true, AppModelHarness.Bound));
        });
        await Assert.IsAssignableFrom<Task>(scenario);

        async Task RunScenario()
        {
            using var h = new AppModelHarness(withRuntime: false);
            var repository = AppModelGitRepo.WithCommit(h.NewFolder("script worktree repo"));
            var project = AppModelSamples.Project("Script Project", repository.Root) with
            {
                Scripts = new ValueList<ProjectScript>([
                    new ProjectScript("First", "Set-Content -Path 'first.txt' -Value 'first' -Encoding utf8", runOnWorktreeCreate: true),
                    new ProjectScript("Skipped", "Set-Content -Path 'skipped.txt' -Value 'skipped' -Encoding utf8"),
                    new ProjectScript("Second", "Set-Content -Path 'second.txt' -Value 'second' -Encoding utf8", runOnWorktreeCreate: true),
                ]),
            };
            var thread = AppModelSamples.Thread(project);
            h.WriteLibrary([project], [thread]);
            var model = new AppModel();
            string? worktreePath = null;

            try
            {
                await model.CreateWorktree(thread.Id);
                worktreePath = model.Thread(thread.Id)?.WorktreePath;
                Assert.False(string.IsNullOrEmpty(worktreePath));
                Assert.True(Directory.Exists(worktreePath));
                var sessions = model.Terminals.Sessions(thread.Id);
                Assert.Equal(new[] { "First", "Second" }, sessions.Select(session => session.Title));
                Assert.All(sessions, session => Assert.Equal(worktreePath, session.Directory));
                Assert.True(model.Runtime(thread.Id).IsTerminalVisible);
                Assert.True(UiPump.Until(
                    () => File.Exists(SwiftPath.AppendingPathComponent(worktreePath!, "first.txt"))
                          && File.Exists(SwiftPath.AppendingPathComponent(worktreePath!, "second.txt")),
                    AppModelHarness.Bound));
                Assert.False(File.Exists(SwiftPath.AppendingPathComponent(worktreePath!, "skipped.txt")));
                Assert.All(sessions, session =>
                {
                    Assert.True(session.IsRunning);
                    Assert.True(session.View.IsRunning);
                });
            }
            finally
            {
                var processIds = model.Terminals.Sessions(thread.Id)
                    .Select(session => session.View.ProcessId)
                    .ToArray();
                model.Terminals.TerminateAll();
                // ED-NEW-shellleak-1: TerminateAll alone ends every shell. Proof first; the kill after it is cleanup only.
                var shellsEnded = UiPump.Until(
                    () => processIds.All(processId => !ShellProbe.IsAlive(processId)),
                    AppModelHarness.Bound);
                foreach (var processId in processIds) ShellProbe.KillIfAlive(processId);
                Assert.True(shellsEnded, "TerminateAll left a shell running");
                if (worktreePath is { } path && Directory.Exists(path))
                    repository.Git("worktree", "remove", "--force", path);
            }
        }
    }

    [Fact]
    [Trait("Req", "PR-TERM-066")] // App/AppModel.swift:1116-1137
    [Trait("Req", "PR-GIT-110")] // App/AppModel.swift:1117-1122
    public async Task Worktree_creation_without_a_commit_does_not_run_flagged_scripts()
    {
        Task? scenario = null;
        headless.OnUiThread(() =>
        {
            scenario = RunScenario();
            // Git.Run captures this dispatcher's synchronization context; keep the UI thread pumping through awaits.
            Assert.True(UiPump.Until(() => scenario?.IsCompleted == true, AppModelHarness.Bound));
        });
        await Assert.IsAssignableFrom<Task>(scenario);

        async Task RunScenario()
        {
            using var h = new AppModelHarness(withRuntime: false);
            var repository = AppModelGitRepo.Empty(h.NewFolder("no commit script repo"));
            var project = AppModelSamples.Project("Script Project", repository.Root) with
            {
                Scripts = new ValueList<ProjectScript>([
                    new ProjectScript("Must not run", "Set-Content -Path 'unexpected.txt' -Value 'unexpected'; exit 0", runOnWorktreeCreate: true),
                ]),
            };
            var thread = AppModelSamples.Thread(project);
            h.WriteLibrary([project], [thread]);
            var model = new AppModel();

            try
            {
                await model.CreateWorktree(thread.Id);

                Assert.Equal("Worktree unavailable", model.Alert?.Title);
                Assert.Equal("New worktrees need a git repository with at least one commit.", model.Alert?.Message);
                Assert.Empty(model.Terminals.Sessions(thread.Id));
                Assert.Null(model.Thread(thread.Id)?.WorktreePath);
                Assert.Null(model.Thread(thread.Id)?.Branch);
                Assert.False(File.Exists(Path.Combine(repository.Root, "unexpected.txt")));
            }
            finally
            {
                model.Terminals.TerminateAll();
            }
        }
    }
}
