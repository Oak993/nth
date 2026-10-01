using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// ensureSession and releaseSession: ThreadRuntime.swift:1430-1620 (B3; the records cite the B2 lines :1439-1604) and
// :2082-2096. A running scripted session stands in for a live one; a launch the runtime must make itself goes to a
// provider with no executable (PrepareBootstrap), to an API provider without a network call (its start only builds
// the in-memory conversation), or to Command Code, whose start reads a transcript file and spawns nothing.
public sealed class ThreadRuntimeEnsureSessionTests
{
    private const string LostConversationLine = "Command Code no longer has this conversation. It starts a new one here, without the messages above.";

    private static IDisposable CommandCodeExecutable() =>
        ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider =>
            provider == ProviderKind.Commandcode ? SwiftURL.Create("file:///factory-test/cli") : null);

    private static (ProviderSession? Session, Exception? Error) Ensure(ThreadRuntimeTurnRig rig, bool allowNewSession = true)
    {
        Task task = null!;
        rig.H.Main.OnMain(() => task = ThreadRuntimeReflection.Call<Task>(rig.Runtime, "EnsureSession", rig.Directory, allowNewSession)!);
        rig.H.Main.Loop.RunUntil(() => task.IsCompleted, AppModelHarness.Bound);
        if (task.IsFaulted) return (null, task.Exception!.InnerException);
        if (task.IsCanceled) return (null, new OperationCanceledException());
        return ((ProviderSession)task.GetType().GetProperty("Result")!.GetValue(task)!, null);
    }

    private static void AssertNotInstalled(Exception? error, ProviderKind provider)
    {
        var failure = Assert.IsType<ProviderError>(error);
        Assert.Equal(provider, Assert.IsType<ProviderError.Case.NotInstalled>(failure.ErrorCase).Provider);
    }

    private static ProviderSession? LiveSession(ThreadRuntimeTurnRig rig) => ThreadRuntimeReflection.Get<ProviderSession>(rig.Runtime, "session");

    private static long Epoch(ThreadRuntimeTurnRig rig) => ThreadRuntimeReflection.Get<long>(rig.Runtime, "sessionEpoch");

    // ---- the reuse test (PR-RUNTIME-023) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-023")] // :1458 running and the signature unchanged: the live session is returned and nothing else happens
    public void A_running_session_with_an_unchanged_signature_is_reused_untouched()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var epoch = Epoch(rig);

        var (session, error) = Ensure(rig);

        Assert.Null(error);
        Assert.Same(rig.Session, session);
        Assert.Same(rig.Session, LiveSession(rig));
        Assert.Equal(0, rig.Session.StopCalls);
        Assert.Equal(epoch, Epoch(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-023")] // a stopped session is not reused: it is released and a new one is launched
    [Trait("Req", "PR-RUNTIME-032")] // :1530-1532 a CLI provider with no executable throws notInstalled, after the old session was released
    public void A_session_that_stopped_is_released_and_a_provider_with_no_executable_throws_not_installed()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Session.IsRunning = false;

        var (session, error) = Ensure(rig);

        Assert.Null(session);
        AssertNotInstalled(error, ProviderKind.Claude);
        Assert.Equal(1, rig.Session.StopCalls);
        Assert.Null(LiveSession(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-023")] // the launch signature must still match: a changed launch flag relaunches
    public void A_session_whose_launch_signature_no_longer_matches_is_not_reused()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.H.Main.OnMain(() => rig.App.Settings.AntiSlopEnabled = !rig.App.Settings.AntiSlopEnabled);

        var (_, error) = Ensure(rig);

        AssertNotInstalled(error, ProviderKind.Claude);
        Assert.Equal(1, rig.Session.StopCalls);
        Assert.Null(LiveSession(rig));
    }

    // ---- releasing and ending the native heads (PR-RUNTIME-026, PR-RUNTIME-044) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-026")] // :1461-1464 releaseSession(stop: true) then stopNativeHeads
    [Trait("Req", "PR-RUNTIME-044")] // :2082-2096 the resets, the stop and the epoch bump
    public void Relaunching_releases_the_old_session_and_ends_its_native_heads()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, hydraOn: true, settings: app => app.Settings.HydraIsolateHeads = false);
        rig.H.Main.OnMain(() => rig.Runtime.Rehearse(new ProviderEvent.AgentStarted(
            new AgentSpawn { Id = "agent-1", Description = "Read the packet", Prompt = "brief", IsBackground = true })));
        var head = Assert.Single(rig.H.Main.OnMain(() => rig.App.HydraHeads(rig.InitialThread.Id).ToArray()));
        Assert.Equal(HydraHeadInfo.Status.Running, head.Hydra!.HydraHeadInfoStatus);
        ThreadRuntimeReflection.Set(rig.Runtime, "unsentSessionID", "held");
        ThreadRuntimeReflection.Set(rig.Runtime, "antiSlopFallbackSent", true);
        ThreadRuntimeReflection.Set(rig.Runtime, "continuationSent", true);
        rig.Session.IsRunning = false;
        var epoch = Epoch(rig);

        var (_, error) = Ensure(rig);

        AssertNotInstalled(error, ProviderKind.Claude);
        Assert.Equal(1, rig.Session.StopCalls);
        Assert.Null(LiveSession(rig));
        Assert.Null(ThreadRuntimeReflection.Get<object>(rig.Runtime, "sessionSignature"));
        Assert.Null(ThreadRuntimeReflection.Get<string>(rig.Runtime, "unsentSessionID"));
        Assert.False(ThreadRuntimeReflection.Get<bool>(rig.Runtime, "antiSlopFallbackSent"));
        Assert.False(ThreadRuntimeReflection.Get<bool>(rig.Runtime, "continuationSent"));
        Assert.True(Epoch(rig) > epoch);
        var ended = rig.H.Main.OnMain(() => rig.App.Thread(head.Id)!.Hydra!);
        Assert.Equal(HydraHeadInfo.Status.Stopped, ended.HydraHeadInfoStatus);
        Assert.Empty(ThreadRuntimeReflection.Get<Dictionary<string, Guid>>(rig.Runtime, "hydraNativeHeads")!);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-044")] // :2086-2090 the four resets run even when there is no session to let go of
    public void Releasing_with_no_session_still_resets_the_delivery_state()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, install: false);
        ThreadRuntimeReflection.Set(rig.Runtime, "unsentSessionID", "held");
        ThreadRuntimeReflection.Set(rig.Runtime, "antiSlopFallbackSent", true);
        ThreadRuntimeReflection.Set(rig.Runtime, "continuationSent", true);
        var epoch = Epoch(rig);

        rig.H.Main.OnMain(() => ThreadRuntimeReflection.Call<object>(rig.Runtime, "ReleaseSession", true));

        Assert.Null(ThreadRuntimeReflection.Get<string>(rig.Runtime, "unsentSessionID"));
        Assert.False(ThreadRuntimeReflection.Get<bool>(rig.Runtime, "antiSlopFallbackSent"));
        Assert.False(ThreadRuntimeReflection.Get<bool>(rig.Runtime, "continuationSent"));
        Assert.Null(ThreadRuntimeReflection.Get<object>(rig.Runtime, "sessionSignature"));
        Assert.Equal(epoch + 1, Epoch(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-044")] // :2090-2095 stop only when asked; the handler is cleared a loop turn later, the epoch silences it meanwhile
    public void A_released_session_is_stopped_only_when_asked_and_its_events_are_silenced_at_once()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var session = rig.Session;
        bool? handlerKeptRightAfter = null;
        var entriesAfterLateEvent = -1;

        rig.H.Main.OnMain(() =>
        {
            ThreadRuntimeReflection.Call<object>(rig.Runtime, "ReleaseSession", true);
            handlerKeptRightAfter = session.OnEvent is not null;
            session.Emit(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "from a released session" }));
            entriesAfterLateEvent = rig.Runtime.Entries.Count;
        });

        Assert.Equal(true, handlerKeptRightAfter);
        Assert.Equal(0, entriesAfterLateEvent);
        Assert.Null(session.OnEvent);
        Assert.Equal(1, session.StopCalls);
        Assert.Null(LiveSession(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-044")] // :2093 `if stop { old.stop() }`: the exit path lets the session go without stopping it
    public void A_session_that_exited_is_let_go_without_being_stopped()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);

        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.Exited(null)));

        Assert.Null(LiveSession(rig));
        Assert.Equal(0, rig.Session.StopCalls);
    }

    // ---- the API-key branch (PR-RUNTIME-027, PR-RUNTIME-031, PR-RUNTIME-040) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-027")] // :1466-1469 an API provider with an empty key throws notInstalled, the old session already released
    public void An_API_provider_with_an_empty_key_throws_not_installed_after_the_old_session_was_released()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek);
        rig.Session.IsRunning = false;

        var (session, error) = Ensure(rig);

        Assert.Null(session);
        AssertNotInstalled(error, ProviderKind.Deepseek);
        Assert.Equal(1, rig.Session.StopCalls);
        Assert.Null(LiveSession(rig));
        Assert.Null(rig.Thread.ProviderSessionID);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-031")] // :1524-1527 an API session's id goes straight onto the thread, no hold-back
    public void An_API_session_writes_its_id_onto_the_thread_at_start_without_holding_it_back()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        rig.H.Main.OnMain(() => rig.App.Settings.SetAPIKeyInput("sk-test-key", ProviderKind.Deepseek));

        var (session, error) = Ensure(rig);

        Assert.Null(error);
        Assert.IsType<DeepSeekSession>(session);
        Assert.False(string.IsNullOrEmpty(rig.Thread.ProviderSessionID));
        Assert.Null(ThreadRuntimeReflection.Get<string>(rig.Runtime, "unsentSessionID"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-031")] // :1601-1603 the CLI branch holds the id back until a message has gone through the session
    public void A_CLI_session_holds_its_id_back_until_a_message_is_sent()
    {
        using var seam = CommandCodeExecutable();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Commandcode, install: false);
        try
        {
            var (session, error) = Ensure(rig);

            Assert.Null(error);
            Assert.NotNull(session);
            Assert.Null(rig.Thread.ProviderSessionID);
            Assert.Equal("", ThreadRuntimeReflection.Get<string>(rig.Runtime, "unsentSessionID"));
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
        }
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-040")] // :1524 / :1601 a cancelled task throws CancellationError, records no id, and the candidate is torn down
    public void A_cancelled_start_throws_cancellation_records_no_session_id_and_leaves_no_session()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        rig.H.Main.OnMain(() => rig.App.Settings.SetAPIKeyInput("sk-test-key", ProviderKind.Deepseek));
        TaskHandle<ProviderSession> handle = null!;
        rig.H.Main.OnMain(() =>
        {
            handle = MainActor.SpawnThrowing(async () => await ThreadRuntimeReflection.Call<Task<ProviderSession>>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
            handle.Cancel();
        });

        rig.H.Main.Loop.RunUntil(() => handle.Completion.IsCompleted, AppModelHarness.Bound);

        Assert.True(handle.Completion.IsFaulted || handle.Completion.IsCanceled);
        Assert.IsAssignableFrom<OperationCanceledException>(handle.Completion.Exception?.InnerException ?? new OperationCanceledException());
        Assert.Null(rig.Thread.ProviderSessionID);
        Assert.Null(LiveSession(rig));
    }

    // ---- the CLI branch: a failed start, the start-over arm (PR-RUNTIME-030, -035, -038) ----

    private static ThreadRuntimeTurnRig LostConversationRig() => new(ProviderKind.Commandcode, thread => thread with
    {
        ProviderSessionID = "conversation-the-cli-no-longer-has",
        ProviderResumeAt = "anchor-1",
    }, install: false);

    [Fact]
    [Trait("Req", "PR-RUNTIME-030")] // :1569-1581 a start that threw: the candidate is released, handler and signature with it
    [Trait("Req", "PR-RUNTIME-035")] // :1570-1572 the starting flag is cleared by the defer
    public void A_start_that_throws_leaves_no_session_and_the_starting_flag_down()
    {
        using var seam = CommandCodeExecutable();
        using var rig = LostConversationRig();

        var (session, error) = Ensure(rig, allowNewSession: false);

        Assert.Null(session);
        Assert.Equal("Command Code no longer has this conversation; starting a new one.",
            Assert.IsType<ProviderError.Case.Failed>(Assert.IsType<ProviderError>(error).ErrorCase).Message);
        Assert.Null(LiveSession(rig));
        Assert.Null(ThreadRuntimeReflection.Get<object>(rig.Runtime, "sessionSignature"));
        Assert.False(ThreadRuntimeReflection.Get<bool>(rig.Runtime, "isStartingSession"));
        Assert.Equal("anchor-1", rig.Thread.ProviderResumeAt);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-038")] // :1584-1599 a lost conversation during a turn: anchor cleared, warning, fresh session started
    public void A_resume_that_lost_the_conversation_during_a_turn_clears_the_anchor_warns_and_starts_a_fresh_session()
    {
        using var seam = CommandCodeExecutable();
        using var rig = LostConversationRig();
        ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", Guid.NewGuid());
        try
        {
            var (session, error) = Ensure(rig);

            Assert.Null(error);
            Assert.NotNull(session);
            Assert.Same(session, LiveSession(rig));
            Assert.True(session!.IsRunning);
            Assert.Null(rig.Thread.ProviderResumeAt);
            var notice = Assert.Single(rig.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.Notice>()).Value;
            Assert.Equal(Notice.Level.Warning, notice.NoticeLevel);
            Assert.Equal(LostConversationLine, notice.Message);
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
        }
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-038")] // :1593-1595 `if currentTurnID != nil`: the warning is only ever written during a running turn
    public void The_lost_conversation_warning_is_not_written_when_no_turn_is_running()
    {
        using var seam = CommandCodeExecutable();
        using var rig = LostConversationRig();
        try
        {
            var (session, error) = Ensure(rig);

            Assert.Null(error);
            Assert.NotNull(session);
            Assert.Null(rig.Thread.ProviderResumeAt);
            Assert.DoesNotContain(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Notice);
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
        }
    }

    // ---- an exit during the start (PR-RUNTIME-035, PR-RUNTIME-029) ----

    /// <summary>Delivers an exit from the session while ensureSession is still inside its start: the first write to the
    /// thread record it makes (a synchronous observation) is where the provider "exits".</summary>
    private static void ExitOnFirstThreadWrite(ThreadRuntimeTurnRig rig, string error)
    {
        var fired = false;
        rig.App.PropertyChanged += (_, _) =>
        {
            if (fired) return;
            fired = true;
            rig.Runtime.Rehearse(new ProviderEvent.Exited(error));
        };
    }

    private static IEnumerable<string> ErrorNotices(ThreadRuntimeTurnRig rig) =>
        rig.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.Notice>()
            .Where(notice => notice.Value.NoticeLevel == Notice.Level.Error).Select(notice => notice.Value.Message);

    [Fact]
    [Trait("Req", "PR-RUNTIME-035")] // :1570-1581 an .exited that arrives while the CLI session starts, even across the start-over, is swallowed
    public void An_exit_that_arrives_while_a_CLI_session_is_starting_is_swallowed()
    {
        using var seam = CommandCodeExecutable();
        using var rig = LostConversationRig();
        ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", Guid.NewGuid());
        ExitOnFirstThreadWrite(rig, "boom");
        try
        {
            var (session, error) = Ensure(rig);

            Assert.Null(error);
            Assert.NotNull(session);
            Assert.Same(session, LiveSession(rig));
            Assert.DoesNotContain("boom", ErrorNotices(rig));
            Assert.False(ThreadRuntimeReflection.Get<bool>(rig.Runtime, "isStartingSession"));
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
        }
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-029")] // :1466-1528 the API branch never sets isStartingSession: an .exited during its start takes the full release-and-fail path
    public void An_exit_that_arrives_while_an_API_session_starts_is_not_swallowed()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        rig.H.Main.OnMain(() => rig.App.Settings.SetAPIKeyInput("sk-test-key", ProviderKind.Deepseek));
        ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", Guid.NewGuid());
        ExitOnFirstThreadWrite(rig, "boom");

        Ensure(rig);

        Assert.Contains("boom", ErrorNotices(rig));
    }

    // ---- warming a session ahead of the first message (PR-RUNTIME-046, PR-RUNTIME-047) ----

    /// <summary>Calls WarmSession and reads the warm-up latch before anything else is pumped.</summary>
    private static TaskHandle? WarmAndRead(ThreadRuntimeTurnRig rig)
    {
        TaskHandle? latch = null;
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.WarmSession();
            latch = ThreadRuntimeReflection.Get<TaskHandle>(rig.Runtime, "warmup");
        });
        return latch;
    }

    private static void WaitForWarmup(ThreadRuntimeTurnRig rig)
    {
        var warmup = ThreadRuntimeReflection.Get<TaskHandle>(rig.Runtime, "warmup");
        if (warmup is not null) rig.H.Main.Loop.RunUntil(() => warmup.Completion.IsCompleted, AppModelHarness.Bound);
        rig.H.Main.RunUntilIdle();
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-046")] // :1416-1427 a CLI chat that is idle, unarchived and sessionless starts its session ahead of the message
    public void An_idle_chat_with_no_session_starts_one_ahead_of_the_first_message()
    {
        using var seam = CommandCodeExecutable();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Commandcode, install: false);
        try
        {
            rig.H.Main.OnMain(() => rig.Runtime.WarmSession());
            WaitForWarmup(rig);

            Assert.NotNull(LiveSession(rig));
            Assert.Null(ThreadRuntimeReflection.Get<TaskHandle>(rig.Runtime, "warmup"));
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
        }
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-046")] // :1417 `session == nil`: a chat that has a session starts no other
    public void A_chat_that_already_has_a_session_warms_nothing()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);

        Assert.Null(WarmAndRead(rig));
        Assert.Same(rig.Session, LiveSession(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-046")] // :1417 `!thread.isArchived`
    public void An_archived_chat_warms_nothing()
    {
        using var seam = CommandCodeExecutable();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Commandcode, thread => thread with { IsArchived = true }, install: false);

        Assert.Null(WarmAndRead(rig));
        Assert.Null(LiveSession(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-046")] // :1417 `!thread.provider.isAPIKeyBased`: API providers never warm
    public void An_API_provider_never_warms_a_session()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        rig.H.Main.OnMain(() => rig.App.Settings.SetAPIKeyInput("sk-test-key", ProviderKind.Deepseek));

        Assert.Null(WarmAndRead(rig));
        Assert.Null(LiveSession(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-046")] // :1417 `phase == .idle`: a chat in the middle of a turn starts no warm-up
    public void A_chat_in_the_middle_of_a_turn_warms_nothing()
    {
        using var seam = CommandCodeExecutable();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Commandcode, install: false);
        rig.H.Main.OnMain(() => rig.Runtime.RehearseTurn("running"));

        Assert.Null(WarmAndRead(rig));
        Assert.Null(LiveSession(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-046")] // :1417 `!isReverting`
    public void A_chat_that_is_reverting_warms_nothing()
    {
        using var seam = CommandCodeExecutable();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Commandcode, install: false);
        ThreadRuntimeReflection.Set(rig.Runtime, "isReverting", true);

        Assert.Null(WarmAndRead(rig));
        Assert.Null(LiveSession(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-047")] // :1425-1426 a warm-up whose start threw keeps warmup non-nil: no warm start is ever tried again by this runtime
    public void A_warm_up_whose_start_failed_disables_warm_starts_for_the_life_of_the_runtime()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Commandcode, install: false);
        rig.H.Main.OnMain(() => rig.Runtime.WarmSession());
        WaitForWarmup(rig);
        var latch = ThreadRuntimeReflection.Get<TaskHandle>(rig.Runtime, "warmup");
        Assert.NotNull(latch);
        Assert.Null(LiveSession(rig));

        using var seam = CommandCodeExecutable();
        try
        {
            rig.H.Main.OnMain(() => rig.Runtime.WarmSession());
            WaitForWarmup(rig);

            Assert.Same(latch, ThreadRuntimeReflection.Get<TaskHandle>(rig.Runtime, "warmup"));
            Assert.Null(LiveSession(rig));
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
        }
    }
}
