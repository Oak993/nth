using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/OpenAICompatibleSession.swift (B2 1fbf81e), :894-1035,
// :1118-1165.
//
// Part 4 of 5 (PR-PROVAPI-162 to 175, UI-PROVAPI-011 to 020): the six built-in tool bodies and MCP
// dispatch inside ExecuteTool, and the native replacements for grep and the login shell. This part's
// tests drive the real ExecuteTool body directly (ExecuteToolForTests), not the ToolHandler seam
// OpenAICompatibleSessionTestSeam supplies for the earlier parts' turn-loop tests.
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenAICompatibleSessionFourthTests
{
    private static OpenAICompatibleSession.Config MakeConfig() => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = new Uri("https://api.deepseek.com/chat/completions"),
        ContextWindow = 64_000,
        IsVisionModel = _ => false,
    };

    // The scheme:path-rootless form (no "//" authority) is used rather than "file:///C:/..." because
    // this port's SwiftURL faithfully reproduces Foundation's POSIX-only file path decoding (SwiftURL.cs
    // :23: "The Windows shape of a file:///C:/... path has no ruling and is out of scope"), so the
    // three-slash form's .Path getter would return "/C:/..." with a spurious leading slash that
    // SwiftPath.IsAbsolute (ED-036/ED-045/ED-138) does not recognize as drive-rooted.
    private static SwiftURL WorkingDirectoryURL(string root) => SwiftURL.Create("file:" + root.Replace('\\', '/'))!;

    private static SessionConfiguration MakeSessionConfiguration(string root,
        RuntimeMode runtimeMode = RuntimeMode.FullAccess, InteractionMode interactionMode = InteractionMode.Build) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = WorkingDirectoryURL(root),
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
        ApiKey = "sk-test",
    };

    private static OpenAICompatibleSession Session(string root,
        RuntimeMode runtimeMode = RuntimeMode.FullAccess, InteractionMode interactionMode = InteractionMode.Build)
    {
        var session = new OpenAICompatibleSession(MakeConfig(), MakeSessionConfiguration(root, runtimeMode, interactionMode));
        session.OnEvent = _ => { };
        return session;
    }

    private static (OpenAICompatibleSession Session, List<ProviderEvent> Events) WiredSession(string root,
        RuntimeMode runtimeMode = RuntimeMode.FullAccess, InteractionMode interactionMode = InteractionMode.Build)
    {
        var session = new OpenAICompatibleSession(MakeConfig(), MakeSessionConfiguration(root, runtimeMode, interactionMode));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, events);
    }

    private static OpenAICompatibleSession.PendingToolCall Call(string name, string argumentsJson) =>
        new("call-1", name, argumentsJson);

    // ApproveIfNeeded raises its Approval event synchronously from the main-loop work item TestRuntime
    // drains here, but the checked continuation that actually registers the pending approval resumes on
    // a separate, asynchronously scheduled continuation (CheckedContinuation<T> is built on a
    // RunContinuationsAsynchronously TaskCompletionSource, matching Swift's own continuation semantics),
    // so it is not guaranteed to have run by the time RunUntilIdle returns. Give it a moment before
    // resolving; this is scheduling slack for the test harness's own thread hop, not the port's timing.
    private static async Task SettleApproval() => await Task.Delay(300);

    // ---- PR-PROVAPI-172, PR-PROVAPI-163: default cases that never reach a tool body ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-163")] // OpenAICompatibleSession.swift:895
    public async Task ExecuteTool_returns_Stopped_verbatim_once_the_turn_is_interrupted()
    {
        using var folder = new ScratchFolder();
        var session = Session(folder.Root);
        session.SetInterruptedForTests(true);
        var result = await session.ExecuteToolForTests(Call("read_file", "{}"));
        Assert.Equal("Stopped.", result);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-172")] // OpenAICompatibleSession.swift:1027-1032
    public async Task ExecuteTool_reports_an_unknown_tool_without_asking_for_approval()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root, RuntimeMode.Supervised);
        var result = await session.ExecuteToolForTests(Call("frobnicate", "{}"));

        Assert.Equal("Error: unknown tool frobnicate.", result);
        Assert.DoesNotContain(events, e => e is ProviderEvent.Approval);
        var started = Assert.Single(events.OfType<ProviderEvent.ToolStarted>()).Call;
        Assert.Equal(ToolCall.Kind.Other, started.ToolCallKind);
        Assert.Equal("frobnicate", started.Title);
        var updated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update;
        Assert.Equal("Unknown tool.", updated.Output);
        Assert.Equal(ToolCall.Status.Failed, updated.Status);
    }

    // ---- PR-PROVAPI-162, UI-PROVAPI-011/012/013/014: declined tool ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-162")] // OpenAICompatibleSession.swift:1035-1038
    [Trait("Req", "UI-PROVAPI-013")] // OpenAICompatibleSession.swift:1071, :1035-1038
    public async Task Declining_read_file_reports_two_different_verbatim_strings()
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("a.txt"), "hello");
        var (session, events) = WiredSession(folder.Root, RuntimeMode.Supervised);
        var task = session.ExecuteToolForTests(Call("read_file", "{\"path\":\"a.txt\"}"));
        runtime.Loop.RunUntilIdle();
        var approval = events.OfType<ProviderEvent.Approval>().Single().Value;
        await SettleApproval();
        session.ResolveApproval(approval.Id, "decline");
        runtime.Loop.RunUntilIdle();
        var result = await task;

        Assert.Equal("The user declined this action. Ask how to proceed instead of retrying it.", result);
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal("The user declined this action.", updated.Output);
        Assert.Equal(ToolCall.Status.Declined, updated.Status);
        Assert.NotEqual(result, updated.Output);
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-011")] // OpenAICompatibleSession.swift:1069
    [Trait("Req", "UI-PROVAPI-012")] // OpenAICompatibleSession.swift:1070
    public async Task Approving_read_file_for_the_whole_turn_skips_every_later_approval()
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("a.txt"), "hello");
        File.WriteAllText(folder.Child("b.txt"), "world");
        var (session, events) = WiredSession(folder.Root, RuntimeMode.Supervised);
        var first = session.ExecuteToolForTests(Call("read_file", "{\"path\":\"a.txt\"}"));
        runtime.Loop.RunUntilIdle();
        var firstApproval = events.OfType<ProviderEvent.Approval>().Single().Value;
        await SettleApproval();
        session.ResolveApproval(firstApproval.Id, "always");
        runtime.Loop.RunUntilIdle();
        Assert.Equal("hello", await first);

        var second = await session.ExecuteToolForTests(Call("read_file", "{\"path\":\"b.txt\"}"));
        Assert.Equal("world", second);
        Assert.Single(events.OfType<ProviderEvent.Approval>());
    }

    // ---- PR-PROVAPI-165, UI-PROVAPI-014/015: read_file ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-165")] // OpenAICompatibleSession.swift:919-930
    [Trait("Req", "UI-PROVAPI-014")] // OpenAICompatibleSession.swift:923
    [Trait("Req", "UI-PROVAPI-015")] // OpenAICompatibleSession.swift:919-930
    public async Task Read_file_titles_the_row_with_the_display_path_and_returns_the_text()
    {
        using var folder = new ScratchFolder();
        Directory.CreateDirectory(folder.Child("sub"));
        File.WriteAllText(folder.Child("sub", "note.txt"), "hello there");
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("read_file", "{\"path\":\"sub/note.txt\"}"));

        Assert.Equal("hello there", result);
        var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
        Assert.Equal(ToolCall.Kind.Read, started.ToolCallKind);
        // The working directory in this test is built as a forward-slash file: URL (see
        // WorkingDirectoryURL's comment), so the relative path this port shows the model keeps forward
        // slashes too; that separator choice is this test's own fixture, not a port behaviour.
        Assert.Equal("sub/note.txt", started.Title);
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal("hello there", updated.Output);
        Assert.Equal(ToolCall.Status.Completed, updated.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-165")] // OpenAICompatibleSession.swift:931-934
    public async Task Read_file_of_a_missing_file_returns_the_Error_prefixed_message()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("read_file", "{\"path\":\"missing.txt\"}"));

        Assert.Equal($"Error: No such file: missing.txt.", result);
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal(ToolCall.Status.Failed, updated.Status);
    }

    // ---- PR-PROVAPI-166, UI-PROVAPI-016: list_files ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-166")] // OpenAICompatibleSession.swift:936-948
    [Trait("Req", "UI-PROVAPI-016")] // OpenAICompatibleSession.swift:938-939
    public async Task List_files_with_an_empty_path_titles_the_row_with_the_working_directorys_own_name()
    {
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("one.txt"), "1");
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("list_files", "{}"));

        Assert.Contains("one.txt", result, StringComparison.Ordinal);
        var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
        Assert.Equal(ToolCall.Kind.Search, started.ToolCallKind);
        Assert.Equal(SwiftPath.LastPathComponent(folder.Root), started.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-166")] // OpenAICompatibleSession.swift:949-952
    public async Task List_files_of_a_non_directory_returns_the_No_such_directory_message()
    {
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("file.txt"), "1");
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("list_files", "{\"path\":\"file.txt\"}"));

        Assert.Equal("Error: No such directory: file.txt.", result);
        Assert.Equal(ToolCall.Status.Failed, events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Status);
    }

    // ---- PR-PROVAPI-167, PR-PROVAPI-173, PR-PROVAPI-174, UI-PROVAPI-017: search_text ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-167")] // OpenAICompatibleSession.swift:957, :959
    [Trait("Req", "UI-PROVAPI-017")] // OpenAICompatibleSession.swift:954-965
    public async Task Search_text_titles_the_row_with_the_pattern_and_approves_with_curly_quotes()
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("a.txt"), "needle here");
        var (session, events) = WiredSession(folder.Root, RuntimeMode.Supervised);
        var task = session.ExecuteToolForTests(Call("search_text", "{\"pattern\":\"needle\"}"));
        runtime.Loop.RunUntilIdle();
        var approval = events.OfType<ProviderEvent.Approval>().Single().Value;
        // U+201C LEFT DOUBLE QUOTATION MARK, U+201D RIGHT DOUBLE QUOTATION MARK.
        Assert.Equal("Search for \U0000201Cneedle\U0000201D", approval.Title);
        await SettleApproval();
        session.ResolveApproval(approval.Id, "allow");
        runtime.Loop.RunUntilIdle();
        var result = await task;

        Assert.Equal("a.txt:1:needle here", result);
        var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
        Assert.Equal("needle", started.Title);
        Assert.Equal(ToolCall.Status.Completed, events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-167")] // OpenAICompatibleSession.swift:957
    public async Task Search_text_with_an_empty_pattern_titles_the_row_Search()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("search_text", "{}"));

        Assert.Equal("Error: a search pattern is required.", result);
        var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
        Assert.Equal("Search", started.Title);
        // This row is always completed, even though the search itself failed: the failure text is
        // carried in the output, not the status (PR-PROVAPI-167's source_evidence and UI-PROVAPI-017).
        Assert.Equal(ToolCall.Status.Completed, events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-173")] // OpenAICompatibleSession.swift:1136-1146
    public async Task Search_text_excludes_the_four_named_directories_and_honours_the_regex_switch()
    {
        using var folder = new ScratchFolder();
        Directory.CreateDirectory(folder.Child("node_modules"));
        File.WriteAllText(folder.Child("node_modules", "skip.txt"), "needle");
        File.WriteAllText(folder.Child("keep.txt"), "abc123");
        var (session, _) = WiredSession(folder.Root);

        var fixedResult = await session.ExecuteToolForTests(Call("search_text", "{\"pattern\":\"needle\"}"));
        Assert.Equal("(no matches)", fixedResult);

        var regexResult = await session.ExecuteToolForTests(Call("search_text", "{\"pattern\":\"[0-9]+\",\"regex\":true}"));
        Assert.Equal("keep.txt:1:abc123", regexResult);

        var literalResult = await session.ExecuteToolForTests(Call("search_text", "{\"pattern\":\"[0-9]+\"}"));
        Assert.Equal("(no matches)", literalResult);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-173")] // OpenAICompatibleSession.swift:1122-1133
    public async Task Search_text_refuses_a_path_that_climbs_out_of_the_project()
    {
        using var outsideRoot = new ScratchFolder();
        using var folder = new ScratchFolder();
        var (session, _) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("search_text", $"{{\"pattern\":\"x\",\"path\":\"..\\\\{Path.GetFileName(outsideRoot.Root)}\"}}"));
        Assert.StartsWith("Error: That path is outside the project.", result, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-174")] // OpenAICompatibleSession.swift:1149-1151
    public async Task Search_text_caps_results_at_120_lines_with_a_truncation_count()
    {
        using var folder = new ScratchFolder();
        // LF only: SwiftString.Split treats "\r\n" as one grapheme cluster that never equals the "\n"
        // separator (SwiftString.cs:137-138), so a CRLF-terminated file would never split into lines.
        File.WriteAllText(folder.Child("many.txt"), string.Join('\n', Enumerable.Range(1, 130).Select(_ => "needle")));
        var (session, _) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("search_text", "{\"pattern\":\"needle\"}"));

        var lines = result.Split('\n');
        Assert.Equal(121, lines.Length);
        // U+2026 HORIZONTAL ELLIPSIS.
        Assert.Equal("\U00002026(130 matches, truncated)", lines[120]);
    }

    // ---- PR-PROVAPI-168, PR-PROVAPI-169, UI-PROVAPI-018: write_file ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-168")] // OpenAICompatibleSession.swift:966-982
    [Trait("Req", "UI-PROVAPI-018")] // OpenAICompatibleSession.swift:969-981
    public async Task Write_file_reports_the_utf8_byte_count_and_shows_a_whole_file_diff()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("write_file", "{\"path\":\"out.txt\",\"content\":\"caf\\u00e9\"}"));

        // U+00E9 LATIN SMALL LETTER E WITH ACUTE: "caf\U000000E9" is 5 UTF-8 bytes.
        Assert.Equal("Wrote out.txt (5 bytes).", result);
        Assert.Equal("caf\U000000E9", File.ReadAllText(folder.Child("out.txt")));
        var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
        Assert.Equal(ToolCall.Kind.Edit, started.ToolCallKind);
        Assert.Single(started.Edits);
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal("Wrote out.txt (5 bytes).", updated.Output);
        Assert.Single(updated.Edits!);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-169")]
    // OpenAICompatibleSession.swift:975-982: write_file's success path has no cancellation guard
    // after the write, unlike every other tool (:905, :928, :946, :963, :1020 all check first).
    // UNTESTABLE dynamically: reproducing the race needs interrupted to flip to true in the window
    // between the write completing and ExecuteTool returning, which real disk I/O completes too fast
    // to land in deterministically, and R-TEST-5 forbids a real timer to fake that window. Verified
    // instead by this static check that the write_file branch (unlike read_file's) contains no
    // `SwiftTask.IsCancelled || interrupted` guard between the write call and its success return.
    public void Write_file_success_path_has_no_post_write_cancellation_guard_unlike_read_file()
    {
        var source = File.ReadAllText(FindSourceFile());
        var writeBranch = SliceBetween(source, "case \"write_file\": // :966", "case \"edit_file\": // :988");
        var readBranch = SliceBetween(source, "case \"read_file\": // :919", "case \"list_files\": // :936");
        Assert.Contains("await fileTools.WriteFile(path, content);", writeBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("if (SwiftTask.IsCancelled || interrupted) return \"Stopped.\";\n                    OnEvent?.Invoke(new ProviderEvent.ToolUpdated(callID, new ToolUpdate // :977-981",
            writeBranch, StringComparison.Ordinal);
        Assert.Contains("if (SwiftTask.IsCancelled || interrupted) return \"Stopped.\"; // :928", readBranch, StringComparison.Ordinal);
    }

    private static string FindSourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "src", "Marrow.Services", "Providers", "OpenAICompatibleSession.cs");

    private static string SliceBetween(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
        return text[startIndex..endIndex];
    }

    // ---- PR-PROVAPI-170, UI-PROVAPI-019: edit_file ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-170")] // OpenAICompatibleSession.swift:989-1005
    [Trait("Req", "UI-PROVAPI-019")] // OpenAICompatibleSession.swift:992-1004
    public async Task Edit_file_accepts_old_string_new_string_and_reports_the_edited_path()
    {
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("f.txt"), "hello world");
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("edit_file",
            "{\"path\":\"f.txt\",\"old_string\":\"world\",\"new_string\":\"there\"}"));

        Assert.Equal("Edited f.txt.", result);
        Assert.Equal("hello there", File.ReadAllText(folder.Child("f.txt")));
        Assert.Single(events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Edits!);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-170")] // OpenAICompatibleSession.swift:990-991
    public async Task Edit_file_also_accepts_the_old_text_new_text_spelling()
    {
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("f.txt"), "hello world");
        var (session, _) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("edit_file",
            "{\"path\":\"f.txt\",\"old_text\":\"world\",\"new_text\":\"there\"}"));

        Assert.Equal("Edited f.txt.", result);
        Assert.Equal("hello there", File.ReadAllText(folder.Child("f.txt")));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-170")] // OpenAICompatibleSession.swift:988-1010 (edit error messages)
    public async Task Edit_file_error_messages_match_the_three_failure_cases()
    {
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("f.txt"), "aa");
        var (session, _) = WiredSession(folder.Root);

        var empty = await session.ExecuteToolForTests(Call("edit_file", "{\"path\":\"f.txt\",\"old_string\":\"\",\"new_string\":\"x\"}"));
        Assert.Equal("Error: old_string must not be empty. Read the file first, then edit an exact block.", empty);

        var noMatch = await session.ExecuteToolForTests(Call("edit_file", "{\"path\":\"f.txt\",\"old_string\":\"zzz\",\"new_string\":\"x\"}"));
        Assert.Equal("Error: That exact text was not found in f.txt. Read the file and copy it exactly.", noMatch);

        var several = await session.ExecuteToolForTests(Call("edit_file", "{\"path\":\"f.txt\",\"old_string\":\"a\",\"new_string\":\"x\"}"));
        Assert.Equal("Error: That text appears 2 times in f.txt. Include more context so it matches once.", several);
    }

    // ---- PR-PROVAPI-171, PR-PROVAPI-175, UI-PROVAPI-020: run_command ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-171")] // ProviderSession.swift:180-192
    [Trait("Req", "UI-PROVAPI-020")] // OpenAICompatibleSession.swift:1013
    public async Task Run_command_unwraps_a_shell_wrapped_command_for_its_title()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("run_command", "{\"command\":\"/bin/zsh -lc 'echo hi'\"}"));

        var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
        Assert.Equal(ToolCall.Kind.Command, started.ToolCallKind);
        Assert.Equal("echo hi", started.Title);
        Assert.Contains("hi", result, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-171")] // OpenAICompatibleSession.swift:1013
    public async Task Run_command_with_an_empty_command_titles_the_row_Run_command()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("run_command", "{}"));

        // OpenAICompatibleSession.swift:1026: a non-empty output (the required-command message counts
        // as one) is always wrapped "exit <code>\n<output>", even in this failure case.
        Assert.Equal("exit 1\nError: a command is required.", result);
        var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
        Assert.Equal("Run command", started.Title);
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal(1, updated.ExitCode);
        Assert.Equal(ToolCall.Status.Failed, updated.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-175")] // OpenAICompatibleSession.swift:1021-1026
    public async Task Run_command_reports_the_real_exit_code_and_output_for_a_non_zero_exit()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("run_command", "{\"command\":\"exit 3\"}"));

        Assert.Equal("(exit 3, no output)", result);
        var updated = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
        Assert.Equal(3, updated.ExitCode);
        Assert.Equal(ToolCall.Status.Failed, updated.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-175")] // OpenAICompatibleSession.swift:1026
    public async Task Run_command_returns_the_output_prefixed_with_its_exit_code_on_success()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        var result = await session.ExecuteToolForTests(Call("run_command", "{\"command\":\"Write-Output hi\"}"));

        Assert.Equal("exit 0\nhi", result);
        Assert.Equal(ToolCall.Status.Completed, events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Status);
    }

    // ---- PR-PROVAPI-164, UI-PROVAPI-014: MCP dispatch ----

    private static MCPServerSpec.Stdio StdioFixture(string script) =>
        new("powershell", new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script]),
            ValueMap<string, string>.Empty);

    private static string FixtureScript([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "..", "Fixtures", "mcp-stdio-fixture.ps1");

    private static async Task SeedRealServer(OpenAICompatibleSession session, string serverId, string toolName)
    {
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance) { [serverId] = StdioFixture(FixtureScript()) });
        session.SeedMcpToolsForTests([new MCPHub.Tool(serverId, toolName, "", new JSONValue.Null())]);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-164")] // OpenAICompatibleSession.swift:899, :904, :910-911
    [Trait("Req", "UI-PROVAPI-014")] // OpenAICompatibleSession.swift:900
    public async Task An_mcp_call_is_titled_with_a_middle_dot_and_returns_its_text_on_success()
    {
        // FullAccess (RequiresApproval always false) so this exercises the real ExecuteTool dispatch
        // and a real MCP round trip without going through the approval continuation: PR-PROVAPI-164's
        // approval title ("Call <server> <tool>") is proven separately below, without a live subprocess,
        // because combining a real MCP child process with the approval wait under Supervised mode is
        // not reliable in this harness (the pending approval's checked continuation and the process's
        // own async I/O can race unpredictably once several such flows run in the same test process).
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        try
        {
            await SeedRealServer(session, "srv-echo", "echo");
            var result = await session.ExecuteToolForTests(Call("mcp__srv-echo__echo", "{\"text\":\"hi\"}"));

            var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
            Assert.Equal(ToolCall.Kind.Mcp, started.ToolCallKind);
            // U+00B7 MIDDLE DOT.
            Assert.Equal("srv-echo \U000000B7 echo", started.Title);
            Assert.Contains("echo:", result, StringComparison.Ordinal);
            Assert.Equal(ToolCall.Status.Completed, events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Status);
        }
        finally
        {
            // The fixture server (mcp-stdio-fixture.ps1) loops reading stdin forever; stop it rather
            // than leaking a live child process and actor state into the next test in this process.
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    [Trait("Req", "UI-PROVAPI-014")] // OpenAICompatibleSession.swift:900 `"Call \(server) \(name)"`
    public async Task An_mcp_calls_approval_title_is_Call_server_name()
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root, RuntimeMode.Supervised);
        var task = session.ApproveIfNeededForTests(ToolCall.Kind.Mcp, "Call srv-echo echo", "{}");
        runtime.Loop.RunUntilIdle();
        var approval = events.OfType<ProviderEvent.Approval>().Single().Value;
        Assert.Equal("Call srv-echo echo", approval.Title);
        Assert.Equal(ApprovalRequest.Kind.Tool, approval.RequestKind);
        await SettleApproval();
        session.ResolveApproval(approval.Id, "allow");
        runtime.Loop.RunUntilIdle();
        Assert.True(await task);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-164")] // OpenAICompatibleSession.swift:906-908
    public async Task An_mcp_call_that_reports_failure_returns_its_text_with_a_failed_status()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        try
        {
            await SeedRealServer(session, "srv-error", "error-tool");
            var result = await session.ExecuteToolForTests(Call("mcp__srv-error__error-tool", "{}"));

            Assert.Equal("failed", result);
            Assert.Equal(ToolCall.Status.Failed, events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Status);
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-164")] // OpenAICompatibleSession.swift:912-915
    public async Task An_mcp_call_to_an_unregistered_server_returns_the_thrown_errors_description()
    {
        using var folder = new ScratchFolder();
        var (session, events) = WiredSession(folder.Root);
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance));
        session.SeedMcpToolsForTests([new MCPHub.Tool("ghost", "tool", "", new JSONValue.Null())]);
        var result = await session.ExecuteToolForTests(Call("mcp__ghost__tool", "{}"));

        Assert.StartsWith("Error: ", result, StringComparison.Ordinal);
        Assert.Equal(ToolCall.Status.Failed, events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Status);
    }
}
