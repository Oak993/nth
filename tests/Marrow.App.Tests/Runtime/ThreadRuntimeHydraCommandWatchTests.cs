using System.Collections;
using System.Reflection;
using System.Text;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// A native head's shell command is watched by the lead runtime (App/Runtime/ThreadRuntime.swift:3486-3512, watchHydraCommand
// and settleHydraCommand, B3) and the lead's own commands by watchCommand (:3382-3392); the one write classification decides
// whether either takes a before-tree (PR-TERM-099). The project is a real git checkout; git runs locally and nothing else does.
public sealed class ThreadRuntimeHydraCommandWatchTests
{
    private const string AgentID = "agent-1";
    private const string ToolID = "tool-1";
    private const string Key = AgentID + "-" + ToolID;

    private sealed record Setup(ThreadRuntimeTurnRig Rig, AppModelGitRepo Repo, string Tree);

    private static Setup Open()
    {
        AppModelGitRepo repo = null!;
        var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, hydraOn: true,
            settings: app => app.Settings.HydraIsolateHeads = false,
            projectFactory: harness => (repo = AppModelGitRepo.WithCommit(harness.NewFolder("project"))).Root);
        var tree = repo.Git("rev-parse", "HEAD^{tree}").Trim();
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.RehearseTurn("Work.");
            ThreadRuntimeReflection.Set(rig.Runtime, "latestTree", tree); // a tree known to predate the command
            rig.Runtime.Rehearse(new ProviderEvent.AgentStarted(new AgentSpawn { Id = AgentID, Description = "Edit it", Prompt = "brief", IsBackground = true }));
        });
        return new Setup(rig, repo, tree);
    }

    private static void Start(ThreadRuntimeTurnRig rig, string command, string tool = ToolID) => rig.H.Main.OnMain(() =>
        rig.Runtime.Rehearse(new ProviderEvent.AgentEvent(AgentID, new ProviderEvent.ToolStarted(tool, new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Command,
            Title = command,
        }))));

    private static void Finish(ThreadRuntimeTurnRig rig, string tool = ToolID) => rig.H.Main.OnMain(() =>
        rig.Runtime.Rehearse(new ProviderEvent.AgentEvent(AgentID, new ProviderEvent.ToolUpdated(tool, new ToolUpdate { Status = ToolCall.Status.Completed }))));

    private static Dictionary<string, string?> Watched(ThreadRuntimeTurnRig rig) =>
        ThreadRuntimeReflection.Get<Dictionary<string, string?>>(rig.Runtime, "hydraCommandTrees")!;

    private static long Epoch(ThreadRuntimeTurnRig rig) => ThreadRuntimeReflection.Get<long>(rig.Runtime, "treeEpoch");

    private static void WaitForSettle(ThreadRuntimeTurnRig rig, string key)
    {
        var settles = ThreadRuntimeReflection.Get<Dictionary<string, TaskHandle>>(rig.Runtime, "commandSettles")!;
        var settle = rig.H.Main.OnMain(() => settles[key]);
        rig.H.Main.Loop.RunUntil(() => settle.Completion.IsCompleted, AppModelHarness.Bound);
        Assert.True(settle.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-193")] // :3488-3494 the same four guards as the lead's own commands: a writer, not already watched, a repository, a before-tree
    [Trait("Req", "PR-TERM-099")] // :3489 a reader is not watched: no before-tree is stored and the epoch does not move
    public void A_native_heads_writing_command_is_watched_and_a_reading_one_is_not()
    {
        var (rig, repo, tree) = Open();
        using var _ = rig;

        var epoch = rig.H.Main.OnMain(() => Epoch(rig));
        Start(rig, "git status", "reader");
        rig.H.Main.OnMain(() =>
        {
            Assert.Empty(Watched(rig)); // a command that cannot write needs no snapshots at all
            Assert.Equal(epoch, Epoch(rig));
        });

        Start(rig, "printf 'x\\n' >> a.txt");
        rig.H.Main.OnMain(() =>
        {
            var before = Assert.Single(Watched(rig));
            Assert.Equal(Key, before.Key);
            Assert.Equal(tree, before.Value); // the tree that stood before the command
            Assert.Equal(epoch + 1, Epoch(rig));
            Assert.Null(ThreadRuntimeReflection.Get<object>(rig.Runtime, "settledTree"));
        });
        GC.KeepAlive(repo);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-193")] // :3489-3490 already watched under that key, or no before-tree to start from: nothing is stored
    public void A_command_already_watched_or_with_no_known_before_tree_is_not_watched_again()
    {
        var (rig, repo, tree) = Open();
        using var _ = rig;
        Start(rig, "printf 'x\\n' >> a.txt");
        var epoch = rig.H.Main.OnMain(() => Epoch(rig));

        ThreadRuntimeReflection.Set(rig.Runtime, "latestTree", "another-tree");
        Start(rig, "printf 'y\\n' >> a.txt"); // the same key
        rig.H.Main.OnMain(() =>
        {
            Assert.Equal(epoch, Epoch(rig));
            Assert.Equal(tree, Assert.Single(Watched(rig)).Value);
        });

        ThreadRuntimeReflection.Set(rig.Runtime, "latestTree", null);
        ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", null); // no snapshot of any kind to start from
        Start(rig, "printf 'z\\n' >> a.txt", "tool-2");
        rig.H.Main.OnMain(() => Assert.Single(Watched(rig)));
        GC.KeepAlive(repo);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-193")] // :3496-3511 the settle adds the changed paths (no empty ones, no build output) to hydraTouched and the turn's touched paths, and produces no edits on a row; it is registered so finishTurn waits for it
    [Trait("Req", "PR-TERM-099")] // :3489 the writer answer made the snapshot, so the command's files are reported
    public void A_settled_heads_command_files_go_to_the_lead_turn_and_not_to_a_row()
    {
        var (rig, repo, tree) = Open();
        using var _ = rig;
        Start(rig, "printf 'x\\n' >> a.txt");
        File.WriteAllBytes(Path.Combine(repo.Root, "a.txt"), Encoding.UTF8.GetBytes("a\nafter\n"));
        Directory.CreateDirectory(Path.Combine(repo.Root, "Cache.noindex"));
        File.WriteAllBytes(Path.Combine(repo.Root, "Cache.noindex", "module.o"), [1, 2, 3]); // build output a head leaves behind

        Finish(rig);

        rig.H.Main.OnMain(() =>
        {
            Assert.Empty(Watched(rig)); // the before-tree was taken
            Assert.True(ThreadRuntimeReflection.Get<Dictionary<string, TaskHandle>>(rig.Runtime, "commandSettles")!.ContainsKey(Key)); // registered with the lead's own settles
        });
        WaitForSettle(rig, Key);

        rig.H.Main.OnMain(() =>
        {
            Assert.Equal(["a.txt"], ThreadRuntimeReflection.Get<HashSet<string>>(rig.Runtime, "hydraTouched")!.Order());
            Assert.Equal(["a.txt"], rig.Runtime.HydraCurrentTurnTouchedPaths.Order());
            // No row of the lead or of the head gained edits.
            Assert.All(rig.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.Tool>(),
                tool => Assert.Empty(tool.Value.Edits));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-193")] // :3496-3498 a settle with nothing watched under that key does nothing
    public void Settling_a_command_that_was_never_watched_does_nothing()
    {
        var (rig, repo, tree) = Open();
        using var _ = rig;

        Finish(rig, "never-started");

        rig.H.Main.OnMain(() =>
        {
            Assert.False(ThreadRuntimeReflection.Get<Dictionary<string, TaskHandle>>(rig.Runtime, "commandSettles")!.ContainsKey(AgentID + "-never-started"));
            Assert.Empty(ThreadRuntimeReflection.Get<HashSet<string>>(rig.Runtime, "hydraTouched")!);
        });
        GC.KeepAlive(repo);
    }

    [Fact]
    [Trait("Req", "PR-TERM-099")] // :3382-3392 the lead's own watchCommand: a reader takes no snapshot, a writer stores one and bumps the epoch
    public void The_leads_own_command_is_watched_only_when_it_may_write()
    {
        var (rig, repo, tree) = Open();
        using var _ = rig;
        var commandTrees = ThreadRuntimeReflection.Get<Dictionary<string, string?>>(rig.Runtime, "commandTrees")!;
        var epoch = rig.H.Main.OnMain(() => Epoch(rig));

        rig.H.Main.OnMain(() => rig.Runtime.Rehearse(new ProviderEvent.ToolStarted("reader", new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "ls -la" })));
        rig.H.Main.OnMain(() =>
        {
            Assert.Empty(commandTrees);
            Assert.Equal(epoch, Epoch(rig));
        });

        rig.H.Main.OnMain(() => rig.Runtime.Rehearse(new ProviderEvent.ToolStarted("writer", new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "git tag v1" })));
        rig.H.Main.OnMain(() =>
        {
            Assert.Equal("writer", Assert.Single(commandTrees).Key);
            Assert.Equal(epoch + 1, Epoch(rig));
        });
        GC.KeepAlive(repo);
    }

    // ---- PR-TERM-099's second consumer: Command Code's auto approval ----

    private static object CommandCodeSession(RuntimeMode mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "cc-term-099-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Commandcode,
            Executable = null,
            WorkingDirectory = SwiftURL.Create(new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri)!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = mode,
            InteractionMode = InteractionMode.Build,
        };
        return Activator.CreateInstance(typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, args: [configuration], culture: null)!;
    }

    private static bool AutoApproves(object session, string tool, JSONValue input) => (bool)session.GetType()
        .GetMethod("AutoApproves", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(session, [tool, input])!;

    [Theory]
    [InlineData("shell_command")]
    [InlineData("powershell")]
    [InlineData("monitor_command")]
    [Trait("Req", "PR-TERM-099")] // CommandCodeSession.swift:708-711 in .auto the three shell tools are approved exactly when the command cannot write
    public void Command_Code_in_auto_mode_approves_a_shell_tool_exactly_when_the_command_cannot_write(string tool)
    {
        using var h = new AppModelHarness();
        var session = CommandCodeSession(RuntimeMode.Auto);

        Assert.True(AutoApproves(session, tool, JSONValue.FromDictionaryLiteral(("command", "git status")))); // reads: no question
        Assert.False(AutoApproves(session, tool, JSONValue.FromDictionaryLiteral(("command", "git tag v1")))); // writes: the user is asked
        Assert.False(AutoApproves(session, tool, JSONValue.FromDictionaryLiteral(("command", "echo hi > out.txt"))));
        Assert.True(AutoApproves(session, tool, JSONValue.FromDictionaryLiteral())); // a missing command reads as ""
        Assert.True(AutoApproves(session, tool, JSONValue.FromDictionaryLiteral(("command", 7)))); // a non-string one too
    }
}
