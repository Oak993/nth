using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Dextra AS-22 (DESIGN.md rev 5, 4.3; ED-NEW-dextra-as22-10): the delegate's model is nth's one-shot text engine. TextGeneration.Complete is the existing private run made
// public for it, so what it does is what the title and commit-message calls already do: the prompt goes on standard input verbatim, Claude runs with no tools and no session
// and Codex read-only, both in the system temporary directory, and a run that fails or says nothing gives null. Offline: the engine is the test's stub program.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-10")]
public sealed class TextGenerationCompleteTests
{
    [Fact]
    public async Task Complete_sends_the_prompt_verbatim_runs_claude_with_no_tools_in_the_temporary_directory_and_returns_the_reply()
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("  {\"answers\": {}}\n"));
        var reply = await TextGeneration.Complete("Answer with JSON.\nQuestion: which database?", TextGenerationHarness.ClaudeEngine(run));
        Assert.Equal("{\"answers\": {}}", reply);
        Assert.Equal("Answer with JSON.\nQuestion: which database?", TextGenerationHarness.Stdin(run));
        var argv = TextGenerationHarness.Argv(run);
        Assert.Contains("--tools", argv);
        Assert.Contains("--no-session-persistence", argv);
        Assert.Contains("dontAsk", argv);
        Assert.True(SwiftPath.SamePath(File.ReadAllText(run.CwdFile), TextGenerationHarness.ExpectedTemporaryDirectory));
    }

    [Fact]
    public async Task Complete_asks_codex_read_only_and_reads_its_output_file()
    {
        var run = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Postgres"));
        var reply = await TextGeneration.Complete("Which database?", TextGenerationHarness.CodexEngine(run));
        Assert.Equal("Postgres", reply);
        var argv = TextGenerationHarness.Argv(run);
        Assert.Contains("read-only", argv);
        Assert.Contains("--ephemeral", argv);
        Assert.Equal("Which database?", TextGenerationHarness.Stdin(run));
    }

    [Fact]
    public async Task A_run_that_fails_or_says_nothing_gives_null()
    {
        var failed = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("anything"), exit: 1);
        Assert.Null(await TextGeneration.Complete("Which database?", TextGenerationHarness.ClaudeEngine(failed)));
        var silent = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("   \n"));
        Assert.Null(await TextGeneration.Complete("Which database?", TextGenerationHarness.ClaudeEngine(silent)));
        var noFile = TextGenerationHarness.PrepareStub("outputfile", TextGenerationHarness.Utf8("Postgres"), writeOutput: false);
        Assert.Null(await TextGeneration.Complete("Which database?", TextGenerationHarness.CodexEngine(noFile)));
    }
}
