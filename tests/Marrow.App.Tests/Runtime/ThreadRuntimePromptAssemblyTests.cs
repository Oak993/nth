using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// What startTurn hands the provider: ThreadRuntime.swift:1283-1376 (B3 1.7.0). The records cite the B2 line numbers
// (:1291-1376); the text and the order are the same. Every test runs a turn through the real StartTurn over an offline
// scripted session (Fixtures/ThreadRuntimeScriptedSession.cs), installed as EnsureSession would have left it.
public sealed class ThreadRuntimePromptAssemblyTests
{
    private static readonly Attachment TextFile = new() { Name = "a.txt", Path = @"C:\work\a.txt", MimeType = "text/plain" };
    private static readonly Attachment Picture = new() { Name = "pic.png", Path = @"C:\work\pic.png", MimeType = "image/png" };
    private static readonly Attachment PdfFile = new() { Name = "b.pdf", Path = @"D:\other dir\b.pdf", MimeType = "application/pdf" };

    private static ThreadRuntimeTurnRig AntiSlopRig(bool antiSlop, bool install = true) =>
        new(ProviderKind.Cursor, install: install, settings: app => app.Settings.AntiSlopEnabled = antiSlop);

    [Fact]
    [Trait("Req", "PR-RUNTIME-068")] // :1291-1295 non-image attachments appended under "Attached files:", path verbatim
    public void Non_image_attachments_are_listed_after_the_text_under_the_fixed_heading_and_images_are_left_out()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("Look at these", [TextFile, Picture, PdfFile]);

        var input = Assert.Single(rig.Session.Inputs);
        Assert.Equal("Look at these\n\nAttached files:\n- C:\\work\\a.txt\n- D:\\other dir\\b.pdf", input.Text);
        Assert.Equal("C:\\work\\pic.png", Assert.Single(input.Images).Path);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-068")] // :1292-1294 `if !files.isEmpty` - no heading without a non-image file
    public void A_message_with_no_non_image_attachment_gets_no_heading()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("Just a picture", [Picture]);

        Assert.Equal("Just a picture", Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-054")] // :1196-1197 phase = .starting; turnStartedAt = .now, before any I/O
    [Trait("Req", "PR-RUNTIME-055")] // :1198-1203 updatedAt = .now, lastStatus = .running
    [Trait("Req", "PR-RUNTIME-073")] // :1351 phase = .running before the send
    public void A_turn_is_starting_with_its_clock_stamped_and_the_thread_running_before_it_is_running_when_sent()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { LastStatus = TurnStatus.Completed, UpdatedAt = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        rig.H.Main.Advance(TimeSpan.FromMinutes(7));
        var phaseWhenSent = (RuntimePhase?)null;
        rig.Session.SendScript = _ =>
        {
            phaseWhenSent = rig.Runtime.Phase;
            return Task.CompletedTask;
        };
        RuntimePhase? startingPhase = null;
        DateTimeOffset? startedAt = null;
        DateTimeOffset now = default;
        ChatThread? whileStarting = null;
        var task = rig.StartTurn("go", whileStarting: () =>
        {
            startingPhase = rig.Runtime.Phase;
            startedAt = rig.Runtime.TurnStartedAt;
            now = SwiftDate.Now;
            whileStarting = rig.App.Thread(rig.InitialThread.Id);
        });

        Assert.Equal(RuntimePhase.Starting, startingPhase);
        Assert.Equal(now, startedAt);
        Assert.True(rig.Runtime.IsRunning);
        Assert.Equal(TurnStatus.Running, whileStarting!.LastStatus);
        Assert.Equal(now, whileStarting.UpdatedAt);

        rig.Finish(task);
        Assert.Equal(RuntimePhase.Running, phaseWhenSent);
        Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase);
        Assert.Equal(startedAt, rig.Runtime.TurnStartedAt);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-055")] // :1202-1203 reopenIfSettled: a settled thread put back to work is open again
    public void Starting_a_turn_reopens_a_settled_thread()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { IsSettled = true, SettledAt = SwiftDate.Now });

        rig.Send("back to work");

        Assert.False(rig.Thread.IsSettled);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-073")] // :1351-1363 all ten TurnInput fields, the per-turn ones read from the thread at send time
    public void The_turn_input_carries_the_threads_current_settings_and_the_images_of_an_image_capable_provider()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with
        {
            Effort = "high", FastMode = true, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        });
        rig.H.Main.OnMain(() => rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with
        {
            Model = "model-after-launch", RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Plan,
        }));

        rig.Send("hello", [Picture]);

        var input = Assert.Single(rig.Session.Inputs);
        Assert.Equal("hello", input.Text);
        Assert.Equal("C:\\work\\pic.png", Assert.Single(input.Images).Path);
        Assert.Equal("model-after-launch", input.Model);
        Assert.Equal("high", input.Effort);
        Assert.Null(input.ServiceTier);
        Assert.True(input.FastMode);
        Assert.Equal(RuntimeMode.FullAccess, input.RuntimeMode);
        Assert.Equal(InteractionMode.Plan, input.InteractionMode);
        Assert.False(input.IsFinalReport);
        Assert.Null(input.Command);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-073")] // :1354 images: thread.provider.supportsImages ? ... : [] - dropped, never sent as paths
    public void A_provider_that_cannot_take_images_gets_none_and_no_file_path_for_them()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Grok, settings: app => app.Settings.AntiSlopEnabled = false);

        rig.Send("see this", [Picture]);

        var input = Assert.Single(rig.Session.Inputs);
        Assert.Empty(input.Images);
        Assert.DoesNotContain("pic.png", input.Text, StringComparison.Ordinal);
        Assert.EndsWith("see this", input.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-073")] // :1357 serviceTier(for:) at :1392-1397 - the fast tier, or "default" when fast mode is off
    public void A_Codex_turn_sends_the_models_fast_tier_while_fast_mode_is_on_and_default_when_it_is_off()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex, thread => thread with { Model = "fast-model", FastMode = true });
        rig.H.Main.OnMain(() => rig.App.Providers.UpdateCatalog(
            new ValueList<ModelOption>([new ModelOption("fast-model", "Fast model", fastTier: "priority")]), ProviderKind.Codex));

        rig.Send("one");
        rig.Complete();
        rig.H.Main.OnMain(() => rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { FastMode = false }));
        rig.Send("two");

        Assert.Equal(["priority", "default"], rig.Session.Inputs.Select(input => input.ServiceTier));
    }

    // ---- the anti-slop prefix (PR-RUNTIME-071) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-071")] // :1323-1334 prefix joined by two newlines, once per session
    public void The_anti_slop_policy_goes_in_front_of_the_first_ordinary_message_only()
    {
        using var rig = AntiSlopRig(antiSlop: true);

        rig.Send("hello");
        rig.Complete();
        rig.Send("again");

        Assert.Equal([AntiSlopPolicy.EnabledInstructions + "\n\nhello", "again"], rig.Session.Inputs.Select(input => input.Text));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-071")] // :1329 AntiSlopPolicy.instructions(enabled:) - the off note is delivered too
    public void With_the_policy_off_the_off_note_still_leads_the_first_message()
    {
        using var rig = AntiSlopRig(antiSlop: false);

        rig.Send("hello");

        Assert.Equal(AntiSlopPolicy.DisabledInstructions + "\n\nhello", Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-071")] // :1326 SlashCommand.name(in: text) == nil: a slash command carries no prefix and spends no delivery
    public void A_slash_command_carries_no_policy_and_leaves_it_for_the_next_ordinary_message()
    {
        using var rig = AntiSlopRig(antiSlop: true);

        rig.Send("/review the diff");
        rig.Complete();
        rig.Send("then this");

        Assert.Equal(["/review the diff", AntiSlopPolicy.EnabledInstructions + "\n\nthen this"], rig.Session.Inputs.Select(input => input.Text));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-071")] // :1329 sessionSignature?.antiSlopEnabled ?? app.settings.antiSlopEnabled - the launch value wins
    public void The_policy_delivered_is_the_one_the_session_was_launched_with_not_the_current_setting()
    {
        using var rig = AntiSlopRig(antiSlop: true);
        // The setting flips after EnsureSession built its signature and before the prompt is built.
        rig.Session.OnFirstIsRunningRead = () => rig.App.Settings.AntiSlopEnabled = false;

        rig.Send("hello");

        Assert.Equal(AntiSlopPolicy.EnabledInstructions + "\n\nhello", Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-071")] // :1325 !keepsHydraPolicyInSystemPrompt: a provider that keeps its policy at launch gets no prefix
    public void A_provider_that_keeps_its_policy_in_the_system_prompt_gets_no_prefix()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, settings: app => app.Settings.AntiSlopEnabled = true);

        rig.Send("hello");

        Assert.Equal("hello", Assert.Single(rig.Session.Inputs).Text);
    }

    // ---- the forwarded continuation and the dropped-block notice (PR-RUNTIME-072) ----

    private static ThreadContinuation Forwarded() => new(Guid.NewGuid(), "Source chat", "the earlier context");

    [Fact]
    [Trait("Req", "PR-RUNTIME-072")] // :1335-1350 outermost layers: drop notice, continuation, then the anti-slop prefix and the text
    public void The_continuation_is_the_outermost_layer_unless_a_dropped_block_notice_is_waiting()
    {
        using var rig = AntiSlopRig(antiSlop: true, install: false);
        var continuation = Forwarded();
        Assert.True(rig.H.Main.Loop.Run(() => rig.Runtime.InstallContinuation(continuation), AppModelHarness.Bound));
        rig.Install(rig.Session);
        ThreadRuntimeReflection.Set(rig.Runtime, "hydraBudget", new HydraDelegationBudget().NoteDropped("No heads went out."));

        rig.Send("hello");
        rig.Complete();
        rig.Send("again");

        Assert.Equal(
            [
                "[Hydra] No heads went out.\n\n---\n\n" + continuation.Prompt + "\n\n" + AntiSlopPolicy.EnabledInstructions + "\n\nhello",
                "again",
            ],
            rig.Session.Inputs.Select(input => input.Text));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-072")] // :1340 `SlashCommand.name(in: text) == nil`: a slash command neither carries the snapshot nor spends it
    public void A_slash_command_neither_carries_the_continuation_nor_spends_its_one_delivery()
    {
        using var rig = AntiSlopRig(antiSlop: true, install: false);
        var continuation = Forwarded();
        Assert.True(rig.H.Main.Loop.Run(() => rig.Runtime.InstallContinuation(continuation), AppModelHarness.Bound));
        rig.Install(rig.Session);

        rig.Send("/plan");
        rig.Complete();
        rig.Send("now the real one");

        Assert.Equal(
            ["/plan", continuation.Prompt + "\n\n" + AntiSlopPolicy.EnabledInstructions + "\n\nnow the real one"],
            rig.Session.Inputs.Select(input => input.Text));
    }

    // ---- delivery commits (PR-RUNTIME-075) and the held-back session id (PR-RUNTIME-041) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-075")] // :1364-1370 a session released mid-send leaves both deliveries pending
    public void A_session_replaced_during_the_send_leaves_both_prefixes_to_go_out_again_on_the_next_one()
    {
        using var rig = AntiSlopRig(antiSlop: true, install: false);
        var continuation = Forwarded();
        Assert.True(rig.H.Main.Loop.Run(() => rig.Runtime.InstallContinuation(continuation), AppModelHarness.Bound));
        rig.Install(rig.Session);
        rig.Session.SendScript = _ =>
        {
            rig.Runtime.StopSession();
            return Task.CompletedTask;
        };

        rig.Send("hello");
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        var replacement = new ThreadRuntimeScriptedSession();
        rig.Install(replacement);
        rig.Send("next");

        Assert.Equal(
            continuation.Prompt + "\n\n" + AntiSlopPolicy.EnabledInstructions + "\n\nnext",
            Assert.Single(replacement.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-075")] // :1364-1366 "Failed sends ... must leave delivery pending"
    [Trait("Req", "PR-RUNTIME-073")] // failure_cases: a throwing send commits neither flag
    public void A_send_that_throws_leaves_both_prefixes_to_go_out_with_the_next_message()
    {
        using var rig = AntiSlopRig(antiSlop: true, install: false);
        var continuation = Forwarded();
        Assert.True(rig.H.Main.Loop.Run(() => rig.Runtime.InstallContinuation(continuation), AppModelHarness.Bound));
        rig.Install(rig.Session);
        rig.Session.SendFailure = new ProviderError(new ProviderError.Case.Failed("boom"));

        rig.Send("hello");
        rig.Session.SendFailure = null;
        rig.Send("again");

        var expected = continuation.Prompt + "\n\n" + AntiSlopPolicy.EnabledInstructions + "\n\n";
        Assert.Equal([expected + "hello", expected + "again"], rig.Session.Inputs.Select(input => input.Text));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-041")] // :1371-1375 the held-back id is written to the thread after the send returns
    public void The_held_back_session_id_reaches_the_thread_once_the_send_returns()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        ThreadRuntimeReflection.Set(rig.Runtime, "unsentSessionID", "held-id");
        string? duringSend = "unset";
        rig.Session.SendScript = _ =>
        {
            duringSend = rig.App.Thread(rig.InitialThread.Id)!.ProviderSessionID;
            return Task.CompletedTask;
        };

        rig.Send("hello");

        Assert.Null(duringSend);
        Assert.Equal("held-id", rig.Thread.ProviderSessionID);
        Assert.Null(ThreadRuntimeReflection.Get<string>(rig.Runtime, "unsentSessionID"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-041")] // failure_cases: a send that throws never reaches the write
    public void A_send_that_throws_leaves_the_session_id_held_back()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        ThreadRuntimeReflection.Set(rig.Runtime, "unsentSessionID", "held-id");
        rig.Session.SendFailure = new ProviderError(new ProviderError.Case.Failed("boom"));

        rig.Send("hello");

        Assert.Null(rig.Thread.ProviderSessionID);
        Assert.Equal("held-id", ThreadRuntimeReflection.Get<string>(rig.Runtime, "unsentSessionID"));
    }

    // ---- slash commands (PR-RUNTIME-067, PR-RUNTIME-076) ----

    private static ValueList<SlashCommand> Commands(params (string Name, string Detail)[] entries) =>
        new(entries.Select(entry => new SlashCommand { Name = entry.Name, Detail = entry.Detail }));

    [Fact]
    [Trait("Req", "PR-RUNTIME-067")] // :1288-1290 first entry whose name matches, from the list keyed by provider and directory
    [Trait("Req", "PR-RUNTIME-076")] // :1376 recordCommand(command.name, for: provider) - the name only
    public void A_slash_command_is_resolved_to_the_first_matching_entry_and_its_name_is_recorded()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.H.Main.OnMain(() => rig.App.Providers.UpdateCommands(
            Commands(("other", "o"), ("review", "first"), ("review", "second")), ProviderKind.Claude, rig.Directory));

        rig.Send("/review the diff");

        var input = Assert.Single(rig.Session.Inputs);
        Assert.Equal("/review the diff", input.Text);
        Assert.Equal("first", input.Command!.Detail);
        Assert.Equal(["review"], rig.H.Main.OnMain(() => rig.App.Providers.RecentCommands["claude"]));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-067")] // failure_cases: no matching name gives nil and the raw text still goes
    [Trait("Req", "PR-RUNTIME-076")] // preconditions: only a resolved command is recorded
    public void An_unmatched_slash_command_sends_the_raw_text_with_no_command_and_records_nothing()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.H.Main.OnMain(() => rig.App.Providers.UpdateCommands(Commands(("review", "first")), ProviderKind.Claude, rig.Directory));

        rig.Send("/nope");

        var input = Assert.Single(rig.Session.Inputs);
        Assert.Equal("/nope", input.Text);
        Assert.Null(input.Command);
        Assert.False(rig.H.Main.OnMain(() => rig.App.Providers.RecentCommands.ContainsKey("claude")));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-067")] // windows_path: the directory is part of the key - a worktree and its project have separate lists
    public void The_command_list_of_the_threads_worktree_is_used_not_the_projects()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "tf-runtime-worktree-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(worktree);
        try
        {
            using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { WorktreePath = worktree });
            rig.H.Main.OnMain(() => rig.App.Providers.UpdateCommands(Commands(("review", "project's")), ProviderKind.Claude, rig.Project.Path));

            rig.Send("/review one");
            rig.Complete();
            rig.H.Main.OnMain(() => rig.App.Providers.UpdateCommands(Commands(("review", "worktree's")), ProviderKind.Claude, worktree));
            rig.Send("/review two");

            Assert.Null(rig.Session.Inputs[0].Command);
            Assert.Equal("worktree's", rig.Session.Inputs[1].Command!.Detail);
        }
        finally
        {
            try { Directory.Delete(worktree, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
