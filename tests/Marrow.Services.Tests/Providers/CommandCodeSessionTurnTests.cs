#if DEBUG
using System.Collections;
using System.Reflection;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/CommandCodeSession.swift (B3 52bc7926), send() :126-207. Every test runs
// real turns through the compiled Command Code stand-in (tests/fixtures/command-code-cli, R-TEST-8, R-TEST-10: offline, never a
// real `cmd` CLI), whose recorded argv, captured stdin and scripted stdout and exit status are the observable side of a turn.
// The data root is redirected into MARROW_PORT_DATA_DIR (see CommandCodeSessionLaunchTests), so the class joins the same
// non-parallel collection (R-TEST-5).
[Collection("Dev data root env")]
public sealed class CommandCodeSessionTurnTests
{
    private static string ValueAfter(IReadOnlyList<string> arguments, string flag)
    {
        var index = arguments.ToList().IndexOf(flag);
        Assert.True(index >= 0 && index + 1 < arguments.Count, flag + " is missing from: " + string.Join(" ", arguments));
        return arguments[index + 1];
    }

    private static async Task SendAndFinish(CommandCodeSessionRig rig, TurnInput turn, int turnNumber)
    {
        await rig.Session.Send(turn);
        rig.WaitForTurns(turnNumber);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-209")] // CommandCodeSession.swift:131-132 (a cleared model keeps the previous one; the effort is replaced unconditionally, nil included)
    public async Task A_turn_without_a_model_keeps_the_previous_model_but_a_turn_without_an_effort_clears_it()
    {
        using var rig = new CommandCodeSessionRig("turn-model-effort");
        await rig.Session.Start();

        await SendAndFinish(rig, CommandCodeSessionRig.Turn(model: "gpt-5.5", effort: "high"), 1);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(model: null, effort: null), 2);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(model: "", effort: "xhigh"), 3);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(model: "gpt-5.4-mini", effort: "high"), 4);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(model: null, effort: ""), 5);

        var first = rig.LaunchArguments(0);
        Assert.Equal("gpt-5.5", ValueAfter(first, "--model"));
        Assert.Equal("high", ValueAfter(first, "--effort"));
        var second = rig.LaunchArguments(1);
        Assert.Equal("gpt-5.5", ValueAfter(second, "--model")); // kept
        Assert.DoesNotContain("--effort", second); // replaced by nil
        var third = rig.LaunchArguments(2);
        Assert.Equal("gpt-5.5", ValueAfter(third, "--model")); // an empty model does not replace it either
        Assert.Equal("xhigh", ValueAfter(third, "--effort"));
        var fourth = rig.LaunchArguments(3);
        Assert.Equal("gpt-5.4-mini", ValueAfter(fourth, "--model")); // a non-empty model does
        Assert.Equal("high", ValueAfter(fourth, "--effort"));
        var fifth = rig.LaunchArguments(4);
        Assert.Equal("gpt-5.4-mini", ValueAfter(fifth, "--model"));
        Assert.DoesNotContain("--effort", fifth); // an empty effort replaces too
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-209")] // CommandCodeSession.swift:129-130,133 (the turn's runtime mode, interaction mode and final-report flag replace the stored ones)
    public async Task Each_turn_replaces_the_runtime_mode_the_interaction_mode_and_the_final_report_flag()
    {
        using var rig = new CommandCodeSessionRig("turn-modes", runtimeMode: RuntimeMode.Supervised, interactionMode: InteractionMode.Build);
        await rig.Session.Start();

        await SendAndFinish(rig, CommandCodeSessionRig.Turn(runtimeMode: RuntimeMode.FullAccess, interactionMode: InteractionMode.Build, isFinalReport: true), 1);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(runtimeMode: RuntimeMode.Supervised, interactionMode: InteractionMode.Plan, isFinalReport: false), 2);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(runtimeMode: RuntimeMode.Supervised, interactionMode: InteractionMode.Build, isFinalReport: false), 3);

        var full = rig.LaunchArguments(0);
        Assert.Equal("1", ValueAfter(full, "--max-turns")); // final report: one reply
        Assert.Contains("--yolo", full);
        Assert.DoesNotContain("--mod", full); // Full access in Build mode loads no gate
        var plan = rig.LaunchArguments(1);
        Assert.Equal("400", ValueAfter(plan, "--max-turns"));
        Assert.Equal("plan", ValueAfter(plan, "--permission-mode"));
        Assert.DoesNotContain("--yolo", plan);
        Assert.Contains("--mod", plan);
        var supervised = rig.LaunchArguments(2);
        Assert.Contains("--yolo", supervised);
        Assert.DoesNotContain("--permission-mode", supervised);
        Assert.Contains("--mod", supervised);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-209")] // CommandCodeSession.swift:134 (turnCounter is incremented per turn; the plan id carries it, :420-425)
    public async Task The_turn_counter_advances_with_every_turn()
    {
        using var rig = new CommandCodeSessionRig("turn-counter", interactionMode: InteractionMode.Plan);
        await rig.Session.Start();
        rig.Script.SetStdout("{\"type\":\"event\",\"event\":{\"type\":\"run_end\",\"result\":{\"finalText\":\"The plan.\"}}}\n");

        await SendAndFinish(rig, CommandCodeSessionRig.Turn(interactionMode: InteractionMode.Plan), 1);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(interactionMode: InteractionMode.Plan), 2);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(interactionMode: InteractionMode.Plan), 3);

        var ids = rig.Events.OfType<ProviderEvent.PlanCompleted>().Select(plan => plan.Id).ToList();
        Assert.Equal(3, ids.Count);
        var prefix = ids[0][..^"plan1".Length];
        Assert.Matches("^cc-[0-9A-Fa-f]{8}-$", prefix);
        Assert.Equal(new[] { prefix + "plan1", prefix + "plan2", prefix + "plan3" }, ids);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-209")] // CommandCodeSession.swift:137-138 (runResult and runError are cleared, so a finished turn's verdict never decides the next)
    public async Task A_previous_turns_result_and_error_do_not_decide_the_next_turn()
    {
        using var rig = new CommandCodeSessionRig("turn-result-reset");
        await rig.Session.Start();
        rig.Script.SetStdout(
            "{\"type\":\"event\",\"event\":{\"type\":\"run_error\",\"error\":\"boom one\"}}\n" +
            "{\"type\":\"result\",\"subtype\":\"success\"}\n");
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(), 1);
        var firstEnd = Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]);
        Assert.Equal(TurnStatus.Completed, firstEnd.Status);

        // Second turn: the CLI says nothing and exits with 1. No result line and no run_error of THIS turn, so the
        // fallback message applies; a stale result would complete it, a stale run_error would repeat "boom one".
        rig.Script.SetStdout("");
        rig.Script.SetExitStatus(1);
        await SendAndFinish(rig, CommandCodeSessionRig.Turn(), 2);

        var secondEnd = Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]);
        Assert.Equal(TurnStatus.Failed, secondEnd.Status);
        Assert.Equal("Command Code exited with code 1.", secondEnd.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-209")] // CommandCodeSession.swift:129-142 (all nine pieces of per-turn state are reset before the run, whatever the previous turn left)
    public async Task Send_resets_the_per_turn_state_before_the_run()
    {
        using var rig = new CommandCodeSessionRig("turn-state", model: "gpt-5.5");
        await rig.Session.Start();
        rig.Hold(); // the run stays alive, so the state can be read while the turn is active

        // State a previous turn could have left behind.
        rig.SetField("interruptRequested", true);
        rig.SetField("runResult", MakeRunResult("success"));
        rig.SetField("runError", "stale error");
        var stream = (StreamedTextTracker)rig.Field("stream")!;
        stream.AppendMessage("leftover text");
        Assert.True(stream.StreamedText);
        var snapshotted = Path.Combine(rig.WorkingDirectory, "old.txt");
        File.WriteAllText(snapshotted, "old contents");
        var snapshots = (EditSnapshots)rig.Field("editSnapshots")!;
        snapshots.Capture("e1", snapshotted);
        Assert.Equal("old contents", snapshots.Text("e1"));
        var toolNames = (IDictionary)rig.Field("toolNames")!;
        toolNames["t1"] = "write_file";
        var toolInputs = (IDictionary)rig.Field("toolInputs")!;
        toolInputs["t1"] = JSONValue.Parse("{\"file_path\":\"old.txt\"}")!;

        await rig.Session.Send(CommandCodeSessionRig.Turn(
            model: "gpt-5.4-mini", effort: "high", runtimeMode: RuntimeMode.FullAccess, interactionMode: InteractionMode.Plan, isFinalReport: true));

        Assert.Equal(RuntimeMode.FullAccess, rig.Field("runtimeMode"));
        Assert.Equal(InteractionMode.Plan, rig.Field("interactionMode"));
        Assert.Equal("gpt-5.4-mini", rig.Field("model"));
        Assert.Equal("high", rig.Field("effort"));
        Assert.Equal(true, rig.Field("isFinalReport"));
        Assert.Equal(1, rig.Field("turnCounter"));
        Assert.Equal(true, rig.Field("turnActive"));
        Assert.Equal(false, rig.Field("interruptRequested"));
        Assert.Null(rig.Field("runResult"));
        Assert.Null(rig.Field("runError"));
        Assert.False(stream.StreamedText);
        Assert.Equal("", snapshots.Text("e1"));
        Assert.Empty(toolNames);
        Assert.Empty(toolInputs);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-217")] // CommandCodeSession.swift:194-195 (the prompt goes to stdin as UTF-8, no code-page translation, no BOM, then the input is closed)
    public async Task The_prompt_is_written_to_stdin_as_plain_utf8_and_the_input_is_closed()
    {
        using var rig = new CommandCodeSessionRig("turn-stdin");
        rig.EnableStdinCapture();
        await rig.Session.Start();
        const string text = "Résumé: naïve 日本語 \U0001F600 — end\r\nsecond line";

        await rig.Session.Send(CommandCodeSessionRig.Turn(text: text));

        // The stand-in reads stdin to its END before it records anything, so these bytes exist only once the handle was closed.
        var bytes = rig.CapturedStdin(0);
        Assert.Equal(Encoding.UTF8.GetBytes(text), bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no byte order mark");
        Assert.True(File.Exists(Path.Combine(rig.Script.ControlDirectory, "stdin-closed-0.txt")));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-217")] // CommandCodeSession.swift:196 (turnStarted with no provider turn id is emitted by send itself, before any message)
    public async Task Send_announces_the_turn_with_no_provider_turn_id_before_any_output_is_handled()
    {
        using var rig = new CommandCodeSessionRig("turn-started");
        await rig.Session.Start();
        rig.Script.SetStdout("{\"type\":\"event\",\"event\":{\"type\":\"run_start\",\"sessionId\":\"s-9\"}}\n");

        await rig.Session.Send(CommandCodeSessionRig.Turn());

        var started = Assert.IsType<ProviderEvent.TurnStarted>(Assert.Single(rig.Events)); // nothing pumped yet: only the announcement
        Assert.Null(started.ProviderTurnID);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-217")] // CommandCodeSession.swift:198-206 (each message goes through handle; only after the stream ends is the exit awaited and didClose called)
    public async Task Output_is_handled_message_by_message_and_the_turn_closes_only_after_it_with_the_exit_status()
    {
        using var rig = new CommandCodeSessionRig("turn-order");
        await rig.Session.Start();
        rig.Script.SetStdout(
            "{\"type\":\"event\",\"event\":{\"type\":\"run_start\",\"sessionId\":\"s-9\"}}\n" +
            "{\"type\":\"event\",\"event\":{\"type\":\"message_start\"}}\n" +
            "{\"type\":\"event\",\"event\":{\"type\":\"text_delta\",\"delta\":\"hi there\"}}\n");
        rig.Script.SetExitStatus(3); // not signed in: only the exit code says so, since no result line came

        await SendAndFinish(rig, CommandCodeSessionRig.Turn(), 1);

        Assert.Collection(rig.Events,
            e => Assert.Null(Assert.IsType<ProviderEvent.TurnStarted>(e).ProviderTurnID),
            e => Assert.Equal("s-9", Assert.IsType<ProviderEvent.SessionReady>(e).SessionID),
            e => Assert.Equal("hi there", Assert.IsType<ProviderEvent.MessageDelta>(e).Text),
            e =>
            {
                var end = Assert.IsType<ProviderEvent.TurnCompleted>(e);
                Assert.Equal(TurnStatus.Failed, end.Status);
                Assert.Equal(
                    "Command Code is not signed in. Run `cmd login` in Terminal, or add a Studio API key in Settings → Providers → Command Code.",
                    end.Error);
            });
        Assert.False((bool)rig.Field("turnActive")!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-217")] // CommandCodeSession.swift:229-235,198-206 (an interrupt terminates the process, which ends the stream and reaches didClose)
    public async Task An_interrupt_terminates_the_process_and_the_turn_still_closes()
    {
        using var rig = new CommandCodeSessionRig("turn-interrupt");
        await rig.Session.Start();
        rig.Hold();
        await rig.Session.Send(CommandCodeSessionRig.Turn());
        rig.LaunchArguments(0); // the process is up and holding

        await rig.Session.Interrupt();
        rig.WaitForTurns(1);

        var end = Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]);
        Assert.Equal(TurnStatus.Interrupted, end.Status);
    }

    private static object MakeRunResult(string subtype)
    {
        var runResultType = typeof(CommandCodeSession).GetNestedType("RunResult", BindingFlags.NonPublic)!;
        var instance = Activator.CreateInstance(runResultType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: null, culture: null)!;
        runResultType.GetProperty("Subtype")!.SetValue(instance, subtype);
        return instance;
    }
}
#endif
