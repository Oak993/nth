using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra review finding (DESIGN.md rev 5, 12.1a "nth's own settings and grants", 12.3 point 2; ED-NEW-dextra-f4-1): nth's own files are never
// written by an agent of Dextra's, whichever way Dextra was started. library.json and UserDefaults.json (Dextra's switches, grants, delegate scope
// and stop chord) sit in nth's data root, beside the store and the activity log, and were in no protected root: a mission's write to them was
// "writes outside the access folders" (a card, or a folder grant's yes) instead of a hard deny. Both start paths (the window's, the launch's for
// the duties) and the delegate's view of an owner's chat (AS-22) now refuse them, and a mission's own worktree inside the store is still its work.
// ED-NEW-dextra-f4-2: so are nth's own program files (the running app's install, Velopack's root when Velopack installed it) and the data folder
// of before the rename (%LOCALAPPDATA%\Marrow Code). Fakes and throwaway folders only; no provider runs.
[Trait("Decision", "ED-NEW-dextra-f4-1")]
[Trait("Feature", "AS-5")]
public sealed class NthOwnFilesTests
{
    private static ApprovalRequest Write(string id, string path) =>
        new(id, ApprovalRequest.Kind.FileChange, path, new ValueList<ApprovalRequest.Option>([
            new("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ]));

    /// <summary>
    /// nth's own files in the data root the test bound, the settings file where the app keeps it, the running app's own program (this test
    /// run's binaries: no Velopack install around them) and the data folder of before the rename.
    /// </summary>
    private static IReadOnlyList<string> NthFiles() =>
    [
        Storage.LibraryURL,
        Path.Combine(Storage.Root, "UserDefaults.json"),
        Path.Combine(Storage.Root, "mcp.json"),
        SettingsStore.ProductionPath(DevDataRoot.LocalApplicationData()),
        Path.Combine(AppContext.BaseDirectory, "Marrow.App.exe"),
        Path.Combine(AppContext.BaseDirectory, "Marrow.App.dll"),
        Path.Combine(DevDataRoot.LocalApplicationData(), AppInfo.LegacyDataFolderName, "UserDefaults.json"),
    ];

    /// <summary>A mission at work in its worktree under the store, its lead chat on record when <paramref name="model"/> is given.</summary>
    private static MissionRecord Working(AssistantController controller, AssistantStore store, Project project, Func<Action, bool> onMain, AppModel? model = null)
    {
        var mission = Guid.NewGuid();
        var lead = Guid.NewGuid();
        var worktree = store.MissionWorktreeFolder(MissionJournal.StoreId(mission));
        Directory.CreateDirectory(worktree);
        var record = new MissionRecord(new Mission(mission, "Fix the login timeout", "dextra", MissionOrigin.Owner, MissionState.Working) { ThreadIds = new([lead]) })
        {
            Brief = "Make it 30 s.",
            ProjectId = project.Id,
            ProjectPath = project.Path,
            LeadThread = lead,
            Created = DateTimeOffset.UtcNow,
            Workspace = new MissionWorkspace(MissionWorkspaceKind.Worktree, project.Path, worktree) { WorkingFolder = worktree },
        };
        onMain(() =>
        {
            model?.InsertThread(new ChatThread(project.Id, ProviderKind.Claude, "model-a", null, SealedProviderConfig.CappedMode)
            {
                Id = lead, MissionID = mission, Title = "Lead", WorktreePath = worktree,
            });
            controller.Runner.Load([record]);
        });
        return record;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-f4-2")]
    public void The_window_start_refuses_a_missions_write_to_nths_settings_and_library_and_its_own_worktree_stays_its_work()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        // The production layout: the store and the activity log under nth's data root, as DextraHost.Production opens them.
        var roots = new DefaultPathAssistantStorageRoots();
        using var store = AssistantStore.Open(roots, new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted });
        var audit = new List<AuditEntry>();
        var autoAnswer = AutoAnswerSwitch.AfterCorpus(() => DextraTaintRig.Pass);
        Assert.True(MissionRig.Within(autoAnswer.Ready));
        var controller = h.Main.OnMain(() => model.StartDextra(store, e =>
        {
            lock (audit) audit.Add(e);
        }, () => [new AssistantProfile("dextra", "Dextra")], protectedRoots: [roots.AuditDirectory], autoAnswer: autoAnswer));
        try
        {
            var record = Working(controller, store, project, body => { h.Main.OnMain(body); return true; }, model);
            var lead = record.LeadThread!.Value;
            AuditEntry Asked(string id, string path)
            {
                Assert.True(h.Main.OnMain(() => controller.Runner.OnApproval(lead, Write(id, path))));
                lock (audit) return audit.Last(e => e.Tool == "approval");
            }

            var n = 0;
            foreach (var file in NthFiles())
            {
                var line = Asked("nth-" + n++, file);
                Assert.Equal(nameof(ActionClass.HardDeny), line.ActionClass);
                Assert.StartsWith("refused: ", line.Result, StringComparison.Ordinal);
                Assert.Empty(controller.Queue.Items);
            }
            Assert.Equal(nameof(ActionClass.HardDeny), Asked("audit", Path.Combine(roots.AuditDirectory, "2026-10-13.jsonl")).ActionClass);
            Assert.Equal(nameof(ActionClass.HardDeny), Asked("store", Path.Combine(store.Root, "missions", "x.json")).ActionClass);

            // Its own worktree, inside the store inside nth's data root, is still plain work, and a folder of the owner's still asks.
            Assert.Equal(nameof(ActionClass.Work), Asked("work", Path.Combine(record.Workspace!.Path, "src", "a.cs")).ActionClass);
            Assert.Equal(nameof(ActionClass.WriteElsewhere), Asked("owner", Path.Combine(h.NewFolder("owner"), "notes.txt")).ActionClass);
            Assert.Equal("owner", Assert.Single(controller.Queue.Items).RequestId);
        }
        finally
        {
            h.Main.OnMain(model.UnbindDextra);
            controller.Dispose();
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-f4-2")]
    public void The_launch_start_for_the_duties_refuses_them_as_well()
    {
        using var temp = new AssistantTempRoot();
        using (var seed = AssistantStore.Open(temp.Roots))
            seed.SaveDuties("dextra", DutyJson.Write(new DutyDocument([new Duty("d1", "Weekly note", "dextra", new ManualTrigger(), "Write the weekly note.")])));
        using var launch = new BackgroundLaunchRig(temp);
        Assert.NotNull(launch.Run(new StartupMode(true, null, null), out var problem));
        Assert.Null(problem);
        var controller = launch.Model.Dextra!;
        var store = launch.Model.DextraStore!;
        var project = AppModelSamples.Project("Project", Path.Combine(temp.Folder, "project"));
        var record = Working(controller, store, project, body => launch.OnMain(() => { body(); return true; }));
        var lead = record.LeadThread!.Value;
        DecisionItem? CardOf(string id, string path)
        {
            Assert.True(launch.OnMain(() => controller.Runner.OnApproval(lead, Write(id, path))));
            return controller.Queue.Items.FirstOrDefault(card => card.RequestId == id);
        }

        var n = 0;
        foreach (var file in NthFiles()) Assert.Null(CardOf("nth-" + n++, file));                  // refused: nobody is asked
        Assert.Null(CardOf("audit", Path.Combine(temp.Roots.AuditDirectory, "2026-10-13.jsonl")));
        var elsewhere = Path.Combine(temp.Folder, "owner", "notes.txt");
        Assert.Equal(ActionClass.WriteElsewhere, CardOf("owner", elsewhere)?.Class);                  // the same path still asks for a folder of the owner's
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-f4-2")]
    public void The_delegate_leaves_an_owners_chat_writing_nths_files_to_the_chat_and_never_raises_a_card_for_it()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        rig.H.Main.OnMain(() =>
        {
            rig.App.AssistantHost = DextraHosts.Make(rig.App);
            rig.App.AssistantHost.Settings.Update(p => p with { FirstRunDone = true, Delegate = DelegateScope.Everywhere });
        });
        var autoAnswer = AutoAnswerSwitch.AfterCorpus(() => DextraTaintRig.Pass);
        Assert.True(MissionRig.Within(autoAnswer.Ready));
        var controller = rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => [new AssistantProfile("dextra", "Dextra")], autoAnswer: autoAnswer));
        try
        {
            var n = 0;
            foreach (var file in NthFiles())
            {
                var id = "nth-" + n++;
                rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.Approval(Write(id, file))));
                Assert.Empty(controller.Queue.Items);                                                  // a hard deny is nobody's to answer
                Assert.Empty(rig.Session.ApprovalAnswers);
                Assert.Contains(rig.H.Main.OnMain(() => rig.Runtime.Approvals), a => a.Id == id);       // the chat keeps its own card
            }

            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.Approval(Write("owner", Path.Combine(temp.Folder, "owner", "notes.txt")))));
            Assert.Equal(("owner", ActionClass.WriteElsewhere), (Assert.Single(controller.Queue.Waiting).RequestId, Assert.Single(controller.Queue.Waiting).Class));
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-f4-2")]
    public void A_Velopack_install_is_protected_from_its_root_and_a_build_run_from_its_output_folder_from_that_folder()
    {
        using var temp = new AssistantTempRoot();
        var root = Path.Combine(temp.Folder, "NthApp");
        var current = Path.Combine(root, "current");
        Directory.CreateDirectory(current);
        Assert.Equal(current, AppModel.InstallRoot(current + Path.DirectorySeparatorChar));            // no Update.exe beside it: its own folder

        File.WriteAllText(Path.Combine(root, "Update.exe"), "");
        Assert.Equal(root, AppModel.InstallRoot(current + Path.DirectorySeparatorChar));               // Velopack's root: Update.exe, current and packages
        Assert.Equal(root, AppModel.InstallRoot(current));

        var context = new ClassifierContext { ProtectedRoots = new ValueList<string>([AppModel.InstallRoot(current)]) };
        foreach (var file in new[] { Path.Combine(root, "Update.exe"), Path.Combine(root, "packages", "NthApp-1.0.0-full.nupkg"), Path.Combine(current, "Marrow.App.dll") })
            Assert.Equal(ActionClass.HardDeny, ActionClassifier.ClassifyApproval(Write("w", file), context, provider: ProviderKind.Claude).Class);
        Assert.Equal(ActionClass.WriteElsewhere, ActionClassifier.ClassifyApproval(Write("w", Path.Combine(temp.Folder, "NthApps", "a.txt")), context, provider: ProviderKind.Claude).Class);
    }
}
