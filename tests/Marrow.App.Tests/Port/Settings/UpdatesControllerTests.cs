using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Updates;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Settings;

// Unit UE-4 (BUILD-PLAN.md 2.4 UE-4: "states from a scripted orchestrator"). The page's controller drives UE-2's real orchestrator over scripted items under the test runtime's main loop and clock:
// no CLI, no network, no real time.
public sealed class UpdatesControllerTests
{
    private static ScriptedUpdateItem Claude(string installed = "2.1.283", string? latest = "2.1.290") => new("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, installed, latest);

    private static ScriptedUpdateItem Codex(string installed = "0.157.1", string? latest = "0.160.0") => new("cli:codex", "Codex", UpdateItemKind.ProviderCli, installed, latest);

    private static ScriptedUpdateItem MarrowItem(string installed = "1.0.0", string? latest = "1.1.0") =>
        new("marrow", "nth", UpdateItemKind.Marrow, installed, latest) { OnUpdate = _ => Task.FromResult(new UpdateOutcome(true, "1.1.0", true, true, null)) };

    private static UpdatesController Make(out UpdateOrchestrator orchestrator, List<string> restarts, params IUpdateItem[] items)
    {
        orchestrator = new UpdateOrchestrator(items);
        return new UpdatesController(new OrchestratorUpdatesRunner(orchestrator, () => restarts.Add("restart")));
    }

    private static UpdatesRowView RowOf(UpdatesController controller, string id) => controller.View.Sections.SelectMany(s => s.Rows).Single(r => r.Id == id);

    [Fact]
    [Trait("Decision", "ED-308")] // opening the page checks once when nothing was ever checked, and remembers when
    public void Attaching_to_a_page_that_was_never_checked_checks_once_and_remembers_when()
    {
        using var runtime = new TestRuntime();
        var claude = Claude("2.1.290", "2.1.290");
        var controller = Make(out _, [], claude, MarrowItem("1.1.0", "1.1.0"));
        runtime.Loop.Run(controller.Attach);
        Assert.Equal(1, claude.Checks);
        Assert.Equal(UpdatesPhase.Current, controller.View.Header.Phase);
        Assert.Equal("Checked just now. nth also keeps Claude Code current in the background.", controller.View.Header.Detail);
        runtime.AdvanceMilliseconds(2.4 * 3_600_000);
        controller.Tick();
        Assert.Equal("Checked 2 hours ago. nth also keeps Claude Code current in the background.", controller.View.Header.Detail);
        controller.Detach();
        runtime.Loop.Run(controller.Attach);
        Assert.Equal(1, claude.Checks);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // an empty orchestrator has nothing to check, so opening the page asks nothing
    public void A_page_with_no_items_does_not_check_and_says_everything_is_current()
    {
        using var runtime = new TestRuntime();
        var controller = Make(out _, []);
        runtime.Loop.Run(controller.Attach);
        Assert.Equal(("Everything is up to date", "Check now"), (controller.View.Header.Title, controller.View.Header.ButtonLabel));
        Assert.Empty(controller.View.Sections);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // the Check now button shows Checking while it reads, then what it found
    public void Check_now_shows_Checking_while_it_reads_and_then_the_result()
    {
        using var runtime = new TestRuntime();
        var claude = Claude("2.1.290", "2.1.290");
        var controller = Make(out _, [], claude);
        runtime.Loop.Run(controller.Attach);
        var gate = new TaskCompletionSource();
        claude.InstalledWait = gate.Task;
        claude.Latest = "2.2.0";
        Task? press = null;
        runtime.OnMain(() => press = controller.Press());
        runtime.RunUntilIdle();
        Assert.Equal(UpdatesPhase.Checking, controller.View.Header.Phase);
        Assert.Equal(("Checking…", true), (controller.View.Header.ButtonLabel, controller.View.Header.ButtonBusy));
        Assert.Equal(RowStatus.Checking, RowOf(controller, "cli:claude").Status);
        gate.SetResult();
        runtime.Loop.RunUntil(() => press!.IsCompleted);
        Assert.Equal(("1 update available", "Update everything"), (controller.View.Header.Title, controller.View.Header.ButtonLabel));
        Assert.Equal("Claude Code 2.2.0.", controller.View.Header.Detail);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // OD-0100: one at a time, Marrow last, then the restart is offered and taken only on the press
    public void Update_everything_runs_the_due_items_one_at_a_time_with_Marrow_last_and_ends_with_the_restart_offer()
    {
        using var runtime = new TestRuntime();
        List<string> log = [];
        var claude = Claude();
        claude.OnUpdate = _ => { log.Add("claude"); return Task.FromResult(new UpdateOutcome(true, "2.1.290", true, false, null)); };
        var marrow = new ScriptedUpdateItem("marrow", "nth", UpdateItemKind.Marrow, "1.0.0", "1.1.0") { OnUpdate = _ => { log.Add("marrow"); return Task.FromResult(new UpdateOutcome(true, "1.1.0", true, true, null)); } };
        List<string> restarts = [];
        var controller = Make(out _, restarts, marrow, claude);
        runtime.Loop.Run(controller.Attach);
        Assert.Equal(("2 updates available", UpdatesAction.UpdateAll), (controller.View.Header.Title, controller.View.Header.Action));
        runtime.Loop.Run(controller.Press);
        Assert.Equal(["claude", "marrow"], log);
        Assert.Equal(UpdatesPhase.Done, controller.View.Header.Phase);
        Assert.Equal(("2 updated", "Restart nth"), (controller.View.Header.Title, controller.View.Header.ButtonLabel));
        Assert.True(controller.View.Banner.Visible);
        Assert.Empty(restarts);
        runtime.Loop.Run(controller.Press);
        Assert.Equal(["restart"], restarts);
        Assert.Equal((UpdatesPhase.Restarting, "Restarting…"), (controller.View.Header.Phase, controller.View.Header.ButtonLabel));
        runtime.Loop.Run(controller.Press);
        Assert.Equal(["restart"], restarts);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // a restart that does not take (the engine's give-up is 25 s) hands the page back
    public void A_restart_that_never_takes_hands_the_button_back_after_the_engines_give_up_time()
    {
        using var runtime = new TestRuntime();
        List<string> restarts = [];
        var controller = Make(out _, restarts, MarrowItem());
        runtime.Loop.Run(controller.Attach);
        runtime.Loop.Run(controller.Press);
        runtime.Loop.Run(controller.Press);
        Assert.Equal(UpdatesPhase.Restarting, controller.View.Header.Phase);
        runtime.AdvanceMilliseconds(24_000);
        Assert.Equal(UpdatesPhase.Restarting, controller.View.Header.Phase);
        runtime.AdvanceMilliseconds(1_500);
        Assert.Equal(("Restart nth", UpdatesPhase.Done), (controller.View.Header.ButtonLabel, controller.View.Header.Phase));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // a failure keeps the others' results; the button becomes Retry and runs only what failed
    public void A_failed_item_is_retried_alone_by_the_button_and_the_others_are_not_run_again()
    {
        using var runtime = new TestRuntime();
        var claude = Claude();
        var attempts = 0;
        var codex = Codex();
        codex.OnUpdate = _ => Task.FromResult(++attempts == 1 ? new UpdateOutcome(false, null, false, false, "failed") : new UpdateOutcome(true, "0.160.0", true, false, null));
        var controller = Make(out _, [], claude, codex);
        runtime.Loop.Run(controller.Attach);
        runtime.Loop.Run(controller.Press);
        Assert.Equal((UpdatesPhase.Failed, "Codex didn’t update", "Retry"), (controller.View.Header.Phase, controller.View.Header.Title, controller.View.Header.ButtonLabel));
        Assert.Equal("Updated Claude Code. The fix is below.", controller.View.Header.Detail);
        Assert.True(RowOf(controller, "cli:codex").CanRetry);
        runtime.Loop.Run(controller.Press);
        Assert.Equal(UpdatesPhase.Done, controller.View.Header.Phase);
        Assert.Equal((1, 2), (claude.Updates, codex.Updates));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // per-row Update runs that row alone; per-row Retry only a failed row, and neither throws when asked at the wrong time
    public void A_rows_own_Update_runs_only_that_row_and_a_wrong_time_request_does_nothing()
    {
        using var runtime = new TestRuntime();
        var claude = Claude();
        var codex = Codex();
        var controller = Make(out _, [], claude, codex);
        runtime.Loop.Run(controller.Attach);
        runtime.Loop.Run(() => controller.RetryRow("cli:claude"));
        Assert.Equal(0, claude.Updates);
        runtime.Loop.Run(() => controller.UpdateRow("cli:claude"));
        Assert.Equal((1, 0), (claude.Updates, codex.Updates));
        Assert.Equal(RowStatus.Updated, RowOf(controller, "cli:claude").Status);
        Assert.Equal(RowStatus.Available, RowOf(controller, "cli:codex").Status);
        runtime.Loop.Run(() => controller.UpdateRow("zz"));
        runtime.Loop.Run(() => controller.UpdateRow("cli:claude"));
        Assert.Equal(1, claude.Updates);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1 "Later": the banner is put away and the restart sentence moves to the detail; a new run brings it back
    public void Later_puts_the_banner_away_until_the_next_run()
    {
        using var runtime = new TestRuntime();
        var claude = Claude();
        var controller = Make(out _, [], MarrowItem(), claude);
        runtime.Loop.Run(controller.Attach);
        runtime.Loop.Run(() => controller.UpdateRow("marrow"));
        Assert.True(controller.View.Banner.Visible);
        controller.DismissBanner();
        Assert.False(controller.View.Banner.Visible);
        Assert.Equal("Updated nth. Restart nth to finish its update.", controller.View.Header.Detail);
        runtime.Loop.Run(() => controller.UpdateRow("cli:claude"));
        Assert.True(controller.View.Banner.Visible);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // mid-run the page names the item, its place and the exact or moving progress; a second press does nothing
    public void While_a_run_goes_the_page_names_the_item_shows_its_progress_and_refuses_a_second_start()
    {
        using var runtime = new TestRuntime();
        var gate = new TaskCompletionSource();
        IProgress<double?>? report = null;
        var claude = Claude();
        claude.OnUpdate = async progress => { report = progress; await gate.Task; return new UpdateOutcome(true, "2.1.290", true, false, null); };
        var marrow = MarrowItem();
        var controller = Make(out _, [], claude, marrow);
        runtime.Loop.Run(controller.Attach);
        Task? run = null;
        runtime.OnMain(() => run = controller.Press());
        runtime.RunUntilIdle();
        Assert.Equal(("Updating Claude Code… 1 of 2", "Updating…", UpdatesAction.None), (controller.View.Header.Title, controller.View.Header.ButtonLabel, controller.View.Header.Action));
        Assert.Equal((RowStatus.Updating, true, (double?)null), (RowOf(controller, "cli:claude").Status, RowOf(controller, "cli:claude").ShowProgress, RowOf(controller, "cli:claude").Progress));
        Assert.Equal(RowStatus.Waiting, RowOf(controller, "marrow").Status);
        report!.Report(.5);
        runtime.RunUntilIdle();
        Assert.Equal((RowStatus.Downloading, "Downloading 50%"), (RowOf(controller, "cli:claude").Status, RowOf(controller, "cli:claude").StatusText));
        runtime.Loop.Run(controller.Press);
        runtime.Loop.Run(() => controller.UpdateRow("marrow"));
        Assert.Equal((1, 0), (claude.Updates, marrow.Updates));
        gate.SetResult();
        runtime.Loop.RunUntil(() => run!.IsCompleted);
        Assert.Equal(("2 updated", 1), (controller.View.Header.Title, marrow.Updates));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // changes made elsewhere (the pill's background check) reach the page, a burst of them as one refresh, and a detached page hears none
    public void Changes_from_elsewhere_reach_an_attached_page_as_one_refresh_and_a_detached_page_hears_none()
    {
        using var runtime = new TestRuntime();
        var controller = Make(out var orchestrator, [], Claude(), Codex());
        runtime.Loop.Run(controller.Attach);
        runtime.RunUntilIdle(); // the refreshes the opening check itself posted
        var seen = 0;
        controller.ViewChanged += () => seen++;
        Task? check = null;
        runtime.OnMain(() => check = orchestrator.CheckNow());
        runtime.Loop.RunUntil(() => check!.IsCompleted);
        runtime.RunUntilIdle();
        Assert.Equal(1, seen);
        Assert.Equal("2 updates available", controller.View.Header.Title);
        controller.Detach();
        runtime.OnMain(() => check = orchestrator.CheckNow());
        runtime.Loop.RunUntil(() => check!.IsCompleted);
        runtime.RunUntilIdle();
        Assert.Equal(1, seen);
    }
}
