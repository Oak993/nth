using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/TextGeneration.swift (B2 1fbf81e), :1-102.
public sealed class TextGenerationTests
{
    /// <summary>Runs ThreadTitle over a Claude engine that replies with <paramref name="reply"/> on standard output.</summary>
    private static async Task<string?> ThreadTitleWithReply(string message, string reply)
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8(reply));
        return await TextGeneration.ThreadTitle(message, TextGenerationHarness.ClaudeEngine(run));
    }

    private static async Task<string?> ThreadTitleViaCodex(string message, string reply)
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8(reply));
        return await TextGeneration.ThreadTitle(message, TextGenerationHarness.CodexEngine(run));
    }

    // ---- PR-PROVAPI-187: threadTitle's prompt, working directory and cleanup ----------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-187")] // Services/Providers/TextGeneration.swift:10-22
    public async Task ThreadTitle_sends_the_prompt_verbatim_with_the_request_appended()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Add a retry to the sync job"));
        _ = await TextGeneration.ThreadTitle("Please retry syncing when it fails", TextGenerationHarness.ClaudeEngine(run));
        var expected =
            "Write a title for this coding task so the user can recognize it weeks later.\n" +
            "Rules: 3 to 7 words, under 40 characters, sentence case, no quotes, no trailing punctuation.\n" +
            "Name the subject and the outcome. Do not copy the request word for word.\n" +
            "The request may be a word or two, or come with attachments; even then, reply with\n" +
            "your single best title and nothing else. Never ask a question, never explain,\n" +
            "never mention the request or the word title.\n" +
            "Reply with the title only.\n" +
            "\n" +
            "Request:\n" +
            "Please retry syncing when it fails";
        Assert.Equal(expected, TextGenerationHarness.Stdin(run));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-187")] // Services/Providers/TextGeneration.swift:21 message.prefix(4_000)
    public async Task ThreadTitle_truncates_the_request_to_its_first_4000_characters()
    {
        var message = new string('a', 4_500);
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix the flaky test"));
        _ = await TextGeneration.ThreadTitle(message, TextGenerationHarness.ClaudeEngine(run));
        var sentPrompt = TextGenerationHarness.Stdin(run);
        var requestSection = sentPrompt[(sentPrompt.IndexOf("Request:\n", StringComparison.Ordinal) + "Request:\n".Length)..];
        Assert.Equal(new string('a', 4_000), requestSection);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-187")] // Services/Providers/TextGeneration.swift:23 FileManager.default.temporaryDirectory
    public async Task ThreadTitle_runs_in_the_system_temporary_directory()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix the flaky test"));
        _ = await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.ClaudeEngine(run));
        Assert.True(SwiftPath.SamePath(File.ReadAllText(run.CwdFile), TextGenerationHarness.ExpectedTemporaryDirectory));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-187")] // Services/Providers/TextGeneration.swift:26-28
    public async Task ThreadTitle_returns_the_cleaned_usable_reply()
    {
        Assert.Equal("Fix the flaky sync test", await ThreadTitleWithReply("fix it", "Fix the flaky sync test"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-187")] // Services/Providers/TextGeneration.swift:23-25
    public async Task ThreadTitle_returns_null_when_the_engine_run_fails()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix the flaky sync test"), exit: 1);
        Assert.Null(await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.ClaudeEngine(run)));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-187")] // Services/Providers/TextGeneration.swift:33-34 neither the cleaned reply nor its first line is usable
    public async Task ThreadTitle_returns_null_when_no_usable_title_can_be_found()
    {
        Assert.Null(await ThreadTitleWithReply("fix it", "I need more context to create a meaningful title. What spec do you mean?"));
    }

    // ---- PR-PROVAPI-188: usable(_:) ----------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-188")] // Services/Providers/TextGeneration.swift:41-42
    public async Task Usable_rejects_a_title_over_64_characters()
    {
        var tooLong = string.Join(" ", Enumerable.Repeat("word", 20)); // far over 64 characters
        Assert.Null(await ThreadTitleWithReply("fix it", tooLong));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-188")] // Services/Providers/TextGeneration.swift:43
    public async Task Usable_rejects_a_title_of_more_than_9_words()
    {
        var tenWords = "one two three four five six seven eight nine ten";
        Assert.Null(await ThreadTitleWithReply("fix it", tenWords));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-188")] // Services/Providers/TextGeneration.swift:43
    public async Task Usable_accepts_a_title_of_exactly_9_words()
    {
        var nineWords = "one two three four five six seven eight nine";
        Assert.Equal(nineWords, await ThreadTitleWithReply("fix it", nineWords));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-188")] // Services/Providers/TextGeneration.swift:44 contains("?")
    public async Task Usable_rejects_a_title_containing_a_question_mark()
    {
        Assert.Null(await ThreadTitleWithReply("fix it", "Fix the flaky sync test?"));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-188")] // Services/Providers/TextGeneration.swift:45
    [InlineData("Fix the flaky sync test:")]
    [InlineData("Fix the flaky sync test;")]
    [InlineData("Fix the flaky sync test,")]
    [InlineData("Fix the flaky sync test!")]
    [InlineData("Fix the flaky sync test\U00002026")] // U+2026 HORIZONTAL ELLIPSIS
    public async Task Usable_rejects_a_title_ending_in_one_of_the_six_forbidden_characters(string reply)
    {
        Assert.Null(await ThreadTitleWithReply("fix it", reply));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-188")] // Services/Providers/TextGeneration.swift:47-50
    [InlineData("I fixed the flaky sync test")]
    [InlineData("I've fixed the flaky sync test")]
    [InlineData("Sorry, I cannot make a title")]
    [InlineData("Sure, here is a title for you")]
    [InlineData("Here is the title you asked for")]
    [InlineData("Please give me more details first")]
    [InlineData("Could you clarify the request please")]
    [InlineData("Can you clarify the request please")]
    [InlineData("What spec do you mean exactly")]
    [InlineData("It seems the request is unclear")]
    [InlineData("It looks like the request is unclear")]
    [InlineData("Unfortunately I cannot make a title")]
    [InlineData("There is no clear subject to name")]
    [InlineData("Not enough detail to title this task")]
    [InlineData("Need more detail to title this task")]
    [InlineData("Maybe you could clarify the request")]
    [InlineData("The request lacks a clear subject")]
    [InlineData("This request lacks a clear subject")]
    public async Task Usable_rejects_a_title_starting_with_one_of_the_18_openers(string reply)
    {
        Assert.Null(await ThreadTitleWithReply("fix it", reply));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-188")] // Services/Providers/TextGeneration.swift:51-52
    [InlineData("Need more context to write it")]
    [InlineData("No meaningful title comes to mind")]
    [InlineData("Sorry as an AI I will not")]
    [InlineData("This task cannot be titled easily")]
    [InlineData("This task can't be titled easily")]
    [InlineData("This task is unable to be named")]
    public async Task Usable_rejects_a_title_containing_one_of_the_6_forbidden_phrases(string reply)
    {
        Assert.Null(await ThreadTitleWithReply("fix it", reply));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-188")] // Services/Providers/TextGeneration.swift:26-34
    public async Task Usable_falls_back_to_the_untruncated_first_line_when_the_60_character_cleanup_makes_it_end_in_an_ellipsis()
    {
        // 61 characters: TextCleanup.SingleLine(_, 60) truncates this to 59 characters plus an ellipsis, whose
        // last character (U+2026) usable(_:) rejects (:45); the untruncated first line is 61 characters, still
        // under the 64-character cap, and has no forbidden ending, so the fallback path accepts it.
        var reply = "Fix the flaky synchronization test across every remote device";
        Assert.Equal(61, reply.Length);
        Assert.Equal(reply, await ThreadTitleWithReply("fix it", reply));
    }

    // ---- PR-PROVAPI-189: commitMessage's prompt, truncation and fence stripping ---------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-189")] // Services/Providers/TextGeneration.swift:58-68
    public async Task CommitMessage_sends_the_prompt_verbatim_with_no_instructions_line_when_instructions_are_empty()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix flaky sync test\n"));
        var directory = TextGenerationHarness.TestFolder();
        _ = await TextGeneration.CommitMessage("M  src/Sync.cs", "diff --git a/src/Sync.cs b/src/Sync.cs", "", TextGenerationHarness.ClaudeEngine(run), directory);
        var expected =
            "Write a git commit message for the changes below.\n" +
            "Rules: an imperative subject line of at most 72 characters with no trailing period. " +
            "Optionally add a blank line and a few short bullet points. Reply with the message only, without code fences.\n" +
            "\n" +
            "Changed files:\n" +
            "M  src/Sync.cs\n" +
            "\n" +
            "Patch:\n" +
            "diff --git a/src/Sync.cs b/src/Sync.cs";
        Assert.Equal(expected, TextGenerationHarness.Stdin(run));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-189")] // Services/Providers/TextGeneration.swift:57
    public async Task CommitMessage_inserts_the_instructions_line_exactly_when_instructions_are_given()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix flaky sync test\n"));
        _ = await TextGeneration.CommitMessage("M  src/Sync.cs", "diff", "Mention the ticket number", TextGenerationHarness.ClaudeEngine(run),
            TextGenerationHarness.TestFolder());
        var sent = TextGenerationHarness.Stdin(run);
        Assert.Contains(
            "code fences.\n\nFollow these instructions too: Mention the ticket number\n\nChanged files:\n",
            sent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-189")] // Services/Providers/TextGeneration.swift:64 summary.prefix(6_000)
    public async Task CommitMessage_truncates_the_summary_to_6000_characters()
    {
        var summary = new string('f', 6_500);
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("msg\n"));
        _ = await TextGeneration.CommitMessage(summary, "diff", "", TextGenerationHarness.ClaudeEngine(run), TextGenerationHarness.TestFolder());
        var sent = TextGenerationHarness.Stdin(run);
        var start = sent.IndexOf("Changed files:\n", StringComparison.Ordinal) + "Changed files:\n".Length;
        var end = sent.IndexOf("\n\nPatch:\n", StringComparison.Ordinal);
        Assert.Equal(new string('f', 6_000), sent[start..end]);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-189")] // Services/Providers/TextGeneration.swift:67 patch.prefix(40_000)
    public async Task CommitMessage_truncates_the_patch_to_40000_characters()
    {
        var patch = new string('p', 40_500);
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("msg\n"));
        _ = await TextGeneration.CommitMessage("summary", patch, "", TextGenerationHarness.ClaudeEngine(run), TextGenerationHarness.TestFolder());
        var sent = TextGenerationHarness.Stdin(run);
        var start = sent.IndexOf("Patch:\n", StringComparison.Ordinal) + "Patch:\n".Length;
        Assert.Equal(new string('p', 40_000), sent[start..]);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-189")] // Services/Providers/TextGeneration.swift:71
    public async Task CommitMessage_strips_every_triple_backtick_from_the_reply()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("```\nFix flaky sync test\n```\n"));
        var result = await TextGeneration.CommitMessage("summary", "diff", "", TextGenerationHarness.ClaudeEngine(run), TextGenerationHarness.TestFolder());
        Assert.Equal("Fix flaky sync test", result);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-189")] // Services/Providers/TextGeneration.swift:72-73
    public async Task CommitMessage_returns_null_when_stripping_and_trimming_leaves_nothing()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("``` \n ```"));
        Assert.Null(await TextGeneration.CommitMessage("summary", "diff", "", TextGenerationHarness.ClaudeEngine(run), TextGenerationHarness.TestFolder()));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-189")] // Services/Providers/TextGeneration.swift:56 directory: URL
    public async Task CommitMessage_runs_in_the_given_directory_not_the_temporary_directory()
    {
        var directory = TextGenerationHarness.TestFolder();
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("msg\n"));
        _ = await TextGeneration.CommitMessage("summary", "diff", "", TextGenerationHarness.ClaudeEngine(run), directory);
        Assert.True(SwiftPath.SamePath(File.ReadAllText(run.CwdFile), directory));
        Assert.False(SwiftPath.SamePath(File.ReadAllText(run.CwdFile), TextGenerationHarness.ExpectedTemporaryDirectory));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-189")] // Services/Providers/TextGeneration.swift:69 a failed run yields nil
    public async Task CommitMessage_returns_null_when_the_engine_run_fails()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix flaky sync test"), exit: 1);
        Assert.Null(await TextGeneration.CommitMessage("summary", "diff", "", TextGenerationHarness.ClaudeEngine(run), TextGenerationHarness.TestFolder()));
    }

    // ---- PR-PROVAPI-190: the Claude engine argument vector, stdin and timeout -----------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-190")] // Services/Providers/TextGeneration.swift:79-83
    public async Task Claude_argument_vector_is_exactly_the_twelve_elements_in_order()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix flaky sync test"));
        _ = await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.ClaudeEngine(run));
        string[] expected =
        [
            "-p", "--output-format", "text", "--model", "haiku", "--tools", "",
            "--disable-slash-commands", "--strict-mcp-config", "--permission-mode", "dontAsk",
            "--no-session-persistence",
        ];
        Assert.Equal(expected, TextGenerationHarness.Argv(run));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-190")] // Services/Providers/TextGeneration.swift:84 Data(prompt.utf8), never an argument
    public async Task Claude_prompt_arrives_on_standard_input_never_as_an_argument()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix flaky sync test"));
        _ = await TextGeneration.ThreadTitle("a very distinctive request text", TextGenerationHarness.ClaudeEngine(run));
        Assert.DoesNotContain(TextGenerationHarness.Argv(run), a => a.Contains("distinctive", StringComparison.Ordinal));
        Assert.Contains("distinctive", TextGenerationHarness.Stdin(run), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-190")] // Services/Providers/TextGeneration.swift:85 a non-success status yields nil
    public async Task Claude_returns_null_on_a_non_zero_exit_even_with_output()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("Fix flaky sync test"), exit: 1);
        Assert.Null(await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.ClaudeEngine(run)));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-190")] // Services/Providers/TextGeneration.swift:86 the trimmed output, or nil when empty
    public async Task Claude_returns_null_when_the_output_is_empty()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("   \n  "));
        Assert.Null(await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.ClaudeEngine(run)));
    }

    // ---- PR-PROVAPI-191: the Codex engine argument vector, output file and timeout ------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:90-93
    public async Task Codex_argument_vector_is_exactly_the_twelve_elements_including_the_embedded_config_quotes()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Fix flaky sync test"));
        _ = await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.CodexEngine(run, "gpt-5-codex"));
        var argv = TextGenerationHarness.Argv(run);
        Assert.Equal(12, argv.Length);
        Assert.Equal("exec", argv[0]);
        Assert.Equal("--ephemeral", argv[1]);
        Assert.Equal("--skip-git-repo-check", argv[2]);
        Assert.Equal("-s", argv[3]);
        Assert.Equal("read-only", argv[4]);
        Assert.Equal("--model", argv[5]);
        Assert.Equal("gpt-5-codex", argv[6]);
        Assert.Equal("--config", argv[7]);
        Assert.Equal("model_reasoning_effort=\"low\"", argv[8]);
        Assert.Equal("--output-last-message", argv[9]);
        Assert.Equal("-", argv[11]);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:84, :94 Data(prompt.utf8), never an argument
    public async Task Codex_prompt_arrives_on_standard_input_never_as_an_argument()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Fix flaky sync test"));
        _ = await TextGeneration.ThreadTitle("a very distinctive codex request", TextGenerationHarness.CodexEngine(run));
        Assert.DoesNotContain(TextGenerationHarness.Argv(run), a => a.Contains("distinctive", StringComparison.Ordinal));
        Assert.Contains("distinctive", TextGenerationHarness.Stdin(run), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:88 DEVIATION OD-0012 F1
    public async Task Codex_output_file_path_is_marrow_prefixed_under_the_temporary_directory()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Fix flaky sync test"));
        _ = await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.CodexEngine(run));
        var outputPath = TextGenerationHarness.Argv(run)[10];
        Assert.True(SwiftPath.SamePath(SwiftPath.DeletingLastPathComponent(outputPath), TextGenerationHarness.ExpectedTemporaryDirectory));
        var name = SwiftPath.LastPathComponent(outputPath);
        Assert.Matches(@"^marrow-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\.txt$", name);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:96-97 read from the output file, not the process output
    public async Task Codex_reads_the_reply_from_the_output_file_not_from_standard_output()
    {
        Assert.Equal("Fix flaky sync test", await ThreadTitleViaCodex("fix it", "Fix flaky sync test"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:89 defer { try? FileManager.default.removeItem(at: output) }
    public async Task Codex_removes_the_output_file_after_a_successful_run()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Fix flaky sync test"));
        _ = await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.CodexEngine(run));
        Assert.False(File.Exists(TextGenerationHarness.Argv(run)[10]));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:89 removed even on failure
    public async Task Codex_removes_the_output_file_after_a_failed_run()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Fix flaky sync test"), exit: 1);
        _ = await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.CodexEngine(run));
        Assert.False(File.Exists(TextGenerationHarness.Argv(run)[10]));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:95 a non-zero exit yields nil
    public async Task Codex_returns_null_on_a_non_zero_exit_even_when_the_file_has_content()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Fix flaky sync test"), exit: 1);
        Assert.Null(await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.CodexEngine(run)));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:96 a missing file yields nil
    public async Task Codex_returns_null_when_the_output_file_was_never_written()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Fix flaky sync test"), writeOutput: false);
        Assert.Null(await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.CodexEngine(run)));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-191")] // Services/Providers/TextGeneration.swift:98 .nilIfEmpty
    public async Task Codex_returns_null_when_the_output_file_is_empty()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", []);
        Assert.Null(await TextGeneration.ThreadTitle("fix it", TextGenerationHarness.CodexEngine(run)));
    }
}
