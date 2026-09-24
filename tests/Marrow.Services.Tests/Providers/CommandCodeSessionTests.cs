using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

public sealed class CommandCodeSessionTests
{
    [Fact]
    [Trait("Req", "PR-PROVCLI-206")] // DroppyCode/Services/Providers/CommandCodeSession.swift:115-124
    public void TranscriptExists_scans_immediate_project_folders_for_the_exact_transcript_name()
    {
        var root = ScratchDirectory();
        try
        {
            var projects = Path.Combine(root, ".commandcode", "projects");
            var first = Path.Combine(projects, "first");
            var second = Path.Combine(projects, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllBytes(Path.Combine(second, "session-123.jsonl"), []);

            Assert.True((bool)InvokeStatic("TranscriptExists", "session-123", new Dictionary<string, string> { ["HOME"] = root })!);
            Assert.False((bool)InvokeStatic("TranscriptExists", "session-123", new Dictionary<string, string> { ["HOME"] = Path.Combine(root, "missing-home") })!);
            Assert.False((bool)InvokeStatic("TranscriptExists", "session-123.jsonl", new Dictionary<string, string> { ["HOME"] = root })!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-218")] // DroppyCode/Services/Providers/CommandCodeSession.swift:209-218
    public void Prompt_names_only_attachments_that_exist()
    {
        var root = ScratchDirectory();
        try
        {
            var existing = Path.Combine(root, "image.png");
            File.WriteAllBytes(existing, []);
            var input = new TurnInput
            {
                Text = "Look at these.",
                Images = new ValueList<Attachment>(
                [
                    new Attachment { Name = "image.png", Path = existing, MimeType = "image/png" },
                    new Attachment { Name = "missing.png", Path = Path.Combine(root, "missing.png"), MimeType = "image/png" },
                ]),
                RuntimeMode = RuntimeMode.Supervised,
                InteractionMode = InteractionMode.Build,
            };

            Assert.Equal(
                "Look at these.\n\nAttached files (read them with read_file):\n- " + existing,
                (string)InvokeStatic("Prompt", input)!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-218")] // DroppyCode/Services/Providers/CommandCodeSession.swift:209-218
    public void Prompt_adds_nothing_when_no_attachment_exists()
    {
        var input = new TurnInput
        {
            Text = "Continue.",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };

        Assert.Equal("Continue.", (string)InvokeStatic("Prompt", input)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-204")] // DroppyCode/Services/Providers/CommandCodeSession.swift:90-92
    public void IsRunning_is_the_started_and_not_stopped_flag_pair()
    {
        var session = CreateSession();
        var type = session.GetType();

        Assert.False((bool)type.GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(session)!);
        SetField(session, "started", true);
        Assert.True((bool)type.GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(session)!);
        SetField(session, "stopped", true);
        Assert.False((bool)type.GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(session)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-220")] // DroppyCode/Services/Providers/CommandCodeSession.swift:237-239
    public async Task Compact_always_fails_with_the_source_message()
    {
        var session = CreateSession();
        var compact = session.GetType().GetMethod("Compact", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        var error = await Assert.ThrowsAsync<ProviderError>(() => (Task)compact.Invoke(session, null)!);

        Assert.Equal("Command Code compacts its context on its own.", error.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-221")] // DroppyCode/Services/Providers/CommandCodeSession.swift:253
    public void AnswerQuestion_emits_no_event()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        session.GetType().GetMethod("AnswerQuestion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(session, ["request", ValueMap<string, ValueList<string>>.Empty]);

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-223")] // DroppyCode/Services/Providers/CommandCodeSession.swift:267-284
    public void Result_frame_adopts_a_different_nonempty_session_id()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"result","sessionId":"resume-123"}""");

        var ready = Assert.Single(events);
        Assert.Equal("SessionReady", ready.GetType().Name);
        Assert.Equal("resume-123", ready.GetType().GetProperty("SessionID")!.GetValue(ready));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-223")] // DroppyCode/Services/Providers/CommandCodeSession.swift:267-284
    public void Unknown_top_level_frame_is_ignored()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"unknown"}""");

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-226")] // DroppyCode/Services/Providers/CommandCodeSession.swift:293-306
    public void Text_delta_without_message_start_opens_a_namespaced_message_lazily()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"text_delta","delta":"hello"}}""");

        var delta = Assert.Single(events);
        Assert.Equal("MessageDelta", delta.GetType().Name);
        Assert.Equal("hello", delta.GetType().GetProperty("Text")!.GetValue(delta));
        Assert.Matches("^cc-[0-9A-F]{8}-m1$", (string)delta.GetType().GetProperty("Id")!.GetValue(delta)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-226")] // DroppyCode/Services/Providers/CommandCodeSession.swift:293-306
    public void Empty_text_delta_is_dropped()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"text_delta","delta":""}}""");

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-227")] // DroppyCode/Services/Providers/CommandCodeSession.swift:307-337
    public void Blank_thinking_end_does_not_complete_reasoning()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"thinking_start"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"thinking_delta","delta":"  \n"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"thinking_end"}}""");

        Assert.Single(events, item => item.GetType().Name == "ReasoningDelta");
        Assert.DoesNotContain(events, item => item.GetType().Name == "ReasoningCompleted");
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-227")] // DroppyCode/Services/Providers/CommandCodeSession.swift:307-337
    public void Whole_message_is_completed_from_content_and_clears_its_id()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"message_start"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"message_end","content":[{"type":"text","text":"complete"}]}}""");

        var completed = Assert.Single(events);
        Assert.Equal("MessageCompleted", completed.GetType().Name);
        Assert.Equal("complete", completed.GetType().GetProperty("Text")!.GetValue(completed));
        Assert.Null(GetField(session, "currentMessageID"));
        Assert.Null(GetField(session, "currentThinkingID"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-205")] // DroppyCode/Services/Providers/CommandCodeSession.swift:98-102
    public async Task Start_throws_not_installed_when_no_executable()
    {
        var session = CreateSession();
        var start = session.GetType().GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        var error = await Assert.ThrowsAsync<ProviderError>(() => (Task<string>)start.Invoke(session, null)!);

        Assert.Equal(
            "Command Code is not installed. Install it and sign in with `cmd login`.",
            error.Message);
    }

    // PR-PROVCLI-207 (mod install, approvals-directory creation, watcher start, started/stopped
    // flags) is the success path immediately past the guard above. Start() calls the parameterless
    // CommandCodeAPI.InstallMod(), which resolves the real Environment.SpecialFolder.LocalApplicationData
    // (CommandCodeAPI.cs:643) with no seam Start() passes through, so driving Start() to that success
    // path in a test would write to the real %LOCALAPPDATA%\Marrow Code\CommandCode - the folder the
    // hard fences forbid touching. UNTESTABLE at this part's boundary for that reason; the same
    // filesystem-seam gap is why PR-PROVCLI-216 and PR-PROVCLI-217 (below) are untestable.

    [Fact]
    [Trait("Req", "PR-PROVCLI-208")] // DroppyCode/Services/Providers/CommandCodeSession.swift:126-128
    public async Task Send_throws_not_running_when_the_session_has_not_started()
    {
        var session = CreateSession();
        var send = session.GetType().GetMethod("Send", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var input = new TurnInput
        {
            Text = "hi",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };

        var error = await Assert.ThrowsAsync<ProviderError>(() => (Task)send.Invoke(session, [input])!);

        Assert.Equal("The agent session is not running.", error.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-208")] // DroppyCode/Services/Providers/CommandCodeSession.swift:126-128
    public async Task Send_throws_the_still_on_last_turn_message_while_a_turn_is_active()
    {
        var session = CreateSessionWithExecutable();
        SetField(session, "started", true);
        SetField(session, "turnActive", true);
        var send = session.GetType().GetMethod("Send", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var input = new TurnInput
        {
            Text = "hi",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };

        var error = await Assert.ThrowsAsync<ProviderError>(() => (Task)send.Invoke(session, [input])!);

        Assert.Equal("Command Code is still on the last turn.", error.Message);
    }

    [Theory]
    [InlineData(false, "gpt-5", null, new[] { "-p", "--output-format", "json", "--skip-onboarding", "--no-auto-update", "--trust", "--tools-enable", "todo_write", "--max-turns", "400", "--model", "gpt-5", "--yolo" })]
    [InlineData(true, null, null, new[] { "-p", "--output-format", "json", "--skip-onboarding", "--no-auto-update", "--trust", "--tools-enable", "todo_write", "--max-turns", "1", "--yolo" })]
    [Trait("Req", "PR-PROVCLI-210")] // DroppyCode/Services/Providers/CommandCodeSession.swift:144-152
    [Trait("Req", "PR-PROVCLI-211")] // DroppyCode/Services/Providers/CommandCodeSession.swift:153-154
    public void BuildArguments_base_vector_and_model_are_exact(bool isFinalReport, string? model, string? effort, string[] expected)
    {
        var arguments = (List<string>)InvokeStaticArgs("BuildArguments",
            isFinalReport, model, effort, InteractionMode.Build, RuntimeMode.Supervised, null, null)!;

        Assert.Equal(expected, arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-211")] // DroppyCode/Services/Providers/CommandCodeSession.swift:153-154
    public void BuildArguments_drops_an_effort_the_models_table_does_not_list()
    {
        var arguments = (List<string>)InvokeStaticArgs("BuildArguments",
            false, "some-model-with-no-efforts", "high", InteractionMode.Build, RuntimeMode.Supervised, null, null)!;

        Assert.DoesNotContain("--effort", arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-212")] // DroppyCode/Services/Providers/CommandCodeSession.swift:155-161
    public void BuildArguments_plan_mode_passes_permission_mode_plan_not_yolo()
    {
        var arguments = (List<string>)InvokeStaticArgs("BuildArguments",
            false, null, null, InteractionMode.Plan, RuntimeMode.Supervised, null, null)!;

        Assert.Equal(["--permission-mode", "plan"], arguments.SkipWhile(a => a != "--permission-mode"));
        Assert.DoesNotContain("--yolo", arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-213")] // DroppyCode/Services/Providers/CommandCodeSession.swift:162-165
    public void BuildArguments_loads_no_mod_in_full_access_build_mode_with_no_mcp_config()
    {
        using var scratch = new StorageScratchFolder();
        var modURL = SwiftTraps.Unwrap(SwiftURL.Create("file:///mod.ts"));
        var arguments = (List<string>)InvokeStaticArgs("BuildArguments",
            false, null, null, InteractionMode.Build, RuntimeMode.FullAccess, modURL, null)!;

        Assert.DoesNotContain("--mod", arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-213")] // DroppyCode/Services/Providers/CommandCodeSession.swift:162-165
    public void BuildArguments_loads_the_mod_when_not_full_access()
    {
        var modURL = SwiftTraps.Unwrap(SwiftURL.Create("file:///mod.ts"));
        var arguments = (List<string>)InvokeStaticArgs("BuildArguments",
            false, null, null, InteractionMode.Build, RuntimeMode.Supervised, modURL, null)!;

        Assert.Contains("--mod", arguments);
        Assert.Contains(modURL.Path, arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-215")] // DroppyCode/Services/Providers/CommandCodeSession.swift:166
    public void BuildArguments_appends_resume_last_after_the_mod()
    {
        var modURL = SwiftTraps.Unwrap(SwiftURL.Create("file:///mod.ts"));
        var arguments = (List<string>)InvokeStaticArgs("BuildArguments",
            false, null, null, InteractionMode.Build, RuntimeMode.Supervised, modURL, "resume-1")!;

        Assert.Equal(["--resume", "resume-1"], arguments.TakeLast(2));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-215")] // DroppyCode/Services/Providers/CommandCodeSession.swift:166
    public void BuildArguments_omits_resume_for_an_empty_session_id()
    {
        var arguments = (List<string>)InvokeStaticArgs("BuildArguments",
            false, null, null, InteractionMode.Build, RuntimeMode.Supervised, null, "")!;

        Assert.DoesNotContain("--resume", arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-214")] // DroppyCode/Services/Providers/CommandCodeSession.swift:168-173
    public void ApprovalsEnvironment_sets_the_directory_outside_full_access()
    {
        var directory = SwiftTraps.Unwrap(SwiftURL.Create(new Uri(ScratchDirectory() + Path.DirectorySeparatorChar).AbsoluteUri));
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        var method = type.GetMethod("ApprovalsEnvironment", BindingFlags.Static | BindingFlags.NonPublic)!;

        var result = (ValueMap<string, string>)method.Invoke(null, [ValueMap<string, string>.Empty, RuntimeMode.Supervised, directory])!;

        Assert.Equal(directory.Path, result["DROPPY_CODE_APPROVALS"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-214")] // DroppyCode/Services/Providers/CommandCodeSession.swift:168-173
    public void ApprovalsEnvironment_explicitly_clears_an_inherited_value_in_full_access()
    {
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        var method = type.GetMethod("ApprovalsEnvironment", BindingFlags.Static | BindingFlags.NonPublic)!;
        var inherited = new ValueMap<string, string>(new Dictionary<string, string> { ["DROPPY_CODE_APPROVALS"] = "stale" });

        var result = (ValueMap<string, string>)method.Invoke(null, [inherited, RuntimeMode.FullAccess, null])!;

        Assert.False(result.ContainsKey("DROPPY_CODE_APPROVALS"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-214")] // DroppyCode/Services/Providers/CommandCodeSession.swift:168-173
    public void ApprovalsEnvironment_clears_a_differently_cased_inherited_key()
    {
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        var method = type.GetMethod("ApprovalsEnvironment", BindingFlags.Static | BindingFlags.NonPublic)!;
        var inherited = new ValueMap<string, string>(new Dictionary<string, string> { ["droppy_code_approvals"] = "stale" });

        var result = (ValueMap<string, string>)method.Invoke(null, [inherited, RuntimeMode.FullAccess, null])!;

        Assert.Empty(result);
    }

    // PR-PROVCLI-209's flag resets (runtimeMode, interactionMode, model, effort, isFinalReport,
    // turnCounter, turnActive, interruptRequested, runResult, runError, streamedText) and its
    // dictionary clears (editSnapshots/toolNames/toolInputs) all happen inline at the top of Send,
    // before Send unconditionally calls the real MCPBridge.InstallBridge() (no DI seam on that call)
    // and reads MCPPaths.ClaudeConfigURL under the live Storage.Locations - so driving Send far enough
    // to observe the reset without touching the real %LOCALAPPDATA%\Marrow Code is not possible without
    // widening Send's signature beyond what this part's records ask for. The flag half of the record is
    // covered indirectly by BuildArguments (:210-215, PR-PROVCLI-210/211/212/213/215) and by
    // Interrupt/Stop's own interruptRequested and turnActive assertions above; the dictionary-clear
    // half and the unconditional-clear-regardless-of-turnActive nuance are UNTESTABLE at this part's
    // boundary for the reason above.
    //
    // PR-PROVCLI-216 (StdioProcess construction, the process.Start() failure wrapping and turnActive
    // reset) and PR-PROVCLI-217 (the stdin prompt write, CloseInput, the .turnStarted event and the
    // read-then-await-exit task) sit even further past that same MCPBridge.InstallBridge() call
    // (CommandCodeSession.cs:176, Send :174-176), so reaching either one from a test hits the identical
    // real-%LOCALAPPDATA%\Marrow Code touch and is UNTESTABLE at this part's boundary for the same
    // reason as PR-PROVCLI-209 and PR-PROVCLI-207 above.

    [Fact]
    [Trait("Req", "PR-PROVCLI-219")] // DroppyCode/Services/Providers/CommandCodeSession.swift:229-235
    public async Task Interrupt_without_an_active_turn_does_nothing()
    {
        var session = CreateSession();
        var interrupt = session.GetType().GetMethod("Interrupt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        await (Task)interrupt.Invoke(session, null)!;

        Assert.False((bool)GetField(session, "interruptRequested")!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-219")] // DroppyCode/Services/Providers/CommandCodeSession.swift:229-235
    public async Task Interrupt_with_an_active_turn_sets_interruptRequested()
    {
        var session = CreateSession();
        SetField(session, "turnActive", true);
        var interrupt = session.GetType().GetMethod("Interrupt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        await (Task)interrupt.Invoke(session, null)!;

        Assert.True((bool)GetField(session, "interruptRequested")!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-222")] // DroppyCode/Services/Providers/CommandCodeSession.swift:255-263
    public void Stop_sets_stopped_and_removes_the_approvals_directory()
    {
        var session = CreateSession();
        var directory = ScratchDirectory();
        var nested = Path.Combine(directory, "child");
        Directory.CreateDirectory(nested);
        var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
        Assert.Equal(directory.Replace('\\', '/'), url.Path);
        SetField(session, "approvalDirectory", url);
        SetField(session, "turnActive", true);

        session.GetType().GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(session, null);

        Assert.True((bool)GetField(session, "stopped")!);
        Assert.False((bool)GetField(session, "turnActive")!);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-225")] // DroppyCode/Services/Providers/CommandCodeSession.swift:41-42
    public void LaunchID_is_the_first_eight_characters_of_an_uppercase_uuid()
    {
        var session = CreateSession();
        var launchID = (string)GetField(session, "launchID")!;

        Assert.Matches("^[0-9A-F]{8}$", launchID);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-224")] // DroppyCode/Services/Providers/CommandCodeSession.swift:286-431
    public void Unlisted_event_name_is_ignored()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"not_a_command_code_event"}}""");

        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-228: model_request_start/end usage and spend ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-228")] // DroppyCode/Services/Providers/CommandCodeSession.swift:338-357
    public void Model_request_start_records_the_active_model()
    {
        var session = CreateSession();
        InvokeHandle(session, """{"type":"event","event":{"type":"model_request_start","model":"claude-sonnet-5"}}""");
        Assert.Equal("claude-sonnet-5", GetField(session, "activeModel"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-228")] // DroppyCode/Services/Providers/CommandCodeSession.swift:338-357
    public void Model_request_end_with_positive_usage_emits_usage_and_token_spend()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var session = CreateSession();
        var events = CaptureEvents(session);

        runtime.OnMain(() => InvokeHandle(session, """{"type":"event","event":{"type":"model_request_end","model":"claude-sonnet-5","usage":{"inputTokens":10,"outputTokens":5}}}"""));

        Assert.Equal(2, events.Count);
        var usage = Assert.IsType<ProviderEvent.Usage>(events[0]);
        Assert.Equal(15, usage.Value.UsedTokens);
        Assert.Equal(1_000_000, usage.Value.WindowTokens);
        var spend = Assert.IsType<ProviderEvent.TokenSpend>(events[1]);
        Assert.Equal(ProviderKind.Commandcode, spend.Value.Provider);
        Assert.Equal(15, spend.Value.TotalTokens);
        Assert.Equal(10, spend.Value.InputTokens);
        Assert.Equal(5, spend.Value.OutputTokens);
        Assert.Equal("claude-sonnet-5", spend.Value.Model);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-228")] // DroppyCode/Services/Providers/CommandCodeSession.swift:338-357
    public void Model_request_end_with_zero_usage_emits_nothing()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"model_request_end","usage":{"inputTokens":0,"outputTokens":0}}}""");

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-228")] // DroppyCode/Services/Providers/CommandCodeSession.swift:338-357
    public void Model_request_end_falls_back_to_the_session_model_when_no_active_model()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var session = CreateSession();
        SetField(session, "model", "claude-sonnet-5");
        var events = CaptureEvents(session);

        runtime.OnMain(() => InvokeHandle(session, """{"type":"event","event":{"type":"model_request_end","usage":{"inputTokens":1,"outputTokens":1}}}"""));

        var usage = Assert.IsType<ProviderEvent.Usage>(events[0]);
        Assert.Equal(1_000_000, usage.Value.WindowTokens);
    }

    // ---- PR-PROVCLI-229: tool_queued ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-229")] // DroppyCode/Services/Providers/CommandCodeSession.swift:358-372
    public void Tool_queued_todo_write_emits_todos_and_no_row()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"tool_queued","toolCallId":"c1","toolName":"todo_write","input":{"todos":[{"content":"a","status":"done"}]}}}""");

        var todos = Assert.Single(events);
        var value = Assert.IsType<ProviderEvent.Todos>(todos).Value;
        Assert.Single(value);
        Assert.Equal("a", value[0].Text);
        Assert.Equal(TodoStep.Status.Done, value[0].TodoStepStatus);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-229")] // DroppyCode/Services/Providers/CommandCodeSession.swift:358-372, :640-646
    public void Tool_queued_write_file_snapshots_the_existing_file_before_the_row()
    {
        var session = CreateSession();
        var root = ScratchDirectory();
        try
        {
            var file = Path.Combine(root, "existing.txt");
            File.WriteAllText(file, "before");
            var events = CaptureEvents(session);

            InvokeHandle(session, """{"type":"event","event":{"type":"tool_queued","toolCallId":"c1","toolName":"write_file","input":{"file_path":""" +
                System.Text.Json.JsonSerializer.Serialize(file) + ""","content":"after"}}}""");

            var started = Assert.Single(events);
            var call = Assert.IsType<ProviderEvent.ToolStarted>(started).Call;
            Assert.Equal(ToolCall.Kind.Edit, call.ToolCallKind);
            var edit = Assert.Single(call.Edits);
            Assert.Contains("before", edit.Diff, StringComparison.Ordinal);
            Assert.Contains("after", edit.Diff, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-229")] // DroppyCode/Services/Providers/CommandCodeSession.swift:358-372, :640-646
    public void Tool_queued_write_file_missing_file_stores_the_empty_snapshot()
    {
        var session = CreateSession();
        var root = ScratchDirectory();
        try
        {
            var file = Path.Combine(root, "missing.txt");
            InvokeHandle(session, """{"type":"event","event":{"type":"tool_queued","toolCallId":"c1","toolName":"write_file","input":{"file_path":""" +
                System.Text.Json.JsonSerializer.Serialize(file) + ""","content":"after"}}}""");

            var snapshots = (System.Collections.IDictionary)GetField(session, "editSnapshots")!;
            Assert.Equal("", snapshots["c1"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-229")] // DroppyCode/Services/Providers/CommandCodeSession.swift:358-372
    public void Tool_queued_rescans_approvals_so_an_already_written_request_is_answered()
    {
        var session = CreateSession();
        var directory = ScratchDirectory();
        try
        {
            var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
            SetField(session, "approvalDirectory", url);
            SetField(session, "turnActive", true);
            SetField(session, "runtimeMode", RuntimeMode.Supervised);
            File.WriteAllText(Path.Combine(directory, "c1.request.json"), """{"toolCallId":"c1","toolName":"read_file","input":{}}""");
            var events = CaptureEvents(session);

            InvokeHandle(session, """{"type":"event","event":{"type":"tool_queued","toolCallId":"c1","toolName":"read_file","input":{}}}""");

            Assert.Contains(events, e => e is ProviderEvent.Approval);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---- PR-PROVCLI-230: tool_update/completed/errored/hook_blocked/denied ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-230")] // DroppyCode/Services/Providers/CommandCodeSession.swift:373-393
    public void Tool_update_emits_only_when_the_partial_text_is_nonempty()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"tool_update","toolCallId":"c1","partial":"hi"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"tool_update","toolCallId":"c1","partial":""}}""");

        var update = Assert.Single(events);
        Assert.Equal("hi", Assert.IsType<ProviderEvent.ToolUpdated>(update).Update.Output);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-230")] // DroppyCode/Services/Providers/CommandCodeSession.swift:373-393
    public void Tool_update_skips_todo_write()
    {
        var session = CreateSession();
        SetToolName(session, "c1", "todo_write");
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"tool_update","toolCallId":"c1","partial":"hi"}}""");

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-230")] // DroppyCode/Services/Providers/CommandCodeSession.swift:373-393
    public void Tool_completed_sets_status_completed_and_omits_output_when_empty()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"tool_completed","toolCallId":"c1","result":""}}""");

        var update = Assert.IsType<ProviderEvent.ToolUpdated>(Assert.Single(events)).Update;
        Assert.Equal(ToolCall.Status.Completed, update.Status);
        Assert.Null(update.Output);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-230")] // DroppyCode/Services/Providers/CommandCodeSession.swift:373-393, :629-638
    public void Tool_completed_reports_a_path_only_edit_when_the_file_is_unchanged()
    {
        var session = CreateSession();
        var root = ScratchDirectory();
        try
        {
            var file = Path.Combine(root, "same.txt");
            File.WriteAllText(file, "same");
            var snapshots = (System.Collections.IDictionary)GetField(session, "editSnapshots")!;
            snapshots["c1"] = "same";
            var inputs = (System.Collections.IDictionary)GetField(session, "toolInputs")!;
            inputs["c1"] = JSONValue.FromDictionaryLiteral(("file_path", file));
            var events = CaptureEvents(session);

            InvokeHandle(session, """{"type":"event","event":{"type":"tool_completed","toolCallId":"c1","result":""}}""");

            var edits = Assert.IsType<ProviderEvent.ToolUpdated>(Assert.Single(events)).Update.Edits;
            var edit = Assert.Single(edits!);
            Assert.Null(edit.Diff);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-230")] // DroppyCode/Services/Providers/CommandCodeSession.swift:373-393
    public void Tool_errored_uses_the_extracted_error_or_the_default()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"tool_errored","toolCallId":"c1","error":"boom"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"tool_errored","toolCallId":"c2","error":null}}""");

        Assert.Equal(2, events.Count);
        Assert.Equal("boom", Assert.IsType<ProviderEvent.ToolUpdated>(events[0]).Update.Output);
        Assert.Equal(ToolCall.Status.Failed, Assert.IsType<ProviderEvent.ToolUpdated>(events[0]).Update.Status);
        Assert.Equal("The tool failed.", Assert.IsType<ProviderEvent.ToolUpdated>(events[1]).Update.Output);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-230")] // DroppyCode/Services/Providers/CommandCodeSession.swift:373-393
    public void Tool_hook_blocked_uses_hookOutput_then_denyMessage_then_the_default_and_resolves_a_pending_approval()
    {
        var session = CreateSession();
        SetPendingApproval(session, "c1", MakeSampleApproval("c1"));
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"tool_hook_blocked","toolCallId":"c1","hookOutput":"blocked reason"}}""");

        Assert.Equal(2, events.Count);
        var update = Assert.IsType<ProviderEvent.ToolUpdated>(events[0]).Update;
        Assert.Equal("blocked reason", update.Output);
        Assert.Equal(ToolCall.Status.Declined, update.Status);
        Assert.IsType<ProviderEvent.RequestResolved>(events[1]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-230")] // DroppyCode/Services/Providers/CommandCodeSession.swift:373-393
    public void Tool_denied_falls_back_to_denyMessage_then_Declined()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"tool_denied","toolCallId":"c1","denyMessage":"no"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"tool_denied","toolCallId":"c2"}}""");

        Assert.Equal("no", Assert.IsType<ProviderEvent.ToolUpdated>(events[0]).Update.Output);
        Assert.Equal("Declined.", Assert.IsType<ProviderEvent.ToolUpdated>(events[1]).Update.Output);
    }

    // ---- PR-PROVCLI-231: subagent_progress ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-231")] // DroppyCode/Services/Providers/CommandCodeSession.swift:394-399
    public void Subagent_progress_picks_the_first_present_subject_and_truncates_at_100()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);
        var longCommand = new string('x', 150);

        InvokeHandle(session, """{"type":"event","event":{"type":"subagent_progress","toolCallId":"c1","toolName":"agent","toolInput":{"command":""" +
            System.Text.Json.JsonSerializer.Serialize(longCommand) + ""","file_path":"ignored.txt"}}}""");

        var output = Assert.IsType<ProviderEvent.ToolOutput>(Assert.Single(events));
        Assert.Equal("agent  " + new string('x', 99) + "…\n", output.Text);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-231")] // DroppyCode/Services/Providers/CommandCodeSession.swift:394-399
    public void Subagent_progress_with_no_subject_is_just_the_tool_name()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"subagent_progress","toolCallId":"c1","toolName":"agent","toolInput":{}}}""");

        Assert.Equal("agent\n", Assert.IsType<ProviderEvent.ToolOutput>(Assert.Single(events)).Text);
    }

    // ---- PR-PROVCLI-232: session_titled, compaction_start/done ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-232")] // DroppyCode/Services/Providers/CommandCodeSession.swift:400-407
    public void Compaction_start_emits_the_exact_notice()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);
        InvokeHandle(session, """{"type":"event","event":{"type":"compaction_start"}}""");
        var notice = Assert.IsType<ProviderEvent.Notice>(Assert.Single(events)).Value;
        Assert.Equal("Compacting context.", notice.Message);
        Assert.Equal(Notice.Level.Info, notice.NoticeLevel);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-232")] // DroppyCode/Services/Providers/CommandCodeSession.swift:400-407
    public void Compaction_done_group_formats_a_positive_count_and_is_plain_at_zero()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"compaction_done","tokensSaved":1500}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"compaction_done","tokensSaved":0}}""");

        Assert.Equal("Compacted context, freeing 1,500 tokens.", Assert.IsType<ProviderEvent.Notice>(events[0]).Value.Message);
        Assert.Equal("Compacted context.", Assert.IsType<ProviderEvent.Notice>(events[1]).Value.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-232")] // DroppyCode/Services/Providers/CommandCodeSession.swift:400-401
    public void Session_titled_emits_only_a_nonempty_title()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"session_titled","title":""}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"session_titled","title":"New title"}}""");

        var title = Assert.Single(events);
        Assert.Equal("New title", Assert.IsType<ProviderEvent.Title>(title).Value);
    }

    // ---- PR-PROVCLI-233: notice level mapping ----

    [Theory]
    [InlineData("error", Notice.Level.Error)]
    [InlineData("warning", Notice.Level.Warning)]
    [InlineData("warn", Notice.Level.Warning)]
    [InlineData("unknown", Notice.Level.Info)]
    [Trait("Req", "PR-PROVCLI-233")] // DroppyCode/Services/Providers/CommandCodeSession.swift:408-415
    public void Notice_maps_level_strings(string level, Notice.Level expected)
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"notice","level":""" +
            System.Text.Json.JsonSerializer.Serialize(level) + ""","message":"m"}}""");

        Assert.Equal(expected, Assert.IsType<ProviderEvent.Notice>(Assert.Single(events)).Value.NoticeLevel);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-233")] // DroppyCode/Services/Providers/CommandCodeSession.swift:408-415
    public void Notice_with_missing_or_empty_message_emits_nothing()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"notice","message":""}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"notice"}}""");

        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-234: run_end ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-234")] // DroppyCode/Services/Providers/CommandCodeSession.swift:420-425
    public void Run_end_in_plan_mode_publishes_the_nonblank_final_text_as_the_plan()
    {
        var session = CreateSession();
        SetField(session, "interactionMode", InteractionMode.Plan);
        SetField(session, "turnCounter", 3);
        var launchID = (string)GetField(session, "launchID")!;
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"run_end","result":{"finalText":"the plan"}}}""");

        var plan = Assert.IsType<ProviderEvent.PlanCompleted>(Assert.Single(events));
        Assert.Equal($"cc-{launchID}-plan3", plan.Id);
        Assert.Equal("the plan", plan.Markdown);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-234")] // DroppyCode/Services/Providers/CommandCodeSession.swift:420-425
    public void Run_end_in_plan_mode_with_blank_text_emits_nothing()
    {
        var session = CreateSession();
        SetField(session, "interactionMode", InteractionMode.Plan);
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"run_end","result":{"finalText":"   "}}}""");

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-234")] // DroppyCode/Services/Providers/CommandCodeSession.swift:420-425
    public void Run_end_in_build_mode_emits_nothing()
    {
        var session = CreateSession();
        SetField(session, "interactionMode", InteractionMode.Build);
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"run_end","result":{"finalText":"text"}}}""");

        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-235/236/237/238: DidClose ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-235")] // DroppyCode/Services/Providers/CommandCodeSession.swift:434-443, :764-770
    public void DidClose_for_a_stale_process_returns_immediately()
    {
        var session = CreateSession();
        var current = new StdioProcess("does-not-run", [], ".", new Dictionary<string, string>());
        SetField(session, "process", current);
        var stale = new StdioProcess("does-not-run", [], ".", new Dictionary<string, string>());
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 0, stale);

        Assert.Empty(events);
        Assert.Same(current, GetField(session, "process"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-235")] // DroppyCode/Services/Providers/CommandCodeSession.swift:434-443
    public void DidClose_resolves_and_clears_every_pending_approval_and_deletes_the_gate_files()
    {
        var session = CreateSession();
        var directory = ScratchDirectory();
        try
        {
            var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
            SetField(session, "approvalDirectory", url);
            File.WriteAllBytes(Path.Combine(directory, "a.request.json"), []);
            File.WriteAllBytes(Path.Combine(directory, "b.reply.json"), []);
            SetPendingApproval(session, "c1", MakeSampleApproval("c1"));
            var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
            SetField(session, "process", process);
            SetField(session, "stopped", true);
            var events = CaptureEvents(session);

            InvokeInstance(session, "DidClose", 0, process);

            Assert.Single(events.OfType<ProviderEvent.RequestResolved>());
            Assert.Empty((System.Collections.IDictionary)GetField(session, "pendingApprovals")!);
            Assert.Empty(Directory.GetFiles(directory));
            // stopped => no turn event beyond the resolved approval.
            Assert.DoesNotContain(events, e => e is ProviderEvent.TurnCompleted);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-236")] // DroppyCode/Services/Providers/CommandCodeSession.swift:445-448
    public void DidClose_reports_interrupted_first_even_with_a_pending_result_line()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        SetField(session, "interruptRequested", true);
        SetField(session, "runResult", MakeRunResult("success", null, null));
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 0, process);

        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events.OfType<ProviderEvent.TurnCompleted>()));
        Assert.Equal(TurnStatus.Interrupted, completed.Status);
        Assert.Null(completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-236")] // DroppyCode/Services/Providers/CommandCodeSession.swift:445-448
    public void DidClose_reports_interrupted_on_exit_code_130()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 130, process);

        Assert.Equal(TurnStatus.Interrupted, Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events.OfType<ProviderEvent.TurnCompleted>())).Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-237")] // DroppyCode/Services/Providers/CommandCodeSession.swift:449-463, :492-495
    public void DidClose_success_with_max_turns_stop_reason_notes_the_cap_but_still_completes()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        SetField(session, "runResult", MakeRunResult("success", "max_turns", null));
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 0, process);

        var notice = Assert.IsType<ProviderEvent.Notice>(events[0]).Value;
        Assert.Equal(Notice.Level.Warning, notice.NoticeLevel);
        Assert.Equal("Command Code stopped at its cap of 400 round trips for one message. Send another to carry on.", notice.Message);
        Assert.Equal(TurnStatus.Completed, Assert.IsType<ProviderEvent.TurnCompleted>(events[1]).Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-237")] // DroppyCode/Services/Providers/CommandCodeSession.swift:449-463
    public void DidClose_max_turns_subtype_completes_with_the_cap_notice()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        SetField(session, "runResult", MakeRunResult("max_turns", null, null));
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 0, process);

        Assert.IsType<ProviderEvent.Notice>(events[0]);
        Assert.Equal(TurnStatus.Completed, Assert.IsType<ProviderEvent.TurnCompleted>(events[1]).Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-237")] // DroppyCode/Services/Providers/CommandCodeSession.swift:449-463, :497-512
    public void DidClose_other_subtype_uses_the_result_error_and_flags_a_rate_limit()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        SetField(session, "runResult", MakeRunResult("error", null, "Rate limit exceeded"));
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 0, process);

        Assert.IsType<ProviderEvent.UsageLimit>(events[0]);
        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(events[1]);
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("Rate limit exceeded", completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-237")] // DroppyCode/Services/Providers/CommandCodeSession.swift:449-463
    public void DidClose_other_subtype_falls_back_through_runError_then_the_default_message()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        SetField(session, "runResult", MakeRunResult("error", null, null));
        SetField(session, "runError", "from run_error");
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 0, process);

        Assert.Equal("from run_error", Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events)).Error);
    }

    [Theory]
    [InlineData(0, TurnStatus.Completed, null)]
    [InlineData(9, TurnStatus.Failed, "The model produced no response.")]
    [Trait("Req", "PR-PROVCLI-238")] // DroppyCode/Services/Providers/CommandCodeSession.swift:464-489
    public void DidClose_with_no_result_line_maps_simple_exit_codes(int status, TurnStatus expected, string? message)
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", status, process);

        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events));
        Assert.Equal(expected, completed.Status);
        Assert.Equal(message, completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-238")] // DroppyCode/Services/Providers/CommandCodeSession.swift:464-489
    public void DidClose_exit_code_3_is_the_sign_in_message()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 3, process);

        Assert.Equal(
            "Command Code is not signed in. Run `cmd login` in Terminal, or add a Studio API key in Settings → Providers → Command Code.",
            Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events)).Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-238")] // DroppyCode/Services/Providers/CommandCodeSession.swift:464-489
    public void DidClose_exit_code_5_emits_usage_limit_then_the_rate_limited_failure()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 5, process);

        Assert.IsType<ProviderEvent.UsageLimit>(events[0]);
        Assert.Equal(
            "Command Code is rate limited. Wait for the window to reset, or buy on-demand credits, which are never throttled.",
            Assert.IsType<ProviderEvent.TurnCompleted>(events[1]).Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-238")] // DroppyCode/Services/Providers/CommandCodeSession.swift:464-489
    public void DidClose_exit_code_8_notes_the_cap_and_completes()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 8, process);

        Assert.IsType<ProviderEvent.Notice>(events[0]);
        Assert.Equal(TurnStatus.Completed, Assert.IsType<ProviderEvent.TurnCompleted>(events[1]).Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-238")] // DroppyCode/Services/Providers/CommandCodeSession.swift:464-489
    public void DidClose_unknown_exit_code_uses_runError_then_the_generic_message()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 42, process);

        Assert.Equal("Command Code exited with code 42.", Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events)).Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-238")] // DroppyCode/Services/Providers/CommandCodeSession.swift:492-495
    public void NoteTurnCap_is_suppressed_for_a_final_report()
    {
        var session = CreateSession();
        var process = new StdioProcess("x", [], ".", new Dictionary<string, string>());
        SetField(session, "process", process);
        SetField(session, "isFinalReport", true);
        var events = CaptureEvents(session);

        InvokeInstance(session, "DidClose", 8, process);

        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events));
        Assert.Equal(TurnStatus.Completed, completed.Status);
    }

    // ---- PR-PROVCLI-239/240: isRateLimit / friendlyError ----

    [Theory]
    [InlineData("A RATE LIMIT hit", true)]
    [InlineData("Usage Exceeded for today", true)]
    [InlineData("window limit reached", true)]
    [InlineData("something else", false)]
    [Trait("Req", "PR-PROVCLI-239")] // DroppyCode/Services/Providers/CommandCodeSession.swift:497-500
    public void IsRateLimit_matches_three_lowercased_substrings(string text, bool expected)
    {
        Assert.Equal(expected, (bool)InvokeStatic("IsRateLimit", text)!);
    }

    [Theory]
    [InlineData("Not Authenticated", "Command Code is not signed in. Run `cmd login` in Terminal, or add a Studio API key in Settings → Providers → Command Code.")]
    [InlineData("Unauthorized request", "Command Code is not signed in. Run `cmd login` in Terminal, or add a Studio API key in Settings → Providers → Command Code.")]
    [InlineData("Session Expired", "Command Code is not signed in. Run `cmd login` in Terminal, or add a Studio API key in Settings → Providers → Command Code.")]
    [InlineData("Insufficient Credits left", "Command Code is out of credits. Top up at commandcode.ai/billing, or wait for the monthly credits to reset.")]
    [InlineData("Out of Credits", "Command Code is out of credits. Top up at commandcode.ai/billing, or wait for the monthly credits to reset.")]
    [InlineData("Something unrelated", "Something unrelated")]
    [Trait("Req", "PR-PROVCLI-240")] // DroppyCode/Services/Providers/CommandCodeSession.swift:502-512
    public void FriendlyError_rewrites_two_families_of_account_errors(string text, string expected)
    {
        Assert.Equal(expected, (string)InvokeStatic("FriendlyError", text)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-240")] // DroppyCode/Services/Providers/CommandCodeSession.swift:502-512
    public void FriendlyError_prefers_the_sign_in_message_when_both_families_match()
    {
        Assert.Equal(
            "Command Code is not signed in. Run `cmd login` in Terminal, or add a Studio API key in Settings → Providers → Command Code.",
            (string)InvokeStatic("FriendlyError", "not authenticated, out of credits")!);
    }

    // ---- PR-PROVCLI-241: errorText ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-241")] // DroppyCode/Services/Providers/CommandCodeSession.swift:514-520
    public void ErrorText_extracts_a_string_a_message_field_or_the_compact_json()
    {
        Assert.Null(InvokeStaticArgs("ErrorText", (object?)null));
        Assert.Null(InvokeStaticArgs("ErrorText", new JSONValue.Null()));
        Assert.Null(InvokeStaticArgs("ErrorText", new JSONValue.String("")));
        Assert.Equal("boom", InvokeStaticArgs("ErrorText", new JSONValue.String("boom")));
        Assert.Equal("msg", InvokeStaticArgs("ErrorText", JSONValue.FromDictionaryLiteral(("message", "msg"))));
        Assert.Null(InvokeStaticArgs("ErrorText", JSONValue.FromDictionaryLiteral()));
        Assert.Equal("{\"code\":1}", InvokeStaticArgs("ErrorText", JSONValue.FromDictionaryLiteral(("code", 1L))));
    }

    // ---- PR-PROVCLI-242: text(in:) ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-242")] // DroppyCode/Services/Providers/CommandCodeSession.swift:522-532
    public void ContentText_reads_blocks_and_renders_an_image_marker()
    {
        Assert.Equal("", InvokeStaticArgs("ContentText", (object?)null));
        Assert.Equal("plain", InvokeStaticArgs("ContentText", new JSONValue.String("plain")));
        var blocks = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("text", "one")),
            JSONValue.FromDictionaryLiteral(("type", "image")),
            JSONValue.FromDictionaryLiteral(("type", "other")));
        Assert.Equal("one\n[image]", InvokeStaticArgs("ContentText", blocks));
    }

    // ---- PR-PROVCLI-243: todos(from:) ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-243")] // DroppyCode/Services/Providers/CommandCodeSession.swift:534-544
    public void Todos_reads_three_text_keys_and_five_status_strings()
    {
        var input = JSONValue.FromDictionaryLiteral(("todos", JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("content", "c"), ("status", "completed")),
            JSONValue.FromDictionaryLiteral(("text", "t"), ("status", "done")),
            JSONValue.FromDictionaryLiteral(("title", "ti"), ("status", "in_progress")),
            JSONValue.FromDictionaryLiteral(("content", "active-one"), ("status", "active")),
            JSONValue.FromDictionaryLiteral(("content", "pending-one")),
            JSONValue.FromDictionaryLiteral(("status", "done")))));

        var result = (System.Collections.IEnumerable)InvokeStatic("Todos", input)!;
        var steps = result.Cast<TodoStep>().ToList();

        Assert.Equal(5, steps.Count);
        Assert.Equal(("c", TodoStep.Status.Done), (steps[0].Text, steps[0].TodoStepStatus));
        Assert.Equal(("t", TodoStep.Status.Done), (steps[1].Text, steps[1].TodoStepStatus));
        Assert.Equal(("ti", TodoStep.Status.Active), (steps[2].Text, steps[2].TodoStepStatus));
        Assert.Equal(("active-one", TodoStep.Status.Active), (steps[3].Text, steps[3].TodoStepStatus));
        Assert.Equal(("pending-one", TodoStep.Status.Pending), (steps[4].Text, steps[4].TodoStepStatus));
    }

    // ---- PR-PROVCLI-244 (dispatch), PR-PROVCLI-394/395 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-394")] // DroppyCode/Services/Providers/CommandCodeSession.swift:553-557
    public void MakeToolCall_shell_command_marks_a_background_run_with_a_middle_dot()
    {
        var session = CreateSession();
        var input = JSONValue.FromDictionaryLiteral(("command", "/bin/zsh -lc 'ls'"), ("description", "list"), ("run_in_background", true));
        var call = (ToolCall)InvokeInstance(session, "MakeToolCall", "shell_command", input, null)!;
        Assert.Equal(ToolCall.Kind.Command, call.ToolCallKind);
        Assert.Equal("ls", call.Title);
        Assert.Equal("list · in the background", call.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-394")] // DroppyCode/Services/Providers/CommandCodeSession.swift:553-557
    public void MakeToolCall_powershell_falls_back_to_the_literal_title()
    {
        var session = CreateSession();
        var call = (ToolCall)InvokeInstance(session, "MakeToolCall", "powershell", JSONValue.FromDictionaryLiteral(), null)!;
        Assert.Equal("Shell command", call.Title);
        Assert.Null(call.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-395")] // DroppyCode/Services/Providers/CommandCodeSession.swift:558-559
    public void MakeToolCall_monitor_command_has_no_detail_even_with_a_description()
    {
        var session = CreateSession();
        var input = JSONValue.FromDictionaryLiteral(("command", "top"), ("description", "watch it"));
        var call = (ToolCall)InvokeInstance(session, "MakeToolCall", "monitor_command", input, null)!;
        Assert.Equal("top", call.Title);
        Assert.Null(call.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-395")] // DroppyCode/Services/Providers/CommandCodeSession.swift:558-559
    public void MakeToolCall_monitor_command_falls_back_to_its_own_literal()
    {
        var session = CreateSession();
        var call = (ToolCall)InvokeInstance(session, "MakeToolCall", "monitor_command", JSONValue.FromDictionaryLiteral(), null)!;
        Assert.Equal("Monitor command", call.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-244")] // DroppyCode/Services/Providers/CommandCodeSession.swift:548-620
    public void MakeToolCall_read_file_shows_the_line_range_only_with_both_offset_and_limit()
    {
        var session = CreateSession();
        var input = JSONValue.FromDictionaryLiteral(("file_path", "a.txt"), ("offset", 10L), ("limit", 20L));
        var call = (ToolCall)InvokeInstance(session, "MakeToolCall", "read_file", input, null)!;
        Assert.Equal(ToolCall.Kind.Read, call.ToolCallKind);
        Assert.Equal("lines 10–30", call.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-244")] // DroppyCode/Services/Providers/CommandCodeSession.swift:548-620
    public void MakeToolCall_glob_and_grep_build_search_rows()
    {
        var session = CreateSession();
        var glob = (ToolCall)InvokeInstance(session, "MakeToolCall", "glob", JSONValue.FromDictionaryLiteral(("pattern", "*.cs")), null)!;
        Assert.Equal(ToolCall.Kind.Search, glob.ToolCallKind);
        Assert.Equal("*.cs", glob.Title);

        var grep = (ToolCall)InvokeInstance(session, "MakeToolCall", "grep",
            JSONValue.FromDictionaryLiteral(("pattern", "foo"), ("glob", "*.cs")), null)!;
        Assert.Equal("foo", grep.Title);
        Assert.Equal("*.cs", grep.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-244")] // DroppyCode/Services/Providers/CommandCodeSession.swift:548-620
    public void MakeToolCall_web_tools_use_the_url_or_query()
    {
        var session = CreateSession();
        var fetch = (ToolCall)InvokeInstance(session, "MakeToolCall", "web_fetch", JSONValue.FromDictionaryLiteral(("url", "https://x")), null)!;
        Assert.Equal(ToolCall.Kind.Web, fetch.ToolCallKind);
        Assert.Equal("https://x", fetch.Title);
        var search = (ToolCall)InvokeInstance(session, "MakeToolCall", "web_search", JSONValue.FromDictionaryLiteral(), null)!;
        Assert.Equal("Search the web", search.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-244")] // DroppyCode/Services/Providers/CommandCodeSession.swift:548-620
    public void MakeToolCall_agent_falls_back_from_description_to_a_truncated_prompt_to_the_default()
    {
        var session = CreateSession();
        var named = (ToolCall)InvokeInstance(session, "MakeToolCall", "agent",
            JSONValue.FromDictionaryLiteral(("description", "d"), ("subagent_type", "researcher")), null)!;
        Assert.Equal("d", named.Title);
        Assert.Equal("researcher", named.Detail);
        var fromPrompt = (ToolCall)InvokeInstance(session, "MakeToolCall", "agent",
            JSONValue.FromDictionaryLiteral(("prompt", new string('p', 200))), null)!;
        Assert.Equal(96, fromPrompt.Title.Length);
        var fallback = (ToolCall)InvokeInstance(session, "MakeToolCall", "agent", JSONValue.FromDictionaryLiteral(), null)!;
        Assert.Equal("Sub-agent", fallback.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-244")] // DroppyCode/Services/Providers/CommandCodeSession.swift:602-607
    public void MakeToolCall_skill_worktree_and_run_command_rows()
    {
        var session = CreateSession();
        var skill = (ToolCall)InvokeInstance(session, "MakeToolCall", "activate_skill", JSONValue.FromDictionaryLiteral(("name", "s")), null)!;
        Assert.Equal("Skill · s", skill.Title);
        var enter = (ToolCall)InvokeInstance(session, "MakeToolCall", "enter_worktree", JSONValue.FromDictionaryLiteral(("name", "wt")), null)!;
        Assert.Equal("Enter worktree", enter.Title);
        Assert.Equal("wt", enter.Detail);
        var run = (ToolCall)InvokeInstance(session, "MakeToolCall", "run_command", JSONValue.FromDictionaryLiteral(("command", "build")), null)!;
        Assert.Equal("Command · build", run.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-244")] // DroppyCode/Services/Providers/CommandCodeSession.swift:609-618
    public void MakeToolCall_mcp_prefix_names_the_server_and_the_remaining_tool_name()
    {
        var session = CreateSession();
        var call = (ToolCall)InvokeInstance(session, "MakeToolCall", "mcp__github__list__issues", JSONValue.FromDictionaryLiteral(("a", 1L)), null)!;
        Assert.Equal(ToolCall.Kind.Mcp, call.ToolCallKind);
        Assert.Equal("github · list__issues", call.Title);
        Assert.Equal("{\"a\":1}", call.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-244")] // DroppyCode/Services/Providers/CommandCodeSession.swift:614-618, :622-627
    public void MakeToolCall_default_humanizes_the_name_and_caps_detail_at_300()
    {
        var session = CreateSession();
        var empty = (ToolCall)InvokeInstance(session, "MakeToolCall", "task_create", JSONValue.FromDictionaryLiteral(), null)!;
        Assert.Equal("Task create", empty.Title);
        Assert.Null(empty.Detail);

        var withDetail = (ToolCall)InvokeInstance(session, "MakeToolCall", "task_create", JSONValue.FromDictionaryLiteral(("x", "y")), null)!;
        Assert.Equal("{\"x\":\"y\"}", withDetail.Detail);

        var longInput = JSONValue.FromDictionaryLiteral(("x", new string('z', 400)));
        var capped = (ToolCall)InvokeInstance(session, "MakeToolCall", "task_create", longInput, null)!;
        Assert.Null(capped.Detail);
    }

    // ---- PR-PROVCLI-245/246: the watcher and scanApprovals ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-245")] // DroppyCode/Services/Providers/CommandCodeSession.swift:650-666
    public void StartWatcher_installs_a_watch_and_StopWatcher_clears_it()
    {
        using var runtime = new TestRuntime();
        var session = CreateSession();
        var directory = ScratchDirectory();
        try
        {
            var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
            InvokeInstance(session, "StartWatcher", url);
            Assert.NotNull(GetField(session, "approvalWatcher"));

            InvokeInstance(session, "StopWatcher");
            Assert.Null(GetField(session, "approvalWatcher"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-245")] // DroppyCode/Services/Providers/CommandCodeSession.swift:650-661
    public void A_write_into_the_watched_directory_triggers_a_rescan()
    {
        using var runtime = new TestRuntime();
        var session = CreateSession();
        var directory = ScratchDirectory();
        try
        {
            var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
            SetField(session, "approvalDirectory", url);
            SetField(session, "turnActive", true);
            var events = CaptureEvents(session);
            InvokeInstance(session, "StartWatcher", url);

            File.WriteAllText(Path.Combine(directory, "c9.request.json"), """{"toolCallId":"c9","toolName":"read_file","input":{}}""");

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !events.Any(e => e is ProviderEvent.Approval))
            {
                runtime.Loop.RunUntilIdle();
                System.Threading.Thread.Sleep(50);
            }
            Assert.True(events.Any(e => e is ProviderEvent.Approval),
                "The watcher never rescanned the approvals directory.");
        }
        finally
        {
            InvokeInstance(session, "StopWatcher");
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-246")] // DroppyCode/Services/Providers/CommandCodeSession.swift:668-681
    public void ScanApprovals_orders_by_last_path_component_deletes_each_file_and_skips_a_pending_id()
    {
        var session = CreateSession();
        var directory = ScratchDirectory();
        try
        {
            var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
            SetField(session, "approvalDirectory", url);
            SetField(session, "turnActive", true);
            File.WriteAllText(Path.Combine(directory, "b.request.json"), """{"toolCallId":"b","toolName":"read_file","input":{}}""");
            File.WriteAllText(Path.Combine(directory, "a.request.json"), """{"toolCallId":"a","toolName":"read_file","input":{}}""");
            File.WriteAllText(Path.Combine(directory, "not-a-request.json"), "{}");
            SetPendingApproval(session, "a", MakeSampleApproval("a"));
            var events = CaptureEvents(session);

            InvokeInstance(session, "ScanApprovals");

            var approvals = events.OfType<ProviderEvent.Approval>().ToList();
            var approval = Assert.Single(approvals);
            Assert.Equal("b", approval.Value.Id);
            Assert.False(File.Exists(Path.Combine(directory, "a.request.json")));
            Assert.False(File.Exists(Path.Combine(directory, "b.request.json")));
            Assert.True(File.Exists(Path.Combine(directory, "not-a-request.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-246")] // DroppyCode/Services/Providers/CommandCodeSession.swift:668-681
    public void ScanApprovals_returns_immediately_without_a_directory_or_an_active_turn()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);
        InvokeInstance(session, "ScanApprovals");
        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-247: autoApproves ----

    [Theory]
    [InlineData("shell_command", "git status", true)]   // reader
    [InlineData("shell_command", "git tag v1", false)]   // writer per ED-060
    [InlineData("write_file", null, true)]
    [Trait("Req", "PR-PROVCLI-247")] // DroppyCode/Services/Providers/CommandCodeSession.swift:683-717
    public void AutoApproves_in_Auto_mode_classifies_by_name_and_write_detection(string name, string? command, bool expected)
    {
        var session = CreateSession();
        SetField(session, "runtimeMode", RuntimeMode.Auto);
        var input = command is null ? JSONValue.FromDictionaryLiteral() : JSONValue.FromDictionaryLiteral(("command", command));
        Assert.Equal(expected, (bool)InvokeInstance(session, "AutoApproves", name, input)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-247")] // DroppyCode/Services/Providers/CommandCodeSession.swift:695-717
    public void AutoApproves_Supervised_always_asks_and_AutoAcceptEdits_only_waves_edits()
    {
        var session = CreateSession();
        var input = JSONValue.FromDictionaryLiteral();
        SetField(session, "runtimeMode", RuntimeMode.Supervised);
        Assert.False((bool)InvokeInstance(session, "AutoApproves", "write_file", input)!);

        SetField(session, "runtimeMode", RuntimeMode.AutoAcceptEdits);
        Assert.True((bool)InvokeInstance(session, "AutoApproves", "write_file", input)!);
        Assert.False((bool)InvokeInstance(session, "AutoApproves", "read_file", input)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-247")] // DroppyCode/Services/Providers/CommandCodeSession.swift:695-717
    public void AutoApproves_Auto_waves_kill_shell_and_the_cron_family_but_not_other_tools()
    {
        var session = CreateSession();
        SetField(session, "runtimeMode", RuntimeMode.Auto);
        var input = JSONValue.FromDictionaryLiteral();
        foreach (var name in new[] { "kill_shell", "cron_create", "cron_delete", "task_stop" })
        {
            Assert.True((bool)InvokeInstance(session, "AutoApproves", name, input)!);
        }
        Assert.False((bool)InvokeInstance(session, "AutoApproves", "read_file", input)!);
    }

    // ---- PR-PROVCLI-248: makeApproval ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-248")] // DroppyCode/Services/Providers/CommandCodeSession.swift:719-739
    public void MakeApproval_offers_the_same_three_options_in_order()
    {
        var session = CreateSession();
        var approval = (ApprovalRequest)InvokeInstance(session, "MakeApproval", "c1", "shell_command", JSONValue.FromDictionaryLiteral(("command", "ls")))!;

        Assert.Equal(ApprovalRequest.Kind.Command, approval.RequestKind);
        Assert.Equal(3, approval.Options.Count);
        Assert.Equal(("allow", "Approve", ApprovalRequest.Option.Role.Approve), (approval.Options[0].Id, approval.Options[0].Title, approval.Options[0].OptionRole));
        Assert.Equal(("always", "Approve for session", ApprovalRequest.Option.Role.ApproveAlways), (approval.Options[1].Id, approval.Options[1].Title, approval.Options[1].OptionRole));
        Assert.Equal(("deny", "Decline", ApprovalRequest.Option.Role.Decline), (approval.Options[2].Id, approval.Options[2].Title, approval.Options[2].OptionRole));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-248")] // DroppyCode/Services/Providers/CommandCodeSession.swift:719-739
    public void MakeApproval_toolItemID_is_set_only_for_a_known_call()
    {
        var session = CreateSession();
        SetToolName(session, "known", "read_file");

        var known = (ApprovalRequest)InvokeInstance(session, "MakeApproval", "known", "read_file", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("known", known.ToolItemID);

        var unknown = (ApprovalRequest)InvokeInstance(session, "MakeApproval", "unknown", "read_file", JSONValue.FromDictionaryLiteral())!;
        Assert.Null(unknown.ToolItemID);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-248")] // DroppyCode/Services/Providers/CommandCodeSession.swift:719-739, :741-747
    public void MakeApproval_kind_is_fileChange_for_an_edit_and_tool_for_everything_else()
    {
        var session = CreateSession();
        var fileChange = (ApprovalRequest)InvokeInstance(session, "MakeApproval", "c1", "write_file", JSONValue.FromDictionaryLiteral(("file_path", "a")))!;
        Assert.Equal(ApprovalRequest.Kind.FileChange, fileChange.RequestKind);
        var tool = (ApprovalRequest)InvokeInstance(session, "MakeApproval", "c1", "read_file", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal(ApprovalRequest.Kind.Tool, tool.RequestKind);
    }

    // ---- PR-PROVCLI-249: resolveApproval ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-249")] // DroppyCode/Services/Providers/CommandCodeSession.swift:241-251, :749-762
    public void ResolveApproval_allow_writes_the_allow_reply()
    {
        var session = CreateSession();
        var directory = ScratchDirectory();
        try
        {
            var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
            SetField(session, "approvalDirectory", url);
            SetPendingApproval(session, "c1", MakeSampleApproval("c1"));
            var events = CaptureEvents(session);

            ((CommandCodeSession)session).ResolveApproval("c1", "allow");

            Assert.IsType<ProviderEvent.RequestResolved>(Assert.Single(events));
            var reply = File.ReadAllText(Path.Combine(directory, "c1.reply.json"));
            Assert.Equal("""{"decision":"allow"}""", reply);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-249")] // DroppyCode/Services/Providers/CommandCodeSession.swift:241-251, :749-762
    public void ResolveApproval_always_also_grants_the_session_and_writes_the_allow_reply()
    {
        var session = CreateSession();
        var directory = ScratchDirectory();
        try
        {
            var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
            SetField(session, "approvalDirectory", url);
            var approval = MakeSampleApproval("c1"); // .command kind
            SetPendingApproval(session, "c1", approval);

            ((CommandCodeSession)session).ResolveApproval("c1", "always");

            var grants = (System.Collections.Generic.HashSet<string>)GetField(session, "sessionGrants")!;
            Assert.Contains("command", grants);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-249")] // DroppyCode/Services/Providers/CommandCodeSession.swift:241-251, :749-762
    public void ResolveApproval_deny_writes_the_marrow_wording_deny_reply()
    {
        var session = CreateSession();
        var directory = ScratchDirectory();
        try
        {
            var url = (SwiftURL)InvokeStaticArgs("NativePathUrl", directory)!;
            SetField(session, "approvalDirectory", url);
            SetPendingApproval(session, "c1", MakeSampleApproval("c1"));

            ((CommandCodeSession)session).ResolveApproval("c1", "deny");

            var reply = File.ReadAllText(Path.Combine(directory, "c1.reply.json"));
            Assert.Equal(
                """{"decision":"deny","reason":"The user declined this in Marrow. Do not retry it; take another approach or explain what you would need."}""",
                reply);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-249")] // DroppyCode/Services/Providers/CommandCodeSession.swift:241-251
    public void ResolveApproval_for_an_unknown_id_does_nothing()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        ((CommandCodeSession)session).ResolveApproval("does-not-exist", "allow");

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-249")] // DroppyCode/Services/Providers/CommandCodeSession.swift:749-762, :741-747
    public void GrantKey_maps_tool_permissions_and_plan_to_the_same_key()
    {
        Assert.Equal("command", InvokeStatic("GrantKey", ApprovalRequest.Kind.Command));
        Assert.Equal("fileChange", InvokeStatic("GrantKey", ApprovalRequest.Kind.FileChange));
        Assert.Equal("tool", InvokeStatic("GrantKey", ApprovalRequest.Kind.Tool));
        Assert.Equal("tool", InvokeStatic("GrantKey", ApprovalRequest.Kind.Permissions));
        Assert.Equal("tool", InvokeStatic("GrantKey", ApprovalRequest.Kind.Plan));
    }

    // ---- Test helpers for part 2 ----

    private static object MakeRunResult(string subtype, string? stopReason, string? error)
    {
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        var runResultType = type.GetNestedType("RunResult", BindingFlags.NonPublic)!;
        var instance = Activator.CreateInstance(runResultType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: null, culture: null)!;
        runResultType.GetProperty("Subtype")!.SetValue(instance, subtype);
        runResultType.GetProperty("StopReason")!.SetValue(instance, stopReason);
        runResultType.GetProperty("Error")!.SetValue(instance, error);
        return instance;
    }

    private static ApprovalRequest MakeSampleApproval(string id) => new(
        id, ApprovalRequest.Kind.Command, "Sample", new ValueList<ApprovalRequest.Option>(
        [
            new("allow", "Approve", ApprovalRequest.Option.Role.Approve),
            new("always", "Approve for session", ApprovalRequest.Option.Role.ApproveAlways),
            new("deny", "Decline", ApprovalRequest.Option.Role.Decline),
        ]));

    private static void SetPendingApproval(object session, string id, ApprovalRequest approval)
    {
        var dictionary = (System.Collections.IDictionary)GetField(session, "pendingApprovals")!;
        dictionary[id] = approval;
    }

    private static void SetToolName(object session, string id, string name)
    {
        var dictionary = (System.Collections.IDictionary)GetField(session, "toolNames")!;
        dictionary[id] = name;
    }

    private static object? InvokeInstance(object session, string name, params object?[] arguments)
    {
        var method = session.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return method.Invoke(session, arguments);
    }

    private static string ScratchDirectory()
    {
        var root = TestDataDirectory.Read()
            ?? throw new InvalidOperationException("MARROW_PORT_DATA_DIR must be set for file-system tests.");
        var directory = Path.Combine(root, "CommandCodeSessionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static object CreateSession()
    {
        var root = ScratchDirectory();
        var workingDirectory = SwiftTraps.Unwrap(SwiftURL.Create(new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri));
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Commandcode,
            Executable = null,
            WorkingDirectory = workingDirectory,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [configuration], culture: null)!;
    }

    private static object CreateSessionWithExecutable()
    {
        var root = ScratchDirectory();
        var workingDirectory = SwiftTraps.Unwrap(SwiftURL.Create(new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri));
        var executable = SwiftTraps.Unwrap(SwiftURL.Create(new Uri(Path.Combine(root, "does-not-exist.exe")).AbsoluteUri));
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Commandcode,
            Executable = executable,
            WorkingDirectory = workingDirectory,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [configuration], culture: null)!;
    }

    private static List<ProviderEvent> CaptureEvents(object session)
    {
        var events = new List<ProviderEvent>();
        var property = session.GetType().GetProperty("OnEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        property.SetValue(session, (Action<ProviderEvent>)(events.Add));
        return events;
    }

    private static void InvokeHandle(object session, string json)
    {
        var message = JSONValue.Parse(json)!;
        session.GetType().GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, [message]);
    }

    private static object? InvokeStatic(string name, params object[] arguments)
    {
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);
    }

    private static object? InvokeStaticArgs(string name, params object?[] arguments)
    {
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);
    }

    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static object? GetField(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
}
