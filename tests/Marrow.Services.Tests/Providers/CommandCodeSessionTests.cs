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
